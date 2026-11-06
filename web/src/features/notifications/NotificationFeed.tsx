import { useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeftRight, Banknote, Bell, CheckCheck, ChevronLeft, PackageX, Receipt, Settings2 } from 'lucide-react';
import { feedApi, type FeedItem } from '@/api/notificationFeed';
import { useAuth } from '@/auth/AuthProvider';
import { Button } from '@/components/ui/Button';
import { Card, EmptyState, ErrorState, PageHeader, Pagination, Spinner } from '@/components/ui/Feedback';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';
import { formatDateTime } from '@/lib/format';

const PAGE_SIZE = 20;

/** The user's notifications, refreshed every minute and when the app comes back to the screen. */
export function useFeed(page = 1, pageSize = PAGE_SIZE) {
  const { status } = useAuth();
  return useQuery({
    queryKey: ['notification-feed', page, pageSize],
    queryFn: () => feedApi.list(page, pageSize),
    enabled: status === 'authenticated',
    refetchInterval: 60_000,
    refetchOnWindowFocus: true,
    placeholderData: keepPreviousData,
  });
}

function timeAgo(value: string) {
  const minutes = Math.floor((Date.now() - Date.parse(value)) / 60_000);
  if (minutes < 1) return 'الآن';
  if (minutes < 60) return `قبل ${minutes} د`;
  if (minutes < 24 * 60) return `قبل ${Math.floor(minutes / 60)} س`;
  return formatDateTime(value);
}

function iconFor(n: FeedItem) {
  if (n.url.startsWith('/account/payroll')) return { Icon: Banknote, tone: 'text-success' };
  if (n.topic === 'Transfers') return { Icon: ArrowLeftRight, tone: 'text-warning' };
  if (n.topic === 'Stock') return { Icon: PackageX, tone: 'text-danger' };
  if (n.topic === 'Sales') return { Icon: Receipt, tone: 'text-brand-700' };
  return { Icon: Bell, tone: 'text-muted' };
}

/** Opening a notification marks it read and goes where it points. */
function useOpen() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  return (n: FeedItem) => {
    if (!n.read) void feedApi.read(n.id).then(() => queryClient.invalidateQueries({ queryKey: ['notification-feed'] }));
    navigate(n.url);
  };
}

function FeedRow({ item: n, onOpen, compact = false }: { item: FeedItem; onOpen: (n: FeedItem) => void; compact?: boolean }) {
  const { Icon, tone } = iconFor(n);
  const lines = n.body.split('\n');
  return (
    <li>
      <button type="button" onClick={() => onOpen(n)}
        className={cn('flex w-full items-start gap-3 px-4 py-3 text-start hover:bg-canvas', !n.read && 'bg-brand-50/60')}>
        <Icon className={cn('mt-0.5 size-5 shrink-0', tone)} aria-hidden />
        <span className="min-w-0 flex-1">
          <span className="flex items-baseline justify-between gap-2">
            <span className={cn('truncate', n.read ? 'font-medium' : 'font-bold')}>{n.title}</span>
            <span className="shrink-0 text-xs text-muted">{timeAgo(n.createdAt)}</span>
          </span>
          {(compact ? lines.slice(0, 2) : lines).map((line, i) => (
            <span key={i} className="block truncate text-sm text-muted">{line}</span>
          ))}
        </span>
        {!n.read && <span className="mt-2 size-2 shrink-0 rounded-full bg-accent-600" aria-label="جديد" />}
      </button>
    </li>
  );
}

/** Dashboard card: the five latest, and «عرض المزيد» to the full list. */
export function LatestNotifications() {
  const feed = useFeed(1, 5);
  const open = useOpen();
  if (feed.isPending || feed.isError) return null;
  const { items, unreadCount, totalCount } = feed.data;
  return (
    <section aria-labelledby="latest-notifications">
      <div className="mb-2 flex items-center justify-between">
        <h2 id="latest-notifications" className="text-sm font-semibold text-muted">
          آخر الإشعارات{unreadCount > 0 && <span className="ms-2 rounded-full bg-accent-600 px-2 py-0.5 text-xs text-white">{unreadCount} جديد</span>}
        </h2>
        {totalCount > 0 && (
          <Link to="/notifications" className="flex h-9 items-center gap-1 text-sm font-semibold text-brand-700">
            عرض المزيد <ChevronLeft className="size-4" />
          </Link>
        )}
      </div>
      {items.length === 0 ? (
        <p className="rounded-card border border-dashed border-line p-4 text-center text-sm text-muted">لا توجد إشعارات بعد</p>
      ) : (
        <Card className="overflow-hidden"><ul className="divide-y divide-line">{items.map((n) => <FeedRow key={n.id} item={n} onOpen={open} compact />)}</ul></Card>
      )}
    </section>
  );
}

export function NotificationFeedPage() {
  const [page, setPage] = useState(1);
  const feed = useFeed(page);
  const open = useOpen();
  const queryClient = useQueryClient();
  const readAll = useMutation({
    mutationFn: feedApi.readAll,
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['notification-feed'] }),
  });

  return (
    <div className="mx-auto max-w-2xl">
      <PageHeader title="الإشعارات" subtitle="آخر 90 يومًا"
        actions={feed.data && feed.data.unreadCount > 0 && (
          <Button variant="outline" loading={readAll.isPending} onClick={() => readAll.mutate()}><CheckCheck className="size-4" /> تعليم الكل كمقروء</Button>
        )} />
      <Link to="/account/notifications" className="mb-3 flex items-center gap-2 text-sm text-brand-700">
        <Settings2 className="size-4" /> إشعارات الجوال على هذا الجهاز
      </Link>
      {feed.isPending ? <Spinner /> : feed.isError ? <ErrorState message={errorMessage(feed.error)} onRetry={() => void feed.refetch()} />
        : feed.data.items.length === 0 ? <EmptyState icon={<Bell className="size-10" />} title="لا توجد إشعارات بعد" />
        : (
          <>
            <Card className={cn('overflow-hidden', feed.isPlaceholderData && 'opacity-60')}>
              <ul className="divide-y divide-line">{feed.data.items.map((n) => <FeedRow key={n.id} item={n} onOpen={open} />)}</ul>
            </Card>
            <Pagination page={feed.data.page} totalPages={feed.data.totalPages} totalCount={feed.data.totalCount} pageSize={PAGE_SIZE} onChange={setPage} />
          </>
        )}
    </div>
  );
}
