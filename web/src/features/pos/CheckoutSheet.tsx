import { useMemo, useState } from 'react';
import { useNavigate } from 'react-router';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { CheckCircle2, CloudOff, Plus, Printer, Share2, ShoppingCart, Trash2, UserRound, X } from 'lucide-react';
import { ApiError } from '@/api/client';
import { customersApi, salesApi, type CreateSaleInput, type CustomerListItem, type Sale } from '@/api/sales';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button, IconButton } from '@/components/ui/Button';
import { Field, Input, Select } from '@/components/ui/Field';
import { Picker } from '@/components/ui/Picker';
import { Sheet } from '@/components/ui/Sheet';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';
import { formatMoney } from '@/lib/format';
import { parseNumber, round2 } from '@/lib/numbers';
import { useUnsavedChangesWarning } from '@/lib/hooks';
import { useLookups, useStoreSettings } from '@/lib/queries';
import { queueSale, searchCustomers, useOfflineState } from '@/lib/offline';
import { lineTotal, type Cart } from './cart';

type CustomerMode = 'walkin' | 'customer';
interface PaymentRow {
  key: string;
  accountId: string;
  amount: string;
  reference: string;
}

export function CheckoutSheet({ cart, subtotal, lineDiscounts, offline = false, canSellOffline = false, onClose, onDone }: {
  cart: Cart;
  subtotal: number;
  /** Discounts already given on lines; with `subtotal` they make the gross the discount limit is measured against. */
  lineDiscounts: number;
  /** No connection: the sale is kept on the device and sent later. */
  offline?: boolean;
  /** The device has a recent enough copy of the products to sell from. */
  canSellOffline?: boolean;
  onClose: () => void;
  onDone: (sale: Sale, change: number) => void;
}) {
  const { can, user } = useAuth();
  const { snapshot } = useOfflineState();
  const queryClient = useQueryClient();
  const lookups = useLookups();
  const settings = useStoreSettings();
  const accounts = lookups.data?.accounts ?? [];
  const cashId = String(accounts.find((a) => a.type === 'Cash')?.id ?? accounts[0]?.id ?? '');
  const canDiscount = can(P.SalesDiscount, P.SalesPriceOverride);
  useUnsavedChangesWarning(true); // an app update waits until checkout is finished

  const [mode, setMode] = useState<CustomerMode>('walkin');
  const [customer, setCustomer] = useState<CustomerListItem | null>(null);
  const [newName, setNewName] = useState('');
  const [newPhone, setNewPhone] = useState('');
  const [discount, setDiscount] = useState('');
  const [split, setSplit] = useState(false);
  // Until the user picks one, the cash account is the default (also once the account list arrives after opening).
  const [pickedAccount, setFullAccount] = useState('');
  const fullAccount = pickedAccount || cashId;
  const fullAccountType = accounts.find((a) => String(a.id) === fullAccount)?.type;
  const [fullRef, setFullRef] = useState('');
  const [tendered, setTendered] = useState('');
  const [rows, setRows] = useState<PaymentRow[]>([]);
  const [error, setError] = useState<string>();

  const discountN = parseNumber(discount) ?? 0;
  const total = round2(subtotal - (Number.isNaN(discountN) ? 0 : discountN));
  // As the server counts it: line and invoice discounts against the lines at their (possibly changed) prices.
  const gross = subtotal + lineDiscounts;
  const discountPercent = gross > 0 ? round2(((gross - total) / gross) * 100) : 0;
  const maxDiscount = settings.data?.maxDiscountPercent ?? 0;
  // Over the store's limit: refused for sellers; allowed (but said out loud) for those who may sell under the limits.
  const overLimit = maxDiscount > 0 && discountPercent > maxDiscount + 0.001;
  const mayExceed = can(P.SalesPriceOverride);
  // Store setting: a transfer taken at the till either waits for verification (stays debt) or counts at once.
  const transfersPending = settings.data?.saleTransfersPending ?? true;
  const isTransfer = (accountId: string) => accounts.find((a) => String(a.id) === accountId)?.type !== 'Cash';

  const payments = useMemo(() => {
    if (!split) return [{ accountId: Number(fullAccount), amount: total, reference: fullRef.trim() || null, pending: transfersPending && fullAccountType !== 'Cash' }].filter((p) => p.amount > 0);
    return rows
      .map((r) => ({ accountId: Number(r.accountId), amount: parseNumber(r.amount) ?? 0, reference: r.reference.trim() || null, pending: transfersPending && isTransfer(r.accountId) }))
      .filter((p) => p.amount > 0);
  }, [split, fullAccount, fullRef, transfersPending, fullAccountType, accounts, rows, total]);
  const paid = round2(payments.filter((p) => !p.pending).reduce((s, p) => s + (Number.isNaN(p.amount) ? 0 : p.amount), 0));
  const pendingPaid = round2(payments.filter((p) => p.pending).reduce((s, p) => s + (Number.isNaN(p.amount) ? 0 : p.amount), 0));
  const remaining = round2(total - paid - pendingPaid);
  const tenderedN = parseNumber(tendered);
  const change = !split && fullAccountType === 'Cash' && tenderedN !== undefined && !Number.isNaN(tenderedN) ? round2(tenderedN - total) : 0;
  const hasCustomer = mode === 'customer' && (customer !== null || (newName.trim() !== '' && newPhone.trim() !== ''));

  const input = (): CreateSaleInput => ({
    idempotencyKey: cart.key,
    priceTier: cart.tier,
    customerId: mode === 'customer' ? customer?.id ?? null : null,
    customerName: mode === 'customer' && !customer ? newName.trim() : null,
    customerPhone: mode === 'customer' && !customer ? newPhone.trim() : null,
    invoiceDiscount: discountN,
    lines: cart.lines.map((l) => ({ productId: l.productId, quantity: l.quantity, unitPrice: l.unitPrice, lineDiscount: l.lineDiscount })),
    payments,
  });

  /** Keeps the sale on the device; the receipt shows a temporary number until it is sent. */
  const saveOffline = async (): Promise<Sale> => {
    const q = await queueSale({
      key: cart.key, userId: user!.id, input: { ...input(), soldOfflineAt: new Date().toISOString() }, total,
      customerName: mode === 'customer' ? customer?.name ?? newName.trim() : 'زبون نقدي',
    });
    // Just what the «تم البيع» sheet shows; id 0 marks a sale the server has not seen yet.
    return {
      id: 0, number: q.number, total, invoiceDiscount: discountN, remaining: Math.max(0, round2(total - paid)), customerName: q.customerName,
      lines: cart.lines.map((l) => ({ productName: l.name, quantity: l.quantity, lineTotal: lineTotal(l) })),
    } as unknown as Sale;
  };

  const submit = useMutation({
    mutationFn: async () => {
      if (offline) return saveOffline();
      try {
        return await salesApi.create(input());
      } catch (e) {
        // The connection dropped while sending: keep it on the device (same key, so it is never saved twice).
        if (e instanceof ApiError && e.isNetwork && canSellOffline) return saveOffline();
        throw e;
      }
    },
    onSuccess: (sale) => {
      for (const k of ['products', 'pos-search', 'sales', 'sales-summary', 'customers', 'notifications', 'inventory-summary'])
        void queryClient.invalidateQueries({ queryKey: [k] });
      onDone(sale, Math.max(0, change));
    },
    // The cart and this form stay as they are, so the user can fix the problem and retry with the same key.
    onError: (e) => setError(errorMessage(e)),
  });

  const confirmSale = () => {
    setError(undefined);
    if (offline && !canSellOffline) return setError('لا يمكن البيع بدون إنترنت: بيانات المنتجات على هذا الجهاز قديمة أو غير موجودة');
    if (Number.isNaN(discountN) || discountN < 0 || discountN > subtotal) return setError('الخصم غير صحيح');
    if (overLimit && !mayExceed) return setError(`الخصم (${discountPercent}%) يتجاوز الحد المسموح (${maxDiscount}%)`);
    if (payments.some((p) => Number.isNaN(p.amount))) return setError('أحد المبالغ غير صحيح');
    if (payments.some((p) => !p.accountId)) return setError('اختر الحساب لكل دفعة');
    if (pendingPaid > 0 && !hasCustomer) return setError('الحوالة بانتظار التحقق تبقى دينًا حتى تأكيدها: اختر العميل أو اكتب اسمه وجواله');
    if (paid + pendingPaid > total) return setError('المدفوع أكبر من الإجمالي؛ سجّل قيمة الفاتورة فقط واحسب الباقي للزبون');
    if (remaining > 0 && !hasCustomer) return setError('المبلغ غير مكتمل: للبيع الآجل اختر العميل أو اكتب اسمه ورقم جواله');
    if (!split && fullAccountType === 'Cash' && tenderedN !== undefined && !Number.isNaN(tenderedN) && tenderedN < total)
      return setError('المبلغ المستلم أقل من الإجمالي؛ استخدم «دفع جزئي أو بأكثر من حساب»');
    submit.mutate();
  };

  return (
    <Sheet open onClose={onClose} title="إتمام البيع" size="lg"
      footer={
        <div className="space-y-2">
          {error && <p role="alert" className="rounded-xl bg-red-50 p-3 text-sm font-medium text-danger">{error}</p>}
          <div className="flex items-center justify-between text-sm">
            <span>الإجمالي <b className="tabular text-xl">{formatMoney(total)}</b></span>
            {pendingPaid > 0 ? <span className="font-semibold text-amber-700">بانتظار التحقق: {formatMoney(pendingPaid)}</span>
              : remaining > 0 ? <span className="font-semibold text-danger">آجل: {formatMoney(remaining)}</span>
              : change > 0 ? <span className="font-semibold text-success">الباقي للزبون: {formatMoney(change)}</span> : null}
          </div>
          {offline && <p className="flex items-center gap-1.5 text-xs text-amber-800"><CloudOff className="size-4" /> بدون إنترنت: تُحفظ على الجهاز وتُرسل عند عودة الاتصال</p>}
          <Button block size="lg" variant="accent" loading={submit.isPending} onClick={confirmSale}>
            <CheckCircle2 className="size-5" /> اعتماد الفاتورة
          </Button>
        </div>
      }>
      <div className="space-y-5">
        {/* Customer */}
        <section>
          <h3 className="mb-2 text-sm font-semibold text-muted">العميل</h3>
          <div className="mb-3 grid grid-cols-2 rounded-xl bg-black/5 p-1">
            {(['walkin', 'customer'] as const).map((m) => (
              <button key={m} type="button" aria-pressed={mode === m} onClick={() => setMode(m)}
                className={cn('h-10 rounded-lg text-sm font-semibold', mode === m ? 'bg-surface shadow-sm' : 'text-muted')}>
                {m === 'walkin' ? 'زبون نقدي' : 'عميل (اسم وجوال)'}
              </button>
            ))}
          </div>
          {mode === 'customer' && (
            customer ? (
              <div className="flex items-center gap-3 rounded-xl border border-line p-3">
                <UserRound className="size-6 text-brand-700" />
                <div className="min-w-0 flex-1">
                  <p className="truncate font-semibold">{customer.name}</p>
                  <p className="ltr text-sm text-muted">{customer.phone}</p>
                  {customer.balance > 0 && <p className="text-xs text-warning">عليه سابقًا: {formatMoney(customer.balance)}</p>}
                </div>
                <IconButton label="تغيير العميل" onClick={() => setCustomer(null)}><X className="size-5" /></IconButton>
              </div>
            ) : (
              <div className="space-y-3">
                <Picker<CustomerListItem>
                  valueLabel={null}
                  placeholder="ابحث عن عميل مسجّل بالاسم أو الجوال"
                  title="اختيار عميل"
                  queryKey={offline ? 'pos-customers-offline' : 'pos-customers'}
                  search={async (q, signal) => offline
                    ? (snapshot ? searchCustomers(snapshot, q).map((c) => ({ ...c, isActive: true, isWalkIn: false })) : [])
                    : (await customersApi.list({ search: q, pageSize: 20, isActive: true }, signal)).items.filter((c) => !c.isWalkIn)}
                  getKey={(c) => c.id}
                  renderItem={(c) => (
                    <div className="flex items-center justify-between gap-2">
                      <div className="min-w-0"><p className="truncate font-semibold">{c.name}</p><p className="ltr text-xs text-muted">{c.phone ?? c.code}</p></div>
                      {c.balance > 0 && <span className="tabular shrink-0 text-sm text-warning">{formatMoney(c.balance)}</span>}
                    </div>
                  )}
                  onSelect={setCustomer}
                />
                <p className="text-center text-xs text-muted">أو عميل جديد</p>
                <div className="grid grid-cols-2 gap-2">
                  <Input aria-label="اسم العميل" placeholder="الاسم" value={newName} onChange={(e) => setNewName(e.target.value)} />
                  <Input aria-label="رقم الجوال" placeholder="رقم الجوال" type="tel" inputMode="tel" className="ltr" value={newPhone} onChange={(e) => setNewPhone(e.target.value)} />
                </div>
                <p className="text-xs text-muted">إذا كان رقم الجوال مسجلًا من قبل تُضاف الفاتورة لنفس العميل.</p>
              </div>
            )
          )}
        </section>

        {/* Discount */}
        {canDiscount && (
          <section>
            <Field label="خصم على الفاتورة (₪)" hint={maxDiscount ? `الحد المسموح ${maxDiscount}% — الخصم الحالي ${discountPercent}%` : undefined}>
              {(id, h) => <Input id={id} aria-describedby={h} inputMode="decimal" className="ltr" value={discount} invalid={overLimit && !mayExceed}
                onChange={(e) => { setDiscount(e.target.value); setError(undefined); }} />}
            </Field>
            {overLimit && (mayExceed ? (
              <p className="mt-2 rounded-lg bg-amber-50 p-2.5 text-xs leading-5 text-amber-800">
                الخصم ({discountPercent}%) أكبر من الحد المسموح ({maxDiscount}%). مسموح لك بصلاحيتك، ويُسجَّل في سجل التدقيق.
              </p>
            ) : (
              <p className="mt-2 rounded-lg bg-red-50 p-2.5 text-xs leading-5 text-danger">الخصم ({discountPercent}%) أكبر من الحد المسموح ({maxDiscount}%) — لن تُقبل الفاتورة.</p>
            ))}
          </section>
        )}

        {/* Payment */}
        <section>
          <h3 className="mb-2 text-sm font-semibold text-muted">الدفع</h3>
          {!split ? (
            <div className="space-y-3">
              <div className="grid grid-cols-3 gap-2" role="radiogroup" aria-label="الحساب المستلم">
                {accounts.map((a) => (
                  <button key={a.id} type="button" role="radio" aria-checked={fullAccount === String(a.id)} onClick={() => setFullAccount(String(a.id))}
                    className={cn('min-h-12 rounded-xl border px-2 text-sm font-semibold leading-tight',
                      fullAccount === String(a.id) ? 'border-brand-700 bg-brand-50 text-brand-800' : 'border-line')}>
                    {a.name}
                  </button>
                ))}
              </div>
              {fullAccountType === 'Cash' ? (
                <Field label="المبلغ المستلم من الزبون (لحساب الباقي)">
                  {(id) => <Input id={id} inputMode="decimal" className="ltr text-lg" placeholder={String(total)} value={tendered} onChange={(e) => setTendered(e.target.value)} />}
                </Field>
              ) : (
                <>
                  <Field label="رقم مرجع الحوالة / العملية">
                    {(id) => <Input id={id} className="ltr" value={fullRef} onChange={(e) => setFullRef(e.target.value)} />}
                  </Field>
                  <TransferNote pending={transfersPending} />
                </>
              )}
              <button type="button" onClick={() => { setSplit(true); setRows([{ key: crypto.randomUUID(), accountId: fullAccount, amount: '', reference: '' }]); }}
                className="h-10 text-sm font-semibold text-brand-700 underline">
                دفع جزئي (آجل) أو بأكثر من حساب
              </button>
            </div>
          ) : (
            <div className="space-y-2">
              {rows.map((r, i) => (
                <div key={r.key} className="rounded-xl border border-line p-3">
                  <div className="grid grid-cols-[1fr_7rem_auto] items-end gap-2">
                    <Select aria-label="الحساب" value={r.accountId} onChange={(e) => setRows((rs) => rs.map((x) => (x.key === r.key ? { ...x, accountId: e.target.value } : x)))}>
                      {accounts.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
                    </Select>
                    <Input aria-label="المبلغ" inputMode="decimal" className="ltr" placeholder="0" value={r.amount}
                      onChange={(e) => setRows((rs) => rs.map((x) => (x.key === r.key ? { ...x, amount: e.target.value } : x)))} />
                    <IconButton label="حذف الدفعة" disabled={rows.length === 1 && i === 0} onClick={() => setRows((rs) => rs.filter((x) => x.key !== r.key))}>
                      <Trash2 className="size-5 text-danger" />
                    </IconButton>
                  </div>
                  {accounts.find((a) => String(a.id) === r.accountId)?.type !== 'Cash' && (
                    <>
                      <Input aria-label="رقم المرجع" placeholder="رقم مرجع الحوالة (اختياري)" className="ltr mt-2"
                        value={r.reference} onChange={(e) => setRows((rs) => rs.map((x) => (x.key === r.key ? { ...x, reference: e.target.value } : x)))} />
                      <div className="mt-2"><TransferNote pending={transfersPending} /></div>
                    </>
                  )}
                </div>
              ))}
              <div className="flex flex-wrap gap-2">
                <Button variant="secondary" size="sm" onClick={() => setRows((rs) => [...rs, { key: crypto.randomUUID(), accountId: cashId, amount: remaining > 0 ? String(remaining) : '', reference: '' }])}>
                  <Plus className="size-4" /> دفعة أخرى
                </Button>
                <Button variant="ghost" size="sm" onClick={() => setSplit(false)}>دفع كامل بحساب واحد</Button>
              </div>
              <p className="text-sm">
                المدفوع: <b className="tabular">{formatMoney(paid)}</b> — المتبقي على العميل:{' '}
                <b className={cn('tabular', remaining > 0 && 'text-danger')}>{formatMoney(Math.max(0, remaining))}</b>
              </p>
            </div>
          )}
        </section>
      </div>
    </Sheet>
  );
}

/** Every non-cash payment is recorded as a transfer; the store setting decides whether it waits for verification. */
function TransferNote({ pending }: { pending: boolean }) {
  return pending ? (
    <p className="rounded-lg bg-amber-50 p-2.5 text-xs leading-5 text-amber-800">
      تُسجَّل حوالة <b>بانتظار التحقق</b> وتبقى دينًا على العميل حتى تأكيدها من شاشة الحوالات — لذلك اكتب اسم العميل وجواله.
    </p>
  ) : (
    <p className="rounded-lg bg-brand-50 p-2.5 text-xs leading-5 text-brand-800">تُسجَّل الحوالة <b>مؤكدة</b> وتُحسب مدفوعة فورًا.</p>
  );
}

export function SaleDoneSheet({ sale, change, onClose }: { sale: Sale; change: number; onClose: () => void }) {
  const navigate = useNavigate();
  const settings = useStoreSettings();
  const text = [
    settings.data?.storeName ?? 'معرض قويدر',
    `فاتورة رقم ${sale.number}`,
    ...sale.lines.map((l) => `• ${l.productName} × ${l.quantity} = ${formatMoney(l.lineTotal)}`),
    sale.invoiceDiscount > 0 ? `خصم: ${formatMoney(sale.invoiceDiscount)}` : null,
    `الإجمالي: ${formatMoney(sale.total)}`,
    sale.remaining > 0 ? `المتبقي: ${formatMoney(sale.remaining)}` : null,
    settings.data?.invoiceFooter ?? null,
  ].filter(Boolean).join('\n');

  const share = async () => {
    if (navigator.share) {
      try {
        await navigator.share({ title: `فاتورة ${sale.number}`, text });
        return;
      } catch {
        /* cancelled: fall through to WhatsApp */
      }
    }
    window.open(`https://wa.me/?text=${encodeURIComponent(text)}`, '_blank', 'noopener');
  };

  // id 0: kept on this device until the connection returns; it can be printed from «الفواتير» once sent.
  const queued = sale.id === 0;
  return (
    <Sheet open onClose={onClose} title={queued ? 'تم البيع — بدون إنترنت' : 'تم البيع'}
      footer={
        <div className="space-y-2">
          {queued ? (
            <Button block variant="outline" size="lg" onClick={() => void share()}><Share2 className="size-5" /> مشاركة</Button>
          ) : (
            <div className="grid grid-cols-2 gap-2">
              <Button variant="outline" size="lg" onClick={() => navigate(`/sales/${sale.id}?print=1`)}><Printer className="size-5" /> طباعة</Button>
              <Button variant="outline" size="lg" onClick={() => void share()}><Share2 className="size-5" /> مشاركة</Button>
            </div>
          )}
          <Button block size="lg" onClick={onClose}><ShoppingCart className="size-5" /> بيع جديد</Button>
        </div>
      }>
      <div className="py-2 text-center">
        <CheckCircle2 className="mx-auto size-14 text-success" />
        <p className="mt-2 text-sm text-muted">{queued ? 'رقم مؤقت' : 'رقم الفاتورة'}</p>
        <p className="ltr text-center text-xl font-bold">{sale.number}</p>
        <p className="tabular mt-3 text-3xl font-bold">{formatMoney(sale.total)}</p>
        {change > 0 && <p className="mt-2 rounded-xl bg-green-50 p-3 text-lg font-bold text-success">الباقي للزبون: {formatMoney(change)}</p>}
        {sale.remaining > 0 && <p className="mt-2 rounded-xl bg-amber-50 p-3 font-semibold text-amber-800">آجل على {sale.customerName}: {formatMoney(sale.remaining)}</p>}
        {queued && <p className="mt-3 rounded-xl bg-amber-50 p-3 text-sm text-amber-900">حُفظت الفاتورة على هذا الجهاز وتُرسل تلقائيًا عند عودة الإنترنت، فتأخذ رقمها النهائي ويمكن طباعتها من «الفواتير».</p>}
      </div>
    </Sheet>
  );
}
