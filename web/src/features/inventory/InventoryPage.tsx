import { useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ClipboardList, PackageCheck, Plus, SlidersHorizontal } from 'lucide-react';
import { productsApi } from '@/api/catalog';
import { inventoryApi } from '@/api/stock';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button } from '@/components/ui/Button';
import { Field } from '@/components/ui/Field';
import { Badge, Card, EmptyState, ErrorState, Pagination, Spinner } from '@/components/ui/Feedback';
import { Stat, Tabs } from '@/components/ui/Layout';
import { Sheet } from '@/components/ui/Sheet';
import { useToast } from '@/components/ui/Toast';
import { CategoryPicker, ProductThumb, StockBadge } from '@/components/catalog/CatalogBits';
import { AdjustmentSheet, MovementList } from '@/components/catalog/StockWidgets';
import { errorMessage } from '@/lib/forms';
import { formatDate, formatMoney, formatQuantity } from '@/lib/format';

type Tab = 'overview' | 'movements' | 'counts';

export function InventoryPage() {
  const { can } = useAuth();
  const [params, setParams] = useSearchParams();
  const tab = (params.get('tab') as Tab | null) ?? 'overview';
  const [adjusting, setAdjusting] = useState(false);

  const tabs: { value: Tab; label: string }[] = [
    { value: 'overview', label: 'نظرة عامة' },
    { value: 'movements', label: 'الحركات' },
    ...(can(P.InventoryCount) ? [{ value: 'counts' as const, label: 'الجرد' }] : []),
  ];

  return (
    <>
      <div className="mb-4 flex items-end justify-between gap-2">
        <h1 className="text-xl font-bold sm:text-2xl">إدارة المخزون</h1>
        {can(P.InventoryAdjust) && (
          <Button variant="secondary" onClick={() => setAdjusting(true)}>
            <SlidersHorizontal className="size-4" /> حركة مخزون
          </Button>
        )}
      </div>
      <Tabs tabs={tabs} value={tab} onChange={(t) => setParams({ tab: t }, { replace: true })} />
      {tab === 'overview' && <Overview />}
      {tab === 'movements' && <MovementList showProduct />}
      {tab === 'counts' && <Counts />}
      {adjusting && <AdjustmentSheet onClose={() => setAdjusting(false)} />}
    </>
  );
}

function Overview() {
  const navigate = useNavigate();
  const [page, setPage] = useState(1);
  const summary = useQuery({ queryKey: ['inventory-summary'], queryFn: inventoryApi.summary });
  const reorder = useQuery({
    queryKey: ['products', { stock: 'NeedsReorder', page }],
    queryFn: () => productsApi.list({ stock: 'NeedsReorder', page, pageSize: 20 }),
    placeholderData: keepPreviousData,
  });

  return (
    <div className="space-y-4">
      {summary.isError ? (
        <ErrorState message={errorMessage(summary.error)} onRetry={() => void summary.refetch()} />
      ) : (
        <div className="grid grid-cols-2 gap-2 lg:grid-cols-4">
          <Stat label="عدد المنتجات" value={summary.data?.productCount ?? '…'} onClick={() => navigate('/products')} />
          {summary.data?.stockValue != null && <Stat label="قيمة المخزون بالتكلفة" value={formatMoney(summary.data.stockValue)} />}
          <Stat label="منخفضة المخزون" value={summary.data?.lowCount ?? '…'} tone={summary.data?.lowCount ? 'warning' : 'default'} onClick={() => navigate('/products?stock=Low')} />
          <Stat label="نفدت من المخزون" value={summary.data?.outCount ?? '…'} tone={summary.data?.outCount ? 'danger' : 'default'} onClick={() => navigate('/products?stock=Out')} />
        </div>
      )}

      <section>
        <h2 className="mb-2 font-bold">منتجات تحتاج إعادة طلب</h2>
        {reorder.isPending ? (
          <Spinner />
        ) : reorder.isError ? (
          <ErrorState message={errorMessage(reorder.error)} onRetry={() => void reorder.refetch()} />
        ) : reorder.data.items.length === 0 ? (
          <EmptyState icon={<PackageCheck className="size-10" />} title="كل المنتجات فوق حد التنبيه" />
        ) : (
          <>
            <ul className="grid grid-cols-1 gap-2 md:grid-cols-2">
              {reorder.data.items.map((p) => (
                <li key={p.id}>
                  <Link to={`/products/${p.id}`} className="flex items-center gap-3 rounded-card border border-line bg-surface p-3">
                    <ProductThumb fileId={p.imageFileId} size="sm" />
                    <div className="min-w-0 flex-1">
                      <p className="truncate font-semibold">{p.displayName}</p>
                      <p className="text-xs text-muted">حد التنبيه: <span className="tabular">{formatQuantity(p.reorderLevel)}</span></p>
                    </div>
                    <StockBadge status={p.stockStatus} quantity={p.quantity} unit={p.unitName} />
                  </Link>
                </li>
              ))}
            </ul>
            <Pagination page={reorder.data.page} totalPages={reorder.data.totalPages} onChange={setPage} />
          </>
        )}
      </section>
    </div>
  );
}

