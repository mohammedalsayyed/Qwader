import { useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ApiError } from '@/api/client';
import { Plus, UserRound } from 'lucide-react';
import { paymentsApi } from '@/api/finance';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button } from '@/components/ui/Button';
import { Field, Input } from '@/components/ui/Field';
import { Badge, EmptyState, ErrorState, PageHeader, Pagination, Spinner } from '@/components/ui/Feedback';
import { DateRangeFilter, resolveRange, type DateRangeValue } from '@/components/ui/DateRange';
import { Stat } from '@/components/ui/Layout';
import { Sheet } from '@/components/ui/Sheet';
import { useToast } from '@/components/ui/Toast';
import { useConfirm } from '@/components/ui/Confirm';
import { AccountChips } from '@/components/finance/FinanceBits';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';
import { formatDate, formatMoney, localToday } from '@/lib/format';
import { useIdempotencyKey } from '@/lib/hooks';
import { parseNumber } from '@/lib/numbers';
import { useLookups, useStoreSettings } from '@/lib/queries';

/**
 * «مصاريف صاحب العمل»: what the owner takes from the store's accounts for themself. Each is a payment voucher of the kind
 * «مسحوبات شخصية»: money leaves the account, it is not a store expense (the store's profit stays), and the balance sheet
 * takes it off the owner's equity.
 */
export function OwnerExpensesPage() {
  const { can } = useAuth();
  const [params, setParams] = useSearchParams();
  const [range, setRange] = useState<DateRangeValue>({ preset: 'thisMonth', ...resolveRange('thisMonth') });
  const [page, setPage] = useState(1);
  // One owner's expenses, or all of them («الكل»).
  const [owner, setOwner] = useState<string | null>(null);
  const creating = params.get('new') === '1';
  const setCreating = (open: boolean) => setParams(open ? { new: '1' } : {}, { replace: true });

  const list = useQuery({
    queryKey: ['payment', 'owner', { range, page, owner }],
    queryFn: () => paymentsApi.list({ purpose: 'OwnerWithdrawal', from: range.from, to: range.to, page, pageSize: 25, includeVoided: true, beneficiary: owner ?? undefined }),
    placeholderData: keepPreviousData,
  });
  const total = useQuery({
    queryKey: ['payment', 'owner-total', range, owner],
    queryFn: () => paymentsApi.ownerTotal(range.from, range.to, owner ?? undefined),
    placeholderData: keepPreviousData,
  });

  return (
    <>
      <PageHeader title="مصاريف صاحب العمل" subtitle="من حسابات المعرض لصاحبه — ليست من مصروفات المعرض، وتُخصم من حقوق الملكية في الميزانية العمومية"
        actions={can(P.VouchersCreate) && <Button onClick={() => setCreating(true)}><Plus className="size-4" /> مصروف جديد</Button>} />
      <div className="mb-3 space-y-3">
        <DateRangeFilter value={range} onChange={(r) => { setRange(r); setPage(1); }} />
        {total.data && (total.data.byOwner?.length ?? 0) > 1 && (
          // Each owner with what they took this period; a tap shows only theirs.
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-4" role="radiogroup" aria-label="صاحب العمل">
            {[{ name: null as string | null, total: total.data.byOwner!.reduce((t, r) => t + r.total, 0), count: total.data.byOwner!.reduce((t, r) => t + r.count, 0) },
              ...total.data.byOwner!].map((r) => (
              <button key={r.name ?? '*'} type="button" role="radio" aria-checked={owner === r.name} onClick={() => { setOwner(r.name); setPage(1); }}
                className={cn('rounded-card border p-3 text-start', owner === r.name ? 'border-brand-700 bg-brand-50' : 'border-line bg-surface')}>
                <span className="block text-sm font-semibold">{r.name ?? 'الكل'}</span>
                <span className="tabular block text-lg font-bold">{formatMoney(r.total)}</span>
                <span className="block text-xs text-muted">{r.count} مصروف</span>
              </button>
            ))}
          </div>
        )}
        {total.data && (
          <div className="grid grid-cols-2 gap-2 lg:max-w-md">
            <Stat label={owner ? `مجموع ${owner} للفترة` : 'مجموع الفترة'} value={formatMoney(total.data.total)} />
            <Stat label="عددها" value={total.data.count} />
          </div>
        )}
      </div>

      {list.isPending ? <Spinner /> : list.isError ? <ErrorState message={errorMessage(list.error)} onRetry={() => void list.refetch()} /> :
        list.data.items.length === 0 ? <EmptyState icon={<UserRound className="size-10" />} title="لا مصاريف لصاحب العمل في هذه الفترة" /> : (
          <>
            <ul className={cn('divide-y divide-line rounded-card border border-line bg-surface', list.isPlaceholderData && 'opacity-60')}>
              {list.data.items.map((v) => (
                <li key={v.id}>
                  <Link to={`/payments/${v.id}`} className="flex items-start justify-between gap-3 p-3 hover:bg-canvas">
                    <div className="min-w-0">
                      <p className="font-semibold">{v.notes ?? v.partyName}</p>
                      {v.notes && v.partyName !== 'صاحب المال' && <p className="text-sm text-muted">{v.partyName}</p>}
                      <p className="text-xs text-muted">{formatDate(v.date)} · {v.accountName} · <span className="ltr inline-block">{v.number}</span></p>
                      {v.status === 'Voided' && <Badge tone="danger">ملغى</Badge>}
                    </div>
                    <p className={cn('tabular shrink-0 font-bold', v.status === 'Voided' && 'text-muted line-through')}>{formatMoney(v.amount)}</p>
                  </Link>
                </li>
              ))}
            </ul>
            <Pagination page={list.data.page} totalPages={list.data.totalPages} onChange={setPage} />
          </>
        )}
      {creating && <OwnerExpenseSheet owner={owner} onClose={() => setCreating(false)} />}
    </>
  );
}

