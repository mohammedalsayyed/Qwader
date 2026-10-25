import { useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowRight, Ban, HandCoins, Paperclip, Plus, Printer, ReceiptText } from 'lucide-react';
import { paymentsApi, receiptsApi, type PaymentPurpose, type ReceiptPurpose, type Voucher } from '@/api/finance';
import { ApiError } from '@/api/client';
import { customersApi, type CustomerListItem } from '@/api/sales';
import { suppliersApi, type SupplierListItem } from '@/api/stock';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button } from '@/components/ui/Button';
import { Field, Input, TextArea } from '@/components/ui/Field';
import { Badge, Card, EmptyState, ErrorState, PageHeader, Pagination, SearchInput, Spinner } from '@/components/ui/Feedback';
import { DateRangeFilter, resolveRange, type DateRangeValue } from '@/components/ui/DateRange';
import { InfoList, StickyActions } from '@/components/ui/Layout';
import { Picker } from '@/components/ui/Picker';
import { Sheet } from '@/components/ui/Sheet';
import { useConfirm } from '@/components/ui/Confirm';
import { useToast } from '@/components/ui/Toast';
import { AccountChips, AttachmentPicker, PrintableVoucher, PrivateFileThumb } from '@/components/finance/FinanceBits';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';
import { formatDateTime, formatMoney } from '@/lib/format';
import { useDebouncedValue, useIdempotencyKey, useUnsavedChangesWarning } from '@/lib/hooks';
import { parseNumber } from '@/lib/numbers';
import { useLookups, useStoreSettings } from '@/lib/queries';
import { amountInWords } from '@/lib/tafqeet';

type Kind = 'receipt' | 'payment';
const KIND = {
  receipt: { title: 'سندات القبض', one: 'سند قبض', path: '/receipts', api: receiptsApi, icon: HandCoins, party: 'الدافع' },
  payment: { title: 'سندات الصرف', one: 'سند صرف', path: '/payments', api: paymentsApi, icon: ReceiptText, party: 'المستفيد' },
} as const;

// ------------------------------------------------------------------ list

export function VouchersPage({ kind }: { kind: Kind }) {
  const meta = KIND[kind];
  const { can } = useAuth();
  const navigate = useNavigate();
  const [search, setSearch] = useState('');
  const [range, setRange] = useState<DateRangeValue>({ preset: 'thisMonth', ...resolveRange('thisMonth') });
  const [page, setPage] = useState(1);
  const term = useDebouncedValue(search.trim());
  const query = useQuery({
    queryKey: [kind, { term, range, page }],
    queryFn: () => meta.api.list({ search: term, from: range.from, to: range.to, page, pageSize: 25, includeVoided: true }),
    placeholderData: keepPreviousData,
  });
  const total = query.data?.items.filter((v) => v.status === 'Posted').reduce((s, v) => s + v.amount, 0) ?? 0;

  return (
    <>
      <PageHeader title={meta.title} />
      <div className="mb-3 space-y-3">
        <SearchInput value={search} onChange={(v) => { setSearch(v); setPage(1); }} placeholder={`رقم السند أو اسم ${meta.party} أو المرجع`} />
        <DateRangeFilter value={range} onChange={(r) => { setRange(r); setPage(1); }} />
      </div>
      {query.isPending ? <Spinner /> : query.isError ? <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} /> :
        query.data.items.length === 0 ? <EmptyState icon={<meta.icon className="size-10" />} title="لا توجد سندات في هذه الفترة" /> : (
          <>
            <p className="mb-2 text-sm text-muted">مجموع هذه الصفحة: <b className="tabular">{formatMoney(total)}</b></p>
            <ul className={cn('grid gap-2 md:grid-cols-2', query.isPlaceholderData && 'opacity-60')}>
              {query.data.items.map((v) => (
                <li key={v.id}>
                  <Link to={`${meta.path}/${v.id}`} className="block rounded-card border border-line bg-surface p-4 hover:border-brand-200">
                    <div className="flex items-start justify-between gap-2">
                      <div className="min-w-0">
                        <p className="truncate font-semibold">{v.partyName}{v.attachmentFileId && <Paperclip className="ms-1 inline size-4 text-muted" />}</p>
                        <p className="text-xs text-muted"><span className="ltr inline-block">{v.number}</span> · {formatDateTime(v.date)}</p>
                        <p className="text-xs text-muted">{v.expenseCategoryName ?? v.purposeName} · {v.accountName}</p>
                      </div>
                      <span className={cn('tabular shrink-0 text-lg font-bold', v.status === 'Voided' && 'text-muted line-through')}>{formatMoney(v.amount)}</span>
                    </div>
                    {v.status === 'Voided' && <div className="mt-1"><Badge tone="danger">ملغى</Badge></div>}
                  </Link>
                </li>
              ))}
            </ul>
            <Pagination page={query.data.page} totalPages={query.data.totalPages} totalCount={query.data.totalCount} pageSize={25} onChange={setPage} />
          </>
        )}
      {can(P.VouchersCreate) && (
        <Button size="lg" onClick={() => navigate(`${meta.path}/new`)} className="fixed bottom-fab end-4 z-30 rounded-full shadow-lg lg:static lg:mt-4">
          <Plus className="size-5" /> {meta.one} جديد
        </Button>
      )}
    </>
  );
}

