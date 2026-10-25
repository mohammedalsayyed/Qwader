import { useState } from 'react';
import { Link } from 'react-router';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { ArrowDownLeft, ArrowUpRight, History } from 'lucide-react';
import { inventoryApi, MOVEMENT_LABELS, type AdjustmentKind, type MovementType } from '@/api/stock';
import { Button } from '@/components/ui/Button';
import { Field, Input, TextArea } from '@/components/ui/Field';
import { EmptyState, ErrorState, FilterChips, Pagination, Spinner } from '@/components/ui/Feedback';
import { Sheet } from '@/components/ui/Sheet';
import { useToast } from '@/components/ui/Toast';
import { cn } from '@/lib/cn';
import { applyServerErrors } from '@/lib/forms';
import { formatDateTime, formatMoney, formatQuantity } from '@/lib/format';
import { useIdempotencyKey } from '@/lib/hooks';
import { zPositive } from '@/lib/numbers';
import { ProductPicker } from './CatalogBits';

const KIND_OPTIONS: { value: AdjustmentKind; label: string; hint: string }[] = [
  { value: 'Damaged', label: 'تالف', hint: 'بضاعة تلفت أو انكسرت' },
  { value: 'Lost', label: 'مفقود', hint: 'نقص غير مفسَّر' },
  { value: 'Found', label: 'زيادة', hint: 'بضاعة وُجدت ولم تكن مسجلة' },
];

const schema = z
  .object({ quantity: zPositive('الكمية مطلوبة'), notes: z.string().max(500).optional() })
  .passthrough();
type FormInput = z.input<typeof schema>;
type FormValues = z.output<typeof schema>;

/** Records damaged / lost / found stock. Product is fixed when opened from a product page. */
export function AdjustmentSheet({
  product,
  onClose,
}: {
  product?: { id: number; displayName: string; quantity: number; unitName: string };
  onClose: () => void;
}) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const [key, renewKey] = useIdempotencyKey();
  const [kind, setKind] = useState<AdjustmentKind>('Damaged');
  const [picked, setPicked] = useState(product ?? null);
  const [pickError, setPickError] = useState<string>();

  const { register, handleSubmit, setError, formState: { errors } } = useForm<FormInput, unknown, FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { quantity: '', notes: '' },
  });

  const save = useMutation({
    mutationFn: (v: FormValues) =>
      inventoryApi.adjust({ idempotencyKey: key, productId: picked!.id, kind, quantity: v.quantity, notes: v.notes?.trim() || null }),
    onSuccess: (r) => {
      renewKey();
      toast.success(`تم تسجيل الحركة ${r.number} — الرصيد الآن ${formatQuantity(r.balanceAfter)}`);
      void queryClient.invalidateQueries({ queryKey: ['products'] });
      void queryClient.invalidateQueries({ queryKey: ['product'] });
      void queryClient.invalidateQueries({ queryKey: ['movements'] });
      void queryClient.invalidateQueries({ queryKey: ['inventory-summary'] });
      void queryClient.invalidateQueries({ queryKey: ['notifications'] });
      onClose();
    },
    onError: (e) => {
      const msg = applyServerErrors(e, setError, ['quantity', 'notes']);
      if (msg) toast.error(msg);
    },
  });

  const onSubmit = handleSubmit((v) => {
    if (!picked) {
      setPickError('اختر المنتج');
      return;
    }
    save.mutate(v);
  });

  return (
    <Sheet open onClose={onClose} title="تسجيل حركة مخزون"
      footer={<Button block size="lg" loading={save.isPending} onClick={() => void onSubmit()}>تسجيل الحركة</Button>}>
      <form onSubmit={onSubmit} noValidate className="space-y-4">
        {product ? (
          <div className="rounded-2xl bg-canvas p-3">
            <p className="font-semibold">{product.displayName}</p>
            <p className="text-sm text-muted">الرصيد الحالي: <span className="tabular">{formatQuantity(product.quantity)}</span> {product.unitName}</p>
          </div>
        ) : (
          <Field label="المنتج" required error={pickError}>
            {(id) => (
              <ProductPicker id={id} invalid={!!pickError} value={picked?.displayName ?? null}
                onSelect={(p) => { setPicked({ id: p.id, displayName: p.displayName, quantity: p.quantity, unitName: p.unitName }); setPickError(undefined); }} />
            )}
          </Field>
        )}

        <fieldset>
          <legend className="mb-2 text-sm font-medium">نوع الحركة</legend>
          <div className="grid grid-cols-3 gap-2">
            {KIND_OPTIONS.map((o) => (
              <button key={o.value} type="button" onClick={() => setKind(o.value)} aria-pressed={kind === o.value}
                className={cn('min-h-16 rounded-xl border p-2 text-center', kind === o.value ? 'border-brand-700 bg-brand-50 text-brand-800' : 'border-line')}>
                <span className="block font-semibold">{o.label}</span>
                <span className="block text-[11px] leading-tight text-muted">{o.hint}</span>
              </button>
            ))}
          </div>
        </fieldset>

        <Field label="الكمية" required error={errors.quantity?.message}>
          {(id, d) => <Input id={id} aria-describedby={d} inputMode="decimal" className="ltr" invalid={!!errors.quantity} {...register('quantity')} />}
        </Field>
        <Field label={kind === 'Lost' ? 'السبب / التفاصيل' : 'ملاحظات'} required={kind === 'Lost'} error={errors.notes?.message}>
          {(id, d) => <TextArea id={id} aria-describedby={d} rows={2} {...register('notes')} />}
        </Field>
      </form>
    </Sheet>
  );
}

