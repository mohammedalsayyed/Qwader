import { useMemo, useState } from 'react';
import { Link } from 'react-router';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Download, Printer } from 'lucide-react';
import { reportsApi, type InvoiceReportRow, type InvoiceStatus } from '@/api/reports';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button, IconButton } from '@/components/ui/Button';
import { Badge, Card, EmptyState, ErrorState, FilterChips, PageHeader, SearchInput, Spinner } from '@/components/ui/Feedback';
import { DateRangeFilter, resolveRange, type DateRangeValue } from '@/components/ui/DateRange';
import { Stat } from '@/components/ui/Layout';
import { useToast } from '@/components/ui/Toast';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';
import { formatDate, formatDateTime, formatMoney } from '@/lib/format';
import { Profit } from '@/features/profit/ProfitLock';

type Filter = 'all' | 'owed' | 'paid' | 'pending';

const STATUS_TONE: Record<InvoiceStatus, 'success' | 'warning' | 'danger' | 'brand'> = {
  'مدفوعة': 'success',
  'جزئي': 'warning',
  'آجل': 'danger',
  'بانتظار التحقق': 'brand',
};

const matches = (r: InvoiceReportRow, f: Filter) =>
  f === 'all' || (f === 'paid' ? r.status === 'مدفوعة' : f === 'pending' ? r.pendingTransfers > 0 : r.remaining > 0);