// ------------------------------------------------------------------ create

export function NewVoucherPage({ kind }: { kind: Kind }) {
  const meta = KIND[kind];
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const toast = useToast();
  const confirm = useConfirm();
  const queryClient = useQueryClient();
  const lookups = useLookups();
  const [key] = useIdempotencyKey();
  const presetCustomer = params.get('customerId') ? Number(params.get('customerId')) : undefined;
  const presetSupplier = params.get('supplierId') ? Number(params.get('supplierId')) : undefined;

  const [purpose, setPurpose] = useState<'party' | 'other'>('party');
  const [customer, setCustomer] = useState<CustomerListItem | null>(null);
  const [supplier, setSupplier] = useState<SupplierListItem | null>(null);
  const [name, setName] = useState('');
  const [phone, setPhone] = useState('');
  const [amount, setAmount] = useState('');
  const [accountId, setAccountId] = useState<number | null>(null);
  const [reference, setReference] = useState('');
  const [notes, setNotes] = useState('');
  const [attachment, setAttachment] = useState<string | null>(null);
  const [errors, setErrors] = useState<Record<string, string>>({});
  useUnsavedChangesWarning(amount !== '');

  // Preselect the customer/supplier when opened from their page.
  useQuery({
    queryKey: ['voucher-preset', presetCustomer, presetSupplier],
    enabled: !!(presetCustomer || presetSupplier),
    queryFn: async () => {
      if (presetCustomer) {
        const c = await customersApi.get(presetCustomer);
        setCustomer(c);
        if (c.balance > 0) setAmount(String(c.balance));
      } else if (presetSupplier) {
        const s = await suppliersApi.get(presetSupplier);
        setSupplier(s);
      }
      return true;
    },
  });

  const party = kind === 'receipt' ? customer : supplier;
  const partyBalance = party?.balance ?? 0;
  const accounts = lookups.data?.accounts ?? [];

  const save = useMutation({
    mutationFn: (confirmDuplicate: boolean) => {
      const base = {
        idempotencyKey: key, amount: parseNumber(amount)!, financialAccountId: accountId!, reference: reference.trim() || null,
        notes: notes.trim() || null, attachmentFileId: attachment, confirmDuplicate,
      };
      return kind === 'receipt'
        ? receiptsApi.create({ ...base, purpose: (purpose === 'party' ? 'DebtCollection' : 'OtherIncome') as ReceiptPurpose, customerId: purpose === 'party' ? customer?.id : null, payerName: purpose === 'other' ? name.trim() : null, payerPhone: phone.trim() || null })
        : paymentsApi.create({ ...base, purpose: (purpose === 'party' ? 'SupplierPayment' : 'Other') as PaymentPurpose, supplierId: purpose === 'party' ? supplier?.id : null, beneficiaryName: purpose === 'other' ? name.trim() : null });
    },
    onSuccess: (v) => {
      for (const k of [kind, 'accounts', 'customers', 'customer', 'suppliers', 'supplier', 'sales-summary']) void queryClient.invalidateQueries({ queryKey: [k] });
      toast.success(`تم حفظ ${meta.one} ${v.number}`);
      navigate(`${meta.path}/${v.id}`, { replace: true });
    },
    onError: async (e) => {
      // Same payer, amount and account a few minutes ago: ask before recording what may be a double entry.
      if (e instanceof ApiError && e.status === 409) {
        if (await confirm({ title: 'سند مشابه مسجّل', message: e.message, confirmLabel: 'نعم، دفعة جديدة' })) save.mutate(true);
        return;
      }
      setErrors({ form: errorMessage(e) });
    },
  });

  const submit = () => {
    const e: Record<string, string> = {};
    if (purpose === 'party' && !party) e.party = kind === 'receipt' ? 'اختر العميل' : 'اختر المورد';
    if (purpose === 'other' && !name.trim()) e.name = `اسم ${meta.party} مطلوب`;
    const n = parseNumber(amount);
    if (!n || Number.isNaN(n) || n <= 0) e.amount = 'أدخل مبلغًا صحيحًا';
    else if (purpose === 'party' && party && n > partyBalance + 0.009)
      e.amount = partyBalance > 0 ? `المبلغ أكبر من ${kind === 'receipt' ? 'دين العميل' : 'المستحق للمورد'} (${formatMoney(partyBalance)})` : 'لا يوجد رصيد مستحق';
    if (!accountId) e.account = 'اختر الحساب';
    setErrors(e);
    if (Object.keys(e).length === 0) save.mutate(false);
  };

  const n = parseNumber(amount);

  return (
    <div className="mx-auto max-w-2xl space-y-4 pb-[calc(4rem+env(safe-area-inset-bottom))] lg:pb-0">
      <button type="button" onClick={() => navigate(-1)} className="flex h-10 items-center gap-1 text-sm text-muted"><ArrowRight className="size-4" /> رجوع</button>
      <PageHeader title={`${meta.one} جديد`} />
      {errors.form && <p role="alert" className="rounded-xl bg-red-50 p-3 text-sm text-danger">{errors.form}</p>}

      <Card className="space-y-4 p-5">
        <div className="grid grid-cols-2 rounded-xl bg-black/5 p-1">
          {(['party', 'other'] as const).map((p) => (
            <button key={p} type="button" aria-pressed={purpose === p} onClick={() => setPurpose(p)}
              className={cn('h-10 rounded-lg text-sm font-semibold', purpose === p ? 'bg-surface shadow-sm' : 'text-muted')}>
              {p === 'party' ? (kind === 'receipt' ? 'تحصيل دين من عميل' : 'دفعة لمورد') : kind === 'receipt' ? 'إيراد آخر' : 'صرف آخر'}
            </button>
          ))}
        </div>

        {purpose === 'party' ? (
          kind === 'receipt' ? (
            <Field label="العميل" required error={errors.party}>
              {(id) => (
                <Picker<CustomerListItem> id={id} invalid={!!errors.party} valueLabel={customer ? customer.name : null} placeholder="ابحث بالاسم أو الجوال" title="اختيار العميل"
                  queryKey="receipt-customers" search={async (q, signal) => (await customersApi.list({ search: q, pageSize: 20, withBalance: true }, signal)).items}
                  getKey={(c) => c.id} renderItem={(c) => <span className="flex justify-between gap-2"><span>{c.name} <span className="ltr text-xs text-muted">{c.phone}</span></span><span className="tabular text-danger">{formatMoney(c.balance)}</span></span>}
                  onSelect={(c) => { setCustomer(c); if (!amount) setAmount(String(c.balance)); }} onClear={() => setCustomer(null)} />
              )}
            </Field>
          ) : (
            <Field label="المورد" required error={errors.party}>
              {(id) => (
                <Picker<SupplierListItem> id={id} invalid={!!errors.party} valueLabel={supplier ? supplier.name : null} placeholder="ابحث باسم المورد" title="اختيار المورد"
                  queryKey="payment-suppliers" search={async (q, signal) => (await suppliersApi.list({ search: q, pageSize: 20, withBalance: true }, signal)).items}
                  getKey={(s) => s.id} renderItem={(s) => <span className="flex justify-between gap-2"><span>{s.name}</span><span className="tabular text-danger">{formatMoney(s.balance)}</span></span>}
                  onSelect={setSupplier} onClear={() => setSupplier(null)} />
              )}
            </Field>
          )
        ) : (
          <div className="grid grid-cols-2 gap-3">
            <Field label={`اسم ${meta.party}`} required error={errors.name}>{(id) => <Input id={id} value={name} invalid={!!errors.name} onChange={(e) => setName(e.target.value)} />}</Field>
            {kind === 'receipt' && <Field label="الجوال">{(id) => <Input id={id} type="tel" inputMode="tel" className="ltr" value={phone} onChange={(e) => setPhone(e.target.value)} />}</Field>}
          </div>
        )}
        {purpose === 'party' && party && (
          <p className="rounded-xl bg-canvas p-3 text-sm">
            {kind === 'receipt' ? 'الدين الحالي على العميل' : 'المستحق للمورد'}: <b className="tabular">{formatMoney(partyBalance)}</b>
            {partyBalance > 0 && <button type="button" className="ms-2 font-semibold text-brand-700 underline" onClick={() => setAmount(String(partyBalance))}>المبلغ كاملًا</button>}
          </p>
        )}

        <Field label="المبلغ (₪)" required error={errors.amount} hint={n && !Number.isNaN(n) ? amountInWords(n) : undefined}>
          {(id, h) => <Input id={id} aria-describedby={h} inputMode="decimal" className="ltr text-lg" invalid={!!errors.amount} value={amount} onChange={(e) => setAmount(e.target.value)} />}
        </Field>
        <AccountChips label={kind === 'receipt' ? 'الحساب المستلم' : 'يُدفع من حساب'} accounts={accounts} value={accountId} onChange={setAccountId} error={errors.account} />
        <div className="grid grid-cols-2 gap-3">
          <Field label="المرجع" hint="رقم حوالة/شيك">{(id, h) => <Input id={id} aria-describedby={h} className="ltr" value={reference} onChange={(e) => setReference(e.target.value)} />}</Field>
          <Field label="البيان / الملاحظات">{(id) => <Input id={id} value={notes} onChange={(e) => setNotes(e.target.value)} />}</Field>
        </div>
        <AttachmentPicker label="مرفق (اختياري)" purpose="VoucherAttachment" value={attachment} onChange={setAttachment} />
      </Card>

      <StickyActions>
        <Button block size="lg" loading={save.isPending} onClick={submit}>حفظ {meta.one}</Button>
      </StickyActions>
    </div>
  );
}

