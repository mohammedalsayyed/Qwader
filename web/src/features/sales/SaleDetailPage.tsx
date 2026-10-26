import { useEffect, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowRight, Ban, Printer, Share2, Undo2 } from 'lucide-react';
import { salesApi, SALE_STATUS, type ReturnCondition, type Sale } from '@/api/sales';
import type { ReturnSettlement } from '@/api/stock';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button } from '@/components/ui/Button';
import { Field, Input, Select, TextArea } from '@/components/ui/Field';
import { Badge, Card, ErrorState, Spinner } from '@/components/ui/Feedback';
import { InfoList } from '@/components/ui/Layout';
import { Sheet } from '@/components/ui/Sheet';
import { useToast } from '@/components/ui/Toast';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';
import { formatDateTime, formatMoney, formatQuantity } from '@/lib/format';
import { useIdempotencyKey } from '@/lib/hooks';
import { parseNumber, round2 } from '@/lib/numbers';
import { amountInWords } from '@/lib/tafqeet';
import { useLookups, useStoreSettings } from '@/lib/queries';

export function SaleDetailPage() {
  const id = Number(useParams().id);
  const [params, setParams] = useSearchParams();
  const { can } = useAuth();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const settings = useStoreSettings();
  const [returning, setReturning] = useState(false);
  const [voiding, setVoiding] = useState(false);
  const query = useQuery({ queryKey: ['sale', id], queryFn: () => salesApi.get(id) });

  // «طباعة» from the till opens this page with ?print=1.
  useEffect(() => {
    if (params.get('print') === '1' && query.data && settings.data) {
      setParams({}, { replace: true });
      setTimeout(() => window.print(), 300);
    }
  }, [params, query.data, settings.data, setParams]);

  const refresh = (sale: Sale) => {
    queryClient.setQueryData(['sale', id], sale);
    for (const k of ['sales', 'sales-summary', 'products', 'customers', 'customer', 'notifications']) void queryClient.invalidateQueries({ queryKey: [k] });
  };

  if (query.isPending) return <Spinner />;
  if (query.isError) return <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} />;
  const s = query.data;
  const returnable = s.status !== 'Voided' && s.lines.some((l) => l.quantity > l.returnedQuantity);

  const share = async () => {
    const text = [settings.data?.storeName, `فاتورة رقم ${s.number}`, `الإجمالي: ${formatMoney(s.total)}`,
      s.remaining > 0 ? `المتبقي: ${formatMoney(s.remaining)}` : null].filter(Boolean).join('\n');
    if (navigator.share) {
      try { await navigator.share({ title: `فاتورة ${s.number}`, text }); return; } catch { /* fall back */ }
    }
    window.open(`https://wa.me/?text=${encodeURIComponent(text)}`, '_blank', 'noopener');
  };

  return (
    <div className="mx-auto max-w-3xl">
      {/* ---------- screen ---------- */}
      <div className="space-y-4 print:hidden">
        <button type="button" onClick={() => navigate(-1)} className="flex h-10 items-center gap-1 text-sm text-muted">
          <ArrowRight className="size-4" /> رجوع
        </button>
        <div className="flex flex-wrap items-start justify-between gap-2">
          <div>
            <h1 className="ltr text-xl font-bold">{s.number}</h1>
            <p className="text-sm text-muted">{formatDateTime(s.date)} · {s.salespersonName}</p>
            <p className="text-sm">
              {s.isWalkIn ? s.customerName : <Link to={`/customers/${s.customerId}`} className="text-brand-700 underline">{s.customerName}</Link>}
              {s.customerPhone && <span className="ltr ms-2 inline-block text-muted">{s.customerPhone}</span>}
            </p>
          </div>
          <Badge tone={SALE_STATUS[s.status].tone}>{SALE_STATUS[s.status].label}</Badge>
        </div>

        {s.status === 'Voided' && <p className="rounded-xl bg-red-50 p-3 text-sm text-danger">أُلغيت في {formatDateTime(s.voidedAt)} — السبب: {s.voidReason}</p>}

        <div className="flex flex-wrap gap-2">
          <Button variant="outline" onClick={() => window.print()}><Printer className="size-4" /> طباعة</Button>
          <Button variant="outline" onClick={() => void share()}><Share2 className="size-4" /> مشاركة</Button>
          {can(P.SalesReturn) && returnable && <Button variant="secondary" onClick={() => setReturning(true)}><Undo2 className="size-4" /> مرتجع</Button>}
          {can(P.SalesVoid) && s.status === 'Posted' && <Button variant="outline" onClick={() => setVoiding(true)}><Ban className="size-4 text-danger" /> إلغاء الفاتورة</Button>}
        </div>

        <Card className="overflow-hidden">
          <ul className="divide-y divide-line">
            {s.lines.map((l) => (
              <li key={l.id} className="p-3">
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <p className="font-semibold">{l.productName}</p>
                    <p className="text-xs text-muted">
                      <span className="tabular">{formatQuantity(l.quantity)}</span> {l.unitName} × {formatMoney(l.unitPrice)}
                      {l.lineDiscount > 0 && <> − خصم {formatMoney(l.lineDiscount)}</>}
                      {l.unitCost != null && <> · التكلفة {formatMoney(l.unitCost)}</>}
                    </p>
                    {l.returnedQuantity > 0 && <p className="text-xs text-warning">مرتجع: <span className="tabular">{formatQuantity(l.returnedQuantity)}</span></p>}
                    {l.priceOverridden && <p className="text-xs text-danger">بيع بأقل من الحد الأدنى بصلاحية خاصة</p>}
                  </div>
                  <p className="tabular shrink-0 font-bold">{formatMoney(l.lineTotal)}</p>
                </div>
              </li>
            ))}
          </ul>
          <div className="border-t border-line bg-canvas px-4">
            <InfoList items={[
              (s.lineDiscountTotal > 0 || s.invoiceDiscount > 0) && { label: 'المجموع قبل الخصم', value: formatMoney(s.grossTotal) },
              s.lineDiscountTotal > 0 && { label: 'خصومات البنود', value: formatMoney(s.lineDiscountTotal) },
              s.invoiceDiscount > 0 && { label: 'خصم الفاتورة', value: formatMoney(s.invoiceDiscount) },
              { label: 'الإجمالي', value: <b className="tabular text-lg">{formatMoney(s.total)}</b> },
              ...s.payments.map((p) => ({ label: `مدفوع — ${p.accountName}`, value: <span className="tabular">{formatMoney(p.amount)}{p.reference && <span className="ltr ms-1 text-xs text-muted">({p.reference})</span>}</span> })),
              s.remaining > 0 && { label: 'آجل عند البيع', value: <span className="tabular text-danger">{formatMoney(s.remaining)}</span> },
              s.returnedTotal > 0 && { label: 'قيمة المرتجعات', value: <span className="tabular text-warning">{formatMoney(s.returnedTotal)}</span> },
              s.profit != null && { label: 'الربح من الفاتورة', value: <span className={cn('tabular', s.profit < 0 ? 'text-danger' : 'text-success')}>{formatMoney(s.profit)}</span> },
            ]} />
          </div>
        </Card>

        {s.transfers.length > 0 && (
          <Card className="p-4">
            <h2 className="mb-2 font-bold">حوالات عند البيع</h2>
            <ul className="divide-y divide-line">
              {s.transfers.map((t) => (
                <li key={t.id} className="flex items-center justify-between gap-2 py-2">
                  <div>
                    <p className="text-sm font-semibold">{t.accountName}{t.reference && <span className="ltr ms-2 inline-block text-muted">{t.reference}</span>}</p>
                    <Link to="/transfers" className="text-xs text-brand-700 underline">{t.status === 'Pending' ? 'بانتظار التحقق' : t.status === 'Confirmed' ? 'مؤكدة' : 'مرفوضة'} — {t.number}</Link>
                  </div>
                  <span className="tabular font-bold">{formatMoney(t.amount)}</span>
                </li>
              ))}
            </ul>
          </Card>
        )}

        {s.returns.length > 0 && (
          <Card className="p-4">
            <h2 className="mb-2 font-bold">المرتجعات</h2>
            <ul className="divide-y divide-line">
              {s.returns.map((r) => (
                <li key={r.id} className="flex items-center justify-between gap-2 py-2">
                  <div>
                    <p className="ltr text-sm font-semibold">{r.number}</p>
                    <p className="text-xs text-muted">{formatDateTime(r.date)} · {r.settlement === 'Refund' ? 'استرداد مبلغ' : 'خصم من دين العميل'}{r.reason && ` · ${r.reason}`}</p>
                  </div>
                  <span className="tabular font-bold">{formatMoney(r.total)}</span>
                </li>
              ))}
            </ul>
          </Card>
        )}
        {s.notes && <Card className="p-4 text-sm">{s.notes}</Card>}
      </div>

      {/* ---------- A4 print ---------- */}
      <PrintableInvoice sale={s} store={settings.data} />

      {returning && <ReturnSheet sale={s} onClose={() => setReturning(false)} onDone={refresh} />}
      {voiding && <VoidSheet sale={s} onClose={() => setVoiding(false)} onDone={refresh} />}
    </div>
  );
}