/** «تقرير فواتير المبيعات»: each invoice of the period — its profit, where its money went, who paid and who still owes. */
export function InvoicesReportPage() {
  const { can } = useAuth();
  const toast = useToast();
  const [range, setRange] = useState<DateRangeValue>({ preset: 'thisMonth', ...resolveRange('thisMonth') });
  const [filter, setFilter] = useState<Filter>('all');
  const [search, setSearch] = useState('');
  const r = { from: range.from, to: range.to };
  const query = useQuery({ queryKey: ['report', 'invoices', r], queryFn: () => reportsApi.invoices(r), placeholderData: keepPreviousData });
  const d = query.data;
  const profit = can(P.ProfitView) && d?.profit != null;

  const rows = useMemo(() => {
    const term = search.trim();
    return (d?.rows ?? []).filter((x) => matches(x, filter)
      && (!term || x.number.includes(term) || x.customerName.includes(term) || x.salesperson.includes(term)));
  }, [d, filter, search]);
  const filtered = rows.length !== (d?.rows.length ?? 0);
  const sum = (k: 'total' | 'paid' | 'remaining') => rows.reduce((s, x) => s + x[k], 0);

  return (
    <div className="space-y-4">
      <PageHeader title="تقرير فواتير المبيعات"
        subtitle={d ? (d.from === d.to ? formatDate(d.from) : `${formatDate(d.from)} — ${formatDate(d.to)}`) : undefined}
        actions={(
          <div className="flex gap-2 print:hidden">
            <Button variant="secondary" onClick={() => reportsApi.exportExcel('invoices', r).catch((e) => toast.error(errorMessage(e)))}>
              <Download className="size-4" /> Excel
            </Button>
            <IconButton label="طباعة / حفظ PDF" onClick={() => window.print()} className="border border-line bg-surface"><Printer className="size-5" /></IconButton>
          </div>
        )} />
      <div className="space-y-3 print:hidden">
        <DateRangeFilter value={range} onChange={setRange} presets={['today', 'yesterday', 'last7', 'thisMonth', 'lastMonth', 'custom']} />
        <SearchInput value={search} onChange={setSearch} placeholder="رقم الفاتورة أو العميل أو الموظف" />
        <FilterChips value={filter} onChange={setFilter}
          options={[{ value: 'all', label: 'الكل' }, { value: 'owed', label: 'عليها فلوس' }, { value: 'paid', label: 'مدفوعة' }, { value: 'pending', label: 'حوالات بانتظار التحقق' }]} />
      </div>

      {query.isPending ? <Spinner /> : query.isError ? <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} /> : d && (
        <div className={cn('space-y-4', query.isPlaceholderData && 'opacity-60')}>
          <div className={cn('grid grid-cols-2 gap-2', profit ? 'lg:grid-cols-5' : 'lg:grid-cols-4')}>
            <Stat label="عدد الفواتير" value={String(d.count)} hint={d.returned > 0 ? `مرتجع ${formatMoney(d.returned)}` : undefined} />
            <Stat label="إجمالي الفواتير" value={formatMoney(d.total)} />
            <Stat label="المدفوع" value={formatMoney(d.paid)} tone="success" />
            <Stat label="ضايل على العملاء" value={formatMoney(d.remaining)} tone={d.remaining > 0 ? 'danger' : 'default'}
              hint={d.pendingTransfers > 0 ? `منها ${formatMoney(d.pendingTransfers)} حوالات بانتظار التحقق` : undefined} />
            {profit && <Stat label="الربح" value={<Profit>{formatMoney(d.profit!)}</Profit>} tone={d.profit! < 0 ? 'danger' : 'success'} hint="بعد المرتجعات" />}
          </div>

          {d.byAccount.length > 0 && (
            <Card className="overflow-hidden break-inside-avoid">
              <h2 className="border-b border-line px-4 py-3 font-bold">وين انحوّلت الفلوس</h2>
              <ul className="divide-y divide-line">
                {d.byAccount.map((a) => (
                  <li key={a.account} className="flex items-center justify-between gap-3 px-4 py-2.5 text-sm">
                    <span>{a.account}</span><span className="font-semibold tabular-nums">{formatMoney(a.amount)}</span>
                  </li>
                ))}
              </ul>
            </Card>
          )}

          {rows.length === 0 ? <EmptyState title="لا توجد فواتير" description="جرّب فترة أو تصفية أخرى" /> : (
            <>
              {/* phones: one card per invoice */}
              <ul className="space-y-2 md:hidden print:hidden">
                {rows.map((x) => (
                  <li key={x.id}>
                    <Link to={`/sales/${x.id}`}>
                      <Card className="space-y-2 p-3">
                        <div className="flex items-start justify-between gap-2">
                          <div className="min-w-0">
                            <p className="font-semibold">{x.customerName}</p>
                            <p className="text-xs text-muted">{x.number} · {formatDateTime(x.date)}{x.salesperson && ` · ${x.salesperson}`}</p>
                          </div>
                          <Badge tone={STATUS_TONE[x.status]}>{x.status}</Badge>
                        </div>
                        <div className="grid grid-cols-3 gap-2 text-sm">
                          <Fig label="الإجمالي" value={formatMoney(x.total)} />
                          <Fig label="المدفوع" value={formatMoney(x.paid)} />
                          <Fig label="المتبقي" value={formatMoney(x.remaining)} danger={x.remaining > 0} />
                        </div>
                        {x.payments.length > 0 && <p className="text-xs text-muted">{x.payments.map((p) => `${p.account}: ${formatMoney(p.amount)}`).join(' · ')}</p>}
                        <div className="flex justify-between text-xs">
                          {x.returned > 0 ? <span className="text-muted">مرتجع {formatMoney(x.returned)}</span> : <span />}
                          {profit && x.profit != null && <span>الربح: <Profit className="font-semibold">{formatMoney(x.profit)}</Profit></span>}
                        </div>
                      </Card>
                    </Link>
                  </li>
                ))}
              </ul>

              {/* wider screens and print: a table */}
              <Card className="hidden overflow-x-auto md:block print:block print:overflow-visible">
                <table className="w-full text-sm">
                  <thead className="bg-gray-50 text-xs text-muted">
                    <tr className="text-start">
                      <Th>الفاتورة</Th><Th>العميل</Th><Th>الموظف</Th><Th num>الإجمالي</Th><Th num>المرتجع</Th>
                      <Th num>المدفوع</Th><Th num>المتبقي</Th><Th>الحالة</Th><Th>وين انحوّلت</Th>{profit && <Th num>الربح</Th>}
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-line">
                    {rows.map((x) => (
                      <tr key={x.id} className="break-inside-avoid align-top hover:bg-gray-50">
                        <Td><Link to={`/sales/${x.id}`} className="font-semibold text-brand-700 hover:underline">{x.number}</Link>
                          <span className="block text-xs text-muted">{formatDateTime(x.date)}</span></Td>
                        <Td>{x.customerName}</Td>
                        <Td>{x.salesperson}</Td>
                        <Td num>{formatMoney(x.total)}</Td>
                        <Td num>{x.returned > 0 ? formatMoney(x.returned) : '—'}</Td>
                        <Td num>{formatMoney(x.paid)}</Td>
                        <Td num className={x.remaining > 0 ? 'font-semibold text-red-700' : undefined}>{formatMoney(x.remaining)}</Td>
                        <Td><Badge tone={STATUS_TONE[x.status]}>{x.status}</Badge></Td>
                        <Td className="text-xs">{x.payments.length === 0 ? '—' : x.payments.map((p) => (
                          <span key={p.account} className="block whitespace-nowrap">{p.account}: {formatMoney(p.amount)}</span>))}</Td>
                        {profit && <Td num>{x.profit != null && <Profit>{formatMoney(x.profit)}</Profit>}</Td>}
                      </tr>
                    ))}
                  </tbody>
                  <tfoot className="border-t-2 border-line bg-gray-50 font-bold">
                    <tr>
                      <Td>{filtered ? `مجموع المعروض (${rows.length})` : `المجموع (${d.count})`}</Td><Td /><Td />
                      <Td num>{formatMoney(sum('total'))}</Td>
                      <Td num>{filtered ? '' : formatMoney(d.returned)}</Td>
                      <Td num>{formatMoney(sum('paid'))}</Td>
                      <Td num>{formatMoney(sum('remaining'))}</Td><Td /><Td />
                      {profit && <Td num><Profit>{formatMoney(rows.reduce((s, x) => s + (x.profit ?? 0), 0))}</Profit></Td>}
                    </tr>
                  </tfoot>
                </table>
              </Card>
            </>
          )}
        </div>
      )}
    </div>
  );
}

function Fig({ label, value, danger }: { label: string; value: string; danger?: boolean }) {
  return (
    <div>
      <span className="block text-xs text-muted">{label}</span>
      <span className={cn('font-semibold tabular-nums', danger && 'text-red-700')}>{value}</span>
    </div>
  );
}

function Th({ children, num }: { children?: React.ReactNode; num?: boolean }) {
  return <th className={cn('whitespace-nowrap px-3 py-2 font-semibold', num ? 'text-end' : 'text-start')}>{children}</th>;
}

function Td({ children, num, className }: { children?: React.ReactNode; num?: boolean; className?: string }) {
  return <td className={cn('px-3 py-2', num && 'whitespace-nowrap text-end tabular-nums', className)}>{children}</td>;
}