// ------------------------------------------------------------------ detail

export function VoucherDetailPage({ kind }: { kind: Kind }) {
  const meta = KIND[kind];
  const id = Number(useParams().id);
  const { can } = useAuth();
  const navigate = useNavigate();
  const toast = useToast();
  const queryClient = useQueryClient();
  const settings = useStoreSettings();
  const [voiding, setVoiding] = useState(false);
  const [reason, setReason] = useState('');
  const query = useQuery({ queryKey: [kind, 'one', id], queryFn: () => meta.api.get(id) });
  const voidIt = useMutation({
    mutationFn: () => meta.api.void(id, reason.trim()),
    onSuccess: (v: Voucher) => {
      queryClient.setQueryData([kind, 'one', id], v);
      for (const k of [kind, 'accounts', 'customers', 'customer', 'suppliers', 'supplier']) void queryClient.invalidateQueries({ queryKey: [k] });
      toast.success('تم إلغاء السند وعكس أثره');
      setVoiding(false);
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  if (query.isPending) return <Spinner />;
  if (query.isError) return <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} />;
  const v = query.data;
  const canVoid = can(P.VouchersVoid) && v.status === 'Posted' && !v.incomingTransferId && v.purpose !== 'Expense';

  return (
    <div className="mx-auto max-w-2xl">
      <div className="space-y-4 print:hidden">
        <button type="button" onClick={() => navigate(meta.path)} className="flex h-10 items-center gap-1 text-sm text-muted"><ArrowRight className="size-4" /> {meta.title}</button>
        <div className="flex items-start justify-between gap-2">
          <div>
            <p className="text-sm text-muted">{meta.one}</p>
            <h1 className="ltr text-xl font-bold">{v.number}</h1>
          </div>
          {v.status === 'Voided' ? <Badge tone="danger">ملغى</Badge> : <Badge tone="success">معتمد</Badge>}
        </div>
        {v.status === 'Voided' && <p className="rounded-xl bg-red-50 p-3 text-sm text-danger">أُلغي في {formatDateTime(v.voidedAt)} — {v.voidReason}</p>}
        <Card className="p-5 text-center">
          <p className="tabular text-3xl font-bold">{formatMoney(v.amount)}</p>
          <p className="mt-1 text-sm text-muted">{amountInWords(v.amount)}</p>
        </Card>
        <Card className="px-4">
          <InfoList items={[
            { label: meta.party, value: v.customerId ? <Link to={`/customers/${v.customerId}`} className="text-brand-700 underline">{v.partyName}</Link> : v.supplierId ? <Link to={`/suppliers/${v.supplierId}`} className="text-brand-700 underline">{v.partyName}</Link> : v.partyName },
            v.partyPhone && { label: 'الجوال', value: <span className="ltr">{v.partyPhone}</span> },
            { label: 'النوع', value: v.expenseCategoryName ? `مصروف — ${v.expenseCategoryName}` : v.purposeName },
            { label: kind === 'receipt' ? 'الحساب المستلم' : 'دُفع من', value: v.accountName },
            v.reference && { label: 'المرجع', value: <span className="ltr">{v.reference}</span> },
            v.notes && { label: 'البيان', value: v.notes },
            { label: 'التاريخ', value: formatDateTime(v.date) },
            { label: 'المستخدم', value: v.createdByName },
            !!v.saleInvoiceId && { label: 'الفاتورة', value: <Link to={`/sales/${v.saleInvoiceId}`} className="text-brand-700 underline">عرض الفاتورة</Link> },
          ]} />
        </Card>
        {v.attachmentFileId && <PrivateFileThumb fileId={v.attachmentFileId} large />}
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" onClick={() => window.print()}><Printer className="size-4" /> طباعة</Button>
          {canVoid && <Button variant="outline" onClick={() => setVoiding(true)}><Ban className="size-4 text-danger" /> إلغاء السند</Button>}
        </div>
      </div>
      <PrintableVoucher kind={kind} voucher={v} store={settings.data} />
      {voiding && (
        <Sheet open onClose={() => setVoiding(false)} title={`إلغاء ${v.number}`}
          footer={<Button block size="lg" variant="danger" loading={voidIt.isPending} disabled={!reason.trim()} onClick={() => voidIt.mutate()}>تأكيد الإلغاء</Button>}>
          <div className="space-y-3">
            <p className="text-sm leading-6 text-muted">سيُعكس أثر السند على الحساب{v.customerId ? ' ودين العميل' : v.supplierId ? ' والمستحق للمورد' : ''}، ويبقى ظاهرًا بحالة «ملغى».</p>
            <Field label="سبب الإلغاء" required>{(id) => <TextArea id={id} rows={2} value={reason} onChange={(e) => setReason(e.target.value)} />}</Field>
          </div>
        </Sheet>
      )}
    </div>
  );
}
