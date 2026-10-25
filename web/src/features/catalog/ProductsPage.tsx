import { useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Download, FileSpreadsheet, MoreVertical, Package, Plus, SlidersHorizontal, Upload } from 'lucide-react';
import { productsApi, type ImportResult, type StockFilter } from '@/api/catalog';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button, IconButton } from '@/components/ui/Button';
import { EmptyState, ErrorState, FilterChips, PageHeader, Pagination, SearchInput, Spinner } from '@/components/ui/Feedback';
import { Sheet } from '@/components/ui/Sheet';
import { useToast } from '@/components/ui/Toast';
import { CategoryPicker, ProductThumb, StockBadge } from '@/components/catalog/CatalogBits';
import { cn } from '@/lib/cn';
import { QuickProductSheet } from './QuickProductSheet';
import { errorMessage } from '@/lib/forms';
import { formatMoney } from '@/lib/format';
import { useDebouncedValue } from '@/lib/hooks';
import { useLookups } from '@/lib/queries';

type Filter = StockFilter | 'Archived';
const FILTERS: { value: Filter; label: string }[] = [
  { value: 'All', label: 'الكل' },
  { value: 'Available', label: 'متوفر' },
  { value: 'Low', label: 'منخفض' },
  { value: 'Out', label: 'نفد' },
  { value: 'Archived', label: 'المؤرشفة' },
];

const PAGE_SIZE = 20;

export function ProductsPage() {
  const { can } = useAuth();
  const [params, setParams] = useSearchParams();
  const [search, setSearch] = useState(params.get('q') ?? '');
  const [menuOpen, setMenuOpen] = useState(false);
  const [importOpen, setImportOpen] = useState(false);
  const [adding, setAdding] = useState(params.get('add') === '1');
  const toast = useToast();
  const lookups = useLookups();

  const filter = (params.get('stock') as Filter | null) ?? 'All';
  const categoryId = params.get('categoryId') ? Number(params.get('categoryId')) : undefined;
  const page = Math.max(1, Number(params.get('page')) || 1);
  const term = useDebouncedValue(search.trim());
  const categoryName = lookups.data?.categories.find((c) => c.id === categoryId)?.name ?? null;

  /** Updates URL filters; any filter change (other than the page itself) returns to page 1. */
  const setParam = (key: string, value: string | undefined) =>
    setParams((p) => {
      const next = new URLSearchParams(p);
      if (value) next.set(key, value);
      else next.delete(key);
      if (key !== 'page') next.delete('page');
      next.delete('add');
      return next;
    }, { replace: key !== 'page' });

  const listParams = {
    search: term,
    categoryId,
    stock: filter === 'Archived' ? undefined : filter,
    archived: filter === 'Archived',
  };

  const query = useQuery({
    queryKey: ['products', { ...listParams, page }],
    queryFn: ({ signal }) => productsApi.list({ ...listParams, page, pageSize: PAGE_SIZE }, signal),
    placeholderData: keepPreviousData,
  });

  const exportExcel = useMutation({
    mutationFn: () => productsApi.exportExcel(listParams),
    onError: (e) => toast.error(errorMessage(e)),
  });

  return (
    <>
      <PageHeader
        title="المنتجات"
        subtitle={query.data ? `${query.data.totalCount} منتج` : undefined}
        actions={
          <>
            {can(P.ProductsManage) && (
              <div className="hidden lg:block">
                <Button onClick={() => setAdding(true)}>
                  <Plus className="size-5" /> إضافة منتج
                </Button>
              </div>
            )}
            <IconButton label="خيارات إضافية" onClick={() => setMenuOpen(true)} className="border border-line bg-surface">
              <MoreVertical className="size-5" />
            </IconButton>
          </>
        }
      />

      <div className="mb-3 space-y-3">
        <SearchInput
          value={search}
          onChange={(v) => {
            setSearch(v);
            setParam('q', v.trim() || undefined);
          }}
          placeholder="ابحث بالاسم أو الكود أو الباركود"
          autoFocus={params.get('focus') === '1'}
        />
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
          <div className="min-w-0 flex-1">
            <FilterChips value={filter} onChange={(v) => setParam('stock', v === 'All' ? undefined : v)} options={FILTERS} />
          </div>
          <div className="sm:w-64">
            <CategoryPicker
              allowCreate={false}
              placeholder="كل الأصناف"
              value={categoryName}
              onSelect={(c) => setParam('categoryId', String(c.id))}
              onClear={() => setParam('categoryId', undefined)}
            />
          </div>
        </div>
      </div>

      {query.isPending ? (
        <Spinner />
      ) : query.isError ? (
        <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} />
      ) : query.data.items.length === 0 ? (
        <EmptyState
          icon={<Package className="size-10" />}
          title={term ? `لا توجد منتجات مطابقة لـ «${term}»` : 'لا توجد منتجات'}
          description={term ? 'جرّب البحث بالكود أو بجزء من الاسم' : undefined}
          action={can(P.ProductsManage) && !term && <Button onClick={() => setAdding(true)}><Plus className="size-4" /> إضافة منتج</Button>}
        />
      ) : (
        <>
          <ul className={cn('grid gap-2 md:grid-cols-2', query.isPlaceholderData && 'opacity-60')}>
            {query.data.items.map((p) => (
              <li key={p.id}>
                <Link to={`/products/${p.id}`} className="flex items-center gap-3 rounded-card border border-line bg-surface p-3 transition hover:border-brand-200 active:bg-brand-50">
                  <ProductThumb fileId={p.imageFileId} />
                  <div className="min-w-0 flex-1">
                    <p className="truncate font-semibold">{p.displayName}</p>
                    <p className="truncate text-xs text-muted">
                      <span className="ltr inline-block">{p.sku}</span> · {p.categoryName}
                    </p>
                    <div className="mt-1.5 flex items-center justify-between gap-2">
                      <span className="tabular text-[15px] font-bold text-brand-800">{formatMoney(p.retailPrice)}</span>
                      <StockBadge status={p.stockStatus} quantity={p.quantity} unit={p.unitName} />
                    </div>
                  </div>
                </Link>
              </li>
            ))}
          </ul>
          <Pagination
            page={query.data.page}
            totalPages={query.data.totalPages}
            totalCount={query.data.totalCount}
            pageSize={PAGE_SIZE}
            onChange={(p) => setParam('page', p > 1 ? String(p) : undefined)}
          />
        </>
      )}

      {can(P.ProductsManage) && (
        <Button size="lg" onClick={() => setAdding(true)} className="fixed bottom-fab end-4 z-30 rounded-full shadow-lg lg:hidden">
          <Plus className="size-5" /> إضافة منتج
        </Button>
      )}

      <Sheet open={menuOpen} onClose={() => setMenuOpen(false)} title="خيارات المنتجات">
        <div className="space-y-1">
          {can(P.ProductsManage) && (
            <Link to="/products/new" onClick={() => setMenuOpen(false)} className="flex h-13 w-full items-center gap-3 rounded-xl px-3 text-start hover:bg-canvas">
              <span className="text-brand-700"><SlidersHorizontal className="size-5" /></span> إضافة منتج بكل التفاصيل
            </Link>
          )}
          <MenuButton icon={<Download className="size-5" />} label="تصدير القائمة إلى Excel"
            onClick={() => { setMenuOpen(false); exportExcel.mutate(); }} />
          {can(P.ProductsManage) && (
            <>
              <MenuButton icon={<Upload className="size-5" />} label="استيراد منتجات من Excel"
                onClick={() => { setMenuOpen(false); setImportOpen(true); }} />
              <MenuButton icon={<FileSpreadsheet className="size-5" />} label="تنزيل نموذج الاستيراد"
                onClick={() => { setMenuOpen(false); productsApi.template().catch((e) => toast.error(errorMessage(e))); }} />
            </>
          )}
        </div>
      </Sheet>
      {importOpen && <ImportSheet onClose={() => setImportOpen(false)} />}
      {adding && <QuickProductSheet onClose={() => { setAdding(false); if (params.get('add')) setParam('add', undefined); }} />}
    </>
  );
}

