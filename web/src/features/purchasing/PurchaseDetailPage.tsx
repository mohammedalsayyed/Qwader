import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowRight, Ban, CheckCircle2, Pencil, Printer, Undo2 } from 'lucide-react';
import { purchasesApi, type Purchase, type ReturnSettlement } from '@/api/stock';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button } from '@/components/ui/Button';
import { Field, Input, Select, TextArea } from '@/components/ui/Field';
import { Card, ErrorState, Spinner } from '@/components/ui/Feedback';
import { InfoList } from '@/components/ui/Layout';
import { Sheet } from '@/components/ui/Sheet';
import { useConfirm } from '@/components/ui/Confirm';
import { useToast } from '@/components/ui/Toast';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';
import { formatDate, formatDateTime, formatMoney, formatQuantity } from '@/lib/format';
import { useIdempotencyKey } from '@/lib/hooks';
import { parseNumber, round2 } from '@/lib/numbers';
import { useLookups, useStoreSettings } from '@/lib/queries';
import { PurchaseStatusBadge } from './PurchasesPage';

export function PurchaseDetailPage() {
  const id = Number(useParams().id);
  const { can } = useAuth();
  const navigate = useNavigate();
  const toast = useToast();
  const confirm = useConfirm();
  const queryClient = useQueryClient();
  const settings = useStoreSettings();
  const [postKey] = useIdempotencyKey();
  const [returning, setReturning] = useState(false);
  const [voiding, setVoiding] = useState(false);
  const query = useQuery({ queryKey: ['purchase', id], queryFn: () => purchasesApi.get(id) });

  const refresh = (p: Purchase) => {
    queryClient.setQueryData(['purchase', id], p);
    for (const k of ['purchases', 'products', 'suppliers', 'notifications', 'inventory-summary']) void queryClient.invalidateQueries({ queryKey: [k] });
    void queryClient.invalidateQueries({ queryKey: ['supplier', p.supplierId] });
  };

  const post = useMutation({
    mutationFn: () => purchasesApi.post(id, postKey),
    onSuccess: (p) => { refresh(p); toast.success('تم اعتماد الفاتورة وإضافة البضاعة للمخزون'); },
    onError: (e) => toast.error(errorMessage(e)),
  });

  if (query.isPending) return <Spinner />;
  if (query.isError) return <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} />;
  const p = query.data;
  const manage = can(P.PurchasesManage);
  const returnable = p.lines.some((l) => l.quantity > l.returnedQuantity);

  return (
    <div className="mx-auto max-w-3xl space-y-4">
      <button type="button" onClick={() => navigate(-1)} className="flex h-10 items-center gap-1 text-sm text-muted print:hidden">
        <ArrowRight className="size-4" /> رجوع
      </button>

      {/* Printed header (A4) */}
      <div className="hidden border-b border-line pb-3 print:block">
        <p className="text-xl font-bold">{settings.data?.storeName}</p>
        <p className="text-sm">{[settings.data?.storeAddress, settings.data?.storePhone].filter(Boolean).join(' · ')}</p>
        <p className="mt-2 text-lg font-bold">فاتورة شراء</p>
      </div>

      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <h1 className="ltr text-xl font-bold">{p.number}</h1>
          <p className="text-sm text-muted">{formatDate(p.date)} · <Link to={`/suppliers/${p.supplierId}`} className="text-brand-700 underline print:no-underline">{p.supplierName}</Link></p>
        </div>
        <PurchaseStatusBadge status={p.status} />
      </div>

      {p.status === 'Voided' && (
        <p className="rounded-xl bg-red-50 p-3 text-sm text-danger">ألغيت في {formatDateTime(p.voidedAt)} — السبب: {p.voidReason}</p>
      )}

      {manage && (
        <div className="flex flex-wrap gap-2 print:hidden">
          {p.status === 'Draft' && (
            <>
              <Button loading={post.isPending} onClick={async () => {
                if (await confirm({ title: 'اعتماد الفاتورة', message: `ستُضاف البضاعة إلى المخزون ويُسجَّل ${formatMoney(p.total - p.paidAmount)} مستحقًا للمورد.`, confirmLabel: 'اعتماد' })) post.mutate();
              }}><CheckCircle2 className="size-4" /> اعتماد</Button>
              <Link to={`/purchases/${p.id}/edit`} className="contents"><Button variant="outline"><Pencil className="size-4" /> تعديل</Button></Link>
            </>
          )}
          {p.status === 'Posted' && returnable && (
            <Button variant="secondary" onClick={() => setReturning(true)}><Undo2 className="size-4" /> مردود للمورد</Button>
          )}
          {p.status !== 'Voided' && (
            <Button variant="outline" onClick={() => setVoiding(true)}><Ban className="size-4 text-danger" /> إلغاء الفاتورة</Button>
          )}
          <Button variant="outline" onClick={() => window.print()}><Printer className="size-4" /> طباعة</Button>
        </div>
      )}

      <Card className="overflow-hidden">
        <ul className="divide-y divide-line">
          {p.lines.map((l) => (
            <li key={l.id} className="p-3 break-inside-avoid">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <p className="font-semibold">{l.productName}</p>
                  <p className="text-xs text-muted">
                    <span className="ltr inline-block">{l.sku}</span> · <span className="tabular">{formatQuantity(l.quantity)}</span> {l.unitName} × {formatMoney(l.unitCost)}
                    {l.lineDiscount > 0 && <> − خصم {formatMoney(l.lineDiscount)}</>}
                  </p>
                  {l.returnedQuantity > 0 && <p className="text-xs text-warning">مرتجع: <span className="tabular">{formatQuantity(l.returnedQuantity)}</span></p>}
                </div>
                <p className="tabular shrink-0 font-bold">{formatMoney(l.lineTotal)}</p>
              </div>
            </li>
          ))}
        </ul>
        <div className="border-t border-line bg-canvas px-4">
          <InfoList items={[
            p.discount > 0 && { label: 'المجموع قبل الخصم', value: formatMoney(p.subtotal) },
            p.discount > 0 && { label: 'خصم الفاتورة', value: formatMoney(p.discount) },
            { label: 'الإجمالي', value: <b className="tabular text-lg">{formatMoney(p.total)}</b> },
            { label: 'المدفوع', value: <span className="tabular">{formatMoney(p.paidAmount)}{p.paymentAccountName && ` (${p.paymentAccountName})`}</span> },
            { label: 'المتبقي عند الشراء', value: <span className={cn('tabular', p.remaining > 0 && 'text-danger')}>{formatMoney(p.remaining)}</span> },
          ]} />
        </div>
      </Card>

      {p.returns.length > 0 && (
        <Card className="p-4">
          <h2 className="mb-2 font-bold">المردودات</h2>
          <ul className="divide-y divide-line">
            {p.returns.map((r) => (
              <li key={r.id} className="flex items-center justify-between gap-2 py-2">
                <div>
                  <p className="ltr text-sm font-semibold">{r.number}</p>
                  <p className="text-xs text-muted">{formatDate(r.date)} · {r.settlement === 'Refund' ? 'استرداد نقدي' : 'خصم من المستحق'}{r.reason && ` · ${r.reason}`}</p>
                </div>
                <span className="tabular font-bold">{formatMoney(r.total)}</span>
              </li>
            ))}
          </ul>
        </Card>
      )}

      <Card className="px-4 print:hidden">
        <InfoList items={[
          p.supplierInvoiceNo && { label: 'رقم فاتورة المورد', value: <span className="ltr">{p.supplierInvoiceNo}</span> },
          { label: 'المستودع', value: p.warehouseName },
          { label: 'أنشأها', value: p.createdByName },
          { label: 'تاريخ الإنشاء', value: formatDateTime(p.createdAt) },
          p.postedAt && { label: 'تاريخ الاعتماد', value: formatDateTime(p.postedAt) },
          p.notes && { label: 'ملاحظات', value: p.notes },
        ]} />
      </Card>

      {returning && <ReturnSheet purchase={p} onClose={() => setReturning(false)} onDone={refresh} />}
      {voiding && <VoidSheet purchase={p} onClose={() => setVoiding(false)} onDone={refresh} />}
    </div>
  );
}

