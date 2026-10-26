import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Archive, ArchiveRestore, ArrowRight, Boxes, Copy, Pencil } from 'lucide-react';
import { productsApi } from '@/api/catalog';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button } from '@/components/ui/Button';
import { Badge, Card, ErrorState, Spinner } from '@/components/ui/Feedback';
import { InfoList, Stat, Tabs } from '@/components/ui/Layout';
import { useConfirm } from '@/components/ui/Confirm';
import { useToast } from '@/components/ui/Toast';
import { ProductThumb, StockBadge } from '@/components/catalog/CatalogBits';
import { AdjustmentSheet, MovementList } from '@/components/catalog/StockWidgets';
import { errorMessage } from '@/lib/forms';
import { formatDate, formatMoney, formatQuantity } from '@/lib/format';

type Tab = 'info' | 'movements';

export function ProductDetailPage() {
  const id = Number(useParams().id);
  const { can } = useAuth();
  const navigate = useNavigate();
  const toast = useToast();
  const confirm = useConfirm();
  const queryClient = useQueryClient();
  const [tab, setTab] = useState<Tab>('info');
  const [adjusting, setAdjusting] = useState(false);

  const query = useQuery({ queryKey: ['product', id], queryFn: () => productsApi.get(id) });

  const archive = useMutation({
    mutationFn: (archived: boolean) => (archived ? productsApi.archive(id) : productsApi.unarchive(id)),
    onSuccess: (_, archived) => {
      toast.success(archived ? 'تمت أرشفة المنتج' : 'تمت إعادة المنتج إلى القائمة');
      void queryClient.invalidateQueries({ queryKey: ['product', id] });
      void queryClient.invalidateQueries({ queryKey: ['products'] });
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  if (query.isPending) return <Spinner />;
  if (query.isError) return <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} />;
  const p = query.data;
  const costVisible = p.averageCost != null;

  return (
    <div className="space-y-4">
      <button type="button" onClick={() => navigate(-1)} className="flex h-10 items-center gap-1 text-sm text-muted">
        <ArrowRight className="size-4" /> رجوع
      </button>

      <Card className="flex gap-4 p-4">
        <ProductThumb fileId={p.imageFileId} size="lg" />
        <div className="min-w-0 flex-1">
          <h1 className="text-lg font-bold leading-snug sm:text-xl">{p.displayName}</h1>
          <p className="text-sm text-muted">
            <span className="ltr inline-block">{p.sku}</span>
            {p.barcode && <> · <span className="ltr inline-block">{p.barcode}</span></>}
          </p>
          <p className="text-sm text-muted">{p.categoryName}{p.brandName && ` · ${p.brandName}`}</p>
          <div className="mt-2 flex flex-wrap gap-1.5">
            <StockBadge status={p.stockStatus} quantity={p.quantity} unit={p.unitName} />
            {p.isArchived && <Badge tone="danger">مؤرشف</Badge>}
          </div>
        </div>
      </Card>

      <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
        <Stat label="سعر المفرق" value={formatMoney(p.retailPrice)} />
        <Stat label="سعر الجملة" value={formatMoney(p.wholesalePrice)} />
        {costVisible ? (
          <>
            <Stat label="متوسط التكلفة" value={formatMoney(p.averageCost)} />
            <Stat label="الربح المتوقع (مفرق)" value={p.expectedProfit != null ? formatMoney(p.expectedProfit) : '—'}
              hint={p.marginPercent != null ? `نسبة الربح ${p.marginPercent}%` : undefined}
              tone={p.expectedProfit != null && p.expectedProfit < 0 ? 'danger' : 'success'} />
          </>
        ) : (
          <Stat label="أقل سعر بيع" value={formatMoney(p.minSalePrice)} />
        )}
      </div>

      <div className="grid grid-cols-2 gap-2 sm:flex">
        {can(P.ProductsManage) && (
          <Link to={`/products/${p.id}/edit`} className="contents">
            <Button variant="secondary"><Pencil className="size-4" /> تعديل</Button>
          </Link>
        )}
        {can(P.InventoryAdjust) && (
          <Button variant="secondary" onClick={() => setAdjusting(true)}><Boxes className="size-4" /> حركة مخزون</Button>
        )}
        {can(P.ProductsManage) && !p.isArchived && (
          <Link to={`/products/new?variantOf=${p.parentProductId ?? p.id}`} className="contents">
            <Button variant="outline"><Copy className="size-4" /> متغير جديد</Button>
          </Link>
        )}
        {can(P.ProductsManage) && (
          <Button
            variant="outline"
            loading={archive.isPending}
            onClick={async () => {
              if (p.isArchived) return archive.mutate(false);
              const ok = await confirm({
                title: 'أرشفة المنتج',
                message: p.quantity > 0
                  ? `ما زال لهذا المنتج رصيد ${formatQuantity(p.quantity)} ${p.unitName}. الأرشفة تخفيه من القوائم والبيع ولا تحذف حركاته. متابعة؟`
                  : 'الأرشفة تخفي المنتج من القوائم والبيع، ويمكن إعادته لاحقًا. متابعة؟',
                confirmLabel: 'أرشفة',
                danger: true,
              });
              if (ok) archive.mutate(true);
            }}
          >
            {p.isArchived ? <><ArchiveRestore className="size-4" /> إلغاء الأرشفة</> : <><Archive className="size-4" /> أرشفة</>}
          </Button>
        )}
      </div>

      <Tabs value={tab} onChange={setTab} tabs={[{ value: 'info', label: 'التفاصيل' }, { value: 'movements', label: 'سجل الحركات' }]} />

      {tab === 'info' ? (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
          <Card className="px-4">
            <InfoList items={[
              { label: 'الصنف', value: p.categoryName },
              { label: 'وحدة القياس', value: p.unitName },
              p.brandName && { label: 'الماركة', value: p.brandName },
              p.model && { label: 'الموديل', value: p.model },
              p.color && { label: 'اللون', value: p.color },
              p.size && { label: 'المقاس', value: p.size },
              p.defaultSupplierName && { label: 'المورد', value: p.defaultSupplierName },
              { label: 'أقل سعر بيع', value: formatMoney(p.minSalePrice) },
              { label: 'حد التنبيه', value: <span className="tabular">{formatQuantity(p.reorderLevel)}</span> },
              costVisible && { label: 'آخر سعر شراء', value: formatMoney(p.purchasePrice) },
              costVisible && { label: 'قيمة المخزون بالتكلفة', value: formatMoney(p.stockValue) },
              costVisible && p.suggestedRetailPrice != null && { label: 'سعر مفرق مقترح', value: formatMoney(p.suggestedRetailPrice) },
              { label: 'تاريخ الإضافة', value: formatDate(p.createdAt) },
            ]} />
            {p.description && <p className="border-t border-line py-3 text-sm leading-6">{p.description}</p>}
          </Card>

          <div className="space-y-4">
            {p.stock.length > 1 && (
              <Card className="px-4">
                <h2 className="pt-3 font-bold">الرصيد حسب المستودع</h2>
                <InfoList items={p.stock.map((s) => ({ label: s.warehouseName, value: <span className="tabular">{formatQuantity(s.quantity)}</span> }))} />
              </Card>
            )}
            {(p.parentProductId || p.variants.length > 0) && (
              <Card className="p-4">
                <h2 className="mb-2 font-bold">المتغيرات (مقاسات / ألوان / موديلات)</h2>
                {p.parentDisplayName && (
                  <p className="mb-2 text-sm text-muted">
                    متغير من: <Link to={`/products/${p.parentProductId}`} className="text-brand-700 underline">{p.parentDisplayName}</Link>
                  </p>
                )}
                <ul className="divide-y divide-line">
                  {p.variants.map((v) => (
                    <li key={v.id}>
                      <Link to={`/products/${v.id}`} className="flex items-center justify-between gap-2 py-2.5">
                        <span className="min-w-0 truncate">{v.displayName}</span>
                        <span className="tabular shrink-0 text-sm text-muted">{formatMoney(v.retailPrice)} · {formatQuantity(v.quantity)}</span>
                      </Link>
                    </li>
                  ))}
                </ul>
              </Card>
            )}
          </div>
        </div>
      ) : (
        <MovementList productId={p.id} />
      )}

      {adjusting && (
        <AdjustmentSheet product={{ id: p.id, displayName: p.displayName, quantity: p.quantity, unitName: p.unitName }} onClose={() => setAdjusting(false)} />
      )}
    </div>
  );
}
