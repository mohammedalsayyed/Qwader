import { useState, type ReactNode } from 'react';
import { Link } from 'react-router';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { FileSpreadsheet } from 'lucide-react';
import { suppliersApi, type SupplierReport, type SupplierReportInvoice } from '@/api/stock';
import { Badge, Card, EmptyState, ErrorState, Spinner } from '@/components/ui/Feedback';
import { Button } from '@/components/ui/Button';
import { DateRangeFilter, type DateRangeValue } from '@/components/ui/DateRange';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';
import { formatDate, formatMoney, formatQuantity } from '@/lib/format';

/**
 * «التفاصيل»: everything about the supplier over the period chosen — what was bought, returned and paid and what is still owed,
 * each purchase invoice (صنف | عدد | سعر | إجمالي) with its payments, returns and the vouchers tied to it, and the payments.
 * A product opens its page, an invoice its invoice, a voucher its voucher. The same figures as the Excel files.
 */
export function SupplierDetails({ supplierId }: { supplierId: number }) {
  const [range, setRange] = useState<DateRangeValue>({ preset: 'all' });
  const [exporting, setExporting] = useState<'sheet' | 'book' | null>(null);
  const [exportError, setExportError] = useState<string | null>(null);
  const query = useQuery({
    queryKey: ['supplier-report', supplierId, range.from, range.to],
    queryFn: () => suppliersApi.report(supplierId, range.from, range.to),
    placeholderData: keepPreviousData,
    enabled: range.preset !== 'custom' || (!!range.from && !!range.to),
  });
  const download = async (kind: 'sheet' | 'book') => {
    setExporting(kind);
    setExportError(null);
    try {
      await (kind === 'sheet' ? suppliersApi.exportStatementSheet : suppliersApi.exportStatement)(supplierId, range.from, range.to);
    } catch (e) { setExportError(errorMessage(e)); } finally { setExporting(null); }
  };

  return (
    <div className="space-y-4">
      <div className="space-y-2 print:hidden">
        <DateRangeFilter value={range} onChange={setRange} />
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" size="sm" loading={exporting === 'sheet'} onClick={() => void download('sheet')}>
            <FileSpreadsheet className="size-4" /> Excel (ورقة واحدة للطباعة)
          </Button>
          <Button variant="outline" size="sm" loading={exporting === 'book'} onClick={() => void download('book')}>
            <FileSpreadsheet className="size-4" /> Excel (عدة أوراق)
          </Button>
        </div>
        {exportError && <p role="alert" className="text-sm text-danger">{exportError}</p>}
      </div>
      {query.isPending ? <Spinner /> : query.isError ? (
        <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} />
      ) : <Report r={query.data} fetching={query.isFetching} />}
    </div>
  );
}

function Report({ r, fetching }: { r: SupplierReport; fetching: boolean }) {
  const posted = r.invoices.filter((i) => !i.voided);
  const paidTotal = r.payments.filter((p) => !p.voided).reduce((s, p) => s + p.amount, 0);
  const lines: [string, number, boolean?][] = [
    ...(r.from ? [['الرصيد قبل الفترة', r.openingBalance] as [string, number]] : []),
    ...(r.oldBalance ? [['رصيد سابق (قبل النظام)', r.oldBalance] as [string, number]] : []),
    ['المشتريات', r.bought],
    ['المردودات للمورد', -r.returned],
    ['المدفوع للمورد', -r.paid],
    ...(r.refunded ? [['استرداد من المورد', r.refunded] as [string, number]] : []),
  ];
  return (
    <div className={cn('space-y-4', fetching && 'opacity-60')}>
      <Card className="overflow-hidden">
        <h3 className="border-b border-line px-4 py-2.5 text-sm font-bold">الملخص</h3>
        <ul className="divide-y divide-line text-sm">
          {lines.map(([label, v]) => (
            <li key={label} className="flex justify-between gap-3 px-4 py-2"><span>{label}</span><span className="tabular font-semibold" dir="ltr">{formatMoney(v)}</span></li>
          ))}
          <li className={cn('flex justify-between gap-3 px-4 py-3 font-bold', r.closing > 0 ? 'bg-amber-50 text-amber-800' : 'bg-success/10 text-success')}>
            <span>{r.closing >= 0 ? 'الباقي له علينا' : 'لنا عنده (دفعنا أكثر)'}</span>
            <span className="tabular text-lg" dir="ltr">{formatMoney(Math.abs(r.closing))}</span>
          </li>
        </ul>
      </Card>

      <section className="space-y-2">
        <h3 className="text-sm font-bold">فواتير الشراء ({posted.length}) — {formatMoney(posted.reduce((s, i) => s + i.total, 0))}</h3>
        {r.invoices.length === 0 ? <EmptyState title="لا توجد فواتير شراء في هذه الفترة" /> : r.invoices.map((inv) => <Invoice key={inv.id} inv={inv} />)}
      </section>

      <Card className="overflow-hidden">
        <h3 className="border-b border-line px-4 py-2.5 text-sm font-bold">الدفعات للمورد — {formatMoney(paidTotal)}</h3>
        {r.payments.length === 0 ? <p className="px-4 py-3 text-sm text-muted">لا توجد دفعات في هذه الفترة.</p> : (
          <ul className="divide-y divide-line text-sm">
            {r.payments.map((p, i) => (
              <li key={i} className={cn('flex items-center justify-between gap-3 px-4 py-2.5', p.voided && 'text-muted line-through')}>
                <span>
                  <Link to={p.voucherId ? `/payments/${p.voucherId}` : `/purchases/${p.purchaseInvoiceId}`} className="ltr font-semibold text-brand-700 underline">{p.number}</Link>
                  <span className="block text-xs text-muted">{formatDate(p.date)} · {p.kind} · من {p.account}{p.notes ? ` · ${p.notes}` : ''}</span>
                </span>
                <span className="tabular font-semibold">{formatMoney(p.amount)}</span>
              </li>
            ))}
          </ul>
        )}
      </Card>
    </div>
  );
}

