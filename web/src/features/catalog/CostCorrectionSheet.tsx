import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { inventoryApi } from '@/api/stock';
import { Button } from '@/components/ui/Button';
import { Field, Input, TextArea } from '@/components/ui/Field';
import { Sheet } from '@/components/ui/Sheet';
import { useToast } from '@/components/ui/Toast';
import { errorMessage } from '@/lib/forms';
import { formatMoney, formatQuantity } from '@/lib/format';
import { useIdempotencyKey } from '@/lib/hooks';
import { parseNumber, round2 } from '@/lib/numbers';

/**
 * «تصحيح التكلفة»: a manager sets the product's average cost when it was entered wrong (an opening balance at 3 instead of 7).
 * The quantity does not move; the stock on hand is revalued, and a «تصحيح تكلفة» movement keeps the old cost, the new one and why.
 */
export function CostCorrectionSheet({ product, onClose }: {
  product: { id: number; displayName: string; quantity: number; unitName: string; averageCost: number };
  onClose: () => void;
}) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [key] = useIdempotencyKey();
  const [cost, setCost] = useState('');
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string | null>(null);

  const newCost = parseNumber(cost);
  const valid = newCost !== undefined && !Number.isNaN(newCost) && newCost >= 0;
  const change = valid ? round2(product.quantity * (newCost! - product.averageCost)) : 0;

  const save = useMutation({
    mutationFn: () => inventoryApi.correctCost({ idempotencyKey: key, productId: product.id, newCost: newCost!, reason: reason.trim() }),
    onSuccess: (r) => {
      for (const k of ['product', 'products', 'movements', 'movements-total', 'inventory-summary']) void queryClient.invalidateQueries({ queryKey: [k] });
      toast.success(`تم تصحيح التكلفة من ${formatMoney(r.oldCost)} إلى ${formatMoney(r.newCost)} (${r.number})`);
      onClose();
    },
    onError: (e) => setError(errorMessage(e)),
  });

  const submit = () => {
    if (!valid) return setError('اكتب التكلفة الصحيحة للقطعة');
    if (!reason.trim()) return setError('اكتب سبب التصحيح');
    setError(null);
    save.mutate();
  };

  return (
    <Sheet open onClose={onClose} title="تصحيح التكلفة" dirty={!!cost || !!reason}
      footer={<Button block size="lg" loading={save.isPending} onClick={submit}>تصحيح التكلفة</Button>}>
      <div className="space-y-4">
        <p className="font-semibold">{product.displayName}</p>
        <p className="rounded-xl bg-canvas p-3 text-sm">
          متوسط التكلفة الحالي: <b className="tabular">{formatMoney(product.averageCost)}</b> للـ{product.unitName}
          {' · '}الكمية الموجودة: <b className="tabular">{formatQuantity(product.quantity)}</b>
          {' · '}قيمتها: <b className="tabular">{formatMoney(round2(product.quantity * product.averageCost))}</b>
        </p>
        <Field label={`التكلفة الصحيحة لل${product.unitName}`} required>
          {(id) => <Input id={id} inputMode="decimal" className="ltr text-lg" autoFocus value={cost} onChange={(e) => { setCost(e.target.value); setError(null); }} />}
        </Field>
        {valid && newCost !== product.averageCost && (
          <p className={change < 0 ? 'rounded-xl bg-red-50 p-3 text-sm text-danger' : 'rounded-xl bg-green-50 p-3 text-sm text-success'}>
            قيمة المخزون تصبح <b className="tabular">{formatMoney(round2(product.quantity * newCost!))}</b>
            {' '}({change < 0 ? 'نقص' : 'زيادة'} <b className="tabular">{formatMoney(Math.abs(change))}</b>). الكمية لا تتغير.
          </p>
        )}
        <Field label="سبب التصحيح" required hint="يظهر في سجل الحركات وسجل التدقيق">
          {(id, d) => <TextArea id={id} aria-describedby={d} rows={2} placeholder="مثال: الرصيد الافتتاحي انكتب بتكلفة 3 بدل 7"
            value={reason} onChange={(e) => { setReason(e.target.value); setError(null); }} />}
        </Field>
        <p className="text-xs text-muted">
          يصحّح تكلفة البضاعة الموجودة الآن فقط؛ الفواتير التي بيعت قبل التصحيح تبقى بتكلفتها وقت البيع. لإدخال بضاعة من مورد استخدم فاتورة شراء.
        </p>
        {error && <p role="alert" className="rounded-xl bg-red-50 p-3 text-sm text-danger">{error}</p>}
      </div>
    </Sheet>
  );
}
