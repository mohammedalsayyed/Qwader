import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ArrowDown } from 'lucide-react';
import { productsApi, type ProductListItem } from '@/api/catalog';
import { inventoryApi } from '@/api/stock';
import { Button } from '@/components/ui/Button';
import { Field, Input } from '@/components/ui/Field';
import { Picker } from '@/components/ui/Picker';
import { Sheet } from '@/components/ui/Sheet';
import { useToast } from '@/components/ui/Toast';
import { ProductRow } from '@/components/catalog/CatalogBits';
import { errorMessage } from '@/lib/forms';
import { formatMoney, formatQuantity } from '@/lib/format';
import { useIdempotencyKey } from '@/lib/hooks';
import { parseNumber, round2 } from '@/lib/numbers';

/**
 * «تفكيك / تجميع»: this product's stock becomes another product's — 20 sets into 120 cups, or 6 cups back into a set.
 * The value that leaves this product enters the other one (its cost per unit = the value ÷ its quantity): nothing is gained
 * or lost, and it is not a stock adjustment.
 */
export function ConversionSheet({ product, onClose }: {
  product: { id: number; displayName: string; quantity: number; unitName: string; averageCost: number | null };
  onClose: () => void;
}) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [key] = useIdempotencyKey();
  const [target, setTarget] = useState<ProductListItem | null>(null);
  const [fromQty, setFromQty] = useState('');
  const [toQty, setToQty] = useState('');
  const [error, setError] = useState<string | null>(null);

  const from = parseNumber(fromQty);
  const to = parseNumber(toQty);
  const fromOk = from !== undefined && !Number.isNaN(from) && from > 0;
  const toOk = to !== undefined && !Number.isNaN(to) && to > 0;
  const value = fromOk && product.averageCost != null ? round2(from! * product.averageCost) : null;

  const save = useMutation({
    mutationFn: () => inventoryApi.convert({ idempotencyKey: key, fromProductId: product.id, fromQuantity: from!, toProductId: target!.id, toQuantity: to!, notes: null }),
    onSuccess: (r) => {
      for (const k of ['product', 'products', 'movements', 'movements-total', 'inventory-summary']) void queryClient.invalidateQueries({ queryKey: [k] });
      toast.success(`تم: ${formatQuantity(from!)} ${product.unitName} ← ${formatQuantity(to!)} ${target!.unitName} (${r.number})`);
      onClose();
    },
    onError: (e) => setError(errorMessage(e)),
  });

  const submit = () => {
    if (!target) return setError('اختر المنتج الناتج');
    if (!fromOk) return setError(`اكتب كم ${product.unitName} تُفكَّك`);
    if (from! > product.quantity) return setError(`الموجود ${formatQuantity(product.quantity)} ${product.unitName} فقط`);
    if (!toOk) return setError(`اكتب كم ${target.unitName} تنتج`);
    setError(null);
    save.mutate();
  };

  return (
    <Sheet open onClose={onClose} title="تفكيك / تجميع" dirty={!!fromQty || !!toQty || !!target}
      footer={<Button block size="lg" loading={save.isPending} onClick={submit}>تنفيذ</Button>}>
      <div className="space-y-4">
        <p className="text-sm text-muted">
          تحويل بضاعة من هذا المنتج إلى منتج آخر: مثلًا 20 طقم ← 120 فنجان للبيع بالمفرد، أو العكس. القيمة تنتقل كما هي، فلا ربح ولا خسارة.
        </p>
        <div className="rounded-xl bg-canvas p-3">
          <p className="font-semibold">{product.displayName}</p>
          <p className="text-sm text-muted">الموجود: <b className="tabular">{formatQuantity(product.quantity)}</b> {product.unitName}
            {product.averageCost != null && <> · تكلفة الـ{product.unitName} <b className="tabular">{formatMoney(product.averageCost)}</b></>}</p>
        </div>
        <Field label={`الكمية التي تُفكَّك (${product.unitName})`} required>
          {(id) => <Input id={id} inputMode="decimal" className="ltr text-lg" autoFocus value={fromQty} onChange={(e) => { setFromQty(e.target.value); setError(null); }} />}
        </Field>
        <ArrowDown className="mx-auto size-5 text-muted" aria-hidden />
        <Field label="المنتج الناتج" required hint="أضفه أولًا من المنتجات إن لم يكن موجودًا (مثلًا «فنجان» بوحدة قطعة)">
          {(id, d) => (
            <Picker<ProductListItem> id={id} describedBy={d} title="المنتج الناتج" placeholder="ابحث عن المنتج…" valueLabel={target?.displayName ?? null}
              queryKey="conversion-products"
              search={async (q, signal) => (await productsApi.list({ search: q, pageSize: 30 }, signal)).items.filter((p) => p.id !== product.id)}
              getKey={(p) => p.id} renderItem={(p) => <ProductRow p={p} />}
              onSelect={(p) => { setTarget(p); setError(null); }} />
          )}
        </Field>
        {target && (
          <Field label={`الكمية الناتجة (${target.unitName})`} required>
            {(id) => <Input id={id} inputMode="decimal" className="ltr text-lg" value={toQty} onChange={(e) => { setToQty(e.target.value); setError(null); }} />}
          </Field>
        )}
        {target && fromOk && toOk && (
          <p className="rounded-xl bg-brand-50 p-3 text-sm leading-6">
            كل 1 {product.unitName} = <b className="tabular">{formatQuantity(round2(to! / from!))}</b> {target.unitName}.
            {value != null && <> تنتقل قيمة <b className="tabular">{formatMoney(value)}</b>، وتكلفة الـ{target.unitName} منها <b className="tabular">{formatMoney(value / to!)}</b>.</>}
          </p>
        )}
        {error && <p role="alert" className="rounded-xl bg-red-50 p-3 text-sm text-danger">{error}</p>}
      </div>
    </Sheet>
  );
}
