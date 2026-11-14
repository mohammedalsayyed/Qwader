import { useState, type ReactNode } from 'react';
import { Link } from 'react-router';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { ArrowRight, MessageCircle, Printer } from 'lucide-react';
import { reportsApi, type PeriodReport, type ProductRow } from '@/api/reports';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button } from '@/components/ui/Button';
import { ErrorState, Spinner } from '@/components/ui/Feedback';
import { Input } from '@/components/ui/Field';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';
import { formatDate, formatMoney, localToday } from '@/lib/format';
import { useStoreSettings } from '@/lib/queries';

const SALARIES = 'الرواتب';
const MONTH = new Intl.DateTimeFormat('ar-EG-u-nu-latn', { month: 'long', year: 'numeric', timeZone: 'UTC' });

/** A month (yyyy-mm) as a range: its first day to its last, or to today while it is still running. */
function monthRange(month: string) {
  const [y, m] = month.split('-').map(Number);
  const last = new Date(Date.UTC(y, m, 0)).toISOString().slice(0, 10);
  const today = localToday();
  return { from: `${month}-01`, to: last > today ? today : last };
}

/**
 * «تقرير المالك»: one A4 page for the owner of the money — the month's profit, expenses and salaries, the money in each
 * account, and what is owed to and by the store. Printed or saved as PDF, or sent as a WhatsApp message. Reads only.
 */
