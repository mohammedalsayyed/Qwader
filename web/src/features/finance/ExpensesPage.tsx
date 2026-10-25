import { useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Ban, Paperclip, Plus, Settings2, Wallet } from 'lucide-react';
import { expensesApi, type ExpenseItem } from '@/api/finance';
import { Button } from '@/components/ui/Button';
import { Field, Input, Select, Switch, TextArea } from '@/components/ui/Field';
import { Badge, EmptyState, ErrorState, FilterChips, PageHeader, Pagination, Spinner } from '@/components/ui/Feedback';
import { DateRangeFilter, resolveRange, type DateRangeValue } from '@/components/ui/DateRange';
import { Stat } from '@/components/ui/Layout';
import { Sheet } from '@/components/ui/Sheet';
import { useToast } from '@/components/ui/Toast';
import { AccountChips, AttachmentPicker } from '@/components/finance/FinanceBits';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';
import { formatDate, formatMoney, localToday } from '@/lib/format';
import { useIdempotencyKey } from '@/lib/hooks';
import { parseNumber } from '@/lib/numbers';
import { useLookups } from '@/lib/queries';

export function ExpensesPage() {
  const [params, setParams] = useSearchParams();
  const lookups = useLookups();
  const [range, setRange] = useState<DateRangeValue>({ preset: 'thisMonth', ...resolveRange('thisMonth') });
  const [category, setCategory] = useState<string>('all');
  const [page, setPage] = useState(1);
  const [voiding, setVoiding] = useState<ExpenseItem | null>(null);
  const [managing, setManaging] = useState(false);
  const creating = params.get('new') === '1';
  const setCreating = (open: boolean) => setParams(open ? { new: '1' } : {}, { replace: true });

  const query = useQuery({
    queryKey: ['expenses', { range, category, page }],
    queryFn: () => expensesApi.list({ from: range.from, to: range.to, categoryId: category === 'all' ? undefined : Number(category), page, pageSize: 25, includeVoided: true }),
    placeholderData: keepPreviousData,
  });
  const total = query.data?.items.filter((e) => e.status === 'Posted').reduce((s, e) => s + e.amount, 0) ?? 0;

  return (
    <>
      <PageHeader title="المصروفات" actions={<Button variant="outline" onClick={() => setManaging(true)}><Settings2 className="size-4" /> الأنواع</Button>} />
      <div className="mb-3 space-y-3">
        <DateRangeFilter value={range} onChange={(r) => { setRange(r); setPage(1); }} />
        <FilterChips value={category} onChange={(v) => { setCategory(v); setPage(1); }}
          options={[{ value: 'all', label: 'كل الأنواع' }, ...(lookups.data?.expenseCategories ?? []).map((c) => ({ value: String(c.id), label: c.name }))]} />
      </div>
      {query.data && <div className="mb-3"><Stat label="مجموع المصروفات المعروضة" value={formatMoney(total)} /></div>}
      {query.isPending ? <Spinner /> : query.isError ? <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} /> :
        query.data.items.length === 0 ? <EmptyState icon={<Wallet className="size-10" />} title="لا توجد مصروفات في هذه الفترة" /> : (
          <>
            <ul className={cn('divide-y divide-line rounded-card border border-line bg-surface', query.isPlaceholderData && 'opacity-60')}>
              {query.data.items.map((e) => (
                <li key={e.id} className="flex items-start justify-between gap-3 p-3">
                  <div className="min-w-0">
                    <p className="font-semibold">{e.categoryName}{e.attachmentFileId && <Paperclip className="ms-1 inline size-4 text-muted" />}</p>
                    <p className="text-sm">{e.description}</p>
                    <p className="text-xs text-muted">
                      {formatDate(e.date)} · {e.accountName} · <Link to={`/payments/${e.paymentVoucherId}`} className="ltr inline-block text-brand-700 underline">{e.paymentVoucherNumber}</Link>
                    </p>
                    {e.status === 'Voided' && <Badge tone="danger">ملغى</Badge>}
                  </div>
                  <div className="flex shrink-0 flex-col items-end gap-1">
                    <span className={cn('tabular font-bold', e.status === 'Voided' && 'text-muted line-through')}>{formatMoney(e.amount)}</span>
                    {e.status === 'Posted' && <button type="button" onClick={() => setVoiding(e)} className="flex h-9 items-center gap-1 text-xs text-danger"><Ban className="size-3.5" /> إلغاء</button>}
                  </div>
                </li>
              ))}
            </ul>
            <Pagination page={query.data.page} totalPages={query.data.totalPages} totalCount={query.data.totalCount} pageSize={25} onChange={setPage} />
          </>
        )}
      <Button size="lg" onClick={() => setCreating(true)} className="fixed bottom-fab end-4 z-30 rounded-full shadow-lg lg:static lg:mt-4">
        <Plus className="size-5" /> مصروف جديد
      </Button>
      {creating && <NewExpenseSheet onClose={() => setCreating(false)} />}
      {voiding && <VoidExpenseSheet expense={voiding} onClose={() => setVoiding(null)} />}
      {managing && <CategoriesSheet onClose={() => setManaging(false)} />}
    </>
  );
}

