import { useState } from 'react';
import { Link } from 'react-router';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeftRight, CheckCircle2, Paperclip, Plus, XCircle } from 'lucide-react';
import { transfersApi, type IncomingTransfer, type TransferStatus } from '@/api/finance';
import { customersApi, type CustomerListItem } from '@/api/sales';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button } from '@/components/ui/Button';
import { Field, Input, TextArea } from '@/components/ui/Field';
import { Badge, EmptyState, ErrorState, PageHeader, Pagination, SearchInput, Spinner } from '@/components/ui/Feedback';
import { InfoList, Tabs } from '@/components/ui/Layout';
import { Picker } from '@/components/ui/Picker';
import { Sheet } from '@/components/ui/Sheet';
import { useConfirm } from '@/components/ui/Confirm';
import { useToast } from '@/components/ui/Toast';
import { AccountChips, AttachmentPicker, PrivateFileThumb } from '@/components/finance/FinanceBits';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';
import { formatDate, formatDateTime, formatMoney, localToday } from '@/lib/format';
import { useDebouncedValue, useIdempotencyKey, useUnsavedChangesWarning } from '@/lib/hooks';
import { parseNumber } from '@/lib/numbers';
import { useLookups } from '@/lib/queries';

const STATUS: Record<TransferStatus, { label: string; tone: 'warning' | 'success' | 'danger' }> = {
  Pending: { label: 'بانتظار التحقق', tone: 'warning' },
  Confirmed: { label: 'مؤكدة', tone: 'success' },
  Rejected: { label: 'مرفوضة', tone: 'danger' },
};

export function TransfersPage() {
  const { can } = useAuth();
  const [tab, setTab] = useState<TransferStatus>('Pending');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [creating, setCreating] = useState(false);
  const [open, setOpen] = useState<IncomingTransfer | null>(null);
  const term = useDebouncedValue(search.trim());
  const query = useQuery({
    queryKey: ['transfers', { tab, term, page }],
    queryFn: () => transfersApi.list({ status: tab, search: term, page, pageSize: 25 }),
    placeholderData: keepPreviousData,
  });
  const pendingCount = useQuery({ queryKey: ['transfers', 'count'], queryFn: () => transfersApi.list({ status: 'Pending', pageSize: 1 }) });

  return (
    <>
      <PageHeader title="الحوالات" subtitle="الحوالات البنكية والمحافظ الواردة — لا تُحسب أموالًا قبل التأكيد" />
      <Tabs value={tab} onChange={(t) => { setTab(t); setPage(1); }}
        tabs={[{ value: 'Pending', label: 'بانتظار التحقق', badge: pendingCount.data?.totalCount }, { value: 'Confirmed', label: 'المؤكدة' }, { value: 'Rejected', label: 'المرفوضة' }]} />
      <div className="mb-3"><SearchInput value={search} onChange={(v) => { setSearch(v); setPage(1); }} placeholder="اسم صاحب الحوالة أو رقم المرجع أو الجوال" /></div>

      {query.isPending ? <Spinner /> : query.isError ? <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} /> :
        query.data.items.length === 0 ? <EmptyState icon={<ArrowLeftRight className="size-10" />} title={tab === 'Pending' ? 'لا توجد حوالات بانتظار التحقق' : 'لا توجد حوالات'} /> : (
          <>
            <ul className={cn('grid gap-2 md:grid-cols-2', query.isPlaceholderData && 'opacity-60')}>
              {query.data.items.map((t) => (
                <li key={t.id}>
                  <button type="button" onClick={() => setOpen(t)} className="block w-full rounded-card border border-line bg-surface p-4 text-start hover:border-brand-200">
                    <div className="flex items-start justify-between gap-2">
                      <div className="min-w-0">
                        <p className="truncate font-semibold">{t.senderName}{t.proofFileId && <Paperclip className="ms-1 inline size-4 text-muted" />}</p>
                        <p className="text-xs text-muted">{t.accountName}{t.referenceNo && <> · <span className="ltr inline-block">{t.referenceNo}</span></>} · {formatDate(t.transferDate)}</p>
                        {t.customerName && <p className="text-xs text-muted">العميل: {t.customerName}{t.saleInvoiceNumber && ` · فاتورة ${t.saleInvoiceNumber}`}</p>}
                      </div>
                      <span className="tabular shrink-0 text-lg font-bold">{formatMoney(t.amount)}</span>
                    </div>
                    <div className="mt-2"><Badge tone={STATUS[t.status].tone}>{STATUS[t.status].label}</Badge></div>
                  </button>
                </li>
              ))}
            </ul>
            <Pagination page={query.data.page} totalPages={query.data.totalPages} onChange={setPage} />
          </>
        )}

      {can(P.TransfersCreate) && (
        <Button size="lg" onClick={() => setCreating(true)} className="fixed bottom-fab end-4 z-30 rounded-full shadow-lg lg:static lg:mt-4">
          <Plus className="size-5" /> تسجيل حوالة
        </Button>
      )}
      {creating && <NewTransferSheet onClose={() => setCreating(false)} />}
      {open && <TransferSheet transfer={open} onClose={() => setOpen(null)} />}
    </>
  );
}

