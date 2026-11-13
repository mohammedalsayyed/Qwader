import { useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Download, FileSpreadsheet, ListFilter, MoreVertical, Package, Plus, SlidersHorizontal, Upload, X } from 'lucide-react';
import { productsApi, type ImportResult, type ProductSort, type StockFilter } from '@/api/catalog';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button, IconButton } from '@/components/ui/Button';
import { EmptyState, ErrorState, FilterChips, PageHeader, Pagination, SearchInput, Spinner } from '@/components/ui/Feedback';
import { Sheet } from '@/components/ui/Sheet';
import { useToast } from '@/components/ui/Toast';
import { CategoryPicker, ProductThumb, StockBadge, SupplierPicker } from '@/components/catalog/CatalogBits';
import { Field, Input, Select } from '@/components/ui/Field';
import { cn } from '@/lib/cn';
import { QuickProductSheet } from './QuickProductSheet';
import { BarcodeScannerSheet, ScanButton } from '@/components/ui/BarcodeScanner';
import { errorMessage } from '@/lib/forms';
import { formatMoney } from '@/lib/format';
import { useDebouncedValue } from '@/lib/hooks';
import { useLookups } from '@/lib/queries';

type Filter = StockFilter | 'Archived' | 'NoCost';
const FILTERS: { value: Filter; label: string }[] = [
  { value: 'All', label: 'الكل' },
  { value: 'Available', label: 'متوفر' },
  { value: 'Low', label: 'منخفض' },
  { value: 'Out', label: 'نفد' },
  { value: 'Archived', label: 'المؤرشفة' },
];
/** Products whose purchase price was never entered: their profit and stock value read as zero until it is. */
const NO_COST: { value: Filter; label: string } = { value: 'NoCost', label: 'بدون سعر شراء' };

const SORTS: { value: ProductSort; label: string }[] = [
  { value: 'Newest', label: 'الأحدث أولًا' },
  { value: 'PriceAsc', label: 'السعر: من الأقل للأعلى' },
  { value: 'PriceDesc', label: 'السعر: من الأعلى للأقل' },
  { value: 'Name', label: 'الاسم (أ ← ي)' },
  { value: 'QuantityDesc', label: 'الكمية: الأكثر أولًا' },
  { value: 'QuantityAsc', label: 'الكمية: الأقل أولًا' },
];

/** A price typed in the URL: a non-negative number, or nothing. */
const priceParam = (v: string | null) => (v !== null && v.trim() !== '' && Number(v) >= 0 ? Number(v) : undefined);

const PAGE_SIZE = 20;

