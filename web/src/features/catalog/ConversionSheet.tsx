import { useState } from 'react';
import { useNavigate } from 'react-router';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ArrowDown } from 'lucide-react';
import { productsApi, type ProductListItem } from '@/api/catalog';
import { inventoryApi } from '@/api/stock';
import { Button } from '@/components/ui/Button';
import { Field, Input, Select } from '@/components/ui/Field';
import { Picker } from '@/components/ui/Picker';
import { Sheet } from '@/components/ui/Sheet';
import { useToast } from '@/components/ui/Toast';
import { ProductRow } from '@/components/catalog/CatalogBits';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';
import { formatCost, formatMoney, formatQuantity } from '@/lib/format';
import { useIdempotencyKey } from '@/lib/hooks';
import { parseNumber, round2 } from '@/lib/numbers';
import { useLookups } from '@/lib/queries';

/**
 * «تفكيك / تجميع»: this product's stock becomes another product's — 20 sets into 120 cups, or 6 cups back into a set.
 * The value that leaves this product enters the other one (its cost per unit = the value ÷ its quantity): nothing is gained
 * or lost, and it is not a stock adjustment. The other product is made by itself («… مفكك», in the chosen unit) unless an
 * existing one is picked.
 */
export function ConversionSheet({ product, onClose }: {
  product: {
    id: number; name: string; displayName: string; quantity: number; unitName: string; averageCost: number | null;
    purchasePrice: number | null; retailPrice: number; wholesalePrice: number; minSalePrice: number;
  };
  onClose: () => void;
}) {
  const toast = useToast();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const lookups = useLookups();
  const [key] = useIdempotencyKey();
  // «new»: the product is made now — this one's name with «مفكك» after it; «existing»: one picked from the products.
  const [mode, setMode] = useState<'new' | 'existing'>('new');
  const units = lookups.data?.units ?? [];
  const [pickedUnit, setPickedUnit] = useState<number | null>(null);
  const unitId = pickedUnit ?? units.find((u) => u.name === 'قطعة')?.id ?? units[0]?.id ?? null;
  const [target, setTarget] = useState<ProductListItem | null>(null);
  const [fromQty, setFromQty] = useState('');
  const [toQty, setToQty] = useState('');
  const [error, setError] = useState<string | null>(null);

  const newName = product.name.endsWith(' مفكك') ? product.name : `${product.name} مفكك`;
  const ready = mode === 'new' ? unitId != null : target != null;
  const toUnitName = mode === 'new' ? units.find((u) => u.id === unitId)?.name ?? '' : target?.unitName ?? '';
  const toName = mode === 'new' ? newName : target?.displayName ?? '';

  const from = parseNumber(fromQty);
  const to = parseNumber(toQty);
  const fromOk = from !== undefined && !Number.isNaN(from) && from > 0;
  const toOk = to !== undefined && !Number.isNaN(to) && to > 0;
  const value = fromOk && product.averageCost != null ? round2(from! * product.averageCost) : null;

  // The other product's prices, worked out from this one's ÷ the ratio (a set at 90 → 15 a cup); each can be changed.
  const ratio = fromOk && toOk ? to! / from! : null;
  const [setPrices, setSetPrices] = useState(true);
  const updatePrices = mode === 'new' || setPrices;
  const [typed, setTyped] = useState<Partial<Record<PriceKey, string>>>({});
  const suggested: Record<PriceKey, string> = {
    retail: ratio ? String(round2(product.retailPrice / ratio)) : '',
    wholesale: ratio ? String(round2(product.wholesalePrice / ratio)) : '',
    min: ratio ? String(round2(product.minSalePrice / ratio)) : '',
  };
  const price = (k: PriceKey) => typed[k] ?? suggested[k];
  const priceNumber = (k: PriceKey) => parseNumber(price(k));
  const purchasePrice = ratio && product.purchasePrice ? Math.round((product.purchasePrice / ratio) * 10000) / 10000 : null;

  const save = useMutation({
    mutationFn: () => inventoryApi.convert({
      idempotencyKey: key, fromProductId: product.id, fromQuantity: from!, toProductId: mode === 'new' ? 0 : target!.id, toQuantity: to!, notes: null,
      newProductUnitId: mode === 'new' ? unitId : null,
      ...(updatePrices ? { toRetailPrice: priceNumber('retail')!, toWholesalePrice: priceNumber('wholesale') ?? 0, toMinSalePrice: priceNumber('min') ?? 0 } : {}),
    }),
    onSuccess: (r) => {
      for (const k of ['product', 'products', 'movements', 'movements-total', 'inventory-summary']) void queryClient.invalidateQueries({ queryKey: [k] });
      toast.success(`تم: ${formatQuantity(from!)} ${product.unitName} ← ${formatQuantity(to!)} ${toUnitName} «${r.toProductName ?? toName}» (${r.number})`);
      onClose();
      if (mode === 'new') navigate(`/products/${r.toProductId}`);
    },
    onError: (e) => setError(errorMessage(e)),
  });

  const submit = () => {
    if (!ready) return setError(mode === 'new' ? 'اختر وحدة القياس' : 'اختر المنتج الناتج');
    if (!fromOk) return setError(`اكتب كم ${product.unitName} تُفكَّك`);
    if (from! > product.quantity) return setError(`الموجود ${formatQuantity(product.quantity)} ${product.unitName} فقط`);
    if (!toOk) return setError(`اكتب كم ${toUnitName} تنتج`);
    if (updatePrices) {
      const r = priceNumber('retail'), w = priceNumber('wholesale') ?? 0, m = priceNumber('min') ?? 0;
      if (!r || Number.isNaN(r) || r <= 0) return setError('اكتب سعر البيع بالمفرق');
      if (Number.isNaN(w) || w < 0 || Number.isNaN(m) || m < 0) return setError('سعر غير صالح');
      if (w > r) return setError('سعر الجملة يجب ألا يتجاوز سعر المفرق');
      if (m > r) return setError('أقل سعر بيع يجب ألا يتجاوز سعر المفرق');
    }
    setError(null);
    save.mutate();
  };

  return (
    <Sheet open onClose={onClose} title="تفكيك / تجميع" dirty={!!fromQty || !!toQty || !!target}
      footer={<Button block size="lg" loading={save.isPending} onClick={submit}>تنفيذ</Button>}>
      <div className="space-y-4">
        <p className="text-sm text-muted">
          تحويل بضاعة من هذا المنتج إلى منتج آخر: مثلًا 20 طقم ← 120 قطعة للبيع بالمفرد، أو العكس. القيمة تنتقل كما هي، فلا ربح ولا خسارة.
        </p>
        <div className="rounded-xl bg-canvas p-3">
          <p className="font-semibold">{product.displayName}</p>
          <p className="text-sm text-muted">الموجود: <b className="tabular">{formatQuantity(product.quantity)}</b> {product.unitName}
            {product.averageCost != null && <> · تكلفة الـ{product.unitName} <b className="tabular">{formatCost(product.averageCost)}</b></>}</p>
        </div>
        <Field label={`الكمية التي تُفكَّك (${product.unitName})`} required>
          {(id) => <Input id={id} inputMode="decimal" className="ltr text-lg" autoFocus value={fromQty} onChange={(e) => { setFromQty(e.target.value); setError(null); }} />}
        </Field>
        <ArrowDown className="mx-auto size-5 text-muted" aria-hidden />

        <div className="space-y-3">
          <p className="text-sm font-medium">المنتج الناتج</p>
          <div className="grid grid-cols-2 gap-2">
            {([['new', 'منتج جديد تلقائيًا'], ['existing', 'منتج موجود']] as const).map(([v, label]) => (
              <button key={v} type="button" onClick={() => { setMode(v); setError(null); }}
                className={cn('min-h-11 rounded-control border px-2 text-sm font-semibold', mode === v ? 'border-brand-600 bg-brand-50 text-brand-700' : 'border-line bg-surface')}>
                {label}
              </button>
            ))}
          </div>
          {mode === 'new' ? (
            <div className="space-y-3 rounded-xl bg-canvas p-3">
              <p className="text-sm">يُضاف منتج باسم <b>«{newName}»</b> بنفس الصنف والماركة والمقاس واللون والمورد.
                <span className="text-muted"> إن كان موجودًا من تفكيك سابق تُضاف الكمية إليه.</span></p>
              <Field label="وحدة القياس" required>
                {(id) => (
                  <Select id={id} value={unitId ?? ''} onChange={(e) => setPickedUnit(Number(e.target.value))}>
                    {units.map((u) => <option key={u.id} value={u.id}>{u.name}</option>)}
                  </Select>
                )}
              </Field>
            </div>
          ) : (
            <Picker<ProductListItem> title="المنتج الناتج" placeholder="ابحث عن المنتج…" valueLabel={target?.displayName ?? null}
              queryKey="conversion-products"
              search={async (q, signal) => (await productsApi.list({ search: q, pageSize: 30 }, signal)).items.filter((p) => p.id !== product.id)}
              getKey={(p) => p.id} renderItem={(p) => <ProductRow p={p} />}
              onSelect={(p) => { setTarget(p); setError(null); }} />
          )}
        </div>

        {ready && (
          <Field label={`الكمية الناتجة (${toUnitName})`} required>
            {(id) => <Input id={id} inputMode="decimal" className="ltr text-lg" value={toQty} onChange={(e) => { setToQty(e.target.value); setError(null); }} />}
          </Field>
        )}
        {ready && fromOk && toOk && (
          <p className="rounded-xl bg-brand-50 p-3 text-sm leading-6">
            كل 1 {product.unitName} = <b className="tabular">{formatQuantity(round2(to! / from!))}</b> {toUnitName}.
            {value != null && <> تنتقل قيمة <b className="tabular">{formatMoney(value)}</b>، وتكلفة الـ{toUnitName} منها <b className="tabular">{formatMoney(value / to!)}</b>.</>}
          </p>
        )}
        {ready && ratio && (
          <div className="space-y-3 rounded-xl border border-line p-3">
            <p className="text-sm">
              آخر سعر شراء لـ«{toName}» يصبح{' '}
              <b className="tabular">{purchasePrice != null ? formatMoney(purchasePrice) : 'التكلفة أعلاه'}</b>
              {purchasePrice != null && <span className="text-muted"> (آخر سعر شراء الـ{product.unitName} {formatMoney(product.purchasePrice!)} ÷ {formatQuantity(round2(ratio))})</span>}.
            </p>
            {mode === 'existing' && (
              <label className="flex items-center gap-2 text-sm font-semibold">
                <input type="checkbox" className="size-4" checked={setPrices} onChange={(e) => setSetPrices(e.target.checked)} />
                تحديث أسعار بيع «{toName}»
              </label>
            )}
            {updatePrices && (
              <>
                <div className="grid grid-cols-3 gap-2">
                  {([['retail', 'سعر المفرق'], ['wholesale', 'سعر الجملة'], ['min', 'أقل سعر بيع']] as const).map(([k, label]) => (
                    <Field key={k} label={label}>
                      {(id) => <Input id={id} inputMode="decimal" className="ltr" value={price(k)}
                        onChange={(e) => { setTyped((t) => ({ ...t, [k]: e.target.value })); setError(null); }} />}
                    </Field>
                  ))}
                </div>
                <p className="text-xs text-muted">
                  محسوبة من أسعار الـ{product.unitName} ÷ {formatQuantity(round2(ratio))}
                  {mode === 'existing' && target && <> (الحالية لـ«{target.displayName}»: مفرق {formatMoney(target.retailPrice)}، جملة {formatMoney(target.wholesalePrice)}، أقل سعر {formatMoney(target.minSalePrice)})</>}
                  . عدّلها إن أردت.
                </p>
              </>
            )}
          </div>
        )}
        {error && <p role="alert" className="rounded-xl bg-red-50 p-3 text-sm text-danger">{error}</p>}
      </div>
    </Sheet>
  );
}

type PriceKey = 'retail' | 'wholesale' | 'min';
