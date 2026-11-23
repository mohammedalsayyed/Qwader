import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Lock, LockOpen } from 'lucide-react';
import { yearClosingApi, type YearClosingStatus } from '@/api/yearClosing';
import { Button } from '@/components/ui/Button';
import { Field, Input, Switch } from '@/components/ui/Field';
import { Card } from '@/components/ui/Feedback';
import { Sheet } from '@/components/ui/Sheet';
import { useToast } from '@/components/ui/Toast';
import { errorMessage } from '@/lib/forms';
import { toAsciiDigits } from '@/lib/arabic';
import { formatDateTime, formatMoney } from '@/lib/format';
import { cn } from '@/lib/cn';

/**
 * «إقفال السنة» (off by default). Closing keeps the balance sheet of the year's last day and locks the year: nothing dated in it
 * can be recorded, edited or voided until it is reopened. Off, no year is locked (even one closed before).
 */
export function YearClosingCard() {
  const toast = useToast();
  const queryClient = useQueryClient();
  const status = useQuery({ queryKey: ['year-closing'], queryFn: yearClosingApi.status });
  const [closing, setClosing] = useState(false);
  const [reopening, setReopening] = useState<number | null>(null);
  const done = (s: YearClosingStatus, message: string) => {
    queryClient.setQueryData(['year-closing'], s);
    toast.success(message);
  };
  const toggle = useMutation({
    mutationFn: yearClosingApi.setEnabled,
    onSuccess: (s) => done(s, s.enabled ? 'تم تفعيل إقفال السنة' : 'تم إيقاف إقفال السنة'),
    onError: (e) => toast.error(errorMessage(e)),
  });

  const s = status.data;
  return (
    <Card className="space-y-3 p-5">
      <h2 className="font-bold">إقفال السنة</h2>
      <p className="text-sm text-muted">
        بعد انتهاء السنة تقفلها: تُحفظ ميزانيتها في آخر يوم، ولا يمكن بعدها تسجيل أو تعديل أو إلغاء أي عملية بتاريخ فيها.
        الأرصدة (الحسابات، ديون العملاء والموردين، المخزون) تكمل في السنة الجديدة، وفواتير السنة المقفلة وتقاريرها تبقى ظاهرة كما هي.
        لا يُحذف شيء. الشرح كامل في «التعليمات».
      </p>
      {s && (
        <>
          <Switch checked={s.enabled} onChange={(v) => toggle.mutate(v)} label="تفعيل إقفال السنة"
            description={s.enabled ? 'مفعّل: السنوات المقفلة لا تقبل أي تعديل' : 'غير مفعّل: لا توجد سنة مقفلة، ويعمل النظام كالمعتاد'} />

          {s.enabled && (
            <div className="space-y-3 rounded-xl border border-line p-3">
              <p className="text-sm">
                {s.lockedThrough ? <>مقفل حتى نهاية <b>{s.lockedThrough}</b>.</> : 'لا توجد سنة مقفلة.'}{' '}
                {s.yearEnded
                  ? <>السنة التالية للإقفال: <b>{s.nextYear}</b>.</>
                  : <>سنة <b>{s.nextYear}</b> تُقفل بعد 31/12/{s.nextYear}.</>}
              </p>
              {s.blockers.length > 0 && (
                <ul className="list-disc space-y-1 rounded-lg bg-amber-50 p-3 ps-7 text-sm">
                  {s.blockers.map((b) => <li key={b}>{b}</li>)}
                </ul>
              )}
              <Button disabled={!s.canClose} onClick={() => setClosing(true)}><Lock className="size-4" /> إقفال سنة {s.nextYear}</Button>
            </div>
          )}

          {s.history.length > 0 && (
            <div className="space-y-2">
              <p className="text-sm font-semibold">السنوات المقفلة</p>
              <ul className="divide-y divide-line rounded-xl border border-line text-sm">
                {s.history.map((h) => (
                  <li key={h.id} className={cn('space-y-1 p-3', h.reopenedAt && 'text-muted')}>
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <span className="font-bold">
                        {h.year} {h.reopenedAt ? '— أُعيد فتحها' : '— مقفلة'}
                      </span>
                      {s.enabled && !h.reopenedAt && h.year === s.lockedThrough && (
                        <Button size="sm" variant="outline" onClick={() => setReopening(h.year)}><LockOpen className="size-4" /> إعادة فتح</Button>
                      )}
                    </div>
                    <p>
                      أُقفلت {formatDateTime(h.closedAt)}{h.closedBy && ` بواسطة ${h.closedBy}`} · صافي الربح <span className="tabular">{formatMoney(h.netProfit)}</span>
                      {' '}· الأصول <span className="tabular">{formatMoney(h.totalAssets)}</span>
                      {h.difference !== 0 && <span className="text-danger"> · فرق محاسبي {formatMoney(h.difference)}</span>}
                    </p>
                    {h.reopenedAt && (
                      <p>أُعيد فتحها {formatDateTime(h.reopenedAt)}{h.reopenedBy && ` بواسطة ${h.reopenedBy}`}: {h.reopenReason}</p>
                    )}
                  </li>
                ))}
              </ul>
            </div>
          )}
        </>
      )}
      {status.isError && <p className="text-sm text-danger">{errorMessage(status.error)}</p>}
      {closing && s && <CloseSheet year={s.nextYear} onClose={() => setClosing(false)} onDone={(n) => { setClosing(false); done(n, `تم إقفال سنة ${s.nextYear}`); }} />}
      {reopening && <ReopenSheet year={reopening} onClose={() => setReopening(null)} onDone={(n) => { done(n, `أُعيد فتح سنة ${reopening}`); setReopening(null); }} />}
    </Card>
  );
}