const TYPE_FILTERS: { value: MovementType | 'all'; label: string }[] = [
  { value: 'all', label: 'الكل' },
  { value: 'Purchase', label: 'شراء' },
  { value: 'Sale', label: 'بيع' },
  { value: 'Damaged', label: 'تالف' },
  { value: 'Lost', label: 'مفقود' },
  { value: 'CountDecrease', label: 'نقص جرد' },
  { value: 'CountIncrease', label: 'زيادة جرد' },
  { value: 'PurchaseReturn', label: 'مردود' },
];

function sourceLink(m: { sourceType: string; sourceId: number | null }): string | null {
  if (!m.sourceId) return null;
  switch (m.sourceType) {
    case 'PurchaseInvoice':
      return `/purchases/${m.sourceId}`;
    case 'StockCount':
      return `/inventory/counts/${m.sourceId}`;
    default:
      return null;
  }
}

/** Stock movement history; scoped to one product when productId is given. */
export function MovementList({ productId, showProduct }: { productId?: number; showProduct?: boolean }) {
  const [type, setType] = useState<MovementType | 'all'>('all');
  const [page, setPage] = useState(1);
  const query = useQuery({
    queryKey: ['movements', { productId, type, page }],
    queryFn: () => inventoryApi.movements({ productId, type: type === 'all' ? undefined : type, page, pageSize: 25 }),
    placeholderData: keepPreviousData,
  });

  return (
    <div>
      <div className="mb-3">
        <FilterChips value={type} onChange={(v) => { setType(v); setPage(1); }} options={TYPE_FILTERS} />
      </div>
      {query.isPending ? (
        <Spinner />
      ) : query.isError ? (
        <ErrorState message={query.error.message} onRetry={() => void query.refetch()} />
      ) : query.data.items.length === 0 ? (
        <EmptyState icon={<History className="size-10" />} title="لا توجد حركات" />
      ) : (
        <>
          <ul className="divide-y divide-line rounded-card border border-line bg-surface">
            {query.data.items.map((m) => {
              const incoming = m.quantity > 0;
              const link = sourceLink(m);
              return (
                <li key={m.id} className="flex items-start gap-3 p-3">
                  <span className={cn('mt-0.5 flex size-9 shrink-0 items-center justify-center rounded-full', incoming ? 'bg-green-50 text-success' : 'bg-red-50 text-danger')}>
                    {incoming ? <ArrowDownLeft className="size-5" /> : <ArrowUpRight className="size-5" />}
                  </span>
                  <div className="min-w-0 flex-1">
                    <div className="flex items-start justify-between gap-2">
                      <p className="font-semibold">{MOVEMENT_LABELS[m.type]}</p>
                      <p className={cn('tabular ltr shrink-0 font-bold', incoming ? 'text-success' : 'text-danger')}>
                        {incoming ? '+' : ''}{formatQuantity(m.quantity)}
                      </p>
                    </div>
                    {showProduct && (
                      <Link to={`/products/${m.productId}`} className="block truncate text-sm text-brand-700">{m.productName}</Link>
                    )}
                    <p className="text-xs text-muted">
                      {formatDateTime(m.occurredAt)}
                      {m.sourceNumber && (
                        <> · {link ? <Link to={link} className="ltr inline-block text-brand-700 underline">{m.sourceNumber}</Link> : <span className="ltr inline-block">{m.sourceNumber}</span>}</>
                      )}
                      {m.userName && <> · {m.userName}</>}
                    </p>
                    <p className="text-xs text-muted">
                      الرصيد بعدها: <span className="tabular">{formatQuantity(m.balanceAfter)}</span>
                      {m.unitCost != null && <> · التكلفة: <span className="tabular">{formatMoney(m.unitCost)}</span></>}
                    </p>
                    {m.notes && <p className="mt-1 text-sm">{m.notes}</p>}
                  </div>
                </li>
              );
            })}
          </ul>
          <Pagination page={query.data.page} totalPages={query.data.totalPages} onChange={setPage} />
        </>
      )}
    </div>
  );
}
