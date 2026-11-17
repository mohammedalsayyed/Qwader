import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { ArrowRight, History, Pencil, Phone, Plus, ReceiptText, ShoppingBag, Truck } from 'lucide-react';
import { purchasesApi, suppliersApi, type Supplier } from '@/api/stock';
import { paymentsApi } from '@/api/finance';
import { AccountChips } from '@/components/finance/FinanceBits';
import { useLookups } from '@/lib/queries';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button } from '@/components/ui/Button';
import { Field, Input, Switch, TextArea } from '@/components/ui/Field';
import { Badge, Card, EmptyState, ErrorState, FilterChips, PageHeader, Pagination, SearchInput, Spinner } from '@/components/ui/Feedback';
import { InfoList, Stat, Tabs } from '@/components/ui/Layout';
import { Sheet } from '@/components/ui/Sheet';
import { useToast } from '@/components/ui/Toast';
import { StatementView } from '@/components/finance/StatementView';
import { useConfirm } from '@/components/ui/Confirm';
import { cn } from '@/lib/cn';
import { applyServerErrors, blankToNull, errorMessage } from '@/lib/forms';
import { formatDate, formatMoney, localToday } from '@/lib/format';
import { useDebouncedValue } from '@/lib/hooks';
import { parseNumber, zOptionalAmount } from '@/lib/numbers';
import { PurchaseStatusBadge } from './PurchasesPage';

type Filter = 'active' | 'balance' | 'all';

export function SuppliersPage() {
  const { can } = useAuth();
  const [search, setSearch] = useState('');
  const [filter, setFilter] = useState<Filter>('active');
  const [page, setPage] = useState(1);
  const [creating, setCreating] = useState(false);
  const term = useDebouncedValue(search.trim());

  const query = useQuery({
    queryKey: ['suppliers', { term, filter, page }],
    queryFn: () => suppliersApi.list({ search: term, page, pageSize: 30, isActive: filter === 'all' ? undefined : true, withBalance: filter === 'balance' || undefined }),
    placeholderData: keepPreviousData,
  });

  return (
    <>
      <PageHeader title="الموردون" actions={<div className="hidden lg:flex">{can(P.SuppliersManage) && <Button onClick={() => setCreating(true)}><Plus className="size-5" /> مورد جديد</Button>}</div>} />
      <div className="mb-3 space-y-3">
        <SearchInput value={search} onChange={(v) => { setSearch(v); setPage(1); }} placeholder="ابحث بالاسم أو الجوال أو الكود" />
        <FilterChips value={filter} onChange={(v) => { setFilter(v); setPage(1); }}
          options={[{ value: 'active', label: 'الفعّالون' }, { value: 'balance', label: 'لهم رصيد' }, { value: 'all', label: 'الكل' }]} />
      </div>
      {query.isPending ? (
        <Spinner />
      ) : query.isError ? (
        <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} />
      ) : query.data.items.length === 0 ? (
        <EmptyState icon={<Truck className="size-10" />} title={term ? 'لا يوجد موردون مطابقون' : 'لا يوجد موردون بعد'} />
      ) : (
        <>
          <ul className="grid grid-cols-1 gap-2 md:grid-cols-2">
            {query.data.items.map((s) => (
              <li key={s.id}>
                <Link to={`/suppliers/${s.id}`} className="flex items-center justify-between gap-3 rounded-card border border-line bg-surface p-4 hover:border-brand-200">
                  <div className="min-w-0">
                    <p className="flex items-center gap-2 font-semibold"><span className="truncate">{s.name}</span>{!s.isActive && <Badge tone="danger">غير فعّال</Badge>}</p>
                    <p className="ltr text-sm text-muted">{s.phone ?? s.code}</p>
                  </div>
                  <div className="shrink-0 text-end">
                    <p className="text-xs text-muted">{s.balance > 0 ? 'مستحق له (علينا)' : s.balance < 0 ? 'لنا عنده' : 'الرصيد'}</p>
                    <p className={`tabular font-bold ${s.balance > 0 ? 'text-danger' : s.balance < 0 ? 'text-success' : ''}`}>{formatMoney(Math.abs(s.balance))}</p>
                  </div>
                </Link>
              </li>
            ))}
          </ul>
          <Pagination page={query.data.page} totalPages={query.data.totalPages} onChange={setPage} />
        </>
      )}
      {can(P.SuppliersManage) && (
        <Button size="lg" onClick={() => setCreating(true)} className="fixed bottom-fab end-4 z-30 rounded-full shadow-lg lg:hidden">
          <Plus className="size-5" /> مورد جديد
        </Button>
      )}
      {creating && <SupplierSheet supplier={null} onClose={() => setCreating(false)} />}
    </>
  );
}

