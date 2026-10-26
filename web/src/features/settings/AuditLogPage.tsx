import { useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { ScrollText } from 'lucide-react';
import { api, type PagedResult } from '@/api/client';
import { EmptyState, ErrorState, FilterChips, PageHeader, Pagination, SearchInput, Spinner } from '@/components/ui/Feedback';
import { DateRangeFilter, resolveRange, type DateRangeValue } from '@/components/ui/DateRange';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';
import { formatDateTime } from '@/lib/format';
import { useDebouncedValue } from '@/lib/hooks';

interface AuditEntry {
  id: number;
  occurredAt: string;
  userName: string | null;
  action: string;
  actionName: string;
  entityType: string;
  entityId: string | null;
  oldValues: string | null;
  newValues: string | null;
  reason: string | null;
  ipAddress: string | null;
}

const AREAS = [
  { value: '', label: 'الكل' },
  { value: 'Sale', label: 'المبيعات' },
  { value: 'Purchase', label: 'المشتريات' },
  { value: 'Product', label: 'المنتجات' },
  { value: 'Inventory', label: 'المخزون' },
  { value: 'Transfer', label: 'الحوالات' },
  { value: 'Voucher', label: 'السندات' },
  { value: 'Account', label: 'الحسابات' },
  { value: 'User', label: 'المستخدمون' },
  { value: 'Auth', label: 'الدخول' },
];

/** Who did what and when: every sensitive operation, with before/after values and the reason given. */
export function AuditLogPage() {
  const [area, setArea] = useState('');
  const [search, setSearch] = useState('');
  const [range, setRange] = useState<DateRangeValue>({ preset: 'last7', ...resolveRange('last7') });
  const [page, setPage] = useState(1);
  const [open, setOpen] = useState<number | null>(null);
  const term = useDebouncedValue(search.trim());
  const query = useQuery({
    queryKey: ['audit', { area, term, range, page }],
    queryFn: () => api<PagedResult<AuditEntry>>('/audit-log', { query: { area, search: term, from: range.from, to: range.to, page, pageSize: 30 } }),
    placeholderData: keepPreviousData,
  });

  return (
    <>
      <PageHeader title="سجل التدقيق" subtitle="العمليات الحساسة: من قام بها ومتى وماذا تغيّر" />
      <div className="mb-3 space-y-3">
        <SearchInput value={search} onChange={(v) => { setSearch(v); setPage(1); }} placeholder="اسم المستخدم أو رقم مستند أو سبب" />
        <DateRangeFilter value={range} onChange={(r) => { setRange(r); setPage(1); }} presets={['today', 'last7', 'thisMonth', 'lastMonth', 'all', 'custom']} />
        <FilterChips value={area} onChange={(v) => { setArea(v); setPage(1); }} options={AREAS} />
      </div>
      {query.isPending ? <Spinner /> : query.isError ? <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} /> :
        query.data.items.length === 0 ? <EmptyState icon={<ScrollText className="size-10" />} title="لا توجد عمليات مسجلة" /> : (
          <>
            <ul className={cn('divide-y divide-line rounded-card border border-line bg-surface', query.isPlaceholderData && 'opacity-60')}>
              {query.data.items.map((a) => (
                <li key={a.id}>
                  <button type="button" onClick={() => setOpen(open === a.id ? null : a.id)} className="w-full p-3 text-start" aria-expanded={open === a.id}>
                    <div className="flex items-start justify-between gap-2">
                      <p className="font-semibold">{a.actionName}</p>
                      <p className="shrink-0 text-xs text-muted">{formatDateTime(a.occurredAt)}</p>
                    </div>
                    <p className="text-sm text-muted">{a.userName ?? 'النظام'}{a.entityId && <> · <span className="ltr inline-block">#{a.entityId}</span></>}</p>
                    {a.reason && <p className="text-sm">السبب: {a.reason}</p>}
                  </button>
                  {open === a.id && (
                    <div className="space-y-2 px-3 pb-3 text-xs">
                      {a.oldValues && <pre className="ltr overflow-x-auto rounded-lg bg-red-50 p-2 whitespace-pre-wrap break-all">{pretty(a.oldValues)}</pre>}
                      {a.newValues && <pre className="ltr overflow-x-auto rounded-lg bg-green-50 p-2 whitespace-pre-wrap break-all">{pretty(a.newValues)}</pre>}
                      {a.ipAddress && <p className="text-muted">IP: <span className="ltr inline-block">{a.ipAddress}</span></p>}
                    </div>
                  )}
                </li>
              ))}
            </ul>
            <Pagination page={query.data.page} totalPages={query.data.totalPages} totalCount={query.data.totalCount} pageSize={30} onChange={setPage} />
          </>
        )}
    </>
  );
}

function pretty(json: string) {
  try {
    return JSON.stringify(JSON.parse(json), null, 1);
  } catch {
    return json;
  }
}
