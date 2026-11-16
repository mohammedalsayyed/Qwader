import { useEffect, useRef, useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { CloudOff, FilePen, Minus, PackageX, Pause, Pencil, Play, Plus, ScanBarcode, ShoppingCart, Trash2, X } from 'lucide-react';
import { useSearchParams } from 'react-router';
import { productsApi, type ProductUnit } from '@/api/catalog';
import { ApiError } from '@/api/client';
import { customersApi, salesApi, type PriceTier, type Sale } from '@/api/sales';
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
import { applyOfflineChanges, findByCode, searchProducts, snapshotAge, useOfflineState } from '@/lib/offline';
import { formatDateTime } from '@/lib/format';
import { cartTotals, floorPrice, lineTotal, tierPrice, toCartProduct, unitChoices, useCart, type CartLine, type HeldCart } from './cart';
import { useIsOffline } from './OfflineSales';
import { CheckoutSheet, SaleDoneSheet } from './CheckoutSheet';
import { BarcodeScannerSheet, ScanButton } from '@/components/ui/BarcodeScanner';

export function PosPage() {
  const { cart, add, update, remove, changeUnit, setTier, reset, startEdit, held, hold, resume, dropHeld, renameHeld, setCustomer } = useCart();
  const [params, setParams] = useSearchParams();
  const [loadingEdit, setLoadingEdit] = useState(false);
  useLookups(); // load accounts early so checkout opens instantly
  const { can, user } = useAuth();
  const canWholesale = can(P.SalesWholesale);
  // A cart saved by a manager on this device must not let a seller keep wholesale prices.
  useEffect(() => {
    if (!canWholesale && cart.tier === 'Wholesale') setTier('Retail');
  }, [canWholesale, cart.tier, setTier]);
  const toast = useToast();
  const confirm = useConfirm();
  const [q, setQ] = useState('');
  const [editing, setEditing] = useState<CartLine | null>(null);
  const [flashKey, setFlashKey] = useState<string | null>(null);
  const [checkout, setCheckout] = useState(false);
  const [done, setDone] = useState<{ sale: Sale; change: number } | null>(null);
  const [flash, setFlash] = useState<number | null>(null);
  const [scanning, setScanning] = useState(false);
  const searchRef = useRef<HTMLDivElement>(null);
  const term = useDebouncedValue(q.trim(), 200);
  const totals = cartTotals(cart);

  // Without internet the till searches the copy of the products saved on this device.
  const offline = useIsOffline();
  const { snapshot } = useOfflineState();
  const age = snapshotAge(snapshot);
  const canSellOffline = offline && age.usable && !!snapshot;
  // The search runs on that copy with or without internet: results show as each letter is typed, with no wait for the
  // server. At the till the copy is brought up to date every few seconds, and the server checks stock and prices on saving.
  // Only a device with no usable copy yet asks the server.
  const onDevice = age.usable && !!snapshot;
  const typed = q.trim();
  const remote = useQuery({
    queryKey: ['pos-search', term],
    queryFn: ({ signal }) => productsApi.list({ search: term, pageSize: 8 }, signal),
    enabled: term.length > 0 && !offline && !onDevice,
    placeholderData: keepPreviousData,
    staleTime: 10_000,
  });
  const localItems = onDevice && typed ? searchProducts(snapshot, typed) : [];
  const results = offline || onDevice
    ? { data: { items: localItems }, isPending: false, isFetching: false }
    : remote;
  const shownTerm = onDevice ? typed : term;

  const addProduct = (p: Parameters<typeof add>[0], unit: ProductUnit | null = null) => {
    const error = add(p, 1, unit);
    if (error) toast.error(error);
    else {
      // Sold anyway; the warning is so the purchase price gets entered (its profit reads as the whole price until then).
      if (p.noCost ?? p.averageCost === 0) toast.info(`تنبيه: تكلفة «${p.displayName}» صفر (لم يُدخَل سعر الشراء) — يكمل البيع عادي، لكن الربح لن يكون صحيحًا`);
      setFlash(p.id);
      setFlashKey(`${p.id}:${unit?.id ?? 0}`);
      setTimeout(() => { setFlash(null); setFlashKey(null); }, 600);
    }
    return !error;
  };

  /** Enter (or a barcode scanner, which types the code and presses Enter): exact code first, else a single match. */
  const onEnter = async () => {
    const code = q.trim();
    if (!code) return;
    if (offline) {
      const local = canSellOffline ? findByCode(snapshot, code) : undefined;
      if (local && addProduct(local)) setQ('');
      else if (!local && localItems.length === 1 && addProduct(localItems[0])) setQ('');
      else if (!local && localItems.length === 0) toast.error(`لا يوجد منتج بالكود أو الاسم «${code}»`);
      searchRef.current?.querySelector('input')?.focus();
      return;
    }
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
    if (offline) {
      const local = canSellOffline ? findByCode(snapshot, code) : undefined;
      if (local) addProduct(local);
      else toast.error(`لا يوجد منتج بالباركود ${code}`);
      return;
    }
    try {
      addProduct(toCartProduct(await productsApi.byCode(code)));
    } catch (e) {
      toast.error(e instanceof ApiError && e.status === 404 ? `لا يوجد منتج بالباركود ${code} — أضفه من شاشة المنتجات` : errorMessage(e));
    }
  };

  const showResults = shownTerm.length > 0;

  // «تعديل الفاتورة» opens the till with ?edit=<id>: the invoice is loaded into the cart as it was sold.
  const editParam = Number(params.get('edit')) || 0;
  useEffect(() => {
    if (!editParam) return;
    if (cart.editing?.saleId === editParam) { setParams({}, { replace: true }); return; }
    let cancelled = false;
    setLoadingEdit(true);
    (async () => {
      try {
        const sale = await salesApi.get(editParam);
        if (sale.status !== 'Posted') throw new Error('تُعدَّل الفاتورة المعتمدة التي ليس عليها مرتجع فقط');
        const ids = [...new Set(sale.lines.map((l) => l.productId))];
        const products = new Map((await Promise.all(ids.map((id) => productsApi.get(id)))).map((p) => [p.id, p]));
        const customer = sale.isWalkIn ? null : await customersApi.get(sale.customerId);
        if (cancelled) return;
        // Its own quantities come back when it is saved, so they count as available while editing.
        const own = (id: number) => sale.lines.filter((l) => l.productId === id).reduce((t, l) => t + l.quantity * (l.unitFactor ?? 1), 0);
        startEdit(sale.priceTier, {
          saleId: sale.id,
          number: sale.number,
          invoiceDiscount: sale.invoiceDiscount,
          // Their balance without this invoice's own debt (it goes when the edit is saved).
          customer: customer && { id: customer.id, name: customer.name, phone: customer.phone, code: customer.code, balance: round2(customer.balance - sale.remaining) },
          walkInName: sale.isWalkIn && sale.customerName !== 'زبون نقدي' ? sale.customerName : null,
          walkInPhone: sale.isWalkIn ? sale.customerPhone : null,
          // What was paid, and the transfers still waiting for verification (they carry over to the new invoice when saved).
          payments: [
            ...sale.payments.map((p) => ({ accountId: p.accountId, amount: p.amount, reference: p.reference })),
            ...sale.transfers.filter((t) => t.status === 'Pending' && t.accountId).map((t) => ({ accountId: t.accountId!, amount: t.amount, reference: t.reference })),
          ],
          // The «instead of …» line the server adds is not carried into the next edit.
          notes: sale.notes?.replace(/^بدل الفاتورة \S+ \(تعديل\)( — )?/, '') || null,
        }, sale.lines.map((l) => {
          const product = toCartProduct(products.get(l.productId)!);
          const net = l.lineTotal / l.quantity;
          // The price it was sold at: one net price when it divides exactly, else the price and its discount.
          const exact = Math.abs(round2(net) * l.quantity - l.lineTotal) < 0.005;
          return {
            p: { ...product, quantity: product.quantity + own(l.productId) },
            unit: product.units?.find((u) => u.id === l.productUnitId) ?? null,
            quantity: l.quantity,
            unitPrice: exact ? round2(net) : l.unitPrice,
            lineDiscount: exact ? 0 : l.lineDiscount,
          };
        }));
        toast.info(`تعديل الفاتورة ${sale.number}: غيّر ما تريد ثم «اعتماد الفاتورة»`);
      } catch (e) {
        if (!cancelled) toast.error(errorMessage(e));
      } finally {
        if (!cancelled) { setLoadingEdit(false); setParams({}, { replace: true }); }
      }
    })();
    return () => { cancelled = true; };
    // Runs when the till is opened for an edit.
  }, [editParam]);

  return (
    <div className="pb-[calc(7rem+env(safe-area-inset-bottom))] lg:grid lg:grid-cols-[minmax(0,1fr)_420px] lg:gap-6 lg:pb-0 2xl:grid-cols-[minmax(0,1fr)_440px_300px]">
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

        {loadingEdit && <Spinner className="py-4" />}
        {cart.editing && (
          <div className="mb-3 flex flex-wrap items-center justify-between gap-2 rounded-xl border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900">
            <span className="flex items-center gap-2">
              <FilePen className="size-5 shrink-0" />
              <span>تعديل الفاتورة <b className="ltr inline-block">{cart.editing.number}</b>: عند الاعتماد تُلغى الأصلية وتُنشأ فاتورة جديدة بدلها (معًا).</span>
            </span>
            <Button size="sm" variant="outline" onClick={() => reset()}><X className="size-4" /> إلغاء التعديل</Button>
          </div>
        )}
        {offline && (canSellOffline ? (
          <p className="mb-3 flex items-start gap-2 rounded-xl bg-amber-50 p-3 text-sm text-amber-900">
            <CloudOff className="mt-0.5 size-4 shrink-0" />
            <span>بيع بدون إنترنت من بيانات {formatDateTime(snapshot.generatedAt)}. تُحفظ الفاتورة على الجهاز وتُرسل عند عودة الاتصال.</span>
          </p>
        ) : (
          <p role="alert" className="mb-3 flex items-start gap-2 rounded-xl bg-red-50 p-3 text-sm text-danger">
            <CloudOff className="mt-0.5 size-4 shrink-0" />
            <span>{snapshot
              ? `لا يمكن البيع بدون إنترنت: بيانات المنتجات على هذا الجهاز أقدم من ${snapshot.maxHours} ساعة. اتصل بالإنترنت لتحديثها.`
              : 'لا يمكن البيع بدون إنترنت: لا توجد بيانات منتجات محفوظة على هذا الجهاز. افتح نقطة البيع مرة واحدة والإنترنت متصل.'}</span>
          </p>
        ))}

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
              <EmptyState icon={<PackageX className="size-8" />} title={`لا توجد نتائج لـ «${shownTerm}»`} />
            ) : (
              <ul className={cn('divide-y divide-line', results.isFetching && 'opacity-60')}>
                {results.data.items.map((p) => {
                  // In base units: a dozen on the invoice holds 12 pieces.
                  const inCart = cart.lines.filter((l) => l.productId === p.id).reduce((s, l) => s + l.quantity * l.factor, 0);
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
                            {/* Average cost, for those who may see it (others get no cost from the server); zero shows as zero. */}
                            {p.averageCost != null && <> · <span className="whitespace-nowrap">التكلفة <span className="tabular font-semibold text-gray-700">{formatMoney(p.averageCost)}</span></span></>}
                          </span>
                        </span>
                        <span className="tabular shrink-0 font-bold">{formatMoney(tierPrice(p, cart.tier))}</span>
                        <span className={cn('flex size-10 shrink-0 items-center justify-center rounded-xl', out ? 'bg-canvas text-muted' : 'bg-brand-700 text-white')}>
                          <Plus className="size-5" />
                        </span>
                      </button>
                      {/* The same product by the dozen (or any other unit it is sold in). */}
                      {(p.units?.length ?? 0) > 0 && (
                        <div className="flex flex-wrap justify-end gap-2 px-3 pb-3">
                          {p.units!.map((u) => {
                            const short = p.quantity - inCart < u.factor;
                            return (
                              <button key={u.id} type="button" disabled={short} onClick={() => addProduct(p, u)}
                                className={cn('inline-flex h-10 items-center gap-1.5 rounded-xl border px-3 text-sm font-semibold',
                                  short ? 'border-line text-muted opacity-60' : 'border-brand-200 bg-brand-50 text-brand-800 active:bg-brand-100',
                                  flashKey === `${p.id}:${u.id}` && 'bg-green-50')}>
                                <Plus className="size-4" aria-hidden /> {u.unitName}
                                <span className="text-xs font-normal">({formatQuantity(u.factor)} {p.unitName})</span>
                                <span className="tabular">{formatMoney(cart.tier === 'Wholesale' ? u.wholesalePrice : u.retailPrice)}</span>
                              </button>
                            );
                          })}
                        </div>
                      )}
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
      {/* Below the widest screens the invoices put aside sit above the invoice; on the widest they have their own column. */}
      {held.length > 0 && (
        <div className="mt-4 lg:hidden">
          <HeldList held={held} onResume={resume} onDrop={dropHeld} onRename={renameHeld} compact />
        </div>
      )}
      <section className={cn('mt-4 lg:mt-0', cart.lines.length === 0 && 'hidden lg:block')} aria-label="الفاتورة">
        {held.length > 0 && (
          <div className="mb-4 hidden lg:block 2xl:hidden">
            <HeldList held={held} onResume={resume} onDrop={dropHeld} onRename={renameHeld} compact />
          </div>
        )}
        <div className="mb-2 flex items-center justify-between">
          <h2 className="font-bold">الفاتورة ({formatQuantity(totals.items)} قطعة)</h2>
          {cart.lines.length > 0 && (
            <div className="flex items-center">
            <button type="button" className="me-3 inline-flex h-10 items-center gap-1 rounded-lg px-2 text-sm font-semibold text-brand-700 hover:bg-brand-50"
              onClick={() => { hold(); toast.success('عُلّقت الفاتورة — تجدها في «الفواتير المعلّقة»'); searchRef.current?.querySelector('input')?.focus(); }}>
              <Pause className="size-4" /> تعليق
            </button>
            <button type="button" className="h-10 px-2 text-sm text-danger"
              onClick={async () => { if (await confirm({ title: 'إفراغ الفاتورة', message: 'سيتم حذف كل المنتجات من هذه الفاتورة.', confirmLabel: 'إفراغ', danger: true })) reset(); }}>
              إفراغ
            </button>
            </div>
          )}
        </div>

        {cart.lines.length === 0 ? (
          <div className="rounded-card border border-dashed border-line p-8 text-center text-sm text-muted">لا توجد منتجات بعد</div>
        ) : (
          <ul className="space-y-2">
            {cart.lines.map((l) => (
              <li key={l.key} className="rounded-card border border-line bg-surface p-3">
                <button type="button" onClick={() => setEditing(l)} className="block w-full text-start font-semibold leading-snug">{l.name}</button>
                {l.noCost && <p className="mt-0.5 text-xs font-semibold text-warning">تكلفته صفر — لم يُدخَل سعر الشراء</p>}
                {l.units.length > 0 && (
                  <div className="mt-1.5 flex rounded-xl bg-black/5 p-1" role="radiogroup" aria-label={`وحدة البيع: ${l.name}`}>
                    {unitChoices(l).map((u) => (
                      <button key={u.productUnitId ?? 0} type="button" role="radio" aria-checked={l.productUnitId === u.productUnitId}
                        onClick={() => { const e = changeUnit(l.key, u.productUnitId); if (e) toast.error(e); }}
                        className={cn('h-9 flex-1 rounded-lg px-2 text-sm font-semibold', l.productUnitId === u.productUnitId ? 'bg-surface shadow-sm' : 'text-muted')}>
                        {u.unitName}{u.factor !== 1 && <span className="text-xs font-normal"> ({formatQuantity(u.factor)})</span>}
                      </button>
                    ))}
                  </div>
                )}
                {/* The price: a button to type any price, and one tap for the lowest (minimum sale price) or the highest (the list price). */}
                <PriceRow line={l} tier={cart.tier} onEdit={() => setEditing(l)}
                  onPick={(unitPrice) => update(l.key, { unitPrice, customPrice: unitPrice !== tierPrice(l, cart.tier),
                    // At the lowest price a line discount would take it under the minimum, so it goes.
                    lineDiscount: unitPrice === minimumPrice(l, cart.tier) ? 0 : l.lineDiscount })} />
                <div className="mt-2 flex items-center justify-between gap-2">
                  <div className="flex items-center gap-1">
                    <IconButton label={l.quantity <= 1 ? `حذف «${l.name}» من الفاتورة` : 'إنقاص الكمية'} className="border border-line"
                      onClick={() => (l.quantity <= 1 ? remove(l.key) : update(l.key, { quantity: l.quantity - 1 }))}>
                      {l.quantity <= 1 ? <Trash2 className="size-5 text-danger" /> : <Minus className="size-5" />}
                    </IconButton>
                    <QuantityInput line={l} onChange={(quantity) => update(l.key, { quantity })} onLimit={(msg) => toast.error(msg)} />
                    <IconButton label="زيادة الكمية" className="border border-line" disabled={l.quantity >= l.available}
                      onClick={() => update(l.key, { quantity: l.quantity + 1 })}>
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
            <Totals subtotal={totals.subtotal} discounts={round2(totals.lineDiscounts + totals.priceReductions)} />
            <Button block size="lg" variant="accent" className="mt-3" onClick={() => setCheckout(true)}>
              <ShoppingCart className="size-5" /> الدفع
            </Button>
          </div>
        )}
      </section>

      {/* Widest screens: the invoices put aside have their own column, beside the invoice. */}
      <aside className="hidden 2xl:block" aria-label="الفواتير المعلّقة">
        <HeldList held={held} onResume={resume} onDrop={dropHeld} onRename={renameHeld} />
      </aside>

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
        <LineSheet line={cart.lines.find((l) => l.key === editing.key) ?? editing} tier={cart.tier}
          tierPrice={tierPrice(cart.lines.find((l) => l.key === editing.key) ?? editing, cart.tier)}
          onSave={(patch) => update(editing.key, patch)} onRemove={() => remove(editing.key)} onClose={() => setEditing(null)} />
      )}
      {checkout && (
        <CheckoutSheet cart={cart} subtotal={totals.subtotal} lineDiscounts={totals.lineDiscounts} offline={offline} canSellOffline={!!snapshot && age.usable}
          onClose={() => setCheckout(false)}
          onCustomer={setCustomer}
          onHold={() => {
            setCheckout(false); hold(); setQ('');
            toast.success('عُلّقت الفاتورة — تجدها في «الفواتير المعلّقة»');
            searchRef.current?.querySelector('input')?.focus();
          }}
          onDone={(sale, change) => {
            setCheckout(false); reset(); setQ(''); setDone({ sale, change });
            // The sold stock leaves the device's copy now, not at the next refresh.
            if (user) void applyOfflineChanges(user).catch(() => undefined);
          }} />
      )}
      {scanning && <BarcodeScannerSheet continuous title="مسح المنتجات" onDetected={(c) => void onScanned(c)} onClose={() => setScanning(false)} />}
      {done && <SaleDoneSheet sale={done.sale} change={done.change} onClose={() => { setDone(null); setQ(''); searchRef.current?.querySelector('input')?.focus(); }} />}
    </div>
  );
}

/** The cart's quantity, typed directly (big quantities) as well as with − and +; never more than in stock. */
function QuantityInput({ line, onChange, onLimit }: { line: CartLine; onChange: (quantity: number) => void; onLimit: (message: string) => void }) {
  const [text, setText] = useState<string | null>(null);
  // A unit counted whole (piece, set, carton) — set in the settings — takes no fractions here either.
  const whole = useLookups().data?.units.find((u) => u.name === line.unitName)?.wholeNumbersOnly ?? false;
  const commit = () => {
    if (text === null) return;
    const n = parseNumber(text);
    setText(null);
    if (n === undefined || Number.isNaN(n) || n <= 0 || n === line.quantity) return;
    if (whole && !Number.isInteger(n)) {
      onLimit(`«${line.name}» يُباع بالـ${line.unitName} بدون كسور`);
      return;
    }
    if (n > line.available) {
      onLimit(`المتوفر من «${line.name}» ${formatQuantity(line.available)} ${line.unitName} فقط`);
      onChange(line.available);
      return;
    }
    onChange(n);
  };
  return (
    <Input aria-label={`الكمية: ${line.name}`} inputMode="decimal" className="tabular h-11 w-16 px-1 text-center text-lg font-bold"
      value={text ?? formatQuantity(line.quantity)}
      onFocus={(e) => { setText(String(line.quantity)); requestAnimationFrame(() => e.target.select()); }}
      onChange={(e) => setText(e.target.value)}
      onBlur={commit}
      onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); e.currentTarget.blur(); } }} />
  );
}

function Totals({ subtotal, discounts }: { subtotal: number; discounts: number }) {
  return (
    <div className="space-y-1">
      {discounts > 0 && (
        <p className="flex justify-between text-sm text-muted"><span>الخصومات</span><span className="tabular">{formatMoney(discounts)}</span></p>
      )}
      <p className="flex items-end justify-between"><span className="font-semibold">الإجمالي</span><span className="tabular text-2xl font-bold">{formatMoney(subtotal)}</span></p>
    </div>
  );
}

/** The lowest price a line may be sold at; for wholesale, a wholesale price set below the minimum is the floor. */
/** The cart line's price: «تعديل» opens the line to type a price; «الأقل» and «الأعلى» set it in one tap. */
function PriceRow({ line: l, tier, onEdit, onPick }: { line: CartLine; tier: PriceTier; onEdit: () => void; onPick: (price: number) => void }) {
  const highest = tierPrice(l, tier);
  const lowest = minimumPrice(l, tier);
  const choices = lowest > 0 && lowest < highest
    ? [{ label: 'الأقل', price: lowest }, { label: 'الأعلى', price: highest }]
    : [];
  // Down to the cost the price is just changed; only what goes under it is a discount (as the invoice records it).
  const reduction = round2(l.quantity * Math.max(0, floorPrice(l, tier) - l.unitPrice));
  const changed = l.unitPrice < highest - 0.004 && reduction <= 0.004;
  return (
    <div className="mt-2">
      <div className="flex items-stretch gap-2">
        <button type="button" onClick={onEdit} aria-label={`تعديل سعر ${l.name}`}
          className="flex min-h-11 min-w-0 flex-1 items-center justify-between gap-2 rounded-xl border border-brand-200 bg-brand-50 px-3 text-brand-800 hover:bg-brand-100 active:bg-brand-100">
          <span className="flex min-w-0 flex-col items-start leading-tight">
            <span className="text-[11px] text-brand-700">{`السعر لكل ${l.unitName}`}</span>
            <span className="tabular text-base font-bold">{formatMoney(l.unitPrice)}</span>
          </span>
          <span className="flex shrink-0 items-center gap-1 text-xs font-semibold"><Pencil className="size-4" aria-hidden /> تعديل</span>
        </button>
        {choices.length > 0 && (
          <div className="flex shrink-0 gap-1 rounded-xl bg-black/5 p-1" role="radiogroup" aria-label={`اختيار سعر ${l.name}`}>
            {choices.map((c) => {
              const active = Math.abs(l.unitPrice - c.price) < 0.005;
              return (
                <button key={c.label} type="button" role="radio" aria-checked={active} onClick={() => onPick(c.price)}
                  className={cn('flex min-w-16 flex-col items-center justify-center rounded-lg px-2 leading-tight transition',
                    active ? 'bg-surface text-brand-800 shadow-sm' : 'text-muted hover:text-ink')}>
                  <span className="text-[11px]">{c.label}</span>
                  <span className="tabular text-sm font-bold">{formatMoney(c.price)}</span>
                </button>
              );
            })}
          </div>
        )}
      </div>
      {(reduction > 0.004 || changed || l.unitPrice > highest + 0.004 || l.lineDiscount > 0) && (
        <p className="mt-1 flex flex-wrap gap-x-3 text-xs font-semibold text-warning">
          {reduction > 0.004 && <span>خصم تحت التكلفة {formatMoney(reduction)}</span>}
          {changed && <span className="text-muted">سعر معدَّل (ليس خصمًا)</span>}
          {l.unitPrice > highest + 0.004 && <span>سعر معدَّل (أعلى من المحدد)</span>}
          {l.lineDiscount > 0 && <span>خصم {formatMoney(l.lineDiscount)}</span>}
        </p>
      )}
    </div>
  );
}

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
  // Anyone may change the price. Under the minimum sale price (for wholesale, a lower wholesale price is the floor) or the cost
  // only with the price-override permission — then it is allowed, and the invoice is marked for review.
  const mayOverride = can(P.SalesPriceOverride);
  const minimum = minimumPrice(line, tier);
  const belowMinimum = !Number.isNaN(netUnit) && netUnit + 0.0001 < minimum;
  const belowCost = line.cost != null && !Number.isNaN(netUnit) && netUnit + 0.0001 < line.cost;

  const save = () => {
    if (q === undefined || Number.isNaN(q) || q <= 0) return setError('أدخل كمية صحيحة');
    if (q > line.available) return setError(`المتوفر ${formatQuantity(line.available)} فقط`);
    if (p === undefined || Number.isNaN(p) || p < 0) return setError('أدخل سعرًا صحيحًا');
    if (Number.isNaN(d) || d < 0 || d > q * p) return setError('الخصم غير صحيح');
    if (belowMinimum && !mayOverride) return setError(`لا يمكن البيع بأقل من أقل سعر بيع (${formatMoney(minimum)})`);
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
            {(id, h) => <Input id={id} aria-describedby={h} inputMode="decimal" className="ltr text-lg" value={price} invalid={belowMinimum && !mayOverride}
              onChange={(e) => { setPrice(e.target.value); setError(undefined); }} />}
          </Field>
        </div>
        {canDiscount && (
          <Field label="خصم على هذا البند (₪)">
            {(id) => <Input id={id} inputMode="decimal" className="ltr" value={discount} onChange={(e) => setDiscount(e.target.value)} />}
          </Field>
        )}
        {belowMinimum && (mayOverride ? (
          <p className="rounded-xl bg-amber-50 p-3 text-sm text-amber-800">
            السعر أقل من أقل سعر بيع ({formatMoney(minimum)}). لديك صلاحية البيع بهذا السعر؛ يُكتب ذلك على الفاتورة وفي سجل التدقيق، ولا تحتاج مراجعة.
          </p>
        ) : (
          <p className="rounded-xl bg-red-50 p-3 text-sm text-danger">السعر أقل من أقل سعر بيع ({formatMoney(minimum)}) — لا يمكن الحفظ.</p>
        ))}
        {belowCost && (
          <p className="rounded-xl bg-amber-50 p-3 text-sm text-amber-800">
            السعر أقل من التكلفة.{mayOverride ? ' لديك صلاحية البيع بهذا السعر؛ يُكتب ذلك على الفاتورة ولا تحتاج مراجعة.' : ' لن يُقبل إلا بصلاحية خاصة.'}
          </p>
        )}
      </div>
    </Sheet>
  );
}