function OwnerExpenseSheet({ owner: preset, onClose }: { owner: string | null; onClose: () => void }) {
  const toast = useToast();
  const confirm = useConfirm();
  const queryClient = useQueryClient();
  const lookups = useLookups();
  const [key] = useIdempotencyKey();
  const [description, setDescription] = useState('');
  const [amount, setAmount] = useState('');
  const [date, setDate] = useState(localToday());
  const [accountId, setAccountId] = useState<number | null>(null);
  const owners = useStoreSettings().data?.owners ?? [];
  const [owner, setOwner] = useState<string | null>(preset);
  const [errors, setErrors] = useState<Record<string, string>>({});

  const save = useMutation({
    mutationFn: (confirmDuplicate: boolean) => paymentsApi.create({
      idempotencyKey: key, purpose: 'OwnerWithdrawal', beneficiaryName: owners.length ? owner! : 'صاحب المال', amount: parseNumber(amount)!,
      financialAccountId: accountId!, notes: description.trim(), confirmDuplicate, date,
    }),
    onSuccess: (v) => {
      for (const k of ['payment', 'accounts', 'balance-sheet']) void queryClient.invalidateQueries({ queryKey: [k] });
      toast.success(`سُجّل ${formatMoney(v.amount)} — سند صرف ${v.number}`);
      onClose();
    },
    onError: async (e) => {
      if (e instanceof ApiError && e.status === 409) {
        if (await confirm({ title: 'مصروف مشابه مسجّل', message: e.message, confirmLabel: 'نعم، مصروف جديد' })) save.mutate(true);
        return;
      }
      setErrors({ form: errorMessage(e) });
    },
  });

  const submit = () => {
    const e: Record<string, string> = {};
    if (!description.trim()) e.description = 'اكتب البيان';
    const n = parseNumber(amount);
    if (!n || Number.isNaN(n) || n <= 0) e.amount = 'أدخل مبلغًا صحيحًا';
    if (!accountId) e.account = 'اختر الحساب';
    if (owners.length && !owner) e.owner = 'اختر صاحب العمل';
    setErrors(e);
    if (Object.keys(e).length === 0) save.mutate(false);
  };

  return (
    <Sheet open onClose={onClose} dirty={!!(description || amount)} title="مصروف لصاحب العمل" size="lg"
      footer={<Button block size="lg" loading={save.isPending} onClick={submit}>حفظ</Button>}>
      <div className="space-y-4">
        {errors.form && <p role="alert" className="rounded-xl bg-red-50 p-3 text-sm text-danger">{errors.form}</p>}
        <p className="rounded-xl bg-brand-50 p-3 text-sm leading-6">
          يخرج المبلغ من حساب المعرض لصاحبه. <b>لا يُحسب مصروفًا على المعرض</b> (الربح لا يتغيّر)، ويُخصم من <b>حقوق الملكية</b> في الميزانية العمومية.
        </p>
        {owners.length > 0 && <OwnerChips owners={owners} value={owner} onChange={(o) => { setOwner(o); setErrors((x) => ({ ...x, owner: '' })); }} error={errors.owner} />}
        <Field label="البيان" required error={errors.description}>
          {(id) => <Input id={id} value={description} maxLength={500} invalid={!!errors.description} placeholder="مثال: مصروف البيت، بنزين السيارة" onChange={(e) => setDescription(e.target.value)} />}
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="المبلغ (₪)" required error={errors.amount}>
            {(id) => <Input id={id} inputMode="decimal" className="ltr text-lg" value={amount} invalid={!!errors.amount} onChange={(e) => setAmount(e.target.value)} />}
          </Field>
          <Field label="التاريخ" hint="يوم الصرف">
            {(id, d) => <Input id={id} aria-describedby={d} type="date" max={localToday()} value={date} onChange={(e) => setDate(e.target.value || localToday())} />}
          </Field>
        </div>
        <AccountChips label="دُفع من حساب" accounts={lookups.data?.accounts ?? []} value={accountId} onChange={setAccountId} error={errors.account} />
      </div>
    </Sheet>
  );
}

/** Picks one of the business owners (from the settings). */
export function OwnerChips({ owners, value, onChange, error, label = 'صاحب العمل' }: {
  owners: string[]; value: string | null; onChange: (owner: string) => void; error?: string; label?: string;
}) {
  return (
    <div>
      <p className="mb-1.5 text-sm font-medium">{label} <span className="text-danger">*</span></p>
      <div className="flex flex-wrap gap-2" role="radiogroup" aria-label={label}>
        {owners.map((o) => (
          <button key={o} type="button" role="radio" aria-checked={value === o} onClick={() => onChange(o)}
            className={cn('min-h-11 rounded-control border px-4 text-sm font-semibold', value === o ? 'border-brand-700 bg-brand-50 text-brand-800' : 'border-line bg-surface')}>
            {o}
          </button>
        ))}
      </div>
      {error && <p className="mt-1 text-sm text-danger">{error}</p>}
    </div>
  );
}
