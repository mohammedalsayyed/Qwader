import { useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Plus, Receipt } from 'lucide-react';
import { salesApi, SALE_STATUS, type SaleStatus } from '@/api/sales';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button } from '@/components/ui/Button';
import { Badge, EmptyState, ErrorState, FilterChips, PageHeader, Pagination, SearchInput, Spinner } from '@/components/ui/Feedback';
import { DateRangeFilter, resolveRange, type DateRangeValue } from '@/components/ui/DateRange';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';
import { formatDateTime, formatMoney } from '@/lib/format';
import { useDebouncedValue } from '@/lib/hooks';

type Filter = 'all' | 'unpaid' | SaleStatus;
const PAGE_SIZE = 25;

export function SalesPage() {
  const { can } = useAuth();
  const navigate = useNavigate();
  const [search, setSearch] = useState('');
  const [filter, setFilter] = useState<Filter>('all');
  const [range, setRange] = useState<DateRangeValue>({ preset: 'today', ...resolveRange('today') });
  const [page, setPage] = useState(1);
  const term = useDebouncedValue(search.trim());

  const query = useQuery({
    queryKey: ['sales', { term, filter, range, page }],
    queryFn: () => salesApi.list({
      search: term, page, pageSize: PAGE_SIZE, from: range.from, to: range.to,
      unpaid: filter === 'unpaid' || undefined,
      status: filter !== 'all' && filter !== 'unpaid' ? filter : undefined,
    }),
    placeholderData: keepPreviousData,
  });

  return (
    <>
      <PageHeader title="فواتير المبيعات" subtitle={can(P.SalesViewAll) ? undefined : 'فواتيرك أنت فقط'} />
      <div className="mb-3 space-y-3">
        <SearchInput value={search} onChange={(v) => { setSearch(v); setPage(1); }} placeholder="رقم الفاتورة أو اسم العميل أو جواله" />
        <DateRangeFilter value={range} onChange={(r) => { setRange(r); setPage(1); }} presets={['today', 'yesterday', 'last7', 'thisMonth', 'lastMonth', 'all', 'custom']} />
        <FilterChips value={filter} onChange={(v) => { setFilter(v); setPage(1); }}
          options={[{ value: 'all', label: 'الكل' }, { value: 'unpaid', label: 'غير مسددة' }, { value: 'PartiallyReturned', label: 'عليها مرتجع' }, { value: 'Voided', label: 'الملغاة' }]} />
      </div>

      {query.isPending ? (
        <Spinner />
      ) : query.isError ? (
        <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} />
      ) : query.data.items.length === 0 ? (
        <EmptyState icon={<Receipt className="size-10" />} title="لا توجد فواتير في هذه الفترة" />
      ) : (
        <>
          <ul className={cn('grid gap-2 md:grid-cols-2', query.isPlaceholderData && 'opacity-60')}>
            {query.data.items.map((s) => {
              const due = s.total - s.paidAmount;
              return (
                <li key={s.id}>
                  <Link to={`/sales/${s.id}`} className="block rounded-card border border-line bg-surface p-4 hover:border-brand-200">
                    <div className="flex items-start justify-between gap-2">
                      <div className="min-w-0">
                        <p className="truncate font-semibold">{s.customerName}</p>
                        <p className="text-xs text-muted"><span className="ltr inline-block">{s.number}</span> · {formatDateTime(s.date)}</p>
                        <p className="text-xs text-muted">{s.itemCount} بند · {s.salespersonName}</p>
                      </div>
                      <Badge tone={SALE_STATUS[s.status].tone}>{SALE_STATUS[s.status].label}</Badge>
                    </div>
                    <div className="mt-2 flex items-end justify-between">
                      <span className={cn('text-xs', due > 0 && s.status !== 'Voided' ? 'font-semibold text-danger' : 'text-muted')}>
                        {s.status === 'Voided' ? '' : due > 0 ? `آجل ${formatMoney(due)}` : 'مدفوعة'}
                      </span>
                      <span className={cn('tabular text-lg font-bold', s.status === 'Voided' && 'text-muted line-through')}>{formatMoney(s.total)}</span>
                    </div>
                  </Link>
                </li>
              );
            })}
          </ul>
          <Pagination page={query.data.page} totalPages={query.data.totalPages} totalCount={query.data.totalCount} pageSize={PAGE_SIZE} onChange={setPage} />
        </>
      )}

      {can(P.SalesCreate) && (
        <Button size="lg" onClick={() => navigate('/pos')} className="fixed bottom-fab end-4 z-30 rounded-full shadow-lg lg:static lg:mt-4">
          <Plus className="size-5" /> بيع جديد
        </Button>
      )}
    </>
  );
}