const schema = z.object({
  name: z.string().trim().min(1, 'اسم المورد مطلوب').max(150),
  phone: z.string().trim().max(20, 'رقم الجوال طويل جدًا').optional(),
  address: z.string().trim().max(200).optional(),
  notes: z.string().trim().max(500).optional(),
  isActive: z.boolean(),
  openingBalance: zOptionalAmount(),
});
type FormInput = z.input<typeof schema>;
type FormValues = z.output<typeof schema>;

export function SupplierSheet({ supplier, onClose }: { supplier: Supplier | null; onClose: () => void }) {
  const toast = useToast();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { register, control, handleSubmit, setError, formState: { errors, isDirty } } = useForm<FormInput, unknown, FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { name: supplier?.name ?? '', phone: supplier?.phone ?? '', address: supplier?.address ?? '', notes: supplier?.notes ?? '', isActive: supplier?.isActive ?? true, openingBalance: '' },
  });

  // The balance: owed to the supplier, or paid to them ahead for goods not arrived yet — before the system (no account moves)
  // or after it (a payment voucher from an account, on its day).
  const [advance, setAdvance] = useState(false);
  const timing = useAdvanceTiming();
  const save = useMutation({
    mutationFn: async (v: FormValues) => {
      const fromAccount = !supplier && advance && timing.when === 'after' && (v.openingBalance ?? 0) > 0;
      const input = { name: v.name, phone: blankToNull(v.phone), address: blankToNull(v.address), notes: blankToNull(v.notes), isActive: v.isActive,
        openingBalance: fromAccount ? 0 : v.openingBalance ?? 0, openingIsAdvance: advance };
      if (supplier) return { saved: await suppliersApi.update(supplier.id, input), paymentError: null };
      const saved = await suppliersApi.create(input);
      if (!fromAccount) return { saved, paymentError: null };
      // The supplier is in; the advance is its own payment voucher (it can fail on its own: not enough in the account).
      try {
        await paymentsApi.create({ idempotencyKey: crypto.randomUUID(), purpose: 'SupplierPayment', supplierId: saved.id, amount: v.openingBalance!,
          financialAccountId: timing.accountId!, date: timing.date, advance: true });
        return { saved: await suppliersApi.get(saved.id), paymentError: null };
      } catch (e) {
        return { saved, paymentError: errorMessage(e) };
      }
    },
    onSuccess: ({ saved, paymentError }) => {
      for (const k of ['suppliers', 'accounts', 'payment-vouchers', 'balance-sheet']) void queryClient.invalidateQueries({ queryKey: [k] });
      queryClient.setQueryData(['supplier', saved.id], saved);
      if (paymentError) toast.error(`أُضيف المورد، لكن لم تُسجَّل الدفعة المقدمة: ${paymentError}. سجّلها من صفحته («رصيد سابق / دفعة مقدمة»).`);
      else toast.success(supplier ? 'تم حفظ بيانات المورد' : 'تمت إضافة المورد');
      onClose();
      if (!supplier) navigate(`/suppliers/${saved.id}`);
    },
    onError: (e) => {
      const msg = applyServerErrors(e, setError, ['name', 'phone', 'address', 'notes', 'openingBalance']);
      if (msg) toast.error(msg);
    },
  });
  const onSubmit = handleSubmit((v) => {
    if (!supplier && advance && timing.when === 'after' && (v.openingBalance ?? 0) > 0 && !timing.validate()) return;
    save.mutate(v);
  });

  return (
    <Sheet open onClose={onClose} dirty={isDirty} title={supplier ? 'تعديل المورد' : 'مورد جديد'}
      footer={<Button block size="lg" loading={save.isPending} onClick={() => void onSubmit()}>حفظ</Button>}>
      <form onSubmit={onSubmit} noValidate className="space-y-4">
        <Field label="اسم المورد" required error={errors.name?.message}>
          {(id, d) => <Input id={id} aria-describedby={d} invalid={!!errors.name} {...register('name')} />}
        </Field>
        <Field label="رقم الجوال" error={errors.phone?.message}>
          {(id, d) => <Input id={id} aria-describedby={d} type="tel" inputMode="tel" className="ltr" {...register('phone')} />}
        </Field>
        <Field label="العنوان" error={errors.address?.message}>{(id) => <Input id={id} {...register('address')} />}</Field>
        {!supplier && (
          <div className="space-y-2">
            <p className="text-sm font-medium">رصيد المورد</p>
            <div className="grid grid-cols-2 gap-2">
              {([[false, 'علينا له (مستحق للمورد)'], [true, 'لنا عنده (دفعنا مقدمًا)']] as const).map(([v, label]) => (
                <button key={label} type="button" onClick={() => setAdvance(v)}
                  className={cn('min-h-11 rounded-control border px-2 text-sm font-semibold', advance === v ? (v ? 'border-success bg-success/10 text-success' : 'border-danger bg-danger/10 text-danger') : 'border-line bg-surface')}>
                  {label}
                </button>
              ))}
            </div>
            <Field label="المبلغ" error={errors.openingBalance?.message}
              hint={advance ? 'مبلغ دفعه المعرض للمورد ولم تصل بضاعته بعد — يبقى على المورد حتى تصل البضاعة' : 'المبلغ الذي يدين به المعرض للمورد قبل بدء استخدام النظام'}>
              {(id, d) => <Input id={id} aria-describedby={d} inputMode="decimal" className="ltr" invalid={!!errors.openingBalance} {...register('openingBalance')} />}
            </Field>
            {advance && <AdvanceTimingFields timing={timing} />}
          </div>
        )}
        <Field label="ملاحظات" error={errors.notes?.message}>{(id) => <TextArea id={id} rows={2} {...register('notes')} />}</Field>
        {supplier && (
          <Controller control={control} name="isActive" render={({ field }) => <Switch label="المورد فعّال" checked={field.value} onChange={field.onChange} />} />
        )}
      </form>
    </Sheet>
  );
}

