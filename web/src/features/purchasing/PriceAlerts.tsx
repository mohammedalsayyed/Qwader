import { useEffect, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { TriangleAlert } from 'lucide-react';
import { productsApi } from '@/api/catalog';
import { purchasesApi, type PriceAlert } from '@/api/stock';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Field';
import { Sheet } from '@/components/ui/Sheet';
import { useToast } from '@/components/ui/Toast';
import { errorMessage } from '@/lib/forms';
import { formatMoney } from '@/lib/format';
import { parseNumber } from '@/lib/numbers';

type Row = { retail: string; wholesale: string; min: string };

/**
 * After a purchase: the invoice's products whose cost is now above a selling price. A notice on the posted invoice, and a sheet
 * (opened at once right after posting, <paramref name="open"/>) with suggested prices to save or leave. Prices never change by themselves.
 */
export function PriceAlerts({ purchaseId, open, onOpenChange }: { purchaseId: number; open: boolean; onOpenChange: (open: boolean) => void }) {
  const alerts = useQuery({ queryKey: ['purchase-price-alerts', purchaseId], queryFn: () => purchasesApi.priceAlerts(purchaseId) });
  const list = alerts.data ?? [];
  if (list.length === 0) return null;
  return (
    <>
      <div className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-amber-300 bg-amber-50 p-3 text-sm print:hidden">
        <span className="flex items-center gap-2">
          <TriangleAlert className="size-5 shrink-0 text-warning" aria-hidden />
          {list.length === 1 ? `«${list[0].name}» تكلفته صارت أعلى من أحد أسعار بيعه` : `${list.length} منتجات تكلفتها صارت أعلى من أحد أسعار بيعها`}
        </span>
        <Button size="sm" variant="outline" onClick={() => onOpenChange(true)}>مراجعة الأسعار</Button>
      </div>
      {open && <PricesSheet alerts={list} onClose={() => onOpenChange(false)} purchaseId={purchaseId} />}
    </>
  );
}

function PricesSheet({ alerts, onClose, purchaseId }: { alerts: PriceAlert[]; onClose: () => void; purchaseId: number }) {
  const { can } = useAuth();
  const toast = useToast();
  const queryClient = useQueryClient();
  const canEdit = can(P.ProductsEditPrices);
  const [rows, setRows] = useState<Record<number, Row>>({});
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    setRows(Object.fromEntries(alerts.map((a) => [a.productId, { retail: String(a.suggestedRetail), wholesale: String(a.suggestedWholesale), min: String(a.suggestedMin) }])));
  }, [alerts]);

  const save = useMutation({
    mutationFn: () => {
      const items = alerts.map((a) => {
        const r = rows[a.productId];
        const retail = parseNumber(r.retail);
        const wholesale = parseNumber(r.wholesale);
        const min = parseNumber(r.min) ?? 0;
        if (retail == null || Number.isNaN(retail) || retail <= 0) throw new Error(`اكتب سعر بيع «${a.name}»`);
        if (wholesale == null || Number.isNaN(wholesale) || wholesale < 0 || Number.isNaN(min) || min < 0) throw new Error(`أسعار «${a.name}» غير صحيحة`);
        if (min > retail) throw new Error(`أقل سعر لـ«${a.name}» أعلى من سعر البيع`);
        return { productId: a.productId, retailPrice: retail, wholesalePrice: wholesale, minSalePrice: min };
      });
      return productsApi.updatePrices(items);
    },
    onSuccess: (n) => {
      toast.success(n ? `تم تحديث أسعار ${n} منتج` : 'لم تتغير أي أسعار');
      for (const k of ['products', 'product', 'purchase-price-alerts', 'pos-catalog']) void queryClient.invalidateQueries({ queryKey: [k] });
      void queryClient.invalidateQueries({ queryKey: ['purchase-price-alerts', purchaseId] });
      onClose();
    },
    onError: (e) => setError(errorMessage(e)),
  });

  const set = (id: number, patch: Partial<Row>) => { setRows((r) => ({ ...r, [id]: { ...r[id], ...patch } })); setError(null); };
  return (
    <Sheet open onClose={onClose} title="تكلفة أعلى من سعر البيع" size="lg"
      footer={canEdit ? (
        <div className="grid grid-cols-2 gap-2">
          <Button block loading={save.isPending} onClick={() => save.mutate()}>حفظ الأسعار الجديدة</Button>
          <Button block variant="outline" onClick={onClose}>لاحقًا، بدون تغيير</Button>
        </div>
      ) : <Button block variant="outline" onClick={onClose}>إغلاق</Button>}>
      <div className="space-y-3 text-sm">
        <p className="text-muted">
          بعد هذه الفاتورة صارت تكلفة هذه المنتجات أعلى من أحد أسعار بيعها، فالبيع بالسعر القديم يكون بخسارة ويُرفض لمن لا يملك صلاحية خاصة.
          الأسعار المقترحة: سعر البيع بنسبة الربح الافتراضية، الجملة بنصفها، وأقل سعر بالتكلفة. عدّلها كما تريد؛ لا يتغير شيء إلا إذا حفظت.
          {!canEdit && ' تعديل الأسعار يحتاج صلاحية «تعديل أسعار البيع»: أبلغ المدير.'}
        </p>
        {alerts.map((a) => {
          const r = rows[a.productId];
          if (!r) return null;
          const low = (v: number) => a.cost != null && v < a.cost;
          return (
            <div key={a.productId} className="space-y-2 rounded-xl border border-line p-3">
              <div className="flex flex-wrap items-baseline justify-between gap-2">
                <p className="font-bold">{a.name} {a.sku && <span className="ltr text-xs font-normal text-muted">{a.sku}</span>}</p>
                {a.cost != null && <p>التكلفة الآن: <b className="tabular">{formatMoney(a.cost)}</b></p>}
              </div>
              <div className="grid grid-cols-3 gap-2">
                {([['retail', 'سعر البيع', a.retailPrice], ['wholesale', 'سعر الجملة', a.wholesalePrice], ['min', 'أقل سعر', a.minSalePrice]] as const).map(([k, label, old]) => (
                  <label key={k} className="space-y-1">
                    <span className="block text-xs font-medium">{label}</span>
                    <Input inputMode="decimal" className="ltr" value={r[k]} disabled={!canEdit} onChange={(e) => set(a.productId, { [k]: e.target.value })} />
                    <span className={low(old) ? 'block text-xs text-danger' : 'block text-xs text-muted'}>
                      كان {old ? formatMoney(old) : 'بدون'}{low(old) && ' (تحت التكلفة)'}
                    </span>
                  </label>
                ))}
              </div>
            </div>
          );
        })}
        {error && <p role="alert" className="text-danger">{error}</p>}
      </div>
    </Sheet>
  );
}
