import { useState } from 'react';
import { useNavigate } from 'react-router';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useForm, useWatch } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { CheckCircle2, SlidersHorizontal } from 'lucide-react';
import { productsApi } from '@/api/catalog';
import type { LookupItem } from '@/api/endpoints';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button } from '@/components/ui/Button';
import { Field, Input } from '@/components/ui/Field';
import { Sheet } from '@/components/ui/Sheet';
import { useToast } from '@/components/ui/Toast';
import { CategoryPicker } from '@/components/catalog/CatalogBits';
import { applyServerErrors } from '@/lib/forms';
import { formatMoney } from '@/lib/format';
import { parseNumber, suggestPrice, zOptionalAmount, zPositive } from '@/lib/numbers';
import { useLookups, useStoreSettings } from '@/lib/queries';

const schema = z.object({
  name: z.string().trim().min(1, 'اكتب اسم المنتج').max(150, 'اسم المنتج طويل جدًا'),
  retailPrice: zPositive('اكتب سعر البيع'),
  purchasePrice: zOptionalAmount(),
  quantity: zOptionalAmount(),
});
type FormInput = z.input<typeof schema>;
type FormValues = z.output<typeof schema>;

/** Values handed to the full form when the user wants more details. */
export interface QuickProductDraft {
  name: string;
  category: LookupItem | null;
  retailPrice: string;
  purchasePrice: string;
  quantity: string;
}

const EMPTY = { name: '', retailPrice: '', purchasePrice: '', quantity: '' };

/**
 * The everyday way to add products: five fields, sensible defaults for the rest
 * (unit «قطعة», wholesale = retail, alert level from settings), and «حفظ وإضافة آخر» for entering a batch.
 */