type DetailTab = 'statement' | 'purchases';

export function SupplierDetailPage() {
  const id = Number(useParams().id);
  const { can } = useAuth();
  const navigate = useNavigate();
  const [tab, setTab] = useState<DetailTab>('statement');
  const [editing, setEditing] = useState(false);
  const [oldBalance, setOldBalance] = useState(false);
  const supplier = useQuery({ queryKey: ['supplier', id], queryFn: () => suppliersApi.get(id) });

  if (supplier.isPending) return <Spinner />;
  if (supplier.isError) return <ErrorState message={errorMessage(supplier.error)} onRetry={() => void supplier.refetch()} />;
  const s = supplier.data;

  return (
    <div className="space-y-4">
      <button type="button" onClick={() => navigate('/suppliers')} className="flex h-10 items-center gap-1 text-sm text-muted">
        <ArrowRight className="size-4" /> الموردون
      </button>
      <div className="flex items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold">{s.name}</h1>
          <p className="ltr text-sm text-muted">{s.code}</p>
        </div>
        <div className="flex gap-2">
          {s.phone && <a href={`tel:${s.phone}`}><Button variant="outline" aria-label="اتصال"><Phone className="size-4" /></Button></a>}
          {can(P.SuppliersManage) && <Button variant="outline" onClick={() => setEditing(true)}><Pencil className="size-4" /> تعديل</Button>}
        </div>
      </div>

      <div className="grid grid-cols-2 gap-2 lg:grid-cols-3">
        <Stat label={s.balance >= 0 ? 'المستحق للمورد (علينا)' : 'لنا عنده (دفعنا مقدمًا)'} value={formatMoney(Math.abs(s.balance))} tone={s.balance > 0 ? 'danger' : s.balance < 0 ? 'success' : 'default'} />
        <Stat label="صافي المشتريات" value={formatMoney(s.totalPurchases)} />
        <Stat label="إجمالي المدفوع" value={formatMoney(s.totalPayments)} />
      </div>

      {can(P.VouchersCreate) && s.balance > 0 && (
        <Link to={`/payments/new?supplierId=${s.id}`} className="block">
          <Button block variant="secondary"><ReceiptText className="size-4" /> دفعة للمورد (سند صرف)</Button>
        </Link>
      )}
      {can(P.PurchasesManage) && (
        <Link to={`/purchases/new?supplierId=${s.id}`} className="block">
          <Button block variant="secondary"><ShoppingBag className="size-4" /> فاتورة شراء جديدة من هذا المورد</Button>
        </Link>
      )}
      {can(P.SuppliersManage) && (
        <Button block variant="outline" onClick={() => setOldBalance(true)}><History className="size-4" /> رصيد سابق (علينا له / لنا عنده)</Button>
      )}

      {(s.address || s.notes) && (
        <Card className="px-4"><InfoList items={[s.address && { label: 'العنوان', value: s.address }, s.notes && { label: 'ملاحظات', value: s.notes }]} /></Card>
      )}

      <Tabs value={tab} onChange={setTab} tabs={[{ value: 'statement', label: 'كشف الحساب' }, { value: 'purchases', label: 'فواتير الشراء' }]} />
      {tab === 'statement' ? (
        <StatementView queryKey={['supplier-statement', id]} load={(from, to) => suppliersApi.statement(id, from, to)}
          debitLabel="مدين (سداد/مردود)" creditLabel="دائن (مشتريات)" balanceHint="الموجب = علينا للمورد، السالب = لنا عنده" />
      ) : (
        <SupplierPurchases supplierId={id} />
      )}

      {editing && <SupplierSheet supplier={s} onClose={() => setEditing(false)} />}
      {oldBalance && <SupplierOldBalanceSheet supplier={s} onClose={() => setOldBalance(false)} />}
    </div>
  );
}

