import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { BellOff, BellRing, Send, Share } from 'lucide-react';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button } from '@/components/ui/Button';
import { Switch } from '@/components/ui/Field';
import { Card, ErrorState, PageHeader, Spinner } from '@/components/ui/Feedback';
import { useToast } from '@/components/ui/Toast';
import { errorMessage } from '@/lib/forms';
import { pushApi, pushSupport, type PushTopics } from '@/lib/push';

const TOPICS: { key: keyof PushTopics; label: string; description: string; permission: string }[] = [
  { key: 'transfers', label: 'الحوالات', description: 'كل حوالة جديدة بانتظار التحقق — من نقطة البيع أو شاشة الحوالات', permission: P.TransfersReview },
  { key: 'sales', label: 'المبيعات', description: 'كل فاتورة بيع جديدة وكل فاتورة تُلغى', permission: P.SalesViewAll },
  { key: 'stock', label: 'المخزون', description: 'عندما يقارب منتج على النفاد أو ينفد بعد البيع', permission: P.InventoryView },
];

/** Per-device phone notifications: turn on, choose topics, send a test, turn off. */
export function NotificationsPage() {
  const { user } = useAuth();
  const toast = useToast();
  const queryClient = useQueryClient();
  const support = pushSupport();
  const allowed = TOPICS.filter((t) => user?.permissions.includes(t.permission));
  const status = useQuery({ queryKey: ['push-status'], queryFn: pushApi.status, enabled: support === 'supported' });
  const [draft, setDraft] = useState<PushTopics>({ transfers: true, sales: true, stock: true });

  const subscribed = status.data?.topics ?? null;
  const topics = subscribed ?? draft;
  const denied = support === 'supported' && Notification.permission === 'denied';

  const save = useMutation({
    mutationFn: (next: PushTopics) => pushApi.enable(status.data!.publicKey, next),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['push-status'] }),
    onError: (e) =>
      toast.error(
        e instanceof Error && e.message === 'denied'
          ? 'رفضت الإذن بالإشعارات. فعّله من إعدادات المتصفح أو الجوال ثم حاول مرة أخرى'
          : e instanceof Error && e.message === 'dismissed'
            ? 'لم يتم منح الإذن بالإشعارات'
            : errorMessage(e),
      ),
  });
  const disable = useMutation({
    mutationFn: pushApi.disable,
    onSuccess: () => {
      toast.success('أُوقفت الإشعارات على هذا الجهاز');
      void queryClient.invalidateQueries({ queryKey: ['push-status'] });
    },
    onError: (e) => toast.error(errorMessage(e)),
  });
  const test = useMutation({
    mutationFn: pushApi.test,
    onSuccess: () => toast.success('أُرسل إشعار تجريبي، سيصل خلال ثوانٍ'),
    onError: (e) => toast.error(errorMessage(e)),
  });

  const toggle = (key: keyof PushTopics, value: boolean) => {
    const next = { ...topics, [key]: value };
    if (subscribed) save.mutate(next);
    else setDraft(next);
  };

  return (
    <div className="mx-auto max-w-xl space-y-4">
      <PageHeader title="الإشعارات" subtitle="تصل للجوال أو اللابتوب حتى لو كان التطبيق مسكّرًا" />

      {support === 'needs-install' && (
        <Card className="space-y-2 p-5">
          <h2 className="font-bold">ثبّت التطبيق أولًا</h2>
          <p className="text-sm leading-6 text-muted">
            على الآيفون تعمل الإشعارات فقط للتطبيق المثبّت على الشاشة الرئيسية: من Safari اضغط زر المشاركة
            <Share className="mx-1 inline size-4" aria-label="مشاركة" /> ثم «إضافة إلى الشاشة الرئيسية»، وافتح التطبيق من هناك وارجع لهذه الصفحة.
          </p>
        </Card>
      )}
      {support === 'unsupported' && (
        <Card className="p-5 text-sm text-muted">هذا المتصفح لا يدعم الإشعارات. استخدم Chrome أو Edge أو Safari حديث.</Card>
      )}

      {support === 'supported' &&
        (status.isPending ? (
          <Spinner />
        ) : status.isError ? (
          <ErrorState message={errorMessage(status.error)} onRetry={() => void status.refetch()} />
        ) : allowed.length === 0 ? (
          <Card className="p-5 text-sm text-muted">صلاحياتك الحالية لا تشمل أي نوع من الإشعارات.</Card>
        ) : (
          <>
            <Card className="space-y-1 p-5">
              <p className="mb-2 flex items-center gap-2 font-bold">
                {subscribed ? <BellRing className="size-5 text-brand-700" aria-hidden /> : <BellOff className="size-5 text-muted" aria-hidden />}
                {subscribed ? 'الإشعارات مفعّلة على هذا الجهاز' : 'الإشعارات غير مفعّلة على هذا الجهاز'}
              </p>
              {allowed.map((t) => (
                <Switch key={t.key} label={t.label} description={t.description} checked={topics[t.key]} onChange={(v) => toggle(t.key, v)} />
              ))}
            </Card>

            {denied && (
              <p className="rounded-xl bg-amber-50 p-3 text-sm leading-6 text-amber-800">
                الإشعارات محظورة لهذا الموقع. افتح إعدادات المتصفح (أو إعدادات الجوال ← الإشعارات) واسمح بها، ثم ارجع هنا.
              </p>
            )}

            {subscribed ? (
              <div className="grid gap-2 sm:grid-cols-2">
                <Button variant="secondary" loading={test.isPending} onClick={() => test.mutate()}>
                  <Send className="size-4" aria-hidden /> إرسال إشعار تجريبي
                </Button>
                <Button variant="outline" loading={disable.isPending} onClick={() => disable.mutate()}>
                  إيقاف الإشعارات هنا
                </Button>
              </div>
            ) : (
              <Button block size="lg" loading={save.isPending} disabled={denied || !allowed.some((t) => topics[t.key])}
                onClick={() => save.mutate(topics)}>
                <BellRing className="size-5" aria-hidden /> تفعيل الإشعارات على هذا الجهاز
              </Button>
            )}
            <p className="text-xs leading-5 text-muted">
              كل جهاز يُفعَّل لوحده (الجوال واللابتوب كل واحد على حدة). عند تسجيل الخروج تتوقف إشعارات هذا الجهاز.
            </p>
          </>
        ))}
    </div>
  );
}