export function QuickProductSheet({ onClose }: { onClose: () => void }) {
  const navigate = useNavigate();
  const toast = useToast();
  const queryClient = useQueryClient();
  const { can } = useAuth();
  const lookups = useLookups();
  const settings = useStoreSettings();
  const [category, setCategory] = useState<LookupItem | null>(null);
  const [categoryError, setCategoryError] = useState<string>();
  const [addedCount, setAddedCount] = useState(0);
  const [lastAdded, setLastAdded] = useState<string | null>(null);

  const canSeeCost = can(P.ProductsViewCost);
  const canOpenStock = can(P.InventoryAdjust, P.PurchasesManage);
  const margin = settings.data?.defaultMarginPercent ?? 25;

  const { register, control, handleSubmit, reset, setFocus, setValue, setError, getValues, formState: { errors } } = useForm<FormInput, unknown, FormValues>({
    resolver: zodResolver(schema),
    defaultValues: EMPTY,
  });
  const [purchaseRaw, retailRaw] = useWatch({ control, name: ['purchasePrice', 'retailPrice'] });
  const cost = parseNumber(purchaseRaw) ?? 0;
  const retail = parseNumber(retailRaw) ?? 0;
  const suggested = suggestPrice(cost, margin);

  const save = useMutation({
    mutationFn: ({ v }: { v: FormValues; another: boolean }) => {
      const unitId = lookups.data?.units.find((u) => u.name === 'قطعة')?.id ?? lookups.data?.units[0]?.id ?? 0;
      return productsApi.create({
        name: v.name,
        categoryId: category!.id,
        unitId,
        retailPrice: v.retailPrice,
        wholesalePrice: v.retailPrice,
        minSalePrice: 0,
        purchasePrice: v.purchasePrice ?? 0,
        reorderLevel: settings.data?.defaultReorderLevel ?? 5,
        openingQuantity: canOpenStock ? v.quantity ?? null : null,
        openingUnitCost: canOpenStock ? v.purchasePrice ?? 0 : null,
      });
    },
    onSuccess: (saved, { another }) => {
      void queryClient.invalidateQueries({ queryKey: ['products'] });
      void queryClient.invalidateQueries({ queryKey: ['notifications'] });
      setAddedCount((n) => n + 1);
      if (another) {
        // Same category, empty fields, cursor back on the name: ready for the next item.
        setLastAdded(`${saved.displayName} (${saved.sku})`);
        reset(EMPTY);
        setTimeout(() => setFocus('name'), 50);
      } else {
        toast.success(`تمت إضافة «${saved.displayName}» بالكود ${saved.sku}`);
        onClose();
      }
    },
    onError: (e) => {
      const msg = applyServerErrors(e, setError, ['name', 'retailPrice', 'purchasePrice']);
      if (msg) toast.error(msg);
    },
  });

  const submit = (another: boolean) =>
    handleSubmit((v) => {
      if (!category) {
        setCategoryError('اختر الصنف');
        return;
      }
      save.mutate({ v, another });
    })();

  const moreDetails = () => {
    const draft: QuickProductDraft = { ...getValues(), category } as QuickProductDraft;
    onClose();
    navigate('/products/new', { state: { draft } });
  };

  return (
    <Sheet
      open
      onClose={onClose}
      title="إضافة منتج"
      footer={
        <div className="space-y-2">
          <div className="flex gap-2">
            <Button size="lg" className="flex-1" loading={save.isPending && !save.variables?.another} disabled={save.isPending} onClick={() => void submit(false)}>
              حفظ
            </Button>
            <Button size="lg" variant="secondary" className="flex-[1.4]" loading={save.isPending && !!save.variables?.another} disabled={save.isPending} onClick={() => void submit(true)}>
              حفظ وإضافة آخر
            </Button>
          </div>
          <button type="button" onClick={moreDetails} className="flex h-10 w-full items-center justify-center gap-1.5 text-sm font-medium text-brand-700">
            <SlidersHorizontal className="size-4" /> تفاصيل أكثر (باركود، صورة، ماركة، مقاس…)
          </button>
        </div>
      }
    >
      <form onSubmit={(e) => { e.preventDefault(); void submit(true); }} noValidate className="space-y-4">
        {lastAdded && (
          <p role="status" className="flex items-start gap-2 rounded-xl bg-green-50 p-3 text-sm text-success">
            <CheckCircle2 className="mt-0.5 size-4 shrink-0" />
            <span>أُضيف: {lastAdded}. {addedCount > 1 && `(${addedCount} منتجات حتى الآن)`} أكمل المنتج التالي.</span>
          </p>
        )}

        <Field label="اسم المنتج" required error={errors.name?.message}>
          {(id, d) => <Input id={id} aria-describedby={d} autoFocus enterKeyHint="next" placeholder="مثال: صحن زجاجي 25 سم" invalid={!!errors.name} {...register('name')} />}
        </Field>

        <Field label="الصنف" required error={categoryError}>
          {(id) => (
            <CategoryPicker id={id} invalid={!!categoryError} value={category?.name ?? null}
              onSelect={(c) => { setCategory(c); setCategoryError(undefined); }} />
          )}
        </Field>

        <div className="grid grid-cols-2 gap-3">
          {canSeeCost && (
            <Field label="سعر الشراء" error={errors.purchasePrice?.message}>
              {(id, d) => <Input id={id} aria-describedby={d} inputMode="decimal" className="ltr" placeholder="0" invalid={!!errors.purchasePrice} {...register('purchasePrice')} />}
            </Field>
          )}
          <Field label="سعر البيع" required error={errors.retailPrice?.message}>
            {(id, d) => <Input id={id} aria-describedby={d} inputMode="decimal" className="ltr" placeholder="0" invalid={!!errors.retailPrice} {...register('retailPrice')} />}
          </Field>
        </div>

        {cost > 0 && (
          <div className="-mt-1 flex flex-wrap items-center justify-between gap-2 text-sm">
            {retail > 0 ? (
              <span className={retail < cost ? 'text-danger' : 'text-success'}>
                {retail < cost ? 'سعر البيع أقل من سعر الشراء!' : `الربح في القطعة: ${formatMoney(retail - cost)}`}
              </span>
            ) : <span />}
            {suggested !== retail && (
              <button type="button" className="font-medium text-brand-700 underline"
                onClick={() => setValue('retailPrice', String(suggested), { shouldValidate: true })}>
                استخدم السعر المقترح {formatMoney(suggested)}
              </button>
            )}
          </div>
        )}

        {canOpenStock && (
          <Field label="الكمية الموجودة الآن" error={errors.quantity?.message} hint="اتركها فارغة إذا لم تصل البضاعة بعد">
            {(id, d) => <Input id={id} aria-describedby={d} inputMode="decimal" className="ltr" placeholder="0" invalid={!!errors.quantity} {...register('quantity')} />}
          </Field>
        )}
      </form>
    </Sheet>
  );
}
