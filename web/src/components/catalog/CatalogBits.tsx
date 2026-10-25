import { useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { Camera, ImageOff, ImagePlus, Loader2, Package, Plus, Trash2 } from 'lucide-react';
import { categoriesApi, filesApi, productsApi, type ProductListItem, type StockStatus } from '@/api/catalog';
import type { LookupItem } from '@/api/endpoints';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button } from '@/components/ui/Button';
import { normalizeArabic } from '@/lib/arabic';
import { useLookups } from '@/lib/queries';
import { suppliersApi, type SupplierListItem } from '@/api/stock';
import { Badge } from '@/components/ui/Feedback';
import { Picker } from '@/components/ui/Picker';
import { useToast } from '@/components/ui/Toast';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';
import { formatMoney, formatQuantity } from '@/lib/format';

export function StockBadge({ status, quantity, unit }: { status: StockStatus; quantity: number; unit?: string }) {
  const tone = status === 'Out' ? 'danger' : status === 'Low' ? 'warning' : 'success';
  const label = status === 'Out' ? 'نفد' : status === 'Low' ? 'منخفض' : 'متوفر';
  return (
    <Badge tone={tone}>
      {label} · <span className="tabular ms-1">{formatQuantity(quantity)}</span>
      {unit && <span className="ms-1">{unit}</span>}
    </Badge>
  );
}

export function ProductThumb({ fileId, size = 'md' }: { fileId: string | null; size?: 'sm' | 'md' | 'lg' }) {
  const [failed, setFailed] = useState(false);
  const box = size === 'lg' ? 'size-28' : size === 'sm' ? 'size-10' : 'size-14';
  if (!fileId || failed)
    return (
      <span className={cn('flex shrink-0 items-center justify-center rounded-xl bg-canvas text-muted', box)}>
        {failed ? <ImageOff className="size-5" /> : <Package className="size-6" />}
      </span>
    );
  return (
    <img
      src={filesApi.url(fileId)}
      alt=""
      loading="lazy"
      onError={() => setFailed(true)}
      className={cn('shrink-0 rounded-xl bg-canvas object-cover', box)}
    />
  );
}

/** Shrinks camera photos before upload (phones produce 3–12 MB images; slow networks matter). */
async function downscale(file: File, maxSide = 1280): Promise<Blob> {
  if (!file.type.startsWith('image/')) return file;
  const bitmap = await createImageBitmap(file).catch(() => null);
  if (!bitmap) return file;
  const scale = Math.min(1, maxSide / Math.max(bitmap.width, bitmap.height));
  if (scale === 1 && file.size < 600_000) return file;
  const canvas = document.createElement('canvas');
  canvas.width = Math.round(bitmap.width * scale);
  canvas.height = Math.round(bitmap.height * scale);
  canvas.getContext('2d')!.drawImage(bitmap, 0, 0, canvas.width, canvas.height);
  return new Promise((resolve) => canvas.toBlob((b) => resolve(b ?? file), 'image/jpeg', 0.82));
}

/** Product photo from the camera or the gallery. */
export function ImagePicker({ value, onChange }: { value: string | null; onChange: (fileId: string | null) => void }) {
  const cameraRef = useRef<HTMLInputElement>(null);
  const galleryRef = useRef<HTMLInputElement>(null);
  const [uploading, setUploading] = useState(false);
  const toast = useToast();

  const handle = async (file: File | undefined) => {
    if (!file) return;
    setUploading(true);
    try {
      const blob = await downscale(file);
      const uploaded = await filesApi.upload(blob, file.name.replace(/\.\w+$/, '') + '.jpg', 'ProductImage');
      onChange(uploaded.id);
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      setUploading(false);
      if (cameraRef.current) cameraRef.current.value = '';
      if (galleryRef.current) galleryRef.current.value = '';
    }
  };

  return (
    <div className="flex items-center gap-3">
      <div className="relative">
        <ProductThumb fileId={value} size="lg" />
        {uploading && (
          <span className="absolute inset-0 flex items-center justify-center rounded-xl bg-white/70">
            <Loader2 className="size-6 animate-spin text-brand-700" />
          </span>
        )}
      </div>
      <div className="flex flex-col gap-2">
        <input ref={cameraRef} type="file" accept="image/*" capture="environment" hidden onChange={(e) => void handle(e.target.files?.[0])} />
        <input ref={galleryRef} type="file" accept="image/jpeg,image/png,image/webp" hidden onChange={(e) => void handle(e.target.files?.[0])} />
        <button type="button" disabled={uploading} onClick={() => cameraRef.current?.click()} className="flex h-11 items-center gap-2 rounded-xl border border-line px-3 text-sm font-medium">
          <Camera className="size-4" /> التقاط صورة
        </button>
        <button type="button" disabled={uploading} onClick={() => galleryRef.current?.click()} className="flex h-11 items-center gap-2 rounded-xl border border-line px-3 text-sm font-medium">
          <ImagePlus className="size-4" /> من المعرض
        </button>
        {value && (
          <button type="button" onClick={() => onChange(null)} className="flex h-9 items-center gap-2 px-1 text-sm text-danger">
            <Trash2 className="size-4" /> إزالة الصورة
          </button>
        )}
      </div>
    </div>
  );
}