export function ProductsPage() {
  const { can } = useAuth();
  const [params, setParams] = useSearchParams();
  const [search, setSearch] = useState(params.get('q') ?? '');
  const [menuOpen, setMenuOpen] = useState(false);
  const [importOpen, setImportOpen] = useState(false);
  const [adding, setAdding] = useState(params.get('add') === '1');
  const [scanning, setScanning] = useState(false);
  const [advancedOpen, setAdvancedOpen] = useState(false);
  const toast = useToast();
  const lookups = useLookups();

  const filter = (params.get('stock') as Filter | null) ?? 'All';
  const categoryId = params.get('categoryId') ? Number(params.get('categoryId')) : undefined;
  const page = Math.max(1, Number(params.get('page')) || 1);
  const term = useDebouncedValue(search.trim());
  const categoryName = lookups.data?.categories.find((c) => c.id === categoryId)?.name ?? null;
  const priceFrom = priceParam(params.get('priceFrom'));
  const priceTo = priceParam(params.get('priceTo'));
  const supplierId = params.get('supplierId') ? Number(params.get('supplierId')) : undefined;
  const supplierName = params.get('supplierName');
  const sort = (params.get('sort') as ProductSort | null) ?? 'Newest';
  /** The «بحث متقدم» choices in use, shown as removable chips under the search. */
  const advanced = [
    priceFrom !== undefined || priceTo !== undefined
      ? { key: 'price', label: priceFrom !== undefined && priceTo !== undefined ? `السعر ${formatMoney(priceFrom)} – ${formatMoney(priceTo)}`
          : priceFrom !== undefined ? `السعر من ${formatMoney(priceFrom)}` : `السعر حتى ${formatMoney(priceTo!)}`, clear: ['priceFrom', 'priceTo'] }
      : null,
    supplierId ? { key: 'supplier', label: `المورد: ${supplierName ?? supplierId}`, clear: ['supplierId', 'supplierName'] } : null,
    sort !== 'Newest' ? { key: 'sort', label: `الترتيب: ${SORTS.find((s) => s.value === sort)?.label ?? sort}`, clear: ['sort'] } : null,
  ].filter((x): x is { key: string; label: string; clear: string[] } => x !== null);

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

  /** Sets several URL params at once (the advanced search) and returns to page 1. */
  const setMany = (values: Record<string, string | undefined>) =>
    setParams((p) => {
      const next = new URLSearchParams(p);
      for (const [k, v] of Object.entries(values)) {
        if (v) next.set(k, v);
        else next.delete(k);
      }
      next.delete('page');
      next.delete('add');
      return next;
    }, { replace: true });

  const listParams = {
    search: term,
    categoryId,
    supplierId,
    priceFrom,
    priceTo,
    sort: sort === 'Newest' ? undefined : sort,
    stock: filter === 'Archived' || filter === 'NoCost' ? undefined : filter,
    archived: filter === 'Archived',
    noCost: filter === 'NoCost' || undefined,
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
              <div className="hidden gap-2 lg:flex">
                <Link to="/products/new" className="contents">
                  <Button variant="secondary">
                    <SlidersHorizontal className="size-5" /> إضافة منتج بكل التفاصيل
                  </Button>
                </Link>
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
        <div className="flex gap-2"><div className="min-w-0 flex-1"><SearchInput
          value={search}
          onChange={(v) => {
            setSearch(v);
            setParam('q', v.trim() || undefined);
          }}
          placeholder="ابحث بالاسم أو الكود أو الباركود"
          autoFocus={params.get('focus') === '1'}
        /></div>
          <ScanButton onClick={() => setScanning(true)} />
          <button type="button" onClick={() => setAdvancedOpen(true)} aria-label="بحث متقدم"
            className={cn('relative flex h-12 shrink-0 items-center gap-1.5 rounded-xl border px-3 text-sm font-semibold',
              advanced.length ? 'border-brand-600 bg-brand-50 text-brand-800' : 'border-line bg-surface text-brand-700')}>
            <ListFilter className="size-5" /> <span className="hidden sm:inline">بحث متقدم</span>
            {advanced.length > 0 && <span className="grid size-5 place-items-center rounded-full bg-brand-700 text-xs text-white">{advanced.length}</span>}
          </button>
        </div>
        {advanced.length > 0 && (
          <div className="flex flex-wrap gap-2">
            {advanced.map((a) => (
              <button key={a.key} type="button" onClick={() => setMany(Object.fromEntries(a.clear.map((k) => [k, undefined])))}
                className="flex items-center gap-1 rounded-full bg-brand-50 px-3 py-1 text-sm text-brand-800 hover:bg-brand-100">
                {a.label} <X className="size-4" aria-label="إزالة" />
              </button>
            ))}
            {advanced.length > 1 && (
              <button type="button" className="px-2 text-sm text-muted underline"
                onClick={() => setMany({ priceFrom: undefined, priceTo: undefined, supplierId: undefined, supplierName: undefined, sort: undefined })}>
                مسح الكل
              </button>
            )}
          </div>
        )}
        <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
          <div className="min-w-0 flex-1">
            <FilterChips value={filter} onChange={(v) => setParam('stock', v === 'All' ? undefined : v)} options={can(P.ProductsViewCost) ? [...FILTERS, NO_COST] : FILTERS} />
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
          <ul className={cn('grid grid-cols-1 gap-2 md:grid-cols-2', query.isPlaceholderData && 'opacity-60')}>
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
                      <span className="tabular text-[15px] font-bold text-brand-800">
                        {formatMoney(p.retailPrice)}
                        {p.minSalePrice > 0 && <span className="ms-1.5 whitespace-nowrap text-xs font-normal text-muted">أقل سعر {formatMoney(p.minSalePrice)}</span>}
                      </span>
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
          <MenuButton icon={<Download className="size-5" />} label={filter !== 'All' || term || categoryId || advanced.length ? 'تصدير القائمة إلى Excel (حسب الفلتر)' : 'تصدير القائمة إلى Excel'}
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
      {scanning && <BarcodeScannerSheet onDetected={(code) => { setSearch(code); setParam('q', code); }} onClose={() => setScanning(false)} />}
      {importOpen && <ImportSheet onClose={() => setImportOpen(false)} />}
      {advancedOpen && (
        <AdvancedSearchSheet
          initial={{ priceFrom, priceTo, supplier: supplierId ? { id: supplierId, name: supplierName ?? String(supplierId) } : null, sort }}
          onApply={(v) => {
            setMany({
              priceFrom: v.priceFrom?.toString(), priceTo: v.priceTo?.toString(),
              supplierId: v.supplier ? String(v.supplier.id) : undefined, supplierName: v.supplier?.name,
              sort: v.sort === 'Newest' ? undefined : v.sort,
            });
            setAdvancedOpen(false);
          }}
          onClose={() => setAdvancedOpen(false)}
        />
      )}
      {adding && <QuickProductSheet onClose={() => { setAdding(false); if (params.get('add')) setParam('add', undefined); }} />}
    </>
  );
}