export function OwnerReportPage() {
  const { can } = useAuth();
  const settings = useStoreSettings();
  const [month, setMonth] = useState(localToday().slice(0, 7));
  const range = monthRange(month);
  const period = useQuery({ queryKey: ['report', 'period', range], queryFn: () => reportsApi.period(range), placeholderData: keepPreviousData });
  const top = useQuery({
    queryKey: ['report', 'products', range, 'Profit', 5],
    queryFn: () => reportsApi.products(range, 'Profit', 5),
    enabled: can(P.ReportsProfit),
    placeholderData: keepPreviousData,
  });
  const p = period.data;
  // An account that held nothing and moved nothing all month is left out: the owner reads only where the money is.
  const accounts = p?.accounts?.filter((a) => a.openingBalance !== 0 || a.in !== 0 || a.out !== 0 || a.closingBalance !== 0) ?? null;
  const monthName = MONTH.format(new Date(`${month}-01T00:00:00Z`));

  return (
    <div className="mx-auto max-w-3xl space-y-4">
      <div className="flex flex-wrap items-end justify-between gap-3 print:hidden">
        <div className="flex items-end gap-2">
          <Link to="/reports" className="mb-2 text-muted hover:text-ink" aria-label="رجوع للتقارير"><ArrowRight className="size-5" /></Link>
          <label className="block">
            <span className="mb-1 block text-sm font-medium">الشهر</span>
            <Input type="month" value={month} max={localToday().slice(0, 7)} onChange={(e) => e.target.value && setMonth(e.target.value)} className="w-48" />
          </label>
        </div>
        <div className="flex gap-2">
          <Button variant="secondary" onClick={() => window.print()}><Printer className="size-4" /> طباعة / PDF</Button>
          <Button variant="secondary" disabled={!p}
            onClick={() => p && window.open(`https://wa.me/?text=${encodeURIComponent(whatsappText(p, settings.data?.storeName ?? '', monthName))}`, '_blank')}>
            <MessageCircle className="size-4" /> واتساب
          </Button>
        </div>
      </div>

      {period.isPending ? <Spinner /> : period.isError ? <ErrorState message={errorMessage(period.error)} onRetry={() => void period.refetch()} /> : p && (
        <article className={cn('space-y-5 rounded-card border border-line bg-surface p-5 print:border-0 print:p-0 sm:p-7', period.isPlaceholderData && 'opacity-60')}>
          <header className="flex items-start justify-between gap-4 border-b-2 border-ink pb-3">
            <div>
              <p className="text-xl font-bold">{settings.data?.storeName}</p>
              <p className="text-sm text-muted">تقرير المالك — {monthName}</p>
            </div>
            <p className="text-start text-xs text-muted">
              من {formatDate(p.from)} إلى {formatDate(p.to)}<br />أُعِدّ في {formatDate(localToday())}
            </p>
          </header>

          {/* The four figures the owner looks for first. */}
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
            <Figure label="المبيعات" value={p.netSales} hint={`${p.invoiceCount} فاتورة`} />
            {p.grossProfit != null && <Figure label="ربح البضاعة" value={p.grossProfit} hint="المبيعات − سعر الشراء" />}
            {p.expenses != null && <Figure label="المصروفات والرواتب" value={p.expenses} />}
            {p.netOperatingProfit != null && <Figure label="صافي الربح" value={p.netOperatingProfit} strong />}
          </div>

          <Block title="1. الربح">
            <Lines rows={[
              ['المبيعات قبل الخصم', p.grossSales],
              ['− الخصومات', p.discounts],
              ['− المرتجعات', p.returns],
              ['= صافي المبيعات', p.netSales, 'strong'],
              ...(p.costOfSales != null ? [['− تكلفة البضاعة المباعة (سعر الشراء)', p.costOfSales] as Row, ['= ربح البضاعة', p.grossProfit!, 'strong'] as Row] : []),
              ...(p.expenses != null ? [['− المصروفات والرواتب', p.expenses] as Row] : []),
              ...(p.netOperatingProfit != null ? [['= صافي الربح', p.netOperatingProfit, 'total'] as Row] : []),
            ]} />
            {p.netSales > 0 && p.netOperatingProfit != null && (
              <p className="mt-2 text-xs text-muted">نسبة صافي الربح من المبيعات: {Math.round((p.netOperatingProfit / p.netSales) * 1000) / 10}%</p>
            )}
          </Block>

          {p.expensesByCategory && (
            <Block title="2. المصروفات والرواتب">
              {p.expensesByCategory.length === 0 ? <p className="text-sm text-muted">لا مصروفات في هذا الشهر.</p> : (
                <Lines rows={[
                  ...[...p.expensesByCategory].sort((a, b) => Number(b.name === SALARIES) - Number(a.name === SALARIES) || b.amount - a.amount)
                    .map((e) => [e.name, e.amount, e.name === SALARIES ? 'strong' : null] as Row),
                  ['= المجموع', p.expenses ?? 0, 'total'],
                ]} />
              )}
            </Block>
          )}

          {accounts && (
            <Block title="3. المصاري في الحسابات">
              <div className="overflow-x-auto">
              <table className="w-full min-w-[480px] text-sm">
                <thead className="text-muted">
                  <tr className="border-b border-line">
                    <th className="py-1.5 text-start font-semibold">الحساب</th>
                    <th className="py-1.5 text-end font-semibold">أول الشهر</th>
                    <th className="py-1.5 text-end font-semibold">داخل</th>
                    <th className="py-1.5 text-end font-semibold">خارج</th>
                    <th className="py-1.5 text-end font-semibold">آخر الشهر</th>
                  </tr>
                </thead>
                <tbody className="tabular">
                  {accounts.map((a) => (
                    <tr key={a.accountId} className="border-b border-line/60">
                      <td className="py-1.5">{a.name}</td>
                      <td className="py-1.5 text-end">{formatMoney(a.openingBalance)}</td>
                      <td className="py-1.5 text-end">{formatMoney(a.in)}</td>
                      <td className="py-1.5 text-end">{formatMoney(a.out)}</td>
                      <td className={cn('py-1.5 text-end font-bold', a.closingBalance < 0 && 'text-danger')}>{formatMoney(a.closingBalance)}</td>
                    </tr>
                  ))}
                </tbody>
                <tfoot className="tabular font-bold">
                  <tr className="border-t-2 border-ink">
                    <td className="py-1.5">المجموع</td>
                    <td className="py-1.5 text-end">{formatMoney(sum(accounts, 'openingBalance'))}</td>
                    <td className="py-1.5 text-end">{formatMoney(sum(accounts, 'in'))}</td>
                    <td className="py-1.5 text-end">{formatMoney(sum(accounts, 'out'))}</td>
                    <td className="py-1.5 text-end">{formatMoney(sum(accounts, 'closingBalance'))}</td>
                  </tr>
                </tfoot>
              </table>
              </div>
              <p className="mt-2 text-xs text-muted">الداخل والخارج يشمل كل حركة: مبيعات، تحصيل، مشتريات، مصروفات، رواتب، وتحويل بين الحسابات.</p>
            </Block>
          )}

          {(p.receivables != null || p.inventoryValue != null) && (
            <Block title="4. لنا وعلينا (اليوم)">
              <Lines rows={[
                ...(p.receivables != null ? [['لنا: ديون على العملاء', p.receivables] as Row] : []),
                ...(p.pendingTransferAmount ? [[`لنا: حوالات بانتظار التحقق (${p.pendingTransferCount})`, p.pendingTransferAmount] as Row] : []),
                ...(p.payables != null ? [['علينا: مستحق للموردين', p.payables] as Row] : []),
                ...(p.inventoryValue != null ? [['البضاعة في المخزن (بسعر الشراء)', p.inventoryValue, 'strong'] as Row] : []),
                ...(p.inventoryLosses ? [['تالف ومفقود خلال الشهر (نقص من البضاعة)', p.inventoryLosses] as Row] : []),
              ]} />
            </Block>
          )}

          {top.data && top.data.length > 0 && (
            <Block title="5. أكثر المنتجات ربحًا">
              <TopProducts rows={top.data} />
            </Block>
          )}

          <p className="rounded-control bg-canvas p-3 text-xs leading-6 text-muted print:bg-transparent print:p-0">
            ملاحظة: الربح غير المصاري الموجودة في الحسابات. جزء من الربح ممكن يكون لسا دين على العملاء، أو صار بضاعة جديدة في المخزن.
            عشان هيك التقرير بيعرض الربح والحسابات و«لنا وعلينا» مع بعض.
          </p>
        </article>
      )}
    </div>
  );
}

