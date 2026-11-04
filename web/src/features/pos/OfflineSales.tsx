import { useEffect } from 'react';
import { Link } from 'react-router';
import { useQueryClient } from '@tanstack/react-query';
import { CloudOff, CloudUpload, RotateCcw, Trash2 } from 'lucide-react';
import { useOnline } from '@/app/pwa';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button } from '@/components/ui/Button';
import { Card, EmptyState, PageHeader } from '@/components/ui/Feedback';
import { useConfirm } from '@/components/ui/Confirm';
import { useToast } from '@/components/ui/Toast';
import { cn } from '@/lib/cn';
import { formatDateTime, formatMoney } from '@/lib/format';
import { discardQueued, refreshOfflineData, retryQueued, snapshotAge, syncQueue, useOfflineState } from '@/lib/offline';

const REFRESH_EVERY = 5 * 60_000;
const SYNC_EVERY = 30_000;

/** True when the till must work from the device's copy: no connection, or the server could not be reached at start. */
export function useIsOffline() {
  const { status } = useAuth();
  return !useOnline() || status === 'offline';
}

/**
 * Keeps the device's copy fresh and sends queued sales. Mounted once in the app layout (and in the offline shell).
 * Offline, it also lends the saved accounts and settings to the screens that read them, so checkout still opens.
 */
export function OfflineSync() {
  const { status, user, can } = useAuth();
  const online = useOnline();
  const queryClient = useQueryClient();
  const { loaded, snapshot } = useOfflineState();
  const canSell = can(P.SalesCreate);
  const ready = status === 'authenticated' && online && canSell && !!user;

  // Fresh copy now (if old or someone else's) and every few minutes.
  useEffect(() => {
    if (!ready || !loaded) return;
    const refresh = () => void refreshOfflineData(user).catch(() => undefined);
    const age = snapshot ? Date.now() - Date.parse(snapshot.generatedAt) : Infinity;
    if (!snapshot || snapshot.user.id !== user.id || age > REFRESH_EVERY) refresh();
    const timer = setInterval(refresh, REFRESH_EVERY);
    return () => clearInterval(timer);
    // Re-run only on session/connection changes, not every time the copy is refreshed.
  }, [ready, loaded, user?.id]);

  // Send queued sales when the connection returns, then every half minute while any wait.
  useEffect(() => {
    if (!ready || !loaded) return;
    const sync = async () => {
      if ((await syncQueue(user.id)) > 0) {
        for (const k of ['sales', 'sales-summary', 'products', 'customers', 'notifications', 'inventory-summary']) void queryClient.invalidateQueries({ queryKey: [k] });
        void refreshOfflineData(user).catch(() => undefined);
      }
    };
    void sync();
    const timer = setInterval(() => void sync(), SYNC_EVERY);
    return () => clearInterval(timer);
  }, [ready, loaded, user, queryClient]);

  // Offline: checkout reads accounts and settings through the usual queries.
  useEffect(() => {
    if (!snapshot) return;
    if (!queryClient.getQueryData(['lookups'])) queryClient.setQueryData(['lookups'], snapshot.lookups);
    if (!queryClient.getQueryData(['settings'])) queryClient.setQueryData(['settings'], snapshot.settings);
  }, [snapshot, queryClient]);

  return null;
}

/** Top-bar chip while this user has sales waiting to be sent. */
export function QueuedSalesChip() {
  const { user } = useAuth();
  const { queue } = useOfflineState();
  const mine = queue.filter((q) => q.userId === user?.id);
  if (mine.length === 0) return null;
  const failed = mine.some((q) => q.error);
  return (
    <Link to="/pos/queue" className={cn('inline-flex h-9 items-center gap-1.5 rounded-lg px-2.5 text-xs font-semibold',
      failed ? 'bg-red-50 text-danger' : 'bg-amber-50 text-amber-800')}>
      {failed ? <CloudOff className="size-4" /> : <CloudUpload className="size-4" />}
      {mine.length} {failed ? 'فاتورة لم تُرسل' : 'بانتظار الإرسال'}
    </Link>
  );
}

/** Sales made without internet that are still on this device: waiting, or refused by the server with its reason. */
export function OfflineQueuePage() {
  const { user } = useAuth();
  const offline = useIsOffline();
  const toast = useToast();
  const confirm = useConfirm();
  const { queue, snapshot } = useOfflineState();
  const mine = queue.filter((q) => q.userId === user?.id);
  const age = snapshotAge(snapshot);

  const sendNow = async () => {
    if (!user) return;
    const sent = await syncQueue(user.id);
    toast.success(sent > 0 ? `تم إرسال ${sent} فاتورة` : 'لم يُرسل شيء — تحقق من الاتصال');
  };

  return (
    <div className="mx-auto max-w-2xl space-y-4">
      <PageHeader title="فواتير بدون إنترنت" />
      <Card className="space-y-1 p-4 text-sm">
        <p>الفواتير التي تُعمل بدون إنترنت تُحفظ على هذا الجهاز وتُرسل تلقائيًا عند عودة الاتصال، وتأخذ رقمها النهائي عندها.</p>
        <p className="text-muted">
          {snapshot ? `آخر تحديث لبيانات البيع: ${formatDateTime(snapshot.generatedAt)}` : 'لا توجد بيانات بيع محفوظة على هذا الجهاز بعد.'}
          {snapshot && !age.usable && ' — انتهت صلاحيتها، اتصل بالإنترنت لتحديثها.'}
        </p>
      </Card>
      {mine.length === 0 ? (
        <EmptyState icon={<CloudUpload className="size-10" />} title="لا توجد فواتير بانتظار الإرسال" />
      ) : (
        <>
          {!offline && <Button block variant="secondary" onClick={() => void sendNow()}><CloudUpload className="size-4" /> إرسال الآن</Button>}
          <ul className="space-y-2">
            {mine.map((q) => (
              <li key={q.key} className={cn('rounded-card border bg-surface p-4', q.error ? 'border-red-200' : 'border-line')}>
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <p className="ltr text-start font-bold">{q.number}</p>
                    <p className="text-sm text-muted">{q.customerName} · {formatDateTime(q.createdAt)}</p>
                  </div>
                  <span className="tabular shrink-0 font-bold">{formatMoney(q.total)}</span>
                </div>
                {q.error ? (
                  <>
                    <p className="mt-2 rounded-lg bg-red-50 p-2.5 text-sm text-danger">رفضها الخادم: {q.error}</p>
                    <div className="mt-2 flex gap-2">
                      <Button size="sm" variant="secondary" onClick={() => void retryQueued(q.key)}><RotateCcw className="size-4" /> إعادة المحاولة</Button>
                      <Button size="sm" variant="ghost" className="text-danger"
                        onClick={async () => {
                          if (await confirm({ title: 'حذف الفاتورة', message: `لن تُسجَّل الفاتورة ${q.number} في النظام. البضاعة والمبلغ لن يظهرا في الحسابات.`, confirmLabel: 'حذف', danger: true }))
                            await discardQueued(q.key);
                        }}>
                        <Trash2 className="size-4" /> حذف
                      </Button>
                    </div>
                  </>
                ) : (
                  <p className="mt-2 text-xs text-amber-800">بانتظار الإرسال</p>
                )}
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  );
}
