import { useEffect, useRef, useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Minus, PackageX, Pencil, Plus, ScanBarcode, ShoppingCart, Trash2 } from 'lucide-react';
import { productsApi } from '@/api/catalog';
import { ApiError } from '@/api/client';
import type { PriceTier, Sale } from '@/api/sales';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button, IconButton } from '@/components/ui/Button';
import { Field, Input } from '@/components/ui/Field';
import { EmptyState, SearchInput, Spinner } from '@/components/ui/Feedback';
import { Sheet } from '@/components/ui/Sheet';
import { useConfirm } from '@/components/ui/Confirm';
import { useToast } from '@/components/ui/Toast';
import { ProductThumb } from '@/components/catalog/CatalogBits';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';
import { formatMoney, formatQuantity } from '@/lib/format';
import { useDebouncedValue } from '@/lib/hooks';
import { parseNumber, round2 } from '@/lib/numbers';
import { useLookups } from '@/lib/queries';
import { cartTotals, lineTotal, tierPrice, toCartProduct, useCart, type CartLine } from './cart';
import { CheckoutSheet, SaleDoneSheet } from './CheckoutSheet';
import { BarcodeScannerSheet, ScanButton } from '@/components/ui/BarcodeScanner';

export function PosPage() {
  const { cart, add, update, remove, setTier, reset } = useCart();
  useLookups(); // load accounts early so checkout opens instantly
  const { can } = useAuth();
  const canWholesale = can(P.SalesWholesale);
  // A cart saved by a manager on this device must not let a seller keep wholesale prices.
  useEffect(() => {
    if (!canWholesale && cart.tier === 'Wholesale') setTier('Retail');
  }, [canWholesale, cart.tier, setTier]);
  const toast = useToast();
  const confirm = useConfirm();
  const [q, setQ] = useState('');
  const [editing, setEditing] = useState<CartLine | null>(null);
  const [checkout, setCheckout] = useState(false);
  const [done, setDone] = useState<{ sale: Sale; change: number } | null>(null);
  const [flash, setFlash] = useState<number | null>(null);
  const [scanning, setScanning] = useState(false);
  const searchRef = useRef<HTMLDivElement>(null);
  const term = useDebouncedValue(q.trim(), 200);
  const totals = cartTotals(cart);

  const results = useQuery({
    queryKey: ['pos-search', term],
    queryFn: ({ signal }) => productsApi.list({ search: term, pageSize: 8 }, signal),
    enabled: term.length > 0,
    placeholderData: keepPreviousData,
    staleTime: 10_000,
  });

  const addProduct = (p: Parameters<typeof add>[0]) => {
    const error = add(p);
    if (error) toast.error(error);
    else {
      setFlash(p.id);
      setTimeout(() => setFlash(null), 600);
    }
    return !error;
  };

  /** Enter (or a barcode scanner, which types the code and presses Enter): exact code first, else a single match. */
  const onEnter = async () => {
    const code = q.trim();
    if (!code) return;
    try {
      const product = await productsApi.byCode(code);
      if (addProduct(toCartProduct(product))) setQ('');
    } catch (e) {
      if (e instanceof ApiError && e.status === 404) {
        const items = results.data?.items ?? [];
        if (items.length === 1 && addProduct(items[0])) setQ('');
        else if (items.length === 0) toast.error(`لا يوجد منتج بالكود أو الاسم «${code}»`);
      } else toast.error(errorMessage(e));
    }
    searchRef.current?.querySelector('input')?.focus();
  };

  /** Camera read: add by exact barcode/code; the scanner stays open for the next item. */
  const onScanned = async (code: string) => {
    try {
      addProduct(toCartProduct(await productsApi.byCode(code)));
    } catch (e) {
      toast.error(e instanceof ApiError && e.status === 404 ? `لا يوجد منتج بالباركود ${code} — أضفه من شاشة المنتجات` : errorMessage(e));
    }
  };

  const showResults = term.length > 0;

  return (
    <div className="pb-[calc(7rem+env(safe-area-inset-bottom))] lg:grid lg:grid-cols-[minmax(0,1fr)_420px] lg:gap-6 lg:pb-0">
      {/* ---------------- search & results ---------------- */}
      <section className="min-w-0">
        <div className="mb-3 flex items-center justify-between gap-2">
          <h1 className="text-xl font-bold">بيع جديد</h1>
          {canWholesale && <div className="flex rounded-xl bg-black/5 p-1" role="radiogroup" aria-label="نوع السعر">
            {(['Retail', 'Wholesale'] as const).map((t) => (
              <button key={t} type="button" role="radio" aria-checked={cart.tier === t} onClick={() => setTier(t)}
                className={cn('h-9 rounded-lg px-4 text-sm font-semibold', cart.tier === t ? 'bg-surface shadow-sm' : 'text-muted')}>
                {t === 'Retail' ? 'مفرق' : 'جملة'}
              </button>
            ))}
          </div>}
        </div>

        <div ref={searchRef} onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); void onEnter(); } }}>
          <div className="flex gap-2">
            <div className="min-w-0 flex-1"><SearchInput value={q} onChange={setQ} autoFocus placeholder="ابحث بالاسم أو الكود أو الباركود" /></div>
            <ScanButton onClick={() => setScanning(true)} />
          </div>
        </div>

        {showResults && (
          <div className="mt-2 overflow-hidden rounded-card border border-line bg-surface">
            {results.isPending ? (
              <Spinner className="py-6" />
            ) : !results.data || results.data.items.length === 0 ? (
              <EmptyState icon={<PackageX className="size-8" />} title={`لا توجد نتائج لـ «${term}»`} />
            ) : (
              <ul className={cn('divide-y divide-line', results.isFetching && 'opacity-60')}>
                {results.data.items.map((p) => {
                  const inCart = cart.lines.find((l) => l.productId === p.id)?.quantity ?? 0;
                  const out = p.quantity - inCart <= 0;
                  return (
                    <li key={p.id}>
                      <button type="button" disabled={out} onClick={() => addProduct(p)}
                        className={cn('flex w-full items-center gap-3 p-3 text-start transition', out ? 'opacity-50' : 'active:bg-brand-50',
                          flash === p.id && 'bg-green-50')}>
                        <ProductThumb fileId={p.imageFileId} size="sm" />
                        <span className="min-w-0 flex-1">
                          <span className="block truncate font-semibold">{p.displayName}</span>
                          <span className="block text-xs text-muted">
                            <span className="ltr inline-block">{p.sku}</span> · {out ? <span className="text-danger">نفد</span> : <>متوفر {formatQuantity(p.quantity - inCart)}</>}
                            {inCart > 0 && <> · في الفاتورة {formatQuantity(inCart)}</>}
                            {p.minSalePrice > 0 && <> · <span className="whitespace-nowrap">أقل سعر <span className="tabular font-semibold text-gray-700">{formatMoney(p.minSalePrice)}</span></span></>}
                          </span>
                        </span>
                        <span className="tabular shrink-0 font-bold">{formatMoney(tierPrice(p, cart.tier))}</span>
                        <span className={cn('flex size-10 shrink-0 items-center justify-center rounded-xl', out ? 'bg-canvas text-muted' : 'bg-brand-700 text-white')}>
                          <Plus className="size-5" />
                        </span>
                      </button>
                    </li>
                  );
                })}
              </ul>
            )}
          </div>
        )}

        {!showResults && cart.lines.length === 0 && (
          <EmptyState icon={<ScanBarcode className="size-10" />} title="ابدأ بإضافة المنتجات"
            description="اكتب اسم المنتج أو كوده، أو امسح الباركود، ثم اضغط على المنتج لإضافته." />
        )}
      </section>

      {/* ---------------- cart ---------------- */}
      <section className={cn('mt-4 lg:mt-0', cart.lines.length === 0 && 'hidden lg:block')} aria-label="الفاتورة">
        <div className="mb-2 flex items-center justify-between">
          <h2 className="font-bold">الفاتورة ({formatQuantity(totals.items)} قطعة)</h2>
          {cart.lines.length > 0 && (
            <button type="button" className="h-10 px-2 text-sm text-danger"
              onClick={async () => { if (await confirm({ title: 'إفراغ الفاتورة', message: 'سيتم حذف كل المنتجات من هذه الفاتورة.', confirmLabel: 'إفراغ', danger: true })) reset(); }}>
              إفراغ
            </button>
          )}
        </div>

        {cart.lines.length === 0 ? (
          <div className="rounded-card border border-dashed border-line p-8 text-center text-sm text-muted">لا توجد منتجات بعد</div>
        ) : (
          <ul className="space-y-2">
            {cart.lines.map((l) => (
              <li key={l.productId} className="rounded-card border border-line bg-surface p-3">
                <button type="button" onClick={() => setEditing(l)} className="block w-full text-start font-semibold leading-snug">{l.name}</button>
                {/* The price is a visible button: sellers may change it (never under the minimum sale price). */}
                <div className="mt-1.5 flex flex-wrap items-center gap-x-2 gap-y-1">
                  <button type="button" onClick={() => setEditing(l)} aria-label={`تعديل سعر ${l.name}`}
                    className="inline-flex h-9 items-center gap-1.5 rounded-lg border border-brand-200 bg-brand-50 px-2.5 text-sm text-brand-800 hover:bg-brand-100 active:bg-brand-100">
                    <Pencil className="size-4" aria-hidden />
                    <span className="tabular font-bold">{formatMoney(l.unitPrice)}</span>
                    <span className="text-xs">تعديل السعر</span>
                  </button>
                  {l.unitPrice !== tierPrice(l, cart.tier) && <span className="text-xs font-semibold text-warning">معدَّل</span>}
                  {l.lineDiscount > 0 && <span className="text-xs text-warning">خصم {formatMoney(l.lineDiscount)}</span>}
                  {minimumPrice(l, cart.tier) > 0 && (
                    <span className="whitespace-nowrap text-xs text-muted">أقل سعر <span className="tabular">{formatMoney(minimumPrice(l, cart.tier))}</span></span>
                  )}
                </div>
                <div className="mt-2 flex items-center justify-between gap-2">
                  <div className="flex items-center gap-1">
                    <IconButton label="إنقاص الكمية" className="border border-line"
                      onClick={() => (l.quantity <= 1 ? remove(l.productId) : update(l.productId, { quantity: l.quantity - 1 }))}>
                      {l.quantity <= 1 ? <Trash2 className="size-5 text-danger" /> : <Minus className="size-5" />}
                    </IconButton>
                    <span className="tabular w-12 text-center text-lg font-bold">{formatQuantity(l.quantity)}</span>
                    <IconButton label="زيادة الكمية" className="border border-line" disabled={l.quantity >= l.available}
                      onClick={() => update(l.productId, { quantity: l.quantity + 1 })}>
                      <Plus className="size-5" />
                    </IconButton>
                  </div>
                  <span className="tabular text-lg font-bold">{formatMoney(lineTotal(l))}</span>
                </div>
              </li>
            ))}
          </ul>
        )}

        {/* Desktop: totals + pay inline */}
        {cart.lines.length > 0 && (
          <div className="mt-3 hidden rounded-card border border-line bg-surface p-4 lg:block">
            <Totals subtotal={totals.subtotal} discounts={totals.lineDiscounts} />
            <Button block size="lg" variant="accent" className="mt-3" onClick={() => setCheckout(true)}>
              <ShoppingCart className="size-5" /> الدفع
            </Button>
          </div>
        )}
      </section>

      {/* Phone: sticky total + pay above the tab bar */}
      {cart.lines.length > 0 && (
        <div className="fixed inset-x-0 bottom-nav z-30 border-t border-line bg-surface/95 px-4 py-3 backdrop-blur lg:hidden">
          <div className="mx-auto flex max-w-lg items-center gap-3">
            <div className="min-w-0 flex-1">
              <p className="text-xs text-muted">الإجمالي · {formatQuantity(totals.items)} قطعة</p>
              <p className="tabular text-2xl font-bold">{formatMoney(totals.subtotal)}</p>
            </div>
            <Button size="lg" variant="accent" className="px-8" onClick={() => setCheckout(true)}>
              الدفع
            </Button>
          </div>
        </div>
      )}

      {editing && (
        <LineSheet line={cart.lines.find((l) => l.productId === editing.productId) ?? editing} tier={cart.tier} tierPrice={tierPrice(editing, cart.tier)}
          onSave={(patch) => update(editing.productId, patch)} onRemove={() => remove(editing.productId)} onClose={() => setEditing(null)} />
      )}
      {checkout && (
        <CheckoutSheet cart={cart} subtotal={totals.subtotal} listTotal={totals.listTotal} onClose={() => setCheckout(false)}
          onDone={(sale, change) => { setCheckout(false); reset(); setDone({ sale, change }); }} />
      )}
      {scanning && <BarcodeScannerSheet continuous title="مسح المنتجات" onDetected={(c) => void onScanned(c)} onClose={() => setScanning(false)} />}
      {done && <SaleDoneSheet sale={done.sale} change={done.change} onClose={() => { setDone(null); searchRef.current?.querySelector('input')?.focus(); }} />}
    </div>
  );
}