function SupplierPurchases({ supplierId }: { supplierId: number }) {
  const [page, setPage] = useState(1);
  const query = useQuery({
    queryKey: ['purchases', { supplierId, page }],
    queryFn: () => purchasesApi.list({ supplierId, page, pageSize: 20 }),
    placeholderData: keepPreviousData,
  });
  if (query.isPending) return <Spinner />;
  if (query.isError) return <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} />;
  if (query.data.items.length === 0) return <EmptyState title="لا توجد فواتير شراء لهذا المورد" />;
  return (
    <>
      <ul className="divide-y divide-line rounded-card border border-line bg-surface">
        {query.data.items.map((p) => (
          <li key={p.id}>
            <Link to={`/purchases/${p.id}`} className="flex items-center justify-between gap-3 p-3">
              <div>
                <p className="ltr font-semibold">{p.number}</p>
                <p className="text-xs text-muted">{formatDate(p.date)}</p>
              </div>
              <div className="flex items-center gap-2">
                <span className="tabular font-bold">{formatMoney(p.total)}</span>
                <PurchaseStatusBadge status={p.status} />
              </div>
            </Link>
          </li>
        ))}
      </ul>
      <Pagination page={query.data.page} totalPages={query.data.totalPages} onChange={setPage} />
    </>
  );
}