function ReturnSheet({ purchase, onClose, onDone }: { purchase: Purchase; onClose: () => void; onDone: (p: Purchase) => void }) {
  const toast = useToast();
  const lookups = useLookups();
  const [key] = useIdempotencyKey();
  const [quantities, setQuantities] = useState<Record<number, string>>({});
  const [settlement, setSettlement] = useState<ReturnSettlement>('ReduceBalance');
  const [accountId, setAccountId] = useState('');
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string>();

  const lines = purchase.lines.filter((l) => l.quantity > l.returnedQuantity);
  const total = round2(lines.reduce((sum, l) => {
    const q = parseNumber(quantities[l.id]) ?? 0;
    return sum + (Number.isNaN(q) ? 0 : q) * l.netUnitCost;
  }, 0));

  const save = useMutation({
    mutationFn: () => purchasesApi.return(purchase.id, {
      idempotencyKey: key,
      settlement,
      refundAccountId: settlement === 'Refund' ? Number(accountId) : null,
      reason: reason.trim() || null,
      lines: lines.filter((l) => (parseNumber(quantities[l.id]) ?? 0) > 0).map((l) => ({ purchaseInvoiceLineId: l.id, quantity: parseNumber(quantities[l.id])! })),
    }),
    onSuccess: (p) => { onDone(p); toast.success('تم تسجيل المردود'); onClose(); },
    onError: (e) => toast.error(errorMessage(e)),
  });

  const submit = () => {
    for (const l of lines) {
      const q = parseNumber(quantities[l.id]);
      if (q !== undefined && (Number.isNaN(q) || q < 0 || q > l.quantity - l.returnedQuantity))
        return setError(`كمية غير صالحة لـ «${l.productName}» (الحد ${formatQuantity(l.quantity - l.returnedQuantity)})`);
    }
    if (total <= 0) return setError('حدد كمية مرتجعة واحدة على الأقل');
    if (settlement === 'Refund' && !accountId) return setError('اختر الحساب الذي استُلم فيه المبلغ');
    setError(undefined);
    save.mutate();
  };

  return (
    <Sheet open onClose={onClose} title="مردود مشتريات" size="lg"
      footer={<Button block size="lg" loading={save.isPending} onClick={submit}>تسجيل المردود ({formatMoney(total)})</Button>}>
      <div className="space-y-4">
        {error && <p role="alert" className="rounded-xl bg-red-50 p-3 text-sm text-danger">{error}</p>}
        <ul className="divide-y divide-line">
          {lines.map((l) => (
            <li key={l.id} className="flex items-center gap-3 py-2">
              <div className="min-w-0 flex-1">
                <p className="truncate font-semibold">{l.productName}</p>
                <p className="text-xs text-muted">المتاح للإرجاع: <span className="tabular">{formatQuantity(l.quantity - l.returnedQuantity)}</span> · {formatMoney(l.netUnitCost)} للوحدة</p>
              </div>
              <input aria-label={`كمية المردود: ${l.productName}`} inputMode="decimal" placeholder="0"
                className="ltr h-12 w-20 shrink-0 rounded-xl border border-line text-center"
                value={quantities[l.id] ?? ''} onChange={(e) => setQuantities((s) => ({ ...s, [l.id]: e.target.value }))} />
            </li>
          ))}
        </ul>
        <Field label="التسوية">
          {(id) => (
            <Select id={id} value={settlement} onChange={(e) => setSettlement(e.target.value as ReturnSettlement)}>
              <option value="ReduceBalance">خصم من المستحق للمورد</option>
              <option value="Refund">استرداد المبلغ نقدًا/تحويلًا</option>
            </Select>
          )}
        </Field>
        {settlement === 'Refund' && (
          <Field label="استُلم المبلغ في حساب" required>
            {(id) => (
              <Select id={id} value={accountId} onChange={(e) => setAccountId(e.target.value)}>
                <option value="">اختر الحساب…</option>
                {lookups.data?.accounts.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
              </Select>
            )}
          </Field>
        )}
        <Field label="السبب">{(id) => <Input id={id} value={reason} onChange={(e) => setReason(e.target.value)} />}</Field>
      </div>
    </Sheet>
  );
}

function VoidSheet({ purchase, onClose, onDone }: { purchase: Purchase; onClose: () => void; onDone: (p: Purchase) => void }) {
  const toast = useToast();
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string>();
  const save = useMutation({
    mutationFn: () => purchasesApi.void(purchase.id, reason.trim()),
    onSuccess: (p) => { onDone(p); toast.success('تم إلغاء الفاتورة وعكس حركاتها'); onClose(); },
    onError: (e) => toast.error(errorMessage(e)),
  });

  return (
    <Sheet open onClose={onClose} title={`إلغاء الفاتورة ${purchase.number}`}
      footer={<Button block size="lg" variant="danger" loading={save.isPending} onClick={() => (reason.trim() ? save.mutate() : setError('سبب الإلغاء مطلوب'))}>تأكيد الإلغاء</Button>}>
      <div className="space-y-3">
        <p className="text-sm leading-6 text-muted">
          {purchase.status === 'Posted'
            ? 'ستُخصم كميات الفاتورة من المخزون، ويُعكس المستحق للمورد، ويُعاد المبلغ المدفوع إلى الحساب. تبقى الفاتورة ظاهرة بحالة «ملغاة».'
            : 'المسودة لم تؤثر على المخزون أو الحسابات، وستُلغى فقط.'}
        </p>
        <Field label="سبب الإلغاء" required error={error}>
          {(id, d) => <TextArea id={id} aria-describedby={d} rows={2} value={reason} onChange={(e) => { setReason(e.target.value); setError(undefined); }} />}
        </Field>
      </div>
    </Sheet>
  );
}
