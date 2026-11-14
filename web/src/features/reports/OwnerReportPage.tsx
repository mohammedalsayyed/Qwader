import { useState, type ReactNode } from 'react';
import { Link } from 'react-router';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { ArrowRight, MessageCircle, Printer } from 'lucide-react';
import { reportsApi, type OwnerReport } from '@/api/reports';
import { Button } from '@/components/ui/Button';
import { ErrorState, Spinner } from '@/components/ui/Feedback';
import { DateRangeFilter, resolveRange, type DateRangeValue } from '@/components/ui/DateRange';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';
import { formatDate, formatMoney, localToday } from '@/lib/format';
import { useStoreSettings } from '@/lib/queries';

const SALARIES = 'الرواتب';

/**
 * «تقرير المالك»: one A4 page for the owner of the money — what is in the accounts, the sales, the profit, expenses and
 * salaries, the debts both ways, and what customers paid on their debts. Printed / saved as PDF or sent on WhatsApp. Reads only.
 */
export function OwnerReportPage() {
  const settings = useStoreSettings();
  const [range, setRange] = useState<DateRangeValue>({ preset: 'today', ...resolveRange('today') });
  const r = { from: range.from, to: range.to };
  const query = useQuery({ queryKey: ['report', 'owner', r], queryFn: () => reportsApi.owner(r), placeholderData: keepPreviousData });
  const o = query.data;
  const p = o?.period;
  const oneDay = p ? p.from === p.to : true;
  const isToday = p?.to === localToday();
  const periodLabel = p ? (oneDay ? (isToday ? `اليوم ${formatDate(p.from)}` : formatDate(p.from)) : `من ${formatDate(p.from)} إلى ${formatDate(p.to)}`) : '';

  return (
    <div className="mx-auto max-w-3xl space-y-4">
      <div className="space-y-3 print:hidden">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <Link to="/reports" className="flex h-10 items-center gap-1 text-sm text-muted"><ArrowRight className="size-4" /> التقارير</Link>
          <div className="flex gap-2">
            <Button variant="secondary" onClick={() => window.print()}><Printer className="size-4" /> طباعة / PDF</Button>
            <Button variant="secondary" disabled={!o}
              onClick={() => o && window.open(`https://wa.me/?text=${encodeURIComponent(whatsappText(o, settings.data?.storeName ?? '', periodLabel))}`, '_blank')}>
              <MessageCircle className="size-4" /> واتساب
            </Button>
          </div>
        </div>
        <DateRangeFilter value={range} onChange={setRange} presets={['today', 'yesterday', 'thisMonth', 'lastMonth', 'custom']} />
      </div>

      {query.isPending ? <Spinner /> : query.isError ? <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} /> : o && p && (
        <article className={cn('space-y-6 rounded-card border border-line bg-surface p-5 print:border-0 print:p-0 sm:p-7', query.isPlaceholderData && 'opacity-60')}>
          <header className="flex items-start justify-between gap-4 border-b-2 border-ink pb-3">
            <div>
              <p className="text-xl font-bold">{settings.data?.storeName}</p>
              <p className="text-sm text-muted">تقرير المالك — {periodLabel}</p>
            </div>
            <p className="text-xs text-muted">أُعِدّ في {formatDate(localToday())}</p>
          </header>

          {/* 1. Money in the accounts, as at the end of the period (today: right now). */}
          {p.accounts && (() => {
            const held = p.accounts.filter((a) => a.closingBalance !== 0);
            const total = held.reduce((s, a) => s + a.closingBalance, 0);
            return (
              <Block title={isToday ? 'المصاري في الحسابات الآن' : `المصاري في الحسابات في ${formatDate(p.to)}`} total={total}>
                {held.length === 0 ? <Empty>لا يوجد رصيد في أي حساب.</Empty> : <Lines rows={held.map((a) => [a.name, a.closingBalance])} />}
              </Block>
            );
          })()}

          {/* 2–3. Sales and profit. */}
          <div className="grid gap-3 sm:grid-cols-2">
            <Figure label={oneDay ? 'مبيعات اليوم' : 'المبيعات'} value={p.netSales}
              hint={`${p.invoiceCount} فاتورة${p.returns > 0 ? ` · بعد مرتجعات ${formatMoney(p.returns)}` : ''}`} />
            {p.grossProfit != null && <Figure label="الربح" value={p.grossProfit} hint="المبيعات − سعر شراء البضاعة" />}
          </div>

          {/* 4. Expenses and salaries. */}
          {p.expensesByCategory && (() => {
            const salaries = p.expensesByCategory.find((e) => e.name === SALARIES)?.amount ?? 0;
            const others = p.expensesByCategory.filter((e) => e.name !== SALARIES && e.amount !== 0);
            return (
              <Block title="المصاريف والرواتب" total={p.expenses ?? 0}>
                <Lines rows={[[SALARIES, salaries, 'strong'], ...others.map((e) => [e.name, e.amount] as Row)]} />
                {p.netOperatingProfit != null && (
                  <p className="mt-2 flex justify-between text-sm text-muted">
                    <span>الربح بعد المصاريف والرواتب</span>
                    <span className={cn('tabular font-semibold', p.netOperatingProfit < 0 ? 'text-danger' : 'text-ink')}>{formatMoney(p.netOperatingProfit)}</span>
                  </p>
                )}
              </Block>
            );
          })()}

          {/* 5. Debts both ways, as of now. */}
          {p.receivables != null && (
            <section className="break-inside-avoid">
              <h2 className="mb-2 font-bold">الديون (حتى الآن)</h2>
              <div className="grid gap-3 sm:grid-cols-2">
                <div className="rounded-control border border-line p-3">
                  <p className="mb-1 text-sm font-semibold text-success">لنا (ديون للمعرض)</p>
                  <Lines rows={[
                    ['على العملاء', p.receivables],
                    ...(o.supplierAdvances ? [['عند الموردين (دفعنا مقدمًا)', o.supplierAdvances] as Row] : []),
                    ['المجموع', p.receivables + (o.supplierAdvances ?? 0), 'total'],
                  ]} />
                </div>
                <div className="rounded-control border border-line p-3">
                  <p className="mb-1 text-sm font-semibold text-danger">علينا (ديون على المعرض)</p>
                  <Lines rows={[
                    ['للموردين', p.payables ?? 0],
                    ['للعملاء (أرصدة لهم)', o.customerCredits ?? 0],
                    ['المجموع', (p.payables ?? 0) + (o.customerCredits ?? 0), 'total'],
                  ]} />
                </div>
              </div>
            </section>
          )}

          {/* 6. What customers paid on what they owed. */}
          {o.collections && (
            <Block title="دفعات من العملاء على ديونهم" total={o.collections.reduce((s, c) => s + c.amount, 0)}>
              {o.collections.length === 0 ? <Empty>لا دفعات على ديون في هذه الفترة.</Empty> : (
                <div className="overflow-x-auto">
                  <table className="w-full min-w-[480px] text-sm">
                    <thead className="text-muted">
                      <tr className="border-b border-line">
                        <th className="py-1.5 text-start font-semibold">العميل</th>
                        {!oneDay && <th className="py-1.5 text-start font-semibold">التاريخ</th>}
                        <th className="py-1.5 text-start font-semibold">إلى حساب</th>
                        <th className="py-1.5 text-end font-semibold">دفع</th>
                        <th className="py-1.5 text-end font-semibold">باقي عليه</th>
                      </tr>
                    </thead>
                    <tbody className="tabular">
                      {o.collections.map((c) => (
                        <tr key={c.id} className="border-b border-line/60">
                          <td className="py-1.5"><Link to={`/customers/${c.customerId}`} className="hover:text-brand-700">{c.customerName}</Link></td>
                          {!oneDay && <td className="py-1.5">{formatDate(c.date)}</td>}
                          <td className="py-1.5">{c.accountName}</td>
                          <td className="py-1.5 text-end font-semibold">{formatMoney(c.amount)}</td>
                          <td className="py-1.5 text-end text-muted">{c.owesNow > 0 ? formatMoney(c.owesNow) : 'سدّد'}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </Block>
          )}
        </article>
      )}
    </div>
  );
}

type Row = [label: string, value: number, emphasis?: 'strong' | 'total' | null];

function Figure({ label, value, hint }: { label: string; value: number; hint?: string }) {
  return (
    <div className="rounded-control border border-line p-4">
      <p className="text-sm text-muted">{label}</p>
      <p className={cn('tabular text-2xl font-bold', value < 0 && 'text-danger')}>{formatMoney(value)}</p>
      {hint && <p className="text-xs text-muted">{hint}</p>}
    </div>
  );
}

function Block({ title, total, children }: { title: string; total?: number; children: ReactNode }) {
  return (
    <section className="break-inside-avoid">
      <div className="mb-2 flex items-baseline justify-between gap-3 border-b border-ink pb-1">
        <h2 className="font-bold">{title}</h2>
        {total != null && <span className={cn('tabular text-lg font-bold', total < 0 && 'text-danger')}>{formatMoney(total)}</span>}
      </div>
      {children}
    </section>
  );
}

const Empty = ({ children }: { children: ReactNode }) => <p className="text-sm text-muted">{children}</p>;

function Lines({ rows }: { rows: Row[] }) {
  return (
    <dl className="text-sm">
      {rows.map(([label, value, emphasis]) => (
        <div key={label} className={cn('flex justify-between gap-3 border-b border-line/60 py-1.5 last:border-b-0',
          emphasis === 'strong' && 'font-semibold', emphasis === 'total' && 'font-bold')}>
          <dt>{label}</dt>
          <dd className={cn('tabular', value < 0 && 'text-danger')}>{formatMoney(value)}</dd>
        </div>
      ))}
    </dl>
  );
}

/** The same report as a short WhatsApp message. */
function whatsappText(o: OwnerReport, store: string, period: string) {
  const p = o.period;
  const m = formatMoney;
  const lines = [`*${store} — تقرير ${period}*`];
  if (p.accounts) {
    const held = p.accounts.filter((a) => a.closingBalance !== 0);
    lines.push('', '*المصاري في الحسابات:*', ...held.map((a) => `• ${a.name}: ${m(a.closingBalance)}`), `المجموع: ${m(held.reduce((s, a) => s + a.closingBalance, 0))}`);
  }
  lines.push('', `*المبيعات:* ${m(p.netSales)} (${p.invoiceCount} فاتورة)`);
  if (p.grossProfit != null) lines.push(`*الربح:* ${m(p.grossProfit)}`);
  if (p.expensesByCategory) {
    lines.push('', `*المصاريف والرواتب:* ${m(p.expenses)}`, ...p.expensesByCategory.filter((e) => e.amount !== 0).map((e) => `• ${e.name}: ${m(e.amount)}`));
    if (p.netOperatingProfit != null) lines.push(`الربح بعد المصاريف: ${m(p.netOperatingProfit)}`);
  }
  if (p.receivables != null) {
    lines.push('', '*الديون:*', `لنا على العملاء: ${m(p.receivables)}`);
    if (o.supplierAdvances) lines.push(`لنا عند الموردين: ${m(o.supplierAdvances)}`);
    lines.push(`علينا للموردين: ${m(p.payables)}`, `علينا للعملاء: ${m(o.customerCredits)}`);
  }
  if (o.collections?.length) {
    lines.push('', `*دفعات من العملاء على ديونهم:* ${m(o.collections.reduce((s, c) => s + c.amount, 0))}`,
      ...o.collections.map((c) => `• ${c.customerName}: ${m(c.amount)}${c.owesNow > 0 ? ` (باقي ${m(c.owesNow)})` : ' (سدّد)'}`));
  }
  return lines.join('\n');
}