/**
 * «رصيد سابق» for a supplier: what stood between the store and them before the system — owed to them («علينا له») or paid to
 * them ahead for goods not arrived yet («لنا عنده»). No money moves in any account; it goes on their statement.
 */
function SupplierOldBalanceSheet({ supplier, onClose }: { supplier: Supplier; onClose: () => void }) {
  const toast = useToast();
  const confirm = useConfirm();
  const queryClient = useQueryClient();
  const [idempotencyKey] = useState(() => crypto.randomUUID());
  const [owed, setOwed] = useState<boolean | null>(null);
  const timing = useAdvanceTiming();
  const fromAccount = owed === false && timing.when === 'after';
  const [amount, setAmount] = useState('');
  const [notes, setNotes] = useState('');
  const [errors, setErrors] = useState<Record<string, string>>({});
  const n = parseNumber(amount);
  const after = owed == null || !n || Number.isNaN(n) ? null : supplier.balance + (owed ? n : -n);
  const describe = (b: number) => (b >= 0 ? `المستحق للمورد ${formatMoney(b)}` : `لنا عنده ${formatMoney(-b)}`);

  const save = useMutation({
    mutationFn: async () => {
      if (!fromAccount) return suppliersApi.oldBalance(supplier.id, { amount: n!, owedToSupplier: owed!, notes: notes.trim() || null });
      // Paid after the system started: a payment voucher from the account, on its day.
      await paymentsApi.create({ idempotencyKey, purpose: 'SupplierPayment', supplierId: supplier.id, amount: n!, financialAccountId: timing.accountId!,
        date: timing.date, advance: true, notes: notes.trim() || null });
      return suppliersApi.get(supplier.id);
    },
    onSuccess: (saved) => {
      queryClient.setQueryData(['supplier', saved.id], saved);
      for (const k of ['suppliers', 'supplier-statement', 'balance-sheet', 'accounts', 'payment-vouchers']) void queryClient.invalidateQueries({ queryKey: [k] });
      toast.success(owed ? `سُجّل ${formatMoney(n)} مستحقًا لـ ${supplier.name}` : `سُجّل ${formatMoney(n)} دفعة مقدمة عند ${supplier.name}`);
      onClose();
    },
    onError: (e) => setErrors({ form: errorMessage(e) }),
  });

  const submit = async () => {
    const e: Record<string, string> = {};
    if (owed == null) e.owed = 'اختر: علينا له أو لنا عنده';
    if (!n || Number.isNaN(n) || n <= 0) e.amount = 'أدخل مبلغًا صحيحًا';
    setErrors(e);
    if (fromAccount && !timing.validate()) return;
    if (Object.keys(e).length) return;
    if (await confirm({ title: 'تسجيل رصيد سابق للمورد', message: `يصبح ${describe(after!)}. ${fromAccount ? `يُخصم ${formatMoney(n)} من الحساب بسند صرف.` : 'لا تتحرك أموال في أي حساب.'}`, confirmLabel: 'تسجيل' })) save.mutate();
  };

  return (
    <Sheet open onClose={onClose} dirty={amount !== '' || owed != null} title="رصيد سابق / دفعة مقدمة"
      footer={<Button block size="lg" loading={save.isPending} onClick={() => void submit()}>تسجيل</Button>}>
      <div className="space-y-4">
        <p className="text-sm text-muted">ما بين المعرض و{supplier.name}: مستحق له من قبل النظام، أو دفعة مقدمة له لم تصل بضاعتها بعد.</p>
        <div>
          <div className="grid grid-cols-2 gap-2">
            {([[true, 'علينا له (مستحق للمورد)'], [false, 'لنا عنده (دفعنا مقدمًا)']] as const).map(([v, label]) => (
              <button key={label} type="button" onClick={() => setOwed(v)}
                className={cn('min-h-12 rounded-control border px-2 text-sm font-semibold', owed === v ? (v ? 'border-danger bg-danger/10 text-danger' : 'border-success bg-success/10 text-success') : 'border-line bg-surface')}>
                {label}
              </button>
            ))}
          </div>
          {errors.owed && <p className="mt-1 text-sm text-danger">{errors.owed}</p>}
        </div>
        <Field label="المبلغ" required error={errors.amount}>
          {(id) => <Input id={id} inputMode="decimal" className="ltr" value={amount} invalid={!!errors.amount} onChange={(e) => setAmount(e.target.value)} />}
        </Field>
        {owed === false && <AdvanceTimingFields timing={timing} />}
        <Field label="ملاحظة" hint="مثلًا: دفعة على طلبية لم تصل">
          {(id) => <Input id={id} value={notes} maxLength={300} onChange={(e) => setNotes(e.target.value)} />}
        </Field>
        <div className="rounded-control bg-canvas p-3 text-sm">
          <p>الآن: <b className="tabular">{describe(supplier.balance)}</b></p>
          {after != null && <p>بعد التسجيل: <b className="tabular">{describe(after)}</b></p>}
        </div>
        {errors.form && <p className="text-sm text-danger">{errors.form}</p>}
      </div>
    </Sheet>
  );
}

