import { useEffect, useState } from 'react';
import { useLocation, useNavigate, useParams, useSearchParams } from 'react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { ArrowRight, Sparkles } from 'lucide-react';
import { productsApi, type ProductDetail, type SaveProductInput } from '@/api/catalog';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button } from '@/components/ui/Button';
import { Field, Input, Select, TextArea } from '@/components/ui/Field';
import { Card, ErrorState, PageHeader, Spinner } from '@/components/ui/Feedback';
import { Collapsible, StickyActions } from '@/components/ui/Layout';
import { useToast } from '@/components/ui/Toast';
import { ImagePicker, SupplierPicker } from '@/components/catalog/CatalogBits';
import { Controller } from 'react-hook-form';
import { CategoryPicker } from '@/components/catalog/CatalogBits';
import type { QuickProductDraft } from './QuickProductSheet';
import { applyServerErrors, blankToNull, errorMessage } from '@/lib/forms';
import { formatMoney } from '@/lib/format';
import { useUnsavedChangesWarning } from '@/lib/hooks';
import { suggestPrice, zAmount, zOptionalAmount, zPositive, parseNumber } from '@/lib/numbers';
import { useLookups, useStoreSettings } from '@/lib/queries';

const text = (max: number) => z.string().trim().max(max, 'النص طويل جدًا').optional();

const schema = z
  .object({
    name: z.string().trim().min(1, 'اسم المنتج مطلوب').max(150, 'اسم المنتج طويل جدًا'),
    categoryId: z.preprocess((v) => (v === '' || v == null ? undefined : Number(v)), z.number({ required_error: 'اختر الصنف', invalid_type_error: 'اختر الصنف' }).min(1, 'اختر الصنف')),
    retailPrice: zPositive('سعر المفرق مطلوب'),
    wholesalePrice: zOptionalAmount(),
    minSalePrice: zOptionalAmount(),
    purchasePrice: zOptionalAmount(),
    reorderLevel: zAmount('حد التنبيه مطلوب'),
    openingQuantity: zOptionalAmount(),
    openingUnitCost: zOptionalAmount(),
    sku: z.string().trim().max(50).regex(/^\S*$/, 'الكود يجب ألا يحتوي على مسافات').optional(),
    barcode: z.string().trim().max(50).regex(/^\S*$/, 'الباركود يجب ألا يحتوي على مسافات').optional(),
    unitId: z.preprocess((v) => (v === '' || v == null ? undefined : Number(v)), z.number({ required_error: 'اختر وحدة القياس' }).min(1, 'اختر وحدة القياس')),
    brandName: text(100),
    size: text(50),
    color: text(50),
    model: text(50),
    description: text(1000),
  })
  .refine((v) => v.minSalePrice == null || v.minSalePrice <= v.retailPrice, { path: ['minSalePrice'], message: 'أقل سعر بيع يجب ألا يتجاوز سعر المفرق' });

type FormInput = z.input<typeof schema>;
type FormValues = z.output<typeof schema>;
const FIELDS = ['name', 'categoryId', 'retailPrice', 'wholesalePrice', 'minSalePrice', 'purchasePrice', 'reorderLevel', 'openingQuantity',
  'openingUnitCost', 'sku', 'barcode', 'unitId', 'brandName', 'size', 'color', 'model', 'description'] as const;

const str = (n: number | null | undefined) => (n == null ? '' : String(n));

export function ProductFormPage() {
  const params = useParams();
  const [search] = useSearchParams();
  const editId = params.id ? Number(params.id) : undefined;
  const variantOf = search.get('variantOf') ? Number(search.get('variantOf')) : undefined;
  const sourceId = editId ?? variantOf;

  const source = useQuery({ queryKey: ['product', sourceId], queryFn: () => productsApi.get(sourceId!), enabled: !!sourceId });
  const lookups = useLookups();
  const settings = useStoreSettings();

  if ((sourceId && source.isPending) || lookups.isPending || settings.isPending) return <Spinner />;
  if (source.isError) return <ErrorState message={errorMessage(source.error)} onRetry={() => void source.refetch()} />;
  if (lookups.isError) return <ErrorState message={errorMessage(lookups.error)} onRetry={() => void lookups.refetch()} />;

  return (
    <ProductForm
      key={`${editId}-${variantOf}`}
      existing={editId ? source.data : undefined}
      variantParent={variantOf ? source.data : undefined}
      margin={settings.data?.defaultMarginPercent ?? 25}
      defaultReorder={settings.data?.defaultReorderLevel ?? 5}
    />
  );
}

