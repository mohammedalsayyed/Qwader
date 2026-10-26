import { useState, type ReactNode } from 'react';
import { Link } from 'react-router';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Download, Printer } from 'lucide-react';
import { reportsApi, type ProductSort } from '@/api/reports';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button, IconButton } from '@/components/ui/Button';
import { BarChart } from '@/components/ui/BarChart';
import { Card, ErrorState, FilterChips, PageHeader, Spinner } from '@/components/ui/Feedback';
import { DateRangeFilter, resolveRange, type DateRangeValue } from '@/components/ui/DateRange';
import { Stat } from '@/components/ui/Layout';
import { useToast } from '@/components/ui/Toast';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';
import { formatDate, formatMoney, formatQuantity } from '@/lib/format';

const WEEKDAY = new Intl.DateTimeFormat('ar-EG-u-nu-latn', { weekday: 'long', day: '2-digit', month: '2-digit', timeZone: 'UTC' });
const SHORT = new Intl.DateTimeFormat('ar-EG-u-nu-latn', { day: 'numeric', month: 'numeric', timeZone: 'UTC' });

export function ReportsPage() {
  const { can } = useAuth();
  const toast = useToast();
  const [range, setRange] = useState<DateRangeValue>({ preset: 'today', ...resolveRange('today') });
  const [sort, setSort] = useState<ProductSort>('Revenue');
  const r = { from: range.from, to: range.to };
  const profit = can(P.ReportsProfit);
  const financial = can(P.ReportsFinancial);
  const multiDay = range.from !== range.to;

  const period = useQuery({ queryKey: ['report', 'period', r], queryFn: () => reportsApi.period(r), placeholderData: keepPreviousData });
  const daily = useQuery({ queryKey: ['report', 'daily', r], queryFn: () => reportsApi.daily(r), enabled: multiDay, placeholderData: keepPreviousData });
  const products = useQuery({ queryKey: ['report', 'products', r, sort], queryFn: () => reportsApi.products(r, sort), placeholderData: keepPreviousData });
  const categories = useQuery({ queryKey: ['report', 'categories', r], queryFn: () => reportsApi.categories(r), placeholderData: keepPreviousData });
  const people = useQuery({ queryKey: ['report', 'people', r], queryFn: () => reportsApi.salespeople(r), placeholderData: keepPreviousData });
  const receivables = useQuery({ queryKey: ['report', 'receivables'], queryFn: reportsApi.receivables, enabled: financial });
  const payables = useQuery({ queryKey: ['report', 'payables'], queryFn: reportsApi.payables, enabled: financial });
  const inventory = useQuery({ queryKey: ['report', 'inventory'], queryFn: reportsApi.inventoryValue, enabled: profit || financial });

  const exportExcel = (name: string) => reportsApi.exportExcel(name, r).catch((e) => toast.error(errorMessage(e)));
  const p = period.data;

  return (
    <div className="space-y-4">
      <PageHeader title="التقارير" subtitle={p ? (p.from === p.to ? formatDate(p.from) : `${formatDate(p.from)} — ${formatDate(p.to)}`) : undefined}
        actions={<IconButton label="طباعة / حفظ PDF" onClick={() => window.print()} className="border border-line bg-surface print:hidden"><Printer className="size-5" /></IconButton>} />
      <div className="print:hidden">
        <DateRangeFilter value={range} onChange={setRange} presets={['today', 'yesterday', 'last7', 'thisMonth', 'lastMonth', 'custom']} />
      </div>

      {period.isPending ? <Spinner /> : period.isError ? <ErrorState message={errorMessage(period.error)} onRetry={() => void period.refetch()} /> : p && (
        <div className={cn('space-y-4', period.isPlaceholderData && 'opacity-60')}>
          {/* ---------- headline figures ---------- */}
          <div className="grid grid-cols-2 gap-2 lg:grid-cols-4">
            <Stat label="صافي المبيعات" value={formatMoney(p.netSales)} hint={`${p.invoiceCount} فاتورة · متوسط ${formatMoney(p.averageInvoice)}`} />
            {p.grossProfit != null && <Stat label="إجمالي الربح" value={formatMoney(p.grossProfit)} tone={p.grossProfit < 0 ? 'danger' : 'success'}
              hint={p.netSales ? `هامش ${Math.round((p.grossProfit / p.netSales) * 1000) / 10}%` : undefined} />}
            {p.netOperatingProfit != null && <Stat label="صافي الربح التشغيلي" value={formatMoney(p.netOperatingProfit)} tone={p.netOperatingProfit < 0 ? 'danger' : 'success'} hint="بعد المصروفات والخسائر" />}
            {p.cashIn != null && <Stat label="المقبوضات" value={formatMoney(p.cashIn)} hint={p.collections ? `منها تحصيل ديون ${formatMoney(p.collections)}` : undefined} />}
            {p.expenses != null && <Stat label="المصروفات" value={formatMoney(p.expenses)} />}
            {p.newDebt != null && <Stat label="ديون جديدة" value={formatMoney(p.newDebt)} tone={p.newDebt > 0 ? 'warning' : 'default'} />}
            {p.pendingTransferAmount != null && p.pendingTransferCount > 0 && (
              <Link to="/transfers" className="contents"><Stat label="حوالات بانتظار التحقق" value={formatMoney(p.pendingTransferAmount)} tone="warning" hint={`${p.pendingTransferCount} حوالة`} /></Link>
            )}
          </div>

          {/* ---------- daily charts ---------- */}
          {multiDay && daily.data && daily.data.length > 1 && (
            <Section title="المبيعات يومًا بيوم" exportKey="daily" onExport={exportExcel}>
              <div className="space-y-6 p-4">
                <BarChart label="صافي المبيعات اليومي" format={formatMoney}
                  data={daily.data.map((d) => ({ key: d.day, label: SHORT.format(new Date(d.day)), title: `صافي المبيعات — ${WEEKDAY.format(new Date(d.day))}`, value: d.netSales }))} />
                {profit && (
                  <BarChart label="إجمالي الربح اليومي" format={formatMoney}
                    data={daily.data.map((d) => ({ key: d.day, label: SHORT.format(new Date(d.day)), title: `إجمالي الربح — ${WEEKDAY.format(new Date(d.day))}`, value: d.grossProfit ?? 0 }))} />
                )}
              </div>
            </Section>
          )}

          {/* ---------- P&L ---------- */}
          <Section title="الأرباح والخسائر" exportKey="summary" onExport={exportExcel}>
            <Lines rows={[
              ['إجمالي المبيعات', p.grossSales],
              ['− الخصومات', p.discounts],
              ['− مردودات المبيعات', p.returns],
              ['= صافي المبيعات', p.netSales, 'strong'],
              ...(p.costOfSales != null ? [
                ['− تكلفة البضاعة المباعة', p.costOfSales] as Row,
                ['= إجمالي الربح', p.grossProfit!, 'strong'] as Row,
                ['− خسائر المخزون (تالف، مفقود، نقص جرد)', p.inventoryLosses!] as Row,
              ] : []),
              ...(p.expenses != null ? [['− المصروفات التشغيلية', p.expenses] as Row] : []),
              ...(p.netOperatingProfit != null ? [['= صافي الربح التشغيلي', p.netOperatingProfit, 'total'] as Row] : []),
            ]} />
            {p.damagedReturnsCost != null && p.damagedReturnsCost > 0 && (
              <p className="px-4 pb-3 text-xs text-muted">تكلفة البضاعة تشمل {formatMoney(p.damagedReturnsCost)} لمرتجعات تالفة لم تعد للمخزون.</p>
            )}
          </Section>

          {p.expensesByCategory && p.expensesByCategory.length > 0 && (
            <Section title="المصروفات حسب النوع">
              <Lines rows={p.expensesByCategory.map((e) => [e.name, e.amount] as Row)} />
            </Section>
          )}

          {/* ---------- money ---------- */}
          {p.cashIn != null && (
            <Section title="التدفق النقدي" hint="الأموال الداخلة والخارجة فعلًا — منفصلة عن الربح، ولا تشمل الأرصدة الافتتاحية والتحويل بين حسابات المعرض">
              <Lines rows={[
                ...(p.inByCategory ?? []).map((c) => [`+ ${c.name}`, c.amount] as Row),
                ['= مجموع الداخل', p.cashIn, 'strong'],
                ...(p.outByCategory ?? []).map((c) => [`− ${c.name}`, c.amount] as Row),
                ['= مجموع الخارج', p.cashOut!, 'strong'],
                ['صافي التدفق النقدي', p.netCashFlow!, 'total'],
              ]} />
            </Section>
          )}
          {p.accounts && (
            <Section title="الحسابات — رصيد نهاية الفترة">
              <ul className="divide-y divide-line">
                {p.accounts.map((a) => (
                  <li key={a.accountId}>
                    <Link to={`/accounts/${a.accountId}`} className="flex items-center justify-between gap-3 px-4 py-3">
                      <span className="min-w-0">
                        <span className="block font-medium">{a.name}</span>
                        <span className="tabular block text-xs text-muted">داخل {formatMoney(a.in)} · خارج {formatMoney(a.out)}</span>
                      </span>
                      <span className={cn('tabular shrink-0 font-bold', a.closingBalance < 0 && 'text-danger')}>{formatMoney(a.closingBalance)}</span>
                    </Link>
                  </li>
                ))}
              </ul>
            </Section>
          )}
        </div>
      )}

      {/* ---------- products & groups ---------- */}
      <Section title="المنتجات الأكثر مبيعًا" exportKey="products" onExport={exportExcel}>
        <div className="px-4 pt-3 print:hidden">
          <FilterChips value={sort} onChange={setSort} options={[
            { value: 'Revenue', label: 'حسب المبيعات' }, { value: 'Quantity', label: 'حسب الكمية' }, ...(profit ? [{ value: 'Profit' as const, label: 'حسب الربح' }] : []),
          ]} />
        </div>
        <QueryList query={products} empty="لا توجد مبيعات في هذه الفترة">
          {(rows) => rows.map((x, i) => (
            <li key={x.productId}>
              <Link to={`/products/${x.productId}`} className="flex items-center gap-3 px-4 py-3">
                <span className="tabular w-6 shrink-0 text-center text-sm text-muted">{i + 1}</span>
                <span className="min-w-0 flex-1">
                  <span className="block truncate font-medium">{x.name}</span>
                  <span className="block text-xs text-muted">{x.categoryName} · {formatQuantity(x.quantity)} قطعة{x.marginPercent != null && ` · هامش ${x.marginPercent}%`}</span>
                </span>
                <span className="shrink-0 text-end">
                  <span className="tabular block font-bold">{formatMoney(x.revenue)}</span>
                  {x.profit != null && <span className={cn('tabular block text-xs', x.profit < 0 ? 'text-danger' : 'text-success')}>ربح {formatMoney(x.profit)}</span>}
                </span>
              </Link>
            </li>
          ))}
        </QueryList>
      </Section>

      <Section title={profit ? 'الأصناف الأكثر ربحًا' : 'المبيعات حسب الصنف'} exportKey="categories" onExport={exportExcel}>
        <QueryList query={categories} empty="لا توجد بيانات">
          {(rows) => rows.map((c) => (
            <li key={c.id} className="flex items-center justify-between gap-3 px-4 py-3">
              <span className="min-w-0"><span className="block font-medium">{c.name}</span><span className="block text-xs text-muted">{c.count} منتج · {formatQuantity(c.quantity)} قطعة</span></span>
              <span className="shrink-0 text-end">
                <span className="tabular block font-bold">{formatMoney(c.profit ?? c.revenue)}</span>
                <span className="block text-xs text-muted">{c.profit != null ? `من مبيعات ${formatMoney(c.revenue)}` : 'مبيعات'}</span>
              </span>
            </li>
          ))}
        </QueryList>
      </Section>

      <Section title="المبيعات حسب الموظف" exportKey="salespeople" onExport={exportExcel}>
        <QueryList query={people} empty="لا توجد بيانات">
          {(rows) => rows.map((c) => (
            <li key={c.id} className="flex items-center justify-between gap-3 px-4 py-3">
              <span><span className="block font-medium">{c.name}</span><span className="block text-xs text-muted">{c.count} فاتورة</span></span>
              <span className="shrink-0 text-end">
                <span className="tabular block font-bold">{formatMoney(c.revenue)}</span>
                {c.profit != null && <span className="tabular block text-xs text-muted">ربح {formatMoney(c.profit)}</span>}
              </span>
            </li>
          ))}
        </QueryList>
      </Section>

      {/* ---------- positions (as of now) ---------- */}
      {financial && (
        <div className="grid gap-4 lg:grid-cols-2">
          <Section title={`ذمم العملاء${p?.receivables ? ` — ${formatMoney(p.receivables)}` : ''}`} exportKey="receivables" onExport={exportExcel}>
            <QueryList query={receivables} empty="لا توجد ديون على العملاء" limit={10}>
              {(rows) => rows.filter((b) => b.balance > 0).map((b) => (
                <li key={b.id}><Link to={`/customers/${b.id}`} className="flex items-center justify-between gap-3 px-4 py-3">
                  <span className="min-w-0"><span className="block truncate font-medium">{b.name}</span><span className="ltr block text-xs text-muted">{b.phone ?? b.code}</span></span>
                  <span className="tabular shrink-0 font-bold text-danger">{formatMoney(b.balance)}</span>
                </Link></li>
              ))}
            </QueryList>
          </Section>
          <Section title={`مستحق للموردين${p?.payables ? ` — ${formatMoney(p.payables)}` : ''}`} exportKey="payables" onExport={exportExcel}>
            <QueryList query={payables} empty="لا توجد مبالغ مستحقة للموردين" limit={10}>
              {(rows) => rows.filter((b) => b.balance > 0).map((b) => (
                <li key={b.id}><Link to={`/suppliers/${b.id}`} className="flex items-center justify-between gap-3 px-4 py-3">
                  <span className="min-w-0 truncate font-medium">{b.name}</span>
                  <span className="tabular shrink-0 font-bold">{formatMoney(b.balance)}</span>
                </Link></li>
              ))}
            </QueryList>
          </Section>
        </div>
      )}
      {(profit || financial) && (
        <Section title={`قيمة المخزون بالتكلفة${p?.inventoryValue != null ? ` — ${formatMoney(p.inventoryValue)}` : ''}`} exportKey="inventory" onExport={exportExcel}>
          <QueryList query={inventory} empty="لا يوجد مخزون">
            {(rows) => rows.map((c) => (
              <li key={c.categoryId} className="flex items-center justify-between gap-3 px-4 py-3">
                <span><span className="block font-medium">{c.categoryName}</span><span className="block text-xs text-muted">{c.productCount} منتج · {formatQuantity(c.quantity)} قطعة</span></span>
                <span className="tabular shrink-0 font-bold">{formatMoney(c.value)}</span>
              </li>
            ))}
          </QueryList>
        </Section>
      )}
    </div>
  );
}