function NewTransferSheet({ onClose }: { onClose: () => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const lookups = useLookups();
  const [key] = useIdempotencyKey();
  const accounts = (lookups.data?.accounts ?? []).filter((a) => a.type !== 'Cash');
  const [sender, setSender] = useState('');
  const [phone, setPhone] = useState('');
  const [amount, setAmount] = useState('');
  const [channel, setChannel] = useState('');
  const [accountId, setAccountId] = useState<number | null>(null);
  const [reference, setReference] = useState('');
  const [date, setDate] = useState(localToday());
  const [proof, setProof] = useState<string | null>(null);
  const [customer, setCustomer] = useState<CustomerListItem | null>(null);
  const [notes, setNotes] = useState('');
  const [errors, setErrors] = useState<Record<string, string>>({});
  useUnsavedChangesWarning(sender !== '' || amount !== '');

  const save = useMutation({
    mutationFn: () => transfersApi.create({
      idempotencyKey: key, senderName: sender.trim(), senderPhone: phone.trim() || null, amount: parseNumber(amount)!, channel: channel.trim() || null,
      financialAccountId: accountId!, referenceNo: reference.trim() || null, transferDate: date, proofFileId: proof, customerId: customer?.id ?? null, notes: notes.trim() || null,
    }),
    onSuccess: (t) => {
      void queryClient.invalidateQueries({ queryKey: ['transfers'] });
      void queryClient.invalidateQueries({ queryKey: ['notifications'] });
      void queryClient.invalidateQueries({ queryKey: ['accounts'] });
      toast.success(`سُجّلت الحوالة ${t.number} بانتظار التحقق`);
      onClose();
    },
    onError: (e) => setErrors({ form: errorMessage(e) }),
  });

  const submit = () => {
    const e: Record<string, string> = {};
    if (!sender.trim()) e.sender = 'اسم صاحب الحوالة مطلوب';
    const n = parseNumber(amount);
    if (!n || Number.isNaN(n) || n <= 0) e.amount = 'أدخل مبلغًا صحيحًا';
    if (!accountId) e.account = 'اختر الحساب المستلم';
    setErrors(e);
    if (Object.keys(e).length === 0) save.mutate();
  };

  return (
    <Sheet open onClose={onClose} title="تسجيل حوالة واردة" size="lg"
      footer={<Button block size="lg" loading={save.isPending} onClick={submit}>حفظ الحوالة</Button>}>
      <div className="space-y-4">
        {errors.form && <p role="alert" className="rounded-xl bg-red-50 p-3 text-sm text-danger">{errors.form}</p>}
        <div className="grid grid-cols-2 gap-3">
          <Field label="اسم صاحب الحوالة" required error={errors.sender}>{(id) => <Input id={id} value={sender} onChange={(e) => setSender(e.target.value)} invalid={!!errors.sender} />}</Field>
          <Field label="رقم جوال المرسل">{(id) => <Input id={id} type="tel" inputMode="tel" className="ltr" value={phone} onChange={(e) => setPhone(e.target.value)} />}</Field>
        </div>
        <Field label="المبلغ (₪)" required error={errors.amount}>{(id) => <Input id={id} inputMode="decimal" className="ltr text-lg" value={amount} onChange={(e) => setAmount(e.target.value)} invalid={!!errors.amount} />}</Field>
        <AccountChips label="الحساب المستلم" accounts={accounts} value={accountId} onChange={setAccountId} error={errors.account} />
        <div className="grid grid-cols-2 gap-3">
          <Field label="رقم مرجع الحوالة">{(id) => <Input id={id} className="ltr" value={reference} onChange={(e) => setReference(e.target.value)} />}</Field>
          <Field label="تاريخ التحويل">{(id) => <Input id={id} type="date" max={localToday()} value={date} onChange={(e) => setDate(e.target.value)} />}</Field>
        </div>
        <Field label="الجهة التي حُوّل منها" hint="مثال: بنك القدس، جوال بي">{(id, h) => <Input id={id} aria-describedby={h} value={channel} onChange={(e) => setChannel(e.target.value)} />}</Field>
        <AttachmentPicker label="صورة إثبات التحويل" purpose="TransferProof" value={proof} onChange={setProof} />
        <Field label="العميل (إذا كانت تسديدًا لدين)">
          {(id) => (
            <Picker<CustomerListItem> id={id} valueLabel={customer ? `${customer.name}${customer.balance > 0 ? ` — عليه ${formatMoney(customer.balance)}` : ''}` : null}
              placeholder="اختياري" title="اختيار العميل" queryKey="transfer-customers"
              search={async (q, signal) => (await customersApi.list({ search: q, pageSize: 20 }, signal)).items.filter((c) => !c.isWalkIn)}
              getKey={(c) => c.id} renderItem={(c) => <span className="flex justify-between gap-2"><span>{c.name}</span>{c.balance > 0 && <span className="tabular text-warning">{formatMoney(c.balance)}</span>}</span>}
              onSelect={setCustomer} onClear={() => setCustomer(null)} />
          )}
        </Field>
        <Field label="ملاحظات">{(id) => <TextArea id={id} rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} />}</Field>
      </div>
    </Sheet>
  );
}

