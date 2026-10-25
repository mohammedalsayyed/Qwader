import { useMemo, useState } from 'react';
import { useNavigate, useParams } from 'react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowRight } from 'lucide-react';
import { inventoryApi, type StockCount } from '@/api/stock';
import { Button } from '@/components/ui/Button';
import { Badge, ErrorState, SearchInput, Spinner } from '@/components/ui/Feedback';
import { StickyActions } from '@/components/ui/Layout';
import { useConfirm } from '@/components/ui/Confirm';
import { useToast } from '@/components/ui/Toast';
import { normalizeArabic } from '@/lib/arabic';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';
import { formatDate, formatQuantity } from '@/lib/format';
import { useUnsavedChangesWarning } from '@/lib/hooks';
import { parseNumber } from '@/lib/numbers';

export function StockCountPage() {
  const id = Number(useParams().id);
  const query = useQuery({ queryKey: ['stock-count', id], queryFn: () => inventoryApi.count(id) });
  if (query.isPending) return <Spinner />;
  if (query.isError) return <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} />;
  return <CountSheet key={query.data.status} count={query.data} />;
}

function CountSheet({ count }: { count: StockCount }) {
  const navigate = useNavigate();
  const toast = useToast();
  const confirm = useConfirm();
  const queryClient = useQueryClient();
  const open = count.status === 'Open';
  const [filter, setFilter] = useState('');
  const [onlyPending, setOnlyPending] = useState(false);
  const [entries, setEntries] = useState<Record<number, string>>(() =>
    Object.fromEntries(count.lines.map((l) => [l.id, l.countedQuantity == null ? '' : String(l.countedQuantity)])));
  const dirty = count.lines.some((l) => (entries[l.id] ?? '') !== (l.countedQuantity == null ? '' : String(l.countedQuantity)));
  useUnsavedChangesWarning(dirty);

  const invalidLine = count.lines.find((l) => {
    const v = parseNumber(entries[l.id]);
    return v !== undefined && (Number.isNaN(v) || v < 0);
  });

  const payload = () => count.lines.map((l) => ({ lineId: l.id, countedQuantity: parseNumber(entries[l.id]) ?? null }));

  const refresh = (data: StockCount) => {
    queryClient.setQueryData(['stock-count', count.id], data);
    void queryClient.invalidateQueries({ queryKey: ['stock-counts'] });
  };

  const save = useMutation({
    mutationFn: () => inventoryApi.saveCounts(count.id, payload()),
    onSuccess: (data) => { refresh(data); toast.success('تم حفظ الكميات'); },
    onError: (e) => toast.error(errorMessage(e)),
  });

  const post = useMutation({
    mutationFn: async () => {
      if (dirty) await inventoryApi.saveCounts(count.id, payload());
      return inventoryApi.postCount(count.id);
    },
    onSuccess: (data) => {
      refresh(data);
      void queryClient.invalidateQueries({ queryKey: ['products'] });
      void queryClient.invalidateQueries({ queryKey: ['inventory-summary'] });
      toast.success('تم اعتماد الجرد وتسوية الفروقات');
    },
    onError: (e) => toast.error(errorMessage(e)),
  });

  const cancel = useMutation({
    mutationFn: () => inventoryApi.cancelCount(count.id),
    onSuccess: () => { void queryClient.invalidateQueries({ queryKey: ['stock-counts'] }); navigate('/inventory?tab=counts'); },
    onError: (e) => toast.error(errorMessage(e)),
  });

  const lines = useMemo(() => {
    const term = normalizeArabic(filter);
    return count.lines.filter((l) =>
      (!term || normalizeArabic(`${l.productName} ${l.sku}`).includes(term)) && (!onlyPending || (entries[l.id] ?? '') === ''));
  }, [count.lines, filter, onlyPending, entries]);

  const counted = count.lines.filter((l) => (entries[l.id] ?? '') !== '').length;

  return (
    <div className="space-y-3 pb-[calc(4rem+env(safe-area-inset-bottom))] lg:pb-0">
      <button type="button" onClick={() => navigate('/inventory?tab=counts')} className="flex h-10 items-center gap-1 text-sm text-muted">
        <ArrowRight className="size-4" /> الجرد
      </button>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h1 className="ltr text-xl font-bold">{count.number}</h1>
          <p className="text-sm text-muted">{formatDate(count.createdAt)} · {count.warehouseName} · عُدّ {counted} من {count.lines.length}</p>
        </div>
        <Badge tone={open ? 'warning' : count.status === 'Posted' ? 'success' : 'neutral'}>
          {open ? 'مفتوح' : count.status === 'Posted' ? 'معتمد' : 'ملغى'}
        </Badge>
      </div>

      <SearchInput value={filter} onChange={setFilter} placeholder="ابحث في بنود الجرد" />
      {open && (
        <label className="flex min-h-11 items-center gap-2 text-sm">
          <input type="checkbox" className="size-5 accent-brand-700" checked={onlyPending} onChange={(e) => setOnlyPending(e.target.checked)} />
          إظهار غير المعدود فقط
        </label>
      )}

      <ul className="divide-y divide-line rounded-card border border-line bg-surface">
        {lines.map((l) => {
          const value = parseNumber(entries[l.id]);
          const expected = l.expectedQuantity ?? l.currentQuantity;
          const diff = l.difference ?? (value !== undefined && !Number.isNaN(value) ? value - expected : null);
          return (
            <li key={l.id} className="flex items-center gap-3 p-3">
              <div className="min-w-0 flex-1">
                <p className="truncate font-semibold">{l.productName}</p>
                <p className="text-xs text-muted">
                  <span className="ltr inline-block">{l.sku}</span> · النظام: <span className="tabular">{formatQuantity(expected)}</span> {l.unitName}
                </p>
                {diff != null && diff !== 0 && (
                  <p className={cn('tabular text-xs font-semibold', diff > 0 ? 'text-success' : 'text-danger')}>
                    الفرق: <span className="ltr inline-block">{diff > 0 ? '+' : ''}{formatQuantity(diff)}</span>
                  </p>
                )}
              </div>
              {open ? (
                <input
                  aria-label={`الكمية المعدودة: ${l.productName}`}
                  inputMode="decimal"
                  value={entries[l.id] ?? ''}
                  onChange={(e) => setEntries((s) => ({ ...s, [l.id]: e.target.value }))}
                  className={cn('ltr h-12 w-24 shrink-0 rounded-xl border px-3 text-center',
                    value !== undefined && (Number.isNaN(value) || value < 0) ? 'border-danger' : 'border-line')}
                />
              ) : (
                <span className="tabular w-20 shrink-0 text-center font-bold">{l.countedQuantity == null ? '—' : formatQuantity(l.countedQuantity)}</span>
              )}
            </li>
          );
        })}
      </ul>

      {open && (
        <StickyActions>
          <Button variant="outline" size="lg" className="flex-1" loading={save.isPending} disabled={!dirty || !!invalidLine} onClick={() => save.mutate()}>
            حفظ
          </Button>
          <Button size="lg" className="flex-[2]" loading={post.isPending} disabled={counted === 0 || !!invalidLine}
            onClick={async () => {
              if (await confirm({
                title: 'اعتماد الجرد',
                message: `سيتم تسوية الفروقات لـ ${counted} منتج حسب الكميات المعدودة، ولا يمكن تعديل الجرد بعد الاعتماد. المنتجات غير المعدودة لن تتأثر.`,
                confirmLabel: 'اعتماد',
              })) post.mutate();
            }}>
            اعتماد الجرد
          </Button>
          <Button variant="ghost" size="lg" loading={cancel.isPending}
            onClick={async () => { if (await confirm({ title: 'إلغاء الجرد', message: 'سيُلغى هذا الجرد دون أي تأثير على المخزون.', confirmLabel: 'إلغاء الجرد', danger: true })) cancel.mutate(); }}>
            إلغاء
          </Button>
        </StickyActions>
      )}
    </div>
  );
}