interface AdvancedValues {
  priceFrom?: number;
  priceTo?: number;
  supplier: { id: number; name: string } | null;
  sort: ProductSort;
}

/** «بحث متقدم»: sale price range, supplier and order. The name, category and stock filters stay on the page itself. */
function AdvancedSearchSheet({ initial, onApply, onClose }: { initial: AdvancedValues; onApply: (v: AdvancedValues) => void; onClose: () => void }) {
  const [from, setFrom] = useState(initial.priceFrom?.toString() ?? '');
  const [to, setTo] = useState(initial.priceTo?.toString() ?? '');
  const [supplier, setSupplier] = useState(initial.supplier);
  const [sort, setSort] = useState<ProductSort>(initial.sort);
  const num = (s: string) => (s.trim() === '' || Number.isNaN(Number(s)) || Number(s) < 0 ? undefined : Number(s));
  const fromN = num(from);
  const toN = num(to);
  const swapped = fromN !== undefined && toN !== undefined && fromN > toN;

  return (
    <Sheet open onClose={onClose} title="بحث متقدم"
      footer={
        <div className="flex gap-2">
          <Button variant="secondary" onClick={() => { setFrom(''); setTo(''); setSupplier(null); setSort('Newest'); }}>مسح</Button>
          <Button block size="lg" disabled={swapped} onClick={() => onApply({ priceFrom: fromN, priceTo: toN, supplier, sort })}>عرض النتائج</Button>
        </div>
      }>
      <div className="space-y-4">
        <div>
          <p className="mb-2 font-semibold">سعر البيع (مفرق، للقطعة)</p>
          <div className="grid grid-cols-2 gap-3">
            <Field label="من">{(id) => <Input id={id} inputMode="decimal" className="ltr" value={from} onChange={(e) => setFrom(e.target.value)} placeholder="0" />}</Field>
            <Field label="إلى" error={swapped ? 'أقل من «من»' : undefined}>{(id) => <Input id={id} inputMode="decimal" className="ltr" invalid={swapped} value={to} onChange={(e) => setTo(e.target.value)} placeholder="بلا حد" />}</Field>
          </div>
        </div>
        <Field label="المورد">
          {(id) => <SupplierPicker id={id} value={supplier?.name ?? null} onSelect={(s) => setSupplier({ id: s.id, name: s.name })} onClear={() => setSupplier(null)} />}
        </Field>
        <Field label="الترتيب">
          {(id) => (
            <Select id={id} value={sort} onChange={(e) => setSort(e.target.value as ProductSort)}>
              {SORTS.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
            </Select>
          )}
        </Field>
      </div>
    </Sheet>
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
        toast.success(`تم استيراد ${r.created} منتج جديد وتحديث ${r.updated}${r.stockChanged ? ` — تعديل كمية ${r.stockChanged} منتج` : ''}`
          + `${r.categoriesAdded ? ` — ${r.categoriesAdded} صنف جديد` : ''}${r.unitsAdded ? ` — ${r.unitsAdded} وحدة جديدة` : ''}`);
        void queryClient.invalidateQueries({ queryKey: ['lookups'] });
        void queryClient.invalidateQueries({ queryKey: ['categories'] });
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
          <li>الكود الموجود مسبقًا يُحدِّث المنتج. بدون كود: إذا كان الاسم لمنتج موجود يُحدَّث، وإلا يُنشأ منتج جديد بكود تلقائي.</li>
          <li>إذا كان الاسم لأكثر من منتج (مقاسات أو ألوان) اكتب المقاس واللون والموديل أو الكود.</li>
          <li>الورقة الثانية «الأصناف والوحدات» فيها الأصناف والوحدات الموجودة وتظهر كقائمة في الملف. أي صنف أو وحدة جديدة تكتبها فيها (أو في عمود المنتج) تُضاف عند الاستيراد، حتى لو لم يستخدمها أي منتج.</li>
          <li>«الكمية الفعلية» تكون في ملف التصدير مساوية لـ«الكمية الحالية»: عدّلها فقط حيث تختلف الكمية على الرف، فيُسجَّل الفرق في جرد واحد. الصف الذي لم تعدّله لا يغيّر شيئًا. وللمنتج الجديد تصبح رصيده الافتتاحي.</li>
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
            {!!result.stockChanged && <> تعديل كمية {result.stockChanged} منتج.</>}
            {!!result.categoriesAdded && <> إضافة {result.categoriesAdded} صنف جديد.</>}
            {!!result.unitsAdded && <> إضافة {result.unitsAdded} وحدة جديدة.</>}
            {result.stockCountNumber && <> فروقات الكمية سُجّلت في الجرد <span className="ltr inline-block font-semibold">{result.stockCountNumber}</span> (إدارة المخزون ← الجرد).</>}
          </p>
        )}
      </div>
    </Sheet>
  );
}