type Row = [label: string, value: number, emphasis?: 'strong' | 'total'];

function Lines({ rows }: { rows: Row[] }) {
  return (
    <dl className="divide-y divide-line">
      {rows.map(([label, value, emphasis]) => (
        <div key={label} className={cn('flex items-center justify-between gap-4 px-4 py-2.5', emphasis === 'total' && 'bg-brand-50')}>
          <dt className={cn('text-sm', emphasis ? 'font-bold' : 'text-muted')}>{label}</dt>
          <dd className={cn('tabular', emphasis === 'total' ? 'text-lg font-bold' : emphasis ? 'font-bold' : 'font-medium', value < 0 && 'text-danger')}>{formatMoney(value)}</dd>
        </div>
      ))}
    </dl>
  );
}

function Section({ title, hint, exportKey, onExport, children }: { title: string; hint?: string; exportKey?: string; onExport?: (key: string) => void; children: ReactNode }) {
  return (
    <Card className="overflow-hidden break-inside-avoid">
      <header className="flex items-start justify-between gap-2 border-b border-line px-4 py-3">
        <div>
          <h2 className="font-bold">{title}</h2>
          {hint && <p className="text-xs text-muted">{hint}</p>}
        </div>
        {exportKey && onExport && (
          <Button variant="ghost" size="sm" className="shrink-0 print:hidden" onClick={() => onExport(exportKey)}>
            <Download className="size-4" /> Excel
          </Button>
        )}
      </header>
      {children}
    </Card>
  );
}

function QueryList<T>({ query, empty, limit, children }: {
  query: { isPending: boolean; isError: boolean; error: unknown; data?: T[]; refetch: () => unknown };
  empty: string;
  limit?: number;
  children: (rows: T[]) => ReactNode;
}) {
  if (query.isPending) return <Spinner className="py-6" />;
  if (query.isError) return <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} />;
  const rows = limit ? (query.data ?? []).slice(0, limit) : query.data ?? [];
  if (rows.length === 0) return <p className="px-4 py-6 text-center text-sm text-muted">{empty}</p>;
  return <ul className="divide-y divide-line">{children(rows)}</ul>;
}