function NewExpenseSheet({ onClose }: { onClose: () => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const lookups = useLookups();
  const [key] = useIdempotencyKey();
  const [categoryId, setCategoryId] = useState('');
  const [description, setDescription] = useState('');
  const [amount, setAmount] = useState('');
  // Cash is the default once the account list is available (it may still be loading when the sheet opens).
  const [pickedAccount, setAccountId] = useState<number | null>(null);
  const accountId = pickedAccount ?? lookups.data?.accounts.find((a) => a.type === 'Cash')?.id ?? null;
  const [date, setDate] = useState(localToday());
  const [attachment, setAttachment] = useState<string | null>(null);
  const [errors, setErrors] = useState<Record<string, string>>({});

  const save = useMutation({
    mutationFn: () => expensesApi.create({ idempotencyKey: key, expenseCategoryId: Number(categoryId), description: description.trim(), amount: parseNumber(amount)!, financialAccountId: accountId!, date, attachmentFileId: attachment }),
    onSuccess: (e) => {
      for (const k of ['expenses', 'accounts', 'payment']) void queryClient.invalidateQueries({ queryKey: [k] });
      toast.success(`تم تسجيل المصروف ${e.number}`);
      onClose();
    },
    onError: (e) => setErrors({ form: errorMessage(e) }),
  });

  const submit = () => {
    const e: Record<string, string> = {};
    if (!categoryId) e.category = 'اختر نوع المصروف';
    if (!description.trim()) e.description = 'اكتب وصف المصروف';
    const n = parseNumber(amount);
    if (!n || Number.isNaN(n) || n <= 0) e.amount = 'أدخل مبلغًا صحيحًا';
    if (!accountId) e.account = 'اختر الحساب';
    setErrors(e);
    if (Object.keys(e).length === 0) save.mutate();
  };

  return (
    <Sheet open onClose={onClose} title="مصروف جديد" size="lg" footer={<Button block size="lg" loading={save.isPending} onClick={submit}>حفظ المصروف</Button>}>
      <div className="space-y-4">
        {errors.form && <p role="alert" className="rounded-xl bg-red-50 p-3 text-sm text-danger">{errors.form}</p>}
        <Field label="نوع المصروف" required error={errors.category}>
          {(id) => (
            <Select id={id} value={categoryId} invalid={!!errors.category} onChange={(e) => setCategoryId(e.target.value)}>
              <option value="">اختر…</option>
              {lookups.data?.expenseCategories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </Select>
          )}
        </Field>
        <Field label="الوصف" required error={errors.description}>{(id) => <Input id={id} value={description} invalid={!!errors.description} placeholder="مثال: فاتورة كهرباء شهر 10" onChange={(e) => setDescription(e.target.value)} />}</Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="المبلغ (₪)" required error={errors.amount}>{(id) => <Input id={id} inputMode="decimal" className="ltr text-lg" value={amount} invalid={!!errors.amount} onChange={(e) => setAmount(e.target.value)} />}</Field>
          <Field label="التاريخ">{(id) => <Input id={id} type="date" max={localToday()} value={date} onChange={(e) => setDate(e.target.value)} />}</Field>
        </div>
        <AccountChips label="دُفع من حساب" accounts={lookups.data?.accounts ?? []} value={accountId} onChange={setAccountId} error={errors.account} />
        <AttachmentPicker label="صورة الفاتورة / الإيصال (اختياري)" purpose="ExpenseAttachment" value={attachment} onChange={setAttachment} />
      </div>
    </Sheet>
  );
}

function VoidExpenseSheet({ expense, onClose }: { expense: ExpenseItem; onClose: () => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [reason, setReason] = useState('');
  const save = useMutation({
    mutationFn: () => expensesApi.void(expense.id, reason.trim()),
    onSuccess: () => { for (const k of ['expenses', 'accounts']) void queryClient.invalidateQueries({ queryKey: [k] }); toast.success('تم إلغاء المصروف وإعادة المبلغ للحساب'); onClose(); },
    onError: (e) => toast.error(errorMessage(e)),
  });
  return (
    <Sheet open onClose={onClose} title={`إلغاء ${expense.number}`}
      footer={<Button block size="lg" variant="danger" loading={save.isPending} disabled={!reason.trim()} onClick={() => save.mutate()}>تأكيد الإلغاء</Button>}>
      <div className="space-y-3">
        <p className="text-sm text-muted">سيعود {formatMoney(expense.amount)} إلى «{expense.accountName}» ويُلغى سند الصرف المرتبط.</p>
        <Field label="سبب الإلغاء" required>{(id) => <TextArea id={id} rows={2} value={reason} onChange={(e) => setReason(e.target.value)} />}</Field>
      </div>
    </Sheet>
  );
}

function CategoriesSheet({ onClose }: { onClose: () => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const query = useQuery({ queryKey: ['expense-categories'], queryFn: expensesApi.categories });
  const [name, setName] = useState('');
  const save = useMutation({
    mutationFn: (v: { id: number | null; name: string; isActive: boolean }) => expensesApi.saveCategory(v.id, { name: v.name, isActive: v.isActive }),
    onSuccess: () => { void queryClient.invalidateQueries({ queryKey: ['expense-categories'] }); void queryClient.invalidateQueries({ queryKey: ['lookups'] }); setName(''); },
    onError: (e) => toast.error(errorMessage(e)),
  });
  return (
    <Sheet open onClose={onClose} title="أنواع المصروفات">
      <div className="space-y-3">
        <div className="flex gap-2">
          <Input aria-label="نوع جديد" placeholder="نوع جديد، مثال: إنترنت" value={name} onChange={(e) => setName(e.target.value)} />
          <Button disabled={!name.trim()} loading={save.isPending} onClick={() => save.mutate({ id: null, name: name.trim(), isActive: true })}><Plus className="size-4" /> إضافة</Button>
        </div>
        {query.isPending ? <Spinner /> : (
          <ul className="divide-y divide-line">
            {query.data?.map((c) => (
              <li key={c.id} className="py-1">
                <Switch label={c.name} description={`${c.expenseCount} مصروف`} checked={c.isActive} onChange={(on) => save.mutate({ id: c.id, name: c.name, isActive: on })} />
              </li>
            ))}
          </ul>
        )}
      </div>
    </Sheet>
  );
}