/** The invoices put aside («معلّقة»): each customer's cart while they keep choosing; one tap takes it back. */
function HeldList({ held, onResume, onDrop, onRename, compact = false }: {
  held: HeldCart[];
  onResume: (id: string) => void;
  onDrop: (id: string) => void;
  onRename: (id: string, label: string) => void;
  compact?: boolean;
}) {
  const confirm = useConfirm();
  const [renaming, setRenaming] = useState<string | null>(null);
  const totalOf = (h: HeldCart) => round2(h.cart.lines.reduce((t, l) => t + lineTotal(l), 0));
  const itemsOf = (h: HeldCart) => h.cart.lines.reduce((t, l) => t + l.quantity * l.factor, 0);
  return (
    <div className={cn(!compact && 'sticky top-4')}>
      <h2 className="mb-2 flex items-center gap-2 font-bold">
        الفواتير المعلّقة {held.length > 0 && <span className="rounded-full bg-amber-100 px-2 text-sm text-amber-900">{held.length}</span>}
      </h2>
      {held.length === 0 ? (
        <p className="rounded-card border border-dashed border-line p-4 text-sm leading-6 text-muted">
          زبون ما زال يختار؟ اضغط «تعليق» فوق الفاتورة: تُحفظ هنا ويمكنك البيع للزبون التالي، ثم تعود إليها بضغطة.
        </p>
      ) : (
        <ul className={cn(compact ? 'flex gap-2 overflow-x-auto pb-1' : 'space-y-2')}>
          {held.map((h) => (
            <li key={h.id} className={cn('rounded-card border border-amber-200 bg-amber-50/60 p-3', compact && 'min-w-56 shrink-0')}>
              {renaming === h.id ? (
                <Input autoFocus defaultValue={h.label} aria-label="اسم الفاتورة المعلّقة" className="h-9"
                  onBlur={(e) => { onRename(h.id, e.target.value); setRenaming(null); }}
                  onKeyDown={(e) => { if (e.key === 'Enter') e.currentTarget.blur(); if (e.key === 'Escape') setRenaming(null); }} />
              ) : (
                <button type="button" className="block w-full truncate text-start font-semibold" title="تغيير الاسم" onClick={() => setRenaming(h.id)}>
                  {h.label}
                </button>
              )}
              <p className="mt-0.5 text-xs text-muted">
                {formatQuantity(itemsOf(h))} قطعة · {new Date(h.heldAt).toLocaleTimeString('ar-PS-u-nu-latn', { hour: '2-digit', minute: '2-digit' })}
                {h.cart.tier === 'Wholesale' && ' · جملة'}{h.cart.editing && ` · تعديل ${h.cart.editing.number}`}
              </p>
              <div className="mt-2 flex items-center justify-between gap-2">
                <span className="tabular font-bold">{formatMoney(totalOf(h))}</span>
                <div className="flex items-center gap-1">
                  <IconButton label={`حذف «${h.label}»`} className="size-9"
                    onClick={async () => { if (await confirm({ title: 'حذف الفاتورة المعلّقة', message: `تُحذف «${h.label}» بمنتجاتها (لم تُسجَّل بعد).`, confirmLabel: 'حذف', danger: true })) onDrop(h.id); }}>
                    <Trash2 className="size-4 text-danger" />
                  </IconButton>
                  <Button size="sm" onClick={() => onResume(h.id)}><Play className="size-4" /> استكمال</Button>
                </div>
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