function ProductForm({ existing, variantParent, margin, defaultReorder }: {
  existing?: ProductDetail;
  variantParent?: ProductDetail;
  margin: number;
  defaultReorder: number;
}) {
  const navigate = useNavigate();
  const toast = useToast();
  const queryClient = useQueryClient();
  const { can } = useAuth();
  const lookups = useLookups();
  // Values carried over from the quick-add sheet («تفاصيل أكثر»).
  const draft = (useLocation().state as { draft?: QuickProductDraft } | null)?.draft;
  const [categoryName, setCategoryName] = useState<string | null>(existing?.categoryName ?? variantParent?.categoryName ?? draft?.category?.name ?? null);
  const [imageId, setImageId] = useState<string | null>(existing?.imageFileId ?? null);
  const [supplier, setSupplier] = useState<{ id: number; name: string } | null>(
    existing?.defaultSupplierId ? { id: existing.defaultSupplierId, name: existing.defaultSupplierName ?? '' } :
      variantParent?.defaultSupplierId ? { id: variantParent.defaultSupplierId, name: variantParent.defaultSupplierName ?? '' } : null);

  const isNew = !existing;
  const base = existing ?? variantParent;
  const canSeeCost = can(P.ProductsViewCost);
  const canEditPrices = isNew || can(P.ProductsEditPrices);
  const canOpenStock = isNew && can(P.InventoryAdjust, P.PurchasesManage);

  const { register, control, handleSubmit, setValue, setError, formState: { errors, isDirty, isSubmitting } } = useForm<FormInput, unknown, FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: base?.name ?? draft?.name ?? '',
      categoryId: base?.categoryId ?? draft?.category?.id ?? '',
      retailPrice: base ? str(base.retailPrice) : draft?.retailPrice ?? '',
      wholesalePrice: str(base?.wholesalePrice),
      minSalePrice: str(base?.minSalePrice),
      purchasePrice: base ? str(base.purchasePrice) : draft?.purchasePrice ?? '',
      reorderLevel: str(base?.reorderLevel ?? defaultReorder),
      openingQuantity: draft?.quantity ?? '',
      openingUnitCost: '',
      sku: existing?.sku ?? '',
      barcode: existing?.barcode ?? '',
      unitId: base?.unitId ?? lookups.data?.units.find((u) => u.name === 'قطعة')?.id ?? lookups.data?.units[0]?.id ?? '',
      brandName: base?.brandName ?? '',
      size: existing?.size ?? '',
      color: existing?.color ?? '',
      model: existing?.model ?? '',
      description: base?.description ?? '',
    },
  });
  useUnsavedChangesWarning(isDirty);

  const [purchaseRaw, retailRaw] = useWatch({ control, name: ['purchasePrice', 'retailPrice'] });
  const cost = parseNumber(purchaseRaw) || (existing?.averageCost ?? 0);
  const retail = parseNumber(retailRaw) ?? 0;
  const suggested = suggestPrice(cost, margin);
  const profit = cost > 0 && retail > 0 ? retail - cost : null;

  const save = useMutation({
    mutationFn: (v: FormValues) => {
      const input: SaveProductInput = {
        name: v.name,
        sku: blankToNull(v.sku),
        barcode: blankToNull(v.barcode),
        categoryId: v.categoryId,
        brandName: blankToNull(v.brandName),
        unitId: v.unitId,
        defaultSupplierId: supplier?.id ?? null,
        parentProductId: existing ? existing.parentProductId : variantParent?.id ?? null,
        size: blankToNull(v.size),
        color: blankToNull(v.color),
        model: blankToNull(v.model),
        description: blankToNull(v.description),
        purchasePrice: canSeeCost || isNew ? v.purchasePrice ?? 0 : null,
        retailPrice: v.retailPrice,
        wholesalePrice: v.wholesalePrice ?? v.retailPrice,
        minSalePrice: v.minSalePrice ?? 0,
        reorderLevel: v.reorderLevel,
        imageFileId: imageId,
        openingQuantity: canOpenStock ? v.openingQuantity ?? null : null,
        openingUnitCost: canOpenStock ? v.openingUnitCost ?? v.purchasePrice ?? null : null,
      };
      return existing ? productsApi.update(existing.id, input) : productsApi.create(input);
    },
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: ['products'] });
      queryClient.setQueryData(['product', saved.id], saved);
      if (variantParent) void queryClient.invalidateQueries({ queryKey: ['product', variantParent.id] });
      toast.success(isNew ? `تمت إضافة المنتج بالكود ${saved.sku}` : 'تم حفظ التعديلات');
      navigate(`/products/${saved.id}`, { replace: true });
    },
    onError: (e) => {
      const msg = applyServerErrors(e, setError, FIELDS);
      if (msg) toast.error(msg);
    },
  });

  useEffect(() => {
    // Keep the opening cost aligned with the purchase price until the user types a different one.
    const cost = parseNumber(purchaseRaw);
    if (cost != null && !Number.isNaN(cost)) setValue('openingUnitCost', String(cost));
  }, [purchaseRaw, setValue]);

  const onSubmit = handleSubmit((v) => save.mutate(v));
  const title = existing ? 'تعديل المنتج' : variantParent ? 'متغير جديد' : 'منتج جديد';

  return (
    <form onSubmit={onSubmit} noValidate className="mx-auto max-w-2xl space-y-4 pb-[calc(4rem+env(safe-area-inset-bottom))] lg:pb-0">
      <button type="button" onClick={() => navigate(-1)} className="flex h-10 items-center gap-1 text-sm text-muted">
        <ArrowRight className="size-4" /> رجوع
      </button>
      <PageHeader title={title} subtitle={variantParent ? `من: ${variantParent.displayName} — حدّد المقاس أو اللون أو الموديل` : undefined} />

      {variantParent && (
        <Card className="grid grid-cols-3 gap-3 p-5">
          <Field label="الموديل" error={errors.model?.message}>{(id) => <Input id={id} {...register('model')} />}</Field>
          <Field label="اللون" error={errors.color?.message}>{(id) => <Input id={id} {...register('color')} />}</Field>
          <Field label="المقاس" error={errors.size?.message}>{(id) => <Input id={id} {...register('size')} />}</Field>
        </Card>
      )}

      <Card className="space-y-4 p-5">
        <Field label="اسم المنتج" required error={errors.name?.message}>
          {(id, d) => <Input id={id} aria-describedby={d} invalid={!!errors.name} placeholder="مثال: صحن زجاجي 25 سم" {...register('name')} />}
        </Field>
        <Field label="الصنف" required error={errors.categoryId?.message}>
          {(id) => (
            <Controller control={control} name="categoryId"
              render={({ field }) => (
                <CategoryPicker id={id} invalid={!!errors.categoryId} value={categoryName}
                  onSelect={(c) => { setCategoryName(c.name); field.onChange(c.id); }} />
              )} />
          )}
        </Field>
      </Card>

      <Card className="space-y-4 p-5">
        <h2 className="font-bold">الأسعار</h2>
        {!canEditPrices && <p className="rounded-xl bg-amber-50 p-3 text-sm text-amber-800">ليست لديك صلاحية تعديل أسعار البيع.</p>}
        <div className="grid grid-cols-2 gap-3">
          {(canSeeCost || isNew) && (
            <Field label="سعر الشراء (التكلفة)" error={errors.purchasePrice?.message}>
              {(id, d) => <Input id={id} aria-describedby={d} inputMode="decimal" className="ltr" invalid={!!errors.purchasePrice} {...register('purchasePrice')} />}
            </Field>
          )}
          <Field label="سعر البيع بالمفرق" required error={errors.retailPrice?.message}>
            {(id, d) => <Input id={id} aria-describedby={d} inputMode="decimal" className="ltr" disabled={!canEditPrices} invalid={!!errors.retailPrice} {...register('retailPrice')} />}
          </Field>
          <Field label="سعر الجملة" error={errors.wholesalePrice?.message} hint="فارغ = سعر المفرق">
            {(id, d) => <Input id={id} aria-describedby={d} inputMode="decimal" className="ltr" disabled={!canEditPrices} invalid={!!errors.wholesalePrice} {...register('wholesalePrice')} />}
          </Field>
          <Field label="أقل سعر بيع مسموح" error={errors.minSalePrice?.message}>
            {(id, d) => <Input id={id} aria-describedby={d} inputMode="decimal" className="ltr" disabled={!canEditPrices} invalid={!!errors.minSalePrice} {...register('minSalePrice')} />}
          </Field>
        </div>
        {cost > 0 && (
          <div className="flex flex-wrap items-center justify-between gap-2 rounded-xl bg-brand-50 p-3 text-sm">
            <span>
              {profit != null && (
                <>الربح المتوقع: <b className={profit < 0 ? 'text-danger' : 'text-success'}>{formatMoney(profit)}</b>
                  {' '}({Math.round((profit / cost) * 1000) / 10}%)</>
              )}
              {profit != null && profit < 0 && <span className="block text-danger">سعر البيع أقل من التكلفة</span>}
            </span>
            {canEditPrices && (
              <Button size="sm" variant="secondary" onClick={() => setValue('retailPrice', String(suggested), { shouldDirty: true, shouldValidate: true })}>
                <Sparkles className="size-4" /> اقتراح {formatMoney(suggested)} (ربح {margin}%)
              </Button>
            )}
          </div>
        )}
      </Card>

      <Card className="space-y-4 p-5">
        <h2 className="font-bold">المخزون</h2>
        <div className="grid grid-cols-2 gap-3">
          {canOpenStock && (
            <>
              <Field label="الكمية الحالية (افتتاحي)" error={errors.openingQuantity?.message} hint="الكمية الموجودة الآن في المعرض">
                {(id, d) => <Input id={id} aria-describedby={d} inputMode="decimal" className="ltr" invalid={!!errors.openingQuantity} {...register('openingQuantity')} />}
              </Field>
              <Field label="تكلفة الوحدة الافتتاحية" error={errors.openingUnitCost?.message}>
                {(id, d) => <Input id={id} aria-describedby={d} inputMode="decimal" className="ltr" invalid={!!errors.openingUnitCost} {...register('openingUnitCost')} />}
              </Field>
            </>
          )}
          <Field label="حد التنبيه عند الانخفاض" required error={errors.reorderLevel?.message}>
            {(id, d) => <Input id={id} aria-describedby={d} inputMode="decimal" className="ltr" invalid={!!errors.reorderLevel} {...register('reorderLevel')} />}
          </Field>
        </div>
        {!isNew && <p className="text-sm text-muted">لتغيير الكمية استخدم فاتورة شراء أو «حركة مخزون» أو الجرد، ليبقى كل تغيير موثقًا.</p>}
      </Card>

      <Collapsible title="تفاصيل إضافية" hint="الكود، الباركود، الوحدة، الماركة، المورد، الصورة" defaultOpen={!!existing}>
        <ImagePicker value={imageId} onChange={setImageId} />
        <div className="grid grid-cols-2 gap-3">
          <Field label="كود المنتج" error={errors.sku?.message} hint={isNew ? 'فارغ = كود تلقائي' : undefined}>
            {(id, d) => <Input id={id} aria-describedby={d} className="ltr" autoCapitalize="characters" invalid={!!errors.sku} {...register('sku')} />}
          </Field>
          <Field label="الباركود" error={errors.barcode?.message}>
            {(id, d) => <Input id={id} aria-describedby={d} inputMode="numeric" className="ltr" invalid={!!errors.barcode} {...register('barcode')} />}
          </Field>
          <Field label="وحدة القياس" required error={errors.unitId?.message}>
            {(id, d) => (
              <Select id={id} aria-describedby={d} {...register('unitId')}>
                {lookups.data?.units.map((u) => <option key={u.id} value={u.id}>{u.name}</option>)}
              </Select>
            )}
          </Field>
          <Field label="الماركة" error={errors.brandName?.message}>{(id) => <Input id={id} {...register('brandName')} />}</Field>
        </div>
        {!variantParent && (
          <div className="grid grid-cols-3 gap-3">
            <Field label="الموديل" error={errors.model?.message}>{(id) => <Input id={id} {...register('model')} />}</Field>
            <Field label="اللون" error={errors.color?.message}>{(id) => <Input id={id} {...register('color')} />}</Field>
            <Field label="المقاس" error={errors.size?.message}>{(id) => <Input id={id} {...register('size')} />}</Field>
          </div>
        )}
        <Field label="المورد المعتاد">
          {(id) => <SupplierPicker id={id} value={supplier?.name ?? null} onSelect={(s) => setSupplier({ id: s.id, name: s.name })} onClear={() => setSupplier(null)} />}
        </Field>
        <Field label="الوصف" error={errors.description?.message}>{(id) => <TextArea id={id} rows={2} {...register('description')} />}</Field>
      </Collapsible>

      <StickyActions>
        <Button type="submit" block size="lg" loading={save.isPending || isSubmitting}>
          {isNew ? 'حفظ المنتج' : 'حفظ التعديلات'}
        </Button>
      </StickyActions>
    </form>
  );
}
