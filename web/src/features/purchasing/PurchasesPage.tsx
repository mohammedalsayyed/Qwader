import { useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Boxes, Plus } from 'lucide-react';
import { purchasesApi, type PurchaseStatus } from '@/api/stock';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button } from '@/components/ui/Button';
import { Badge, EmptyState, ErrorState, FilterChips, PageHeader, Pagination, SearchInput, Spinner } from '@/components/ui/Feedback';
import { DateRangeFilter, type DateRangeValue } from '@/components/ui/DateRange';
import { errorMessage } from '@/lib/forms';
import { formatDate, formatMoney } from '@/lib/format';
import { useDebouncedValue } from '@/lib/hooks';

const STATUS: Record<PurchaseStatus, { label: string; tone: 'warning' | 'success' | 'neutral' }> = {
  Draft: { label: 'مسودة', tone: 'warning' },
  Posted: { label: 'معتمدة', tone: 'success' },
  Voided: { label: 'ملغاة', tone: 'neutral' },
};

export function PurchaseStatusBadge({ status }: { status: PurchaseStatus }) {
  return <Badge tone={STATUS[status].tone}>{STATUS[status].label}</Badge>;
}

export function PurchasesPage() {
  const { can } = useAuth();
  const navigate = useNavigate();
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<PurchaseStatus | 'all'>('all');
  const [range, setRange] = useState<DateRangeValue>({ preset: 'all' });
  const [page, setPage] = useState(1);
  const term = useDebouncedValue(search.trim());

  const query = useQuery({
    queryKey: ['purchases', { term, status, range, page }],
    queryFn: () => purchasesApi.list({ search: term, status: status === 'all' ? undefined : status, from: range.from, to: range.to, page, pageSize: 25 }),
    placeholderData: keepPreviousData,
  });

  return (
    <>
      <PageHeader title="المشتريات" subtitle="فواتير شراء البضائع من الموردين" />
      <div className="mb-3 space-y-3">
        <SearchInput value={search} onChange={(v) => { setSearch(v); setPage(1); }} placeholder="رقم الفاتورة أو رقم فاتورة المورد أو اسمه" />
        <FilterChips value={status} onChange={(v) => { setStatus(v); setPage(1); }}
          options={[{ value: 'all', label: 'الكل' }, { value: 'Draft', label: 'المسودات' }, { value: 'Posted', label: 'المعتمدة' }, { value: 'Voided', label: 'الملغاة' }]} />
        <DateRangeFilter value={range} onChange={(r) => { setRange(r); setPage(1); }} />
      </div>
      {query.isPending ? (
        <Spinner />
      ) : query.isError ? (
        <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} />
      ) : query.data.items.length === 0 ? (
        <EmptyState icon={<Boxes className="size-10" />} title="لا توجد فواتير شراء" />
      ) : (
        <>
          <ul className="grid gap-2 md:grid-cols-2">
            {query.data.items.map((p) => (
              <li key={p.id}>
                <Link to={`/purchases/${p.id}`} className="block rounded-card border border-line bg-surface p-4 hover:border-brand-200">
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0">
                      <p className="truncate font-semibold">{p.supplierName}</p>
                      <p className="text-xs text-muted"><span className="ltr inline-block">{p.number}</span> · {formatDate(p.date)} · {p.lineCount} بند</p>
                    </div>
                    <PurchaseStatusBadge status={p.status} />
                  </div>
                  <div className="mt-2 flex items-end justify-between">
                    <span className="text-xs text-muted">
                      {p.total - p.paidAmount - p.returnedTotal > 0 && p.status === 'Posted' ? `متبقي ${formatMoney(p.total - p.paidAmount)}` : p.paidAmount >= p.total ? 'مدفوعة' : ''}
                    </span>
                    <span className="tabular text-lg font-bold">{formatMoney(p.total)}</span>
                  </div>
                </Link>
              </li>
            ))}
          </ul>
          <Pagination page={query.data.page} totalPages={query.data.totalPages} onChange={setPage} />
        </>
      )}
      {can(P.PurchasesManage) && (
        <Button size="lg" onClick={() => navigate('/purchases/new')} className="fixed bottom-fab end-4 z-30 rounded-full shadow-lg lg:static lg:mt-4">
          <Plus className="size-5" /> فاتورة شراء
        </Button>
      )}
    </>
  );
}