type Row = [label: string, value: number, emphasis?: 'strong' | 'total' | null];

const sum = <T,>(rows: T[], key: keyof T) => rows.reduce((s, r) => s + Number(r[key]), 0);

function Figure({ label, value, hint, strong }: { label: string; value: number; hint?: string; strong?: boolean }) {
  return (
    <div className={cn('rounded-control border border-line p-3', strong && 'border-ink')}>
      <p className="text-xs text-muted">{label}</p>
      <p className={cn('tabular text-lg font-bold', value < 0 && 'text-danger')}>{formatMoney(value)}</p>
      {hint && <p className="text-xs text-muted">{hint}</p>}
    </div>
  );
}

function Block({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="break-inside-avoid">
      <h2 className="mb-2 font-bold">{title}</h2>
      {children}
    </section>
  );
}

function Lines({ rows }: { rows: Row[] }) {
  return (
    <dl className="text-sm">
      {rows.map(([label, value, emphasis]) => (
        <div key={label} className={cn('flex justify-between gap-3 border-b border-line/60 py-1.5',
          emphasis === 'strong' && 'font-semibold', emphasis === 'total' && 'border-b-0 border-t-2 border-t-ink text-base font-bold')}>
          <dt>{label}</dt>
          <dd className={cn('tabular', value < 0 && 'text-danger')}>{formatMoney(value)}</dd>
        </div>
      ))}
    </dl>
  );
}

function TopProducts({ rows }: { rows: ProductRow[] }) {
  return (
    <table className="w-full text-sm">
      <thead className="text-muted">
        <tr className="border-b border-line">
          <th className="py-1.5 text-start font-semibold">المنتج</th>
          <th className="py-1.5 text-end font-semibold">المبيعات</th>
          <th className="py-1.5 text-end font-semibold">الربح</th>
        </tr>
      </thead>
      <tbody className="tabular">
        {rows.map((r) => (
          <tr key={r.productId} className="border-b border-line/60">
            <td className="py-1.5">{r.name}</td>
            <td className="py-1.5 text-end">{formatMoney(r.revenue)}</td>
            <td className="py-1.5 text-end font-semibold">{formatMoney(r.profit)}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/** The same report as a short WhatsApp message. */
function whatsappText(p: PeriodReport, store: string, month: string) {
  const m = (v: number | null | undefined) => formatMoney(v);
  const lines = [`*${store} — تقرير ${month}*`, '', `المبيعات: ${m(p.netSales)} (${p.invoiceCount} فاتورة)`];
  if (p.grossProfit != null) lines.push(`ربح البضاعة: ${m(p.grossProfit)}`);
  if (p.expenses != null) lines.push(`المصروفات والرواتب: ${m(p.expenses)}`);
  if (p.netOperatingProfit != null) lines.push(`*صافي الربح: ${m(p.netOperatingProfit)}*`);
  if (p.expensesByCategory?.length) lines.push('', '*المصروفات:*', ...p.expensesByCategory.map((e) => `• ${e.name}: ${m(e.amount)}`));
  if (p.accounts?.length) {
    lines.push('', '*المصاري في الحسابات (آخر الشهر):*', ...p.accounts.filter((a) => a.closingBalance !== 0).map((a) => `• ${a.name}: ${m(a.closingBalance)}`));
    lines.push(`المجموع: ${m(sum(p.accounts, 'closingBalance'))}`);
  }
  if (p.receivables != null) lines.push('', `لنا عند العملاء: ${m(p.receivables)}`);
  if (p.payables != null) lines.push(`علينا للموردين: ${m(p.payables)}`);
  if (p.inventoryValue != null) lines.push(`البضاعة في المخزن: ${m(p.inventoryValue)}`);
  return lines.join('\n');
}