/**
 * When a supplier advance was paid: before the system (the accounts' balances started after it, so no account moves) or after
 * it (taken out of an account on its day, as a payment voucher).
 */
function useAdvanceTiming() {
  const lookups = useLookups();
  const [when, setWhen] = useState<'before' | 'after'>('after');
  const [picked, setPicked] = useState<number | null>(null);
  const [date, setDate] = useState(localToday());
  const [error, setError] = useState('');
  const accounts = lookups.data?.accounts ?? [];
  const accountId = picked ?? accounts.find((a) => a.type === 'Cash')?.id ?? null;
  return {
    when, setWhen, accountId, setAccountId: (id: number | null) => { setPicked(id); setError(''); }, date, setDate, error, accounts,
    validate: () => {
      if (when === 'after' && !accountId) { setError('اختر الحساب الذي دُفعت منه'); return false; }
      return true;
    },
  };
}

function AdvanceTimingFields({ timing }: { timing: ReturnType<typeof useAdvanceTiming> }) {
  return (
    <div className="space-y-3 rounded-control bg-canvas p-3">
      <p className="text-sm font-medium">متى دُفعت؟</p>
      <div className="grid grid-cols-2 gap-2">
        {([['after', 'بعد استخدام النظام (من حساب)'], ['before', 'قبل استخدام النظام']] as const).map(([v, label]) => (
          <button key={v} type="button" onClick={() => timing.setWhen(v)}
            className={cn('min-h-11 rounded-control border px-2 text-sm font-semibold', timing.when === v ? 'border-brand-600 bg-brand-50 text-brand-700' : 'border-line bg-surface')}>
            {label}
          </button>
        ))}
      </div>
      {timing.when === 'before' ? (
        <p className="text-xs text-muted">لا يُخصم من أي حساب: أرصدة الحسابات الافتتاحية سُجّلت بعد هذه الدفعة.</p>
      ) : (
        <>
          <AccountChips label="دُفعت من حساب" accounts={timing.accounts} value={timing.accountId} onChange={timing.setAccountId} error={timing.error} />
          <Field label="تاريخ الدفع" hint="يُخصم من الحساب بسند صرف في هذا اليوم">
            {(id, d) => <Input id={id} aria-describedby={d} type="date" max={localToday()} value={timing.date} onChange={(e) => timing.setDate(e.target.value || localToday())} />}
          </Field>
        </>
      )}
    </div>
  );
}
