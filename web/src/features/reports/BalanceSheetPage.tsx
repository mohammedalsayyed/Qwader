import { useState, type ReactNode } from 'react';
import { Link } from 'react-router';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { AlertTriangle, CheckCircle2, Printer } from 'lucide-react';
import { reportsApi, type BalanceSection } from '@/api/reports';
import { IconButton } from '@/components/ui/Button';
import { ErrorState, PageHeader, Spinner } from '@/components/ui/Feedback';
import { Field, Input } from '@/components/ui/Field';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';
import { formatDate, formatMoney, localToday } from '@/lib/format';
import { useStoreSettings } from '@/lib/queries';
import { ProfitGate } from '@/features/profit/ProfitLock';

/**
 * «الميزانية العمومية»: what the store owns, owes and what is left for its owner, at the end of a chosen day — from the
 * store's own ledgers. The accounting difference is shown as it is; nothing is adjusted to make it balance.
 */
export function BalanceSheetPage() {
  const settings = useStoreSettings();
  const [date, setDate] = useState(localToday());
  const query = useQuery({ queryKey: ['balance-sheet', date], queryFn: () => reportsApi.balanceSheet(date), placeholderData: keepPreviousData });
  const b = query.data;
  const balanced = b ? Math.abs(b.difference) < 0.005 : true;

  return (
    <div className="mx-auto max-w-4xl space-y-4">
      <PageHeader title="الميزانية العمومية" subtitle={b ? `كما في نهاية ${formatDate(b.asOf)}` : undefined}
        actions={<IconButton label="طباعة / حفظ PDF" onClick={() => window.print()} className="border border-line bg-surface print:hidden"><Printer className="size-5" /></IconButton>} />
      <div className="print:hidden">
        <Field label="كما في تاريخ" className="w-48">
          {(id) => <Input id={id} type="date" max={localToday()} value={date} onChange={(e) => setDate(e.target.value || localToday())} />}
        </Field>
      </div>

      {/* The sheet is made of the profit: hidden until the user's password, like every profit on screen. */}
      <ProfitGate title="الميزانية العمومية مخفية">
      {query.isPending ? <Spinner /> : query.isError ? <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} /> : b && (
        <div className={cn('space-y-5', query.isPlaceholderData && 'opacity-60')}>
          <p className="hidden text-lg font-bold print:block">{settings.data?.storeName} — الميزانية العمومية كما في {formatDate(b.asOf)}</p>

          {/* The four figures, and whether the equation holds. */}
          <div className="grid grid-cols-2 gap-2 lg:grid-cols-4">
            <Figure label="إجمالي الأصول" value={b.totalAssets} />
            <Figure label="إجمالي الالتزامات" value={b.totalLiabilities} />
            <Figure label="إجمالي حقوق الملكية" value={b.totalEquity} hint="صافي قيمة المعرض لصاحبه" />
            <div className={cn('rounded-card border p-3', balanced ? 'border-green-300 bg-green-50' : 'border-red-300 bg-red-50')}>
              <p className="flex items-center gap-1 text-xs text-muted">
                {balanced ? <CheckCircle2 className="size-4 text-success" /> : <AlertTriangle className="size-4 text-danger" />} الفرق المحاسبي
              </p>
              <p className={cn('tabular text-xl font-bold', balanced ? 'text-success' : 'text-danger')}>{formatMoney(b.difference)}</p>
              <p className="text-xs text-muted">{balanced ? 'متوازنة: الأصول = الالتزامات + حقوق الملكية' : 'غير متوازنة — يُعرض الفرق كما هو'}</p>
            </div>
          </div>

          <div className="grid gap-5 lg:grid-cols-2">
            <Side title="أولًا: الأصول" total={b.totalAssets}>
              {b.assets.map((s) => <Section key={s.title} section={s} />)}
            </Side>
            <div className="space-y-5">
              <Side title="ثانيًا: الالتزامات" total={b.totalLiabilities}>
                {b.liabilities.map((s) => <Section key={s.title} section={s} />)}
              </Side>
              <Side title="ثالثًا: حقوق الملكية" total={b.totalEquity}>
                {b.equity.map((s) => <Section key={s.title} section={s} />)}
              </Side>
              <div className="rounded-card border border-ink/20 bg-canvas p-3">
                <div className="flex justify-between font-bold">
                  <span>الالتزامات + حقوق الملكية</span>
                  <span className="tabular">{formatMoney(b.totalLiabilities + b.totalEquity)}</span>
                </div>
              </div>
            </div>
          </div>

          <Side title={`صافي ربح ${new Date(`${b.yearStart}T00:00:00Z`).getUTCFullYear()} حتى التاريخ — التفصيل`}>
            <Lines lines={b.yearProfit.map((l) => ({ ...l, strong: l.name === 'صافي الربح' }))} />
            <p className="mt-2 text-xs text-muted">
              التالف والمفقود وفائض الجرد وتصحيح التكلفة لا تُحسب في الربح (تُغيّر رأس المال في البضاعة)؛ تظهر في حقوق الملكية كبنود مستقلة.
            </p>
          </Side>

          <div className="rounded-card border border-amber-200 bg-amber-50 p-3 text-sm leading-6">
            <p className="font-semibold">بنود غير مسجّلة في النظام (لا تظهر بأرقام):</p>
            <ul className="list-inside list-disc text-muted">{b.notRecorded.map((n) => <li key={n}>{n}</li>)}</ul>
          </div>
        </div>
      )}
      </ProfitGate>
    </div>
  );
}

function Figure({ label, value, hint }: { label: string; value: number; hint?: string }) {
  return (
    <div className="rounded-card border border-line bg-surface p-3">
      <p className="text-xs text-muted">{label}</p>
      <p className={cn('tabular text-xl font-bold', value < 0 && 'text-danger')}>{formatMoney(value)}</p>
      {hint && <p className="text-xs text-muted">{hint}</p>}
    </div>
  );
}

function Side({ title, total, children }: { title: string; total?: number; children: ReactNode }) {
  return (
    <section className="break-inside-avoid rounded-card border border-line bg-surface p-4">
      <div className="mb-2 flex items-baseline justify-between gap-3 border-b-2 border-ink pb-1">
        <h2 className="font-bold">{title}</h2>
        {total != null && <span className="tabular text-lg font-bold">{formatMoney(total)}</span>}
      </div>
      <div className="space-y-3">{children}</div>
    </section>
  );
}

function Section({ section }: { section: BalanceSection }) {
  return (
    <div>
      <div className="flex justify-between text-sm font-semibold text-muted">
        <span>{section.title}</span>
        <span className="tabular">{formatMoney(section.total)}</span>
      </div>
      <Lines lines={section.lines} />
    </div>
  );
}

function Lines({ lines }: { lines: { name: string; amount: number; link?: string | null; strong?: boolean }[] }) {
  return (
    <dl className="text-sm">
      {lines.map((l) => (
        <div key={l.name} className={cn('flex justify-between gap-3 border-b border-line/60 py-1.5 last:border-b-0', l.strong && 'font-bold')}>
          <dt className="min-w-0">{l.link ? <Link to={l.link} className="hover:text-brand-700 hover:underline print:no-underline">{l.name}</Link> : l.name}</dt>
          <dd className={cn('tabular shrink-0', l.amount < 0 && 'text-danger')}>{formatMoney(l.amount)}</dd>
        </div>
      ))}
    </dl>
  );
}