function Invoice({ inv }: { inv: SupplierReportInvoice }) {
  return (
    <Card className={cn('overflow-hidden', inv.voided && 'opacity-70')}>
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-line bg-canvas px-4 py-2.5">
        <div>
          <Link to={`/purchases/${inv.id}`} className="ltr font-bold text-brand-700 underline">{inv.number}</Link>
          <span className="ms-2 text-xs text-muted">{formatDate(inv.date)}{inv.supplierInvoiceNo ? ` · فاتورة المورد ${inv.supplierInvoiceNo}` : ''}</span>
        </div>
        <div className="flex items-center gap-2">
          {inv.voided && <Badge tone="danger">ملغاة</Badge>}
          <span className="tabular font-bold">{formatMoney(inv.total)}</span>
        </div>
      </div>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="text-muted">
            <tr className="border-b border-line">
              <th className="px-4 py-2 text-start font-semibold">الصنف</th>
              <th className="px-2 py-2 text-start font-semibold">العدد</th>
              <th className="px-2 py-2 text-start font-semibold">السعر</th>
              <th className="px-4 py-2 text-start font-semibold">الإجمالي</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {inv.lines.map((l, i) => (
              <tr key={i}>
                <td className="px-4 py-2">
                  <Link to={`/products/${l.productId}`} className="font-semibold text-brand-700 underline">{l.name}</Link>
                  <span className="block text-xs text-muted">
                    {[l.sku, l.unit, l.lineDiscount ? `خصم ${formatMoney(l.lineDiscount)}` : null, l.returnedQuantity ? `رُجّع ${formatQuantity(l.returnedQuantity)}` : null].filter(Boolean).join(' · ')}
                  </span>
                </td>
                <td className="tabular px-2 py-2">{formatQuantity(l.quantity)}</td>
                <td className="tabular px-2 py-2">{formatMoney(l.unitCost)}</td>
                <td className="tabular px-4 py-2 font-semibold">{formatMoney(l.lineTotal)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <ul className="divide-y divide-line border-t border-line text-sm">
        {inv.discount !== 0 && <Row label={inv.discountIsIncome ? 'خصم على الفاتورة (خصم مكتسب)' : 'خصم على الفاتورة'} value={-inv.discount} />}
        {inv.paidWhenBuying.map((p, i) => <Row key={`p${i}`} label={`مدفوع عند الشراء من ${p.account}`} value={p.amount} />)}
        {inv.returns.map((x) => <Row key={`r${x.id}`} label={<>مردود <span className="ltr inline-block">{x.number}</span> · {formatDate(x.date)}</>} value={-x.total} />)}
        {inv.expenses.map((e) => (
          <li key={`e${e.paymentVoucherId}`} className={cn('flex justify-between gap-3 px-4 py-2', e.voided && 'text-muted line-through')}>
            <span>
              سند صرف <Link to={`/payments/${e.paymentVoucherId}`} className="ltr font-semibold text-brand-700 underline">{e.number}</Link> مرتبط بالفاتورة
              <span className="block text-xs text-muted">{e.category}: {e.description} · من {e.account} · {formatDate(e.date)}</span>
            </span>
            <span className="tabular">{formatMoney(e.amount)}</span>
          </li>
        ))}
      </ul>
    </Card>
  );
}

function Row({ label, value }: { label: ReactNode; value: number }) {
  return <li className="flex justify-between gap-3 px-4 py-2"><span>{label}</span><span className="tabular" dir="ltr">{formatMoney(value)}</span></li>;
}