function MenuButton({ icon, label, onClick }: { icon: React.ReactNode; label: string; onClick: () => void }) {
  return (
    <button type="button" onClick={onClick} className="flex h-13 w-full items-center gap-3 rounded-xl px-3 text-start hover:bg-canvas">
      <span className="text-brand-700">{icon}</span> {label}
    </button>
  );
}

function ImportSheet({ onClose }: { onClose: () => void }) {
  const [file, setFile] = useState<File | null>(null);
  const [result, setResult] = useState<ImportResult | null>(null);
  const toast = useToast();
  const queryClient = useQueryClient();

  const upload = useMutation({
    mutationFn: (f: File) => productsApi.importExcel(f),
    onSuccess: (r) => {
      setResult(r);
      if (r.succeeded) {
        toast.success(`تم استيراد ${r.created} منتج جديد وتحديث ${r.updated}`);
        void queryClient.invalidateQueries({ queryKey: ['products'] });
        void queryClient.invalidateQueries({ queryKey: ['lookups'] });
      }
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  return (
    <Sheet open onClose={onClose} title="استيراد منتجات من Excel" size="lg"
      footer={
        <Button block size="lg" disabled={!file} loading={upload.isPending} onClick={() => file && upload.mutate(file)}>
          استيراد
        </Button>
      }>
      <div className="space-y-4 text-[15px] leading-7">
        <ul className="list-disc space-y-1 ps-5 text-sm text-muted">
          <li>الأعمدة المطلوبة: اسم المنتج، الصنف، سعر المفرق. نزّل النموذج من قائمة الخيارات.</li>
          <li>الكود الموجود مسبقًا يُحدِّث المنتج، والكود الفارغ يُنشئ منتجًا جديدًا بكود تلقائي.</li>
          <li>الأصناف غير الموجودة تُنشأ تلقائيًا.</li>
          <li>إذا وُجد خطأ في أي صف لا يُستورد شيء، وتظهر الأخطاء برقم الصف لتصحيحها.</li>
        </ul>
        <label className="flex min-h-24 cursor-pointer flex-col items-center justify-center gap-2 rounded-2xl border-2 border-dashed border-line p-4 text-center hover:bg-canvas">
          <FileSpreadsheet className="size-8 text-brand-700" />
          <span className="font-medium">{file ? file.name : 'اختر ملف Excel (xlsx)'}</span>
          <input type="file" hidden accept=".xlsx,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            onChange={(e) => { setFile(e.target.files?.[0] ?? null); setResult(null); }} />
        </label>

        {result && !result.succeeded && (
          <div role="alert" className="rounded-2xl bg-red-50 p-4">
            <p className="mb-2 font-bold text-danger">لم يتم الاستيراد — صحّح الأخطاء التالية ثم أعد المحاولة:</p>
            <ul className="max-h-64 space-y-1 overflow-y-auto text-sm">
              {result.errors.map((e, i) => (
                <li key={i}>{e.row > 0 && <span className="font-semibold">صف {e.row}: </span>}{e.message}</li>
              ))}
            </ul>
          </div>
        )}
        {result?.succeeded && (
          <p className="rounded-2xl bg-green-50 p-4 font-medium text-success">
            تم بنجاح: {result.created} منتج جديد، و{result.updated} منتج محدَّث.
          </p>
        )}
      </div>
    </Sheet>
  );
}