const COUNT_STATUS = { Open: { label: 'مفتوح', tone: 'warning' }, Posted: { label: 'معتمد', tone: 'success' }, Cancelled: { label: 'ملغى', tone: 'neutral' } } as const;

function Counts() {
  const [page, setPage] = useState(1);
  const [creating, setCreating] = useState(false);
  const counts = useQuery({ queryKey: ['stock-counts', page], queryFn: () => inventoryApi.counts({ page }), placeholderData: keepPreviousData });

  return (
    <>
      <Button block variant="secondary" className="mb-3" onClick={() => setCreating(true)}>
        <Plus className="size-4" /> جرد جديد
      </Button>
      {counts.isPending ? (
        <Spinner />
      ) : counts.isError ? (
        <ErrorState message={errorMessage(counts.error)} onRetry={() => void counts.refetch()} />
      ) : counts.data.items.length === 0 ? (
        <EmptyState icon={<ClipboardList className="size-10" />} title="لا توجد عمليات جرد" description="الجرد يقارن الكميات الفعلية على الرفوف برصيد النظام ويسوّي الفرق." />
      ) : (
        <>
          <ul className="space-y-2">
            {counts.data.items.map((c) => (
              <li key={c.id}>
                <Link to={`/inventory/counts/${c.id}`}>
                  <Card className="flex items-center justify-between gap-3 p-4">
                    <div>
                      <p className="ltr font-semibold">{c.number}</p>
                      <p className="text-sm text-muted">{formatDate(c.createdAt)} · عُدّ {c.countedLines} من {c.lineCount}</p>
                      {c.notes && <p className="text-sm">{c.notes}</p>}
                    </div>
                    <Badge tone={COUNT_STATUS[c.status].tone}>{COUNT_STATUS[c.status].label}</Badge>
                  </Card>
                </Link>
              </li>
            ))}
          </ul>
          <Pagination page={counts.data.page} totalPages={counts.data.totalPages} onChange={setPage} />
        </>
      )}
      {creating && <NewCountSheet onClose={() => setCreating(false)} />}
    </>
  );
}

function NewCountSheet({ onClose }: { onClose: () => void }) {
  const navigate = useNavigate();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [category, setCategory] = useState<{ id: number; name: string } | null>(null);

  const create = useMutation({
    mutationFn: () => inventoryApi.createCount({ categoryId: category?.id ?? null }),
    onSuccess: (c) => {
      void queryClient.invalidateQueries({ queryKey: ['stock-counts'] });
      navigate(`/inventory/counts/${c.id}`);
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  return (
    <Sheet open onClose={onClose} title="جرد جديد"
      footer={<Button block size="lg" loading={create.isPending} onClick={() => create.mutate()}>بدء الجرد</Button>}>
      <div className="space-y-3">
        <Field label="المنتجات المشمولة">
          {(id) => (
            <CategoryPicker id={id} allowCreate={false} placeholder="كل المنتجات" value={category ? `صنف: ${category.name}` : null}
              onSelect={setCategory} onClear={() => setCategory(null)} />
          )}
        </Field>
        <p className="text-sm leading-6 text-muted">
          أدخل الكمية المعدودة لكل منتج، ويمكنك الحفظ والمتابعة لاحقًا. عند الاعتماد يُقارَن العدد برصيد النظام في تلك اللحظة، ويُسجَّل الفرق كزيادة أو نقص جرد.
        </p>
      </div>
    </Sheet>
  );
}