/** Confirmed by typing the year: closing locks a whole year of documents. */
function CloseSheet({ year, onClose, onDone }: { year: number; onClose: () => void; onDone: (s: YearClosingStatus) => void }) {
  const [typed, setTyped] = useState('');
  const [error, setError] = useState<string | null>(null);
  const close = useMutation({ mutationFn: () => yearClosingApi.close(year), onSuccess: onDone, onError: (e) => setError(errorMessage(e)) });
  const ok = toAsciiDigits(typed.trim()) === String(year);
  return (
    <Sheet open onClose={onClose} title={`إقفال سنة ${year}`} dirty={!!typed}
      footer={<Button block disabled={!ok} loading={close.isPending} onClick={() => close.mutate()}><Lock className="size-4" /> إقفال سنة {year}</Button>}>
      <div className="space-y-3 text-sm leading-6">
        <ul className="list-disc space-y-1 ps-5">
          <li>تُحفظ ميزانية <b>31/12/{year}</b> كما هي الآن.</li>
          <li>لا يمكن بعدها تسجيل أو تعديل أو إلغاء أي عملية بتاريخ في {year} أو قبلها (فواتير، سندات، مصروفات، مخزون).</li>
          <li>الأرصدة تكمل في السنة الجديدة: تحصيل دين قديم أو مرتجع على فاتورة قديمة يُسجَّل بتاريخ اليوم.</li>
          <li>يمكن إعادة فتحها لاحقًا مع كتابة السبب.</li>
        </ul>
        <Field label={`للتأكيد اكتب ${year}`} error={error ?? undefined}>
          {(id) => <Input id={id} inputMode="numeric" className="ltr" value={typed} onChange={(e) => { setTyped(e.target.value); setError(null); }} />}
        </Field>
      </div>
    </Sheet>
  );
}

function ReopenSheet({ year, onClose, onDone }: { year: number; onClose: () => void; onDone: (s: YearClosingStatus) => void }) {
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string | null>(null);
  const reopen = useMutation({ mutationFn: () => yearClosingApi.reopen(year, reason), onSuccess: onDone, onError: (e) => setError(errorMessage(e)) });
  return (
    <Sheet open onClose={onClose} title={`إعادة فتح سنة ${year}`} dirty={!!reason}
      footer={<Button block disabled={reason.trim().length < 3} loading={reopen.isPending} onClick={() => reopen.mutate()}><LockOpen className="size-4" /> إعادة فتح</Button>}>
      <div className="space-y-3 text-sm leading-6">
        <p>تصبح السنة مفتوحة للتعديل حتى تقفلها من جديد. يُسجَّل من أعاد فتحها والسبب.</p>
        <Field label="السبب" required error={error ?? undefined}>
          {(id) => <Input id={id} value={reason} maxLength={500} onChange={(e) => { setReason(e.target.value); setError(null); }} />}
        </Field>
      </div>
    </Sheet>
  );
}