export function ProductRow({ p, showCost }: { p: ProductListItem; showCost?: boolean }) {
  return (
    <div className="flex items-center gap-3">
      <ProductThumb fileId={p.imageFileId} size="sm" />
      <div className="min-w-0 flex-1">
        <p className="truncate font-semibold">{p.displayName}</p>
        <p className="truncate text-xs text-muted">
          <span className="ltr inline-block">{p.sku}</span> · {p.categoryName}
        </p>
      </div>
      <div className="shrink-0 text-end">
        <p className="tabular text-sm font-bold">{formatMoney(showCost && p.averageCost != null ? p.averageCost : p.retailPrice)}</p>
        <p className={cn('tabular text-xs', p.stockStatus === 'Out' ? 'text-danger' : p.stockStatus === 'Low' ? 'text-warning' : 'text-muted')}>
          {formatQuantity(p.quantity)} {p.unitName}
        </p>
      </div>
    </div>
  );
}

export function ProductPicker({
  value,
  onSelect,
  onClear,
  showCost,
  invalid,
  id,
  placeholder = 'ابحث بالاسم أو الكود أو الباركود',
}: {
  value: string | null;
  onSelect: (p: ProductListItem) => void;
  onClear?: () => void;
  showCost?: boolean;
  invalid?: boolean;
  id?: string;
  placeholder?: string;
}) {
  return (
    <Picker<ProductListItem>
      id={id}
      invalid={invalid}
      valueLabel={value}
      placeholder={placeholder}
      title="اختيار منتج"
      queryKey="products"
      search={async (q, signal) => (await productsApi.list({ search: q, pageSize: 30 }, signal)).items}
      getKey={(p) => p.id}
      renderItem={(p) => <ProductRow p={p} showCost={showCost} />}
      onSelect={onSelect}
      onClear={onClear}
    />
  );
}

export function SupplierPicker({
  value,
  onSelect,
  onClear,
  invalid,
  id,
}: {
  value: string | null;
  onSelect: (s: SupplierListItem) => void;
  onClear?: () => void;
  invalid?: boolean;
  id?: string;
}) {
  return (
    <Picker<SupplierListItem>
      id={id}
      invalid={invalid}
      valueLabel={value}
      placeholder="ابحث باسم المورد أو جواله"
      title="اختيار مورد"
      queryKey="suppliers"
      search={async (q, signal) => (await suppliersApi.list({ search: q, pageSize: 30, isActive: true }, signal)).items}
      getKey={(s) => s.id}
      renderItem={(s) => (
        <div className="flex items-center justify-between gap-3">
          <div className="min-w-0">
            <p className="truncate font-semibold">{s.name}</p>
            <p className="ltr text-xs text-muted">{s.phone ?? s.code}</p>
          </div>
          {s.balance !== 0 && <span className="tabular shrink-0 text-sm text-muted">{formatMoney(s.balance)}</span>}
        </div>
      )}
      onSelect={onSelect}
      onClear={onClear}
    />
  );
}

/**
 * Searchable category select over the cached lookups (categories are few, so filtering is local and instant).
 * Users who manage products can create a missing category straight from the search text.
 */
export function CategoryPicker({
  value,
  onSelect,
  onClear,
  invalid,
  id,
  placeholder = 'اختر الصنف',
  allowCreate = true,
}: {
  value: string | null;
  onSelect: (c: LookupItem) => void;
  onClear?: () => void;
  invalid?: boolean;
  id?: string;
  placeholder?: string;
  allowCreate?: boolean;
}) {
  const lookups = useLookups();
  const { can } = useAuth();
  const queryClient = useQueryClient();
  const toast = useToast();
  const [creating, setCreating] = useState(false);
  const categories = lookups.data?.categories ?? [];

  const create = async (name: string, select: (c: LookupItem) => void) => {
    setCreating(true);
    try {
      const created = await categoriesApi.create({ name, isActive: true });
      await queryClient.invalidateQueries({ queryKey: ['lookups'] });
      void queryClient.invalidateQueries({ queryKey: ['categories'] });
      toast.success(`تمت إضافة الصنف «${created.name}»`);
      select({ id: created.id, name: created.name });
    } catch (e) {
      toast.error(errorMessage(e));
    } finally {
      setCreating(false);
    }
  };

  return (
    <Picker<LookupItem>
      id={id}
      invalid={invalid}
      valueLabel={value}
      placeholder={placeholder}
      title="اختيار الصنف"
      queryKey={`categories-${lookups.dataUpdatedAt}`}
      search={async (q) => {
        const term = normalizeArabic(q);
        return term ? categories.filter((c) => normalizeArabic(c.name).includes(term)) : categories;
      }}
      getKey={(c) => c.id}
      renderItem={(c) => <span className="block py-1 font-medium">{c.name}</span>}
      onSelect={onSelect}
      onClear={onClear}
      footer={
        allowCreate && can(P.ProductsManage)
          ? (q, select) => {
              const exists = categories.some((c) => normalizeArabic(c.name) === normalizeArabic(q));
              if (!q || exists) return null;
              return (
                <Button variant="secondary" block className="mt-3" loading={creating} onClick={() => void create(q, select)}>
                  <Plus className="size-4" /> إضافة «{q}» كصنف جديد
                </Button>
              );
            }
          : undefined
      }
    />
  );
}
