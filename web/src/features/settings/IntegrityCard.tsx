import { useMutation } from '@tanstack/react-query';
import { Link } from 'react-router';
import { CircleAlert, CircleCheck, ShieldCheck } from 'lucide-react';
import { api } from '@/api/client';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Feedback';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';
import { formatDateTime, formatMoney, formatQuantity } from '@/lib/format';

interface IntegrityIssue { label: string; stored: number; computed: number; link: string | null }
interface IntegrityCheck { name: string; explanation: string; checked: number; issueCount: number; issues: IntegrityIssue[] }
interface IntegrityReport { checkedAt: string; ok: boolean; checks: IntegrityCheck[] }

/** Quantities are shown as quantities, everything else as money. */
const QUANTITY_CHECKS = new Set(['كميات المخزون', 'الكميات المرتجعة']);

/**
 * «فحص سلامة الحسابات»: recomputes every stored balance from the movements behind it and shows any difference.
 * Read-only — nothing is corrected — so it can be run on live data at any time.
 */
export function IntegrityCard() {
  const run = useMutation({ mutationFn: () => api<IntegrityReport>('/settings/integrity') });
  const report = run.data;

  return (
    <Card className="space-y-3 p-5">
      <h2 className="flex items-center gap-2 font-bold"><ShieldCheck className="size-5" aria-hidden /> فحص سلامة الحسابات</h2>
      <p className="text-sm text-muted">
        يعيد حساب كل رصيد من الحركات التي خلفه ويقارنه بالمحفوظ: الحسابات المالية، ديون العملاء، مستحقات الموردين، كميات المخزون ومتوسط التكلفة،
        وإجماليات الفواتير. الفحص <b>للقراءة فقط</b> ولا يغيّر أي شيء.
      </p>
      <Button variant="secondary" loading={run.isPending} onClick={() => run.mutate()}>
        {report ? 'إعادة الفحص' : 'ابدأ الفحص'}
      </Button>
      {run.isError && <p role="alert" className="rounded-xl bg-red-50 p-3 text-sm text-danger">{errorMessage(run.error)}</p>}

      {report && (
        <div className="space-y-3">
          <p role="status" className={cn('flex items-center gap-2 rounded-xl p-3 font-semibold', report.ok ? 'bg-green-50 text-success' : 'bg-red-50 text-danger')}>
            {report.ok ? <CircleCheck className="size-5 shrink-0" /> : <CircleAlert className="size-5 shrink-0" />}
            {report.ok
              ? 'كل الأرصدة مطابقة لحركاتها.'
              : `يوجد ${report.checks.reduce((s, c) => s + c.issueCount, 0)} فرق — التفاصيل أدناه.`}
            <span className="ms-auto text-xs font-normal text-muted">{formatDateTime(report.checkedAt)}</span>
          </p>
          <ul className="divide-y divide-line rounded-xl border border-line">
            {report.checks.map((c) => (
              <li key={c.name} className="p-3">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <p className="font-semibold">{c.name}</p>
                    <p className="text-xs text-muted">{c.explanation}</p>
                  </div>
                  <span className={cn('shrink-0 rounded-full px-2.5 py-1 text-xs font-semibold', c.issueCount ? 'bg-red-50 text-danger' : 'bg-green-50 text-success')}>
                    {c.issueCount ? `${c.issueCount} فرق` : `سليم · ${c.checked}`}
                  </span>
                </div>
                {c.issues.length > 0 && (
                  <div className="mt-2 overflow-x-auto">
                    <table className="w-full text-sm">
                      <thead className="text-xs text-muted">
                        <tr><th className="py-1 text-start font-medium">البند</th><th className="py-1 text-start font-medium">المحفوظ</th>
                          <th className="py-1 text-start font-medium">من الحركات</th><th className="py-1 text-start font-medium">الفرق</th></tr>
                      </thead>
                      <tbody className="divide-y divide-line">
                        {c.issues.map((i, n) => {
                          const fmt = (v: number) => (QUANTITY_CHECKS.has(c.name) ? formatQuantity(v) : formatMoney(v));
                          return (
                            <tr key={n}>
                              <td className="py-1.5 pe-2">{i.link ? <Link to={i.link} className="text-brand-700 underline">{i.label}</Link> : i.label}</td>
                              <td className="tabular py-1.5 pe-2">{fmt(i.stored)}</td>
                              <td className="tabular py-1.5 pe-2">{fmt(i.computed)}</td>
                              <td className="tabular py-1.5 font-semibold text-danger">{fmt(i.stored - i.computed)}</td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                    {c.issueCount > c.issues.length && <p className="mt-1 text-xs text-muted">وأيضًا {c.issueCount - c.issues.length} غيرها.</p>}
                  </div>
                )}
              </li>
            ))}
          </ul>
        </div>
      )}
    </Card>
  );
}