function PrintableInvoice({ sale: s, store }: { sale: Sale; store?: { storeName: string; storeAddress: string | null; storePhone: string | null; invoiceFooter: string | null } }) {
  return (
    <article className="hidden text-[13px] text-black print:block">
      <header className="flex items-start justify-between border-b-2 border-black pb-3">
        <div>
          <p className="text-xl font-bold">{store?.storeName}</p>
          {store?.storeAddress && <p>{store.storeAddress}</p>}
          {store?.storePhone && <p className="ltr text-end">{store.storePhone}</p>}
        </div>
        <div className="text-end">
          <p className="text-lg font-bold">فاتورة مبيعات</p>
          <p className="ltr">{s.number}</p>
          <p>{formatDateTime(s.date)}</p>
          {s.status === 'Voided' && <p className="font-bold">ملغاة</p>}
        </div>
      </header>
      <p className="my-3">العميل: <b>{s.customerName}</b>{s.customerPhone && <span className="ltr ms-2 inline-block">{s.customerPhone}</span>}</p>
      <table className="w-full border-collapse">
        <thead>
          <tr className="border-y border-black">
            <th className="py-1.5 text-start">#</th>
            <th className="py-1.5 text-start">الصنف</th>
            <th className="py-1.5 text-center">الكمية</th>
            <th className="py-1.5 text-center">السعر</th>
            <th className="py-1.5 text-center">الخصم</th>
            <th className="py-1.5 text-end">المجموع</th>
          </tr>
        </thead>
        <tbody>
          {s.lines.map((l, i) => (
            <tr key={l.id} className="border-b border-gray-300 break-inside-avoid">
              <td className="py-1.5">{i + 1}</td>
              <td className="py-1.5">{l.productName}</td>
              <td className="tabular py-1.5 text-center">{formatQuantity(l.quantity)}</td>
              <td className="tabular py-1.5 text-center">{formatMoney(l.unitPrice)}</td>
              <td className="tabular py-1.5 text-center">{l.lineDiscount > 0 ? formatMoney(l.lineDiscount) : '—'}</td>
              <td className="tabular py-1.5 text-end">{formatMoney(l.lineTotal)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <div className="ms-auto mt-3 w-64 space-y-1">
        {s.invoiceDiscount > 0 && <p className="flex justify-between"><span>خصم الفاتورة</span><span className="tabular">{formatMoney(s.invoiceDiscount)}</span></p>}
        <p className="flex justify-between border-t border-black pt-1 text-base font-bold"><span>الإجمالي</span><span className="tabular">{formatMoney(s.total)}</span></p>
        <p className="flex justify-between"><span>المدفوع</span><span className="tabular">{formatMoney(s.paidAmount)}</span></p>
        {s.remaining > 0 && <p className="flex justify-between font-bold"><span>المتبقي</span><span className="tabular">{formatMoney(s.remaining)}</span></p>}
      </div>
      <p className="mt-3 rounded border border-black p-2">{amountInWords(s.total)}</p>
      {store?.invoiceFooter && <p className="mt-8 text-center">{store.invoiceFooter}</p>}
      <p className="mt-2 text-center text-[11px] text-gray-500">الموظف: {s.salespersonName}</p>
    </article>
  );
}

function ReturnSheet({ sale, onClose, onDone }: { sale: Sale; onClose: () => void; onDone: (s: Sale) => void }) {
  const toast = useToast();
  const lookups = useLookups();
  const [key] = useIdempotencyKey();
  const lines = sale.lines.filter((l) => l.quantity > l.returnedQuantity);
  const [qty, setQty] = useState<Record<number, string>>({});
  const [condition, setCondition] = useState<Record<number, ReturnCondition>>({});
  const [settlement, setSettlement] = useState<ReturnSettlement>(sale.isWalkIn ? 'Refund' : sale.remaining > 0 ? 'ReduceBalance' : 'Refund');
  const [accountId, setAccountId] = useState(String(sale.payments[0]?.accountId ?? lookups.data?.accounts[0]?.id ?? ''));
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string>();

  const total = round2(lines.reduce((sum, l) => {
    const q = parseNumber(qty[l.id]) ?? 0;
    return sum + (Number.isNaN(q) ? 0 : (q * l.netTotal) / l.quantity);
  }, 0));

  const save = useMutation({
    mutationFn: () => salesApi.return(sale.id, {
      idempotencyKey: key,
      settlement,
      refundAccountId: settlement === 'Refund' ? Number(accountId) : null,
      reason: reason.trim() || null,
      lines: lines.filter((l) => (parseNumber(qty[l.id]) ?? 0) > 0)
        .map((l) => ({ saleInvoiceLineId: l.id, quantity: parseNumber(qty[l.id])!, condition: condition[l.id] ?? 'Sellable' })),
    }),
    onSuccess: (s) => { onDone(s); toast.success('تم تسجيل المرتجع'); onClose(); },
    onError: (e) => setError(errorMessage(e)),
  });

  const submit = () => {
    for (const l of lines) {
      const q = parseNumber(qty[l.id]);
      if (q !== undefined && (Number.isNaN(q) || q < 0 || q > l.quantity - l.returnedQuantity))
        return setError(`كمية غير صحيحة لـ «${l.productName}» (المتاح ${formatQuantity(l.quantity - l.returnedQuantity)})`);
    }
    if (total <= 0) return setError('حدد كمية مرتجعة واحدة على الأقل');
    if (settlement === 'Refund' && !accountId) return setError('اختر الحساب الذي سيُدفع منه المبلغ');
    setError(undefined);
    save.mutate();
  };

  return (
    <Sheet open onClose={onClose} title={`مرتجع من ${sale.number}`} size="lg"
      footer={<Button block size="lg" loading={save.isPending} onClick={submit}>تسجيل المرتجع ({formatMoney(total)})</Button>}>
      <div className="space-y-4">
        {error && <p role="alert" className="rounded-xl bg-red-50 p-3 text-sm text-danger">{error}</p>}
        <ul className="divide-y divide-line">
          {lines.map((l) => (
            <li key={l.id} className="py-3">
              <div className="flex items-center gap-3">
                <div className="min-w-0 flex-1">
                  <p className="truncate font-semibold">{l.productName}</p>
                  <p className="text-xs text-muted">المتاح للإرجاع <span className="tabular">{formatQuantity(l.quantity - l.returnedQuantity)}</span> · {formatMoney(l.netTotal / l.quantity)} للوحدة</p>
                </div>
                <input aria-label={`الكمية المرتجعة: ${l.productName}`} inputMode="decimal" placeholder="0"
                  className="ltr h-12 w-20 shrink-0 rounded-xl border border-line text-center" value={qty[l.id] ?? ''}
                  onChange={(e) => setQty((s) => ({ ...s, [l.id]: e.target.value }))} />
              </div>
              {(parseNumber(qty[l.id]) ?? 0) > 0 && (
                <div className="mt-2 grid grid-cols-2 gap-2">
                  {(['Sellable', 'Damaged'] as const).map((c) => (
                    <button key={c} type="button" aria-pressed={(condition[l.id] ?? 'Sellable') === c} onClick={() => setCondition((s) => ({ ...s, [l.id]: c }))}
                      className={cn('h-10 rounded-lg border text-sm font-medium', (condition[l.id] ?? 'Sellable') === c ? 'border-brand-700 bg-brand-50 text-brand-800' : 'border-line')}>
                      {c === 'Sellable' ? 'صالح — يرجع للمخزون' : 'تالف — لا يرجع'}
                    </button>
                  ))}
                </div>
              )}
            </li>
          ))}
        </ul>
        <Field label="التسوية">
          {(id) => (
            <Select id={id} value={settlement} onChange={(e) => setSettlement(e.target.value as ReturnSettlement)} disabled={sale.isWalkIn}>
              <option value="Refund">إعادة المبلغ للعميل</option>
              {!sale.isWalkIn && <option value="ReduceBalance">خصم من دين العميل / رصيد له</option>}
            </Select>
          )}
        </Field>
        {settlement === 'Refund' && (
          <Field label="يُدفع من حساب" required>
            {(id) => (
              <Select id={id} value={accountId} onChange={(e) => setAccountId(e.target.value)}>
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

function VoidSheet({ sale, onClose, onDone }: { sale: Sale; onClose: () => void; onDone: (s: Sale) => void }) {
  const toast = useToast();
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string>();
  const save = useMutation({
    mutationFn: () => salesApi.void(sale.id, reason.trim()),
    onSuccess: (s) => { onDone(s); toast.success('تم إلغاء الفاتورة وعكس حركاتها'); onClose(); },
    onError: (e) => setError(errorMessage(e)),
  });
  return (
    <Sheet open onClose={onClose} title={`إلغاء الفاتورة ${sale.number}`}
      footer={<Button block size="lg" variant="danger" loading={save.isPending} onClick={() => (reason.trim() ? save.mutate() : setError('سبب الإلغاء مطلوب'))}>تأكيد الإلغاء</Button>}>
      <div className="space-y-3">
        <p className="text-sm leading-6 text-muted">
          ستعود الكميات إلى المخزون، وتُعكس المبالغ المدفوعة ({formatMoney(sale.paidAmount)}) من الحسابات
          {!sale.isWalkIn && ' ويُلغى الدين على العميل'}. تبقى الفاتورة ظاهرة بحالة «ملغاة».
        </p>
        <Field label="سبب الإلغاء" required error={error}>
          {(id, d) => <TextArea id={id} aria-describedby={d} rows={2} value={reason} onChange={(e) => { setReason(e.target.value); setError(undefined); }} />}
        </Field>
      </div>
    </Sheet>
  );
}