function Totals({ subtotal, discounts }: { subtotal: number; discounts: number }) {
  return (
    <div className="space-y-1">
      {discounts > 0 && (
        <p className="flex justify-between text-sm text-muted"><span>خصومات البنود</span><span className="tabular">{formatMoney(discounts)}</span></p>
      )}
      <p className="flex items-end justify-between"><span className="font-semibold">الإجمالي</span><span className="tabular text-2xl font-bold">{formatMoney(subtotal)}</span></p>
    </div>
  );
}

/** The lowest price a line may be sold at; for wholesale, a wholesale price set below the minimum is the floor. */
const minimumPrice = (l: Pick<CartLine, 'minSalePrice' | 'wholesalePrice'>, tier: PriceTier) =>
  tier === 'Wholesale' ? Math.min(l.minSalePrice, l.wholesalePrice) : l.minSalePrice;

/** Quantity, price and discount of one line. Price/discount changes are checked again by the server. */
function LineSheet({ line, tier, tierPrice: listPrice, onSave, onRemove, onClose }: {
  line: CartLine;
  tier: PriceTier;
  tierPrice: number;
  onSave: (patch: Partial<CartLine>) => void;
  onRemove: () => void;
  onClose: () => void;
}) {
  const { can } = useAuth();
  const canDiscount = can(P.SalesDiscount, P.SalesPriceOverride);
  const [qty, setQty] = useState(String(line.quantity));
  const [price, setPrice] = useState(String(line.unitPrice));
  const [discount, setDiscount] = useState(line.lineDiscount ? String(line.lineDiscount) : '');
  const [error, setError] = useState<string>();

  const q = parseNumber(qty);
  const p = parseNumber(price);
  const d = parseNumber(discount) ?? 0;
  const netUnit = q && p !== undefined && !Number.isNaN(p) ? (q * p - d) / q : NaN;
  // Anyone may change the price; nobody may go under the minimum sale price (for wholesale, a lower wholesale price is the floor).
  const minimum = minimumPrice(line, tier);
  const belowMinimum = !Number.isNaN(netUnit) && netUnit + 0.0001 < minimum;
  const belowCost = !belowMinimum && line.cost != null && !Number.isNaN(netUnit) && netUnit + 0.0001 < line.cost;

  const save = () => {
    if (q === undefined || Number.isNaN(q) || q <= 0) return setError('أدخل كمية صحيحة');
    if (q > line.available) return setError(`المتوفر ${formatQuantity(line.available)} فقط`);
    if (p === undefined || Number.isNaN(p) || p < 0) return setError('أدخل سعرًا صحيحًا');
    if (Number.isNaN(d) || d < 0 || d > q * p) return setError('الخصم غير صحيح');
    if (belowMinimum) return setError(`لا يمكن البيع بأقل من أقل سعر بيع (${formatMoney(minimum)})`);
    onSave({ quantity: q, unitPrice: p, customPrice: p !== listPrice, lineDiscount: round2(d) });
    onClose();
  };

  return (
    <Sheet open onClose={onClose} title={line.name}
      footer={
        <div className="flex gap-2">
          <Button block size="lg" onClick={save}>حفظ</Button>
          <Button size="lg" variant="outline" aria-label="حذف من الفاتورة" onClick={() => { onRemove(); onClose(); }}>
            <Trash2 className="size-5 text-danger" />
          </Button>
        </div>
      }>
      <div className="space-y-4">
        {error && <p role="alert" className="rounded-xl bg-red-50 p-3 text-sm text-danger">{error}</p>}
        <div className="grid grid-cols-2 gap-3">
          <Field label={`الكمية (${line.unitName})`} hint={`المتوفر ${formatQuantity(line.available)}`}>
            {(id, h) => <Input id={id} aria-describedby={h} inputMode="decimal" className="ltr text-lg" value={qty} onChange={(e) => setQty(e.target.value)} />}
          </Field>
          <Field label="سعر الوحدة" hint={`السعر المحدد ${formatMoney(listPrice)}${minimum > 0 ? ` · أقل سعر ${formatMoney(minimum)}` : ''}`}>
            {(id, h) => <Input id={id} aria-describedby={h} inputMode="decimal" className="ltr text-lg" value={price} invalid={belowMinimum}
              onChange={(e) => { setPrice(e.target.value); setError(undefined); }} />}
          </Field>
        </div>
        {canDiscount && (
          <Field label="خصم على هذا البند (₪)">
            {(id) => <Input id={id} inputMode="decimal" className="ltr" value={discount} onChange={(e) => setDiscount(e.target.value)} />}
          </Field>
        )}
        {belowMinimum && (
          <p className="rounded-xl bg-red-50 p-3 text-sm text-danger">السعر أقل من أقل سعر بيع ({formatMoney(minimum)}) — لا يمكن الحفظ.</p>
        )}
        {belowCost && (
          <p className="rounded-xl bg-amber-50 p-3 text-sm text-amber-800">
            السعر أقل من التكلفة.{can(P.SalesPriceOverride) ? ' لديك صلاحية البيع بهذا السعر.' : ' لن يُقبل إلا بصلاحية خاصة.'}
          </p>
        )}
      </div>
    </Sheet>
  );
}