function TransferSheet({ transfer: t, onClose }: { transfer: IncomingTransfer; onClose: () => void }) {
  const { can } = useAuth();
  const toast = useToast();
  const confirm = useConfirm();
  const queryClient = useQueryClient();
  const [rejecting, setRejecting] = useState(false);
  const [reason, setReason] = useState('');
  const done = (msg: string) => {
    for (const k of ['transfers', 'notifications', 'accounts', 'account', 'customers', 'customer', 'sale']) void queryClient.invalidateQueries({ queryKey: [k] });
    toast.success(msg);
    onClose();
  };
  const approve = useMutation({ mutationFn: () => transfersApi.confirm(t.id), onSuccess: (r) => done(`تم تأكيد الحوالة وإصدار سند القبض ${r.receiptVoucherNumber}`), onError: (e) => toast.error(errorMessage(e)) });
  const reject = useMutation({ mutationFn: () => transfersApi.reject(t.id, reason.trim()), onSuccess: () => done('تم رفض الحوالة'), onError: (e) => toast.error(errorMessage(e)) });
  const reviewer = can(P.TransfersReview) && t.status === 'Pending';

  return (
    <Sheet open onClose={onClose} title={`حوالة ${t.number}`} size="lg"
      footer={reviewer && (rejecting ? (
        <div className="space-y-2">
          <TextArea aria-label="سبب الرفض" rows={2} placeholder="سبب الرفض (مطلوب)" value={reason} onChange={(e) => setReason(e.target.value)} />
          <div className="flex gap-2">
            <Button block variant="danger" loading={reject.isPending} disabled={!reason.trim()} onClick={() => reject.mutate()}>تأكيد الرفض</Button>
            <Button variant="outline" onClick={() => setRejecting(false)}>تراجع</Button>
          </div>
        </div>
      ) : (
        <div className="flex gap-2">
          <Button block size="lg" loading={approve.isPending} onClick={async () => {
            if (await confirm({ title: 'تأكيد الحوالة', message: `تأكدت أن ${formatMoney(t.amount)} وصلت فعلًا إلى «${t.accountName}»؟ سيُضاف المبلغ للرصيد${t.customerName ? ` ويُخصم من دين ${t.customerName}` : ''}.`, confirmLabel: 'نعم، وصلت' }))
              approve.mutate();
          }}><CheckCircle2 className="size-5" /> وصلت — تأكيد</Button>
          <Button size="lg" variant="outline" onClick={() => setRejecting(true)}><XCircle className="size-5 text-danger" /> رفض</Button>
        </div>
      ))}>
      <div className="space-y-4">
        <div className="flex items-center justify-between">
          <span className="tabular text-2xl font-bold">{formatMoney(t.amount)}</span>
          <Badge tone={STATUS[t.status].tone}>{STATUS[t.status].label}</Badge>
        </div>
        {t.proofFileId ? <PrivateFileThumb fileId={t.proofFileId} large /> : <p className="rounded-xl bg-canvas p-3 text-sm text-muted">لا توجد صورة إثبات</p>}
        <InfoList items={[
          { label: 'صاحب الحوالة', value: t.senderName },
          t.senderPhone && { label: 'الجوال', value: <span className="ltr">{t.senderPhone}</span> },
          { label: 'الحساب المستلم', value: t.accountName },
          t.channel && { label: 'الجهة', value: t.channel },
          t.referenceNo && { label: 'رقم المرجع', value: <span className="ltr">{t.referenceNo}</span> },
          { label: 'تاريخ التحويل', value: formatDate(t.transferDate) },
          t.customerName && { label: 'العميل', value: <Link to={`/customers/${t.customerId}`} className="text-brand-700 underline">{t.customerName}</Link> },
          t.saleInvoiceNumber && { label: 'الفاتورة', value: <Link to={`/sales/${t.saleInvoiceId}`} className="ltr text-brand-700 underline">{t.saleInvoiceNumber}</Link> },
          { label: 'سجّلها', value: `${t.recordedByName ?? ''} — ${formatDateTime(t.createdAt)}` },
          t.reviewedByName && { label: t.status === 'Confirmed' ? 'أكّدها' : 'رفضها', value: `${t.reviewedByName} — ${formatDateTime(t.reviewedAt)}` },
          t.rejectReason && { label: 'سبب الرفض', value: t.rejectReason },
          t.receiptVoucherNumber && { label: 'سند القبض', value: <Link to={`/receipts/${t.receiptVoucherId}`} className="ltr text-brand-700 underline">{t.receiptVoucherNumber}</Link> },
          t.notes && { label: 'ملاحظات', value: t.notes },
        ]} />
      </div>
    </Sheet>
  );
}
