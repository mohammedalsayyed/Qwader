import { useMemo, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowRight, Plus, Trash2 } from 'lucide-react';
import type { ProductDetail, ProductListItem, ProductUnit } from '@/api/catalog';
import { QuickProductSheet } from '@/features/catalog/QuickProductSheet';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { purchasesApi, suppliersApi, type Purchase } from '@/api/stock';
import { ApiError } from '@/api/client';
import { Button, IconButton } from '@/components/ui/Button';
import { Field, Input, Select, TextArea } from '@/components/ui/Field';
import { Card, EmptyState, ErrorState, PageHeader, Spinner } from '@/components/ui/Feedback';
import { Collapsible, StickyActions } from '@/components/ui/Layout';
import { PickerSheet } from '@/components/ui/Picker';
import { useConfirm } from '@/components/ui/Confirm';
import { useToast } from '@/components/ui/Toast';
import { ProductRow, SupplierPicker } from '@/components/catalog/CatalogBits';
import { productsApi } from '@/api/catalog';
import { errorMessage } from '@/lib/forms';
import { formatMoney, formatQuantity, localToday } from '@/lib/format';
import { useIdempotencyKey, useUnsavedChangesWarning } from '@/lib/hooks';
import { parseNumber, round2 } from '@/lib/numbers';
import { useLookups } from '@/lib/queries';
import { cn } from '@/lib/cn';

interface PayRow {
  key: string;
  accountId: string;
  amount: string;
}

interface Line {
  key: string;
  productId: number;
  name: string;
  unitName: string;
  /** Bought in an extra unit (dozen); null = the base unit. */
  productUnitId: number | null;
  factor: number;
  /** For switching the unit: the base unit's name and cost, and the product's other units (unknown for a saved draft). */
  baseUnitName: string;
  baseCost: number | null;
  units: ProductUnit[];
  quantity: string;
  unitCost: string;
  lineDiscount: string;
}

export function PurchaseFormPage() {
  const editId = useParams().id ? Number(useParams().id) : undefined;
  const existing = useQuery({ queryKey: ['purchase', editId], queryFn: () => purchasesApi.get(editId!), enabled: !!editId });
  const [search] = useSearchParams();
  const presetSupplierId = search.get('supplierId') ? Number(search.get('supplierId')) : undefined;
  const presetSupplier = useQuery({ queryKey: ['supplier', presetSupplierId], queryFn: () => suppliersApi.get(presetSupplierId!), enabled: !!presetSupplierId });

  if ((editId && existing.isPending) || (presetSupplierId && presetSupplier.isPending)) return <Spinner />;
  if (existing.isError) return <ErrorState message={errorMessage(existing.error)} onRetry={() => void existing.refetch()} />;
  if (existing.data && existing.data.status === 'Voided') return <ErrorState message="لا يمكن تعديل فاتورة ملغاة" />;
  if (existing.data && existing.data.lines.some((l) => l.returnedQuantity > 0))
    return <ErrorState message="على الفاتورة مردود للمورد؛ ألغِ المردود أولًا ثم عدّلها" />;

  return (
    <PurchaseForm
      existing={existing.data}
      initialSupplier={existing.data ? { id: existing.data.supplierId, name: existing.data.supplierName } : presetSupplier.data ? { id: presetSupplier.data.id, name: presetSupplier.data.name } : null}
    />
  );
}

function PurchaseForm({ existing, initialSupplier }: { existing?: Purchase; initialSupplier: { id: number; name: string } | null }) {
  const navigate = useNavigate();
  const toast = useToast();
  const confirm = useConfirm();
  const queryClient = useQueryClient();
  const lookups = useLookups();
  const [key] = useIdempotencyKey();

  const [supplier, setSupplier] = useState(initialSupplier);
  const [date, setDate] = useState(existing ? existing.date.slice(0, 10) : localToday());
  const [supplierInvoiceNo, setSupplierInvoiceNo] = useState(existing?.supplierInvoiceNo ?? '');
  // Editing a posted invoice: saving voids it and posts the new one together. The «instead of …» line is the server's.
  const posted = existing?.status === 'Posted';
  const [notes, setNotes] = useState((existing?.notes ?? '').replace(/^بدل الفاتورة \S+ \(تعديل\)( — )?/, ''));
  const [discount, setDiscount] = useState(existing?.discount ? String(existing.discount) : '');
  // Paid now, account by account: part cash, part bank… (one row is the usual case).
  const [pays, setPays] = useState<PayRow[]>(() =>
    existing?.payments.length
      ? existing.payments.map((p) => ({ key: crypto.randomUUID(), accountId: String(p.accountId), amount: String(p.amount) }))
      : [{ key: crypto.randomUUID(), accountId: '', amount: '' }]);
  const [lines, setLines] = useState<Line[]>(() =>
    existing?.lines.map((l) => ({
      key: crypto.randomUUID(), productId: l.productId, name: l.productName, unitName: l.unitName,
      productUnitId: l.productUnitId ?? null, factor: l.unitFactor ?? 1, baseUnitName: (l.unitFactor ?? 1) === 1 ? l.unitName : '', baseCost: null, units: [],
      quantity: String(l.quantity), unitCost: String(l.unitCost), lineDiscount: l.lineDiscount ? String(l.lineDiscount) : '',
    })) ?? []);
  const [picking, setPicking] = useState(false);
  // A product the store does not have yet, created from the invoice (null = closed; the text is the name to start with).
  const [creating, setCreating] = useState<string | null>(null);
  const canCreateProducts = useAuth().can(P.ProductsManage);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [touched, setTouched] = useState(false);
  useUnsavedChangesWarning(touched);

  const computed = useMemo(() => {
    const rows = lines.map((l) => {
      const q = parseNumber(l.quantity) ?? 0;
      const c = parseNumber(l.unitCost) ?? 0;
      const d = parseNumber(l.lineDiscount) ?? 0;
      return round2((Number.isNaN(q) ? 0 : q) * (Number.isNaN(c) ? 0 : c) - (Number.isNaN(d) ? 0 : d));
    });
    const subtotal = round2(rows.reduce((a, b) => a + b, 0));
    const disc = parseNumber(discount) ?? 0;
    const total = round2(subtotal - (Number.isNaN(disc) ? 0 : disc));
    const paidN = round2(pays.reduce((sum, p) => { const n = parseNumber(p.amount) ?? 0; return sum + (Number.isNaN(n) ? 0 : n); }, 0));
    return { rows, subtotal, total, paid: paidN, remaining: round2(total - paidN) };
  }, [lines, discount, pays]);
  // The supplier's balance once this invoice is in: what they hold of ours (advances) or what we owe them already counts.
  const supplierQuery = useQuery({ queryKey: ['supplier', supplier?.id], queryFn: () => suppliersApi.get(supplier!.id), enabled: !!supplier });
  const balanceAfter = supplierQuery.data
    ? round2(supplierQuery.data.balance - (posted && existing ? existing.total - existing.paidAmount : 0) + computed.remaining)
    : null;
  const updatePay = (k: string, patch: Partial<PayRow>) => {
    setTouched(true);
    setErrors((x) => ({ ...x, [`pa${k}`]: '', [`pc${k}`]: '', paid: '' }));
    setPays((ps) => ps.map((p) => (p.key === k ? { ...p, ...patch } : p)));
  };

  const update = (k: string, patch: Partial<Line>) => {
    setTouched(true);
    setLines((ls) => ls.map((l) => (l.key === k ? { ...l, ...patch } : l)));
  };

  const addProduct = (p: ProductListItem | ProductDetail) => {
    // The last purchase price (what is usually written on the supplier's invoice); the average cost only when there is none.
    const cost = ('purchasePrice' in p ? p.purchasePrice : null) || p.averageCost || null;
    setPicking(false);
    setTouched(true);
    setErrors((e) => ({ ...e, lines: '' }));
    setLines((ls) => {
      const found = ls.find((l) => l.productId === p.id && l.productUnitId === null);
      if (found) return ls.map((l) => (l === found ? { ...l, quantity: String((parseNumber(l.quantity) || 0) + 1) } : l));
      return [...ls, {
        key: crypto.randomUUID(), productId: p.id, name: p.displayName, unitName: p.unitName, productUnitId: null, factor: 1,
        baseUnitName: p.unitName, baseCost: cost, units: p.units ?? [],
        quantity: '1', unitCost: cost ? String(cost) : '', lineDiscount: '',
      }];
    });
  };

  /** Buys the line by another unit: the cost follows (12 × the piece's cost) unless one was typed by hand. */
  const switchUnit = (l: Line, unit: ProductUnit | null) => {
    const factor = unit?.factor ?? 1;
    const autoCost = l.baseCost != null && parseNumber(l.unitCost) === round2(l.baseCost * l.factor);
    update(l.key, {
      productUnitId: unit?.id ?? null, factor, unitName: unit?.unitName ?? l.baseUnitName,
      unitCost: autoCost || !l.unitCost ? (l.baseCost != null ? String(round2(l.baseCost * factor)) : '') : l.unitCost,
    });
  };

  const validate = (): string | null => {
    const e: Record<string, string> = {};
    if (!supplier) e.supplier = 'اختر المورد';
    if (lines.length === 0) e.lines = 'أضف منتجًا واحدًا على الأقل';
    lines.forEach((l, i) => {
      const q = parseNumber(l.quantity);
      const c = parseNumber(l.unitCost);
      const d = parseNumber(l.lineDiscount) ?? 0;
      if (q === undefined || Number.isNaN(q) || q <= 0) e[`q${l.key}`] = 'كمية غير صالحة';
      if (c === undefined || Number.isNaN(c) || c < 0) e[`c${l.key}`] = 'تكلفة غير صالحة';
      if (Number.isNaN(d) || d < 0 || d > (q ?? 0) * (c ?? 0)) e[`d${l.key}`] = 'خصم غير صالح';
      void i;
    });
    const disc = parseNumber(discount) ?? 0;
    if (Number.isNaN(disc) || disc < 0 || disc > computed.subtotal) e.discount = 'الخصم غير صالح أو أكبر من الإجمالي';
    for (const p of pays) {
      const n = parseNumber(p.amount) ?? 0;
      if (Number.isNaN(n) || n < 0) e[`pa${p.key}`] = 'المبلغ غير صالح';
      else if (n > 0 && !p.accountId) e[`pc${p.key}`] = 'اختر الحساب الذي دُفع منه';
    }
    if (computed.paid > computed.total) e.paid = 'المدفوع أكبر من إجمالي الفاتورة';
    setErrors(e);
    return Object.keys(e).length ? 'يرجى تصحيح الحقول المشار إليها' : null;
  };

  const save = useMutation({
    mutationFn: (post: boolean) => {
      const input = {
        idempotencyKey: key,
        supplierId: supplier!.id,
        date,
        supplierInvoiceNo: supplierInvoiceNo.trim() || null,
        discount: parseNumber(discount) ?? 0,
        paidAmount: computed.paid,
        paymentAccountId: null,
        payments: pays.filter((p) => (parseNumber(p.amount) ?? 0) > 0).map((p) => ({ accountId: Number(p.accountId), amount: parseNumber(p.amount)! })),
        notes: notes.trim() || null,
        lines: lines.map((l) => ({ productId: l.productId, quantity: parseNumber(l.quantity)!, unitCost: parseNumber(l.unitCost)!, lineDiscount: parseNumber(l.lineDiscount) ?? 0, productUnitId: l.productUnitId })),
        post,
      };
      if (existing && posted) return purchasesApi.replace(existing.id, { ...input, post: true });
      return existing ? purchasesApi.updateDraft(existing.id, input) : purchasesApi.create(input);
    },
    onSuccess: (saved, post) => {
      setTouched(false);
      void queryClient.invalidateQueries({ queryKey: ['purchases'] });
      void queryClient.invalidateQueries({ queryKey: ['products'] });
      void queryClient.invalidateQueries({ queryKey: ['suppliers'] });
      void queryClient.invalidateQueries({ queryKey: ['supplier', saved.supplierId] });
      void queryClient.invalidateQueries({ queryKey: ['notifications'] });
      queryClient.setQueryData(['purchase', saved.id], saved);
      toast.success(posted ? `تم تعديل الفاتورة: أُلغيت ${existing!.number} وأصبحت ${saved.number}`
        : post ? `تم اعتماد الفاتورة ${saved.number} وإضافة البضاعة للمخزون` : `تم حفظ المسودة ${saved.number}`);
      navigate(`/purchases/${saved.id}`, { replace: true, state: post || posted ? { checkPrices: true } : undefined });
    },
    onError: (e) => {
      if (e instanceof ApiError && e.fieldErrors) setErrors(Object.fromEntries(Object.entries(e.fieldErrors).map(([k, v]) => [k === 'paymentAccountId' || k === 'paidAmount' || k.startsWith('payments') ? 'paid' : k === 'supplierId' ? 'supplier' : k, v[0]])));
      toast.error(errorMessage(e));
    },
  });

  const submit = async (post: boolean) => {
    const problem = validate();
    if (problem) return toast.error(problem);
    if (posted && !(await confirm({
      title: 'حفظ تعديل الفاتورة',
      message: `تُلغى الفاتورة ${existing!.number} (تخرج بضاعتها وتُعكس دفعتها ومستحق المورد) وتُعتمد بدلها فاتورة جديدة بإجمالي ${formatMoney(computed.total)}، معًا. `
        + 'ما بِيع من البضاعة يبقى مبيعًا، بشرط ألا تقل الكمية الجديدة عنه.',
      confirmLabel: 'حفظ التعديل',
    }))) return;
    if (post && !posted && !(await confirm({
      title: 'اعتماد فاتورة الشراء',
      message: `سيُضاف ${lines.length} بند إلى المخزون بإجمالي ${formatMoney(computed.total)}${computed.remaining > 0 ? `، ويُسجَّل ${formatMoney(computed.remaining)} مستحقًا للمورد` : ''}. يمكن تعديلها لاحقًا من «تعديل الفاتورة» (تُلغى وتُعتمد بدلها فاتورة جديدة).`,
      confirmLabel: 'اعتماد',
    }))) return;
    save.mutate(post);
  };

  return (
    <div className="mx-auto max-w-3xl space-y-4 pb-[calc(8rem+env(safe-area-inset-bottom))] lg:pb-0">
      <button type="button" onClick={() => navigate(-1)} className="flex h-10 items-center gap-1 text-sm text-muted">
        <ArrowRight className="size-4" /> رجوع
      </button>
      <PageHeader title={posted ? `تعديل الفاتورة ${existing!.number}` : existing ? `تعديل المسودة ${existing.number}` : 'فاتورة شراء جديدة'} />
      {posted && (
        <p className="rounded-xl border border-amber-300 bg-amber-50 p-3 text-sm text-amber-900">
          تعديل فاتورة معتمدة: عند الحفظ تُلغى الأصلية وتُعتمد فاتورة جديدة بدلها (معًا، أو لا شيء). المخزون ومستحق المورد والدفعة تُحسب من جديد.
        </p>
      )}

      <Card className="space-y-4 p-5">
        <Field label="المورد" required error={errors.supplier}>
          {(id) => <SupplierPicker id={id} invalid={!!errors.supplier} value={supplier?.name ?? null} onSelect={(s) => { setSupplier({ id: s.id, name: s.name }); setTouched(true); setErrors((e) => ({ ...e, supplier: '' })); }} />}
        </Field>
        <div className="grid grid-cols-2 gap-3">
          <Field label="التاريخ" required>{(id) => <Input id={id} type="date" max={localToday()} value={date} onChange={(e) => setDate(e.target.value)} />}</Field>
          <Field label="رقم فاتورة المورد">{(id) => <Input id={id} className="ltr" value={supplierInvoiceNo} onChange={(e) => setSupplierInvoiceNo(e.target.value)} />}</Field>
        </div>
      </Card>

      <section>
        <div className="mb-2 flex items-center justify-between">
          <h2 className="font-bold">البنود ({lines.length})</h2>
          <div className="flex gap-2">
            {canCreateProducts && <Button variant="outline" onClick={() => setCreating('')}><Plus className="size-4" /> منتج جديد</Button>}
            <Button variant="secondary" onClick={() => setPicking(true)}><Plus className="size-4" /> إضافة منتج</Button>
          </div>
        </div>
        {errors.lines && <p role="alert" className="mb-2 text-sm text-danger">{errors.lines}</p>}
        {lines.length === 0 ? (
          <Card><EmptyState title="لم تتم إضافة منتجات" action={<Button onClick={() => setPicking(true)}><Plus className="size-4" /> إضافة منتج</Button>} /></Card>
        ) : (
          <ul className="space-y-2">
            {lines.map((l, i) => (
              <li key={l.key}>
                <Card className="p-3">
                  <div className="mb-2 flex items-start justify-between gap-2">
                    <p className="font-semibold leading-snug">{l.name}</p>
                    <IconButton label="حذف البند" onClick={() => { setTouched(true); setLines((ls) => ls.filter((x) => x.key !== l.key)); }} className="-mt-1 -me-1 text-danger">
                      <Trash2 className="size-5" />
                    </IconButton>
                  </div>
                  {l.units.length > 0 && (
                    <div className="mb-2 flex rounded-xl bg-black/5 p-1" role="radiogroup" aria-label={`وحدة الشراء: ${l.name}`}>
                      {[null, ...l.units].map((u) => (
                        <button key={u?.id ?? 0} type="button" role="radio" aria-checked={l.productUnitId === (u?.id ?? null)}
                          onClick={() => switchUnit(l, u)}
                          className={cn('h-9 flex-1 rounded-lg px-2 text-sm font-semibold', l.productUnitId === (u?.id ?? null) ? 'bg-surface shadow-sm' : 'text-muted')}>
                          {u ? <>{u.unitName} <span className="text-xs font-normal">({formatQuantity(u.factor)})</span></> : l.baseUnitName}
                        </button>
                      ))}
                    </div>
                  )}
                  <div className="grid grid-cols-3 gap-2">
                    <Field label={`الكمية (${l.unitName})`} error={errors[`q${l.key}`]}
                      hint={l.factor !== 1 && l.baseUnitName && (parseNumber(l.quantity) ?? 0) > 0 ? `= ${formatQuantity(round2((parseNumber(l.quantity) ?? 0) * l.factor))} ${l.baseUnitName}` : undefined}>
                      {(id) => <Input id={id} inputMode="decimal" className="ltr" invalid={!!errors[`q${l.key}`]} value={l.quantity} onChange={(e) => update(l.key, { quantity: e.target.value })} />}
                    </Field>
                    <Field label={l.factor !== 1 ? `تكلفة ${l.unitName}` : 'تكلفة الوحدة'} error={errors[`c${l.key}`]}>
                      {(id) => <Input id={id} inputMode="decimal" className="ltr" invalid={!!errors[`c${l.key}`]} value={l.unitCost} onChange={(e) => update(l.key, { unitCost: e.target.value })} />}
                    </Field>
                    <Field label="خصم البند" error={errors[`d${l.key}`]}>
                      {(id) => <Input id={id} inputMode="decimal" className="ltr" invalid={!!errors[`d${l.key}`]} value={l.lineDiscount} onChange={(e) => update(l.key, { lineDiscount: e.target.value })} />}
                    </Field>
                  </div>
                  <p className="mt-2 text-end text-sm">الإجمالي: <b className="tabular">{formatMoney(computed.rows[i])}</b></p>
                </Card>
              </li>
            ))}
          </ul>
        )}
      </section>

      <Card className="space-y-4 p-5">
        <h2 className="font-bold">الدفع</h2>
        <div className="grid grid-cols-2 gap-3">
          <Field label="خصم على الفاتورة" error={errors.discount} hint="خصم المورد للمعرض: لا يغيّر تكلفة المنتجات، ويُحسب ربحًا (خصم مكتسب)">
            {(id) => <Input id={id} inputMode="decimal" className="ltr" invalid={!!errors.discount} value={discount} onChange={(e) => { setDiscount(e.target.value); setTouched(true); }} />}
          </Field>
        </div>
        <div className="space-y-2">
          <p className="text-sm font-medium">المدفوع الآن {pays.length > 1 && <span className="text-muted">(من أكثر من حساب)</span>}</p>
          {pays.map((p, i) => {
            // «الكل» / «الباقي»: what is left of the invoice after the other rows.
            const others = round2(computed.paid - (Number.isNaN(parseNumber(p.amount) ?? 0) ? 0 : parseNumber(p.amount) ?? 0));
            const rest = Math.max(0, round2(computed.total - others));
            return (
              <div key={p.key} className="space-y-1">
                <div className={cn('grid gap-1', pays.length > 1 ? 'grid-cols-[minmax(0,1fr)_7rem_auto_auto]' : 'grid-cols-[minmax(0,1fr)_7rem_auto]')}>
                  <Select aria-label="دُفع من حساب" invalid={!!errors[`pc${p.key}`]} value={p.accountId}
                    onChange={(e) => updatePay(p.key, { accountId: e.target.value })}>
                    <option value="">اختر الحساب…</option>
                    {lookups.data?.accounts.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
                  </Select>
                  <Input aria-label="المبلغ" inputMode="decimal" className="ltr" placeholder="0" invalid={!!errors[`pa${p.key}`]} value={p.amount}
                    onChange={(e) => updatePay(p.key, { amount: e.target.value })} />
                  <Button variant="outline" size="sm" className="shrink-0 px-2" onClick={() => updatePay(p.key, { amount: String(rest) })}>
                    {i === 0 && pays.length === 1 ? 'الكل' : 'الباقي'}
                  </Button>
                  {pays.length > 1 && (
                    <IconButton label="حذف" onClick={() => { setTouched(true); setPays((ps) => ps.filter((x) => x.key !== p.key)); }}>
                      <Trash2 className="size-4" />
                    </IconButton>
                  )}
                </div>
                {(errors[`pc${p.key}`] || errors[`pa${p.key}`]) && <p className="text-sm text-danger">{errors[`pc${p.key}`] || errors[`pa${p.key}`]}</p>}
              </div>
            );
          })}
          <Button variant="ghost" size="sm" onClick={() => { setTouched(true); setPays((ps) => [...ps, { key: crypto.randomUUID(), accountId: '', amount: '' }]); }}>
            <Plus className="size-4" /> الدفع من حساب آخر
          </Button>
          {errors.paid && <p className="text-sm text-danger">{errors.paid}</p>}
        </div>
        <Collapsible title="ملاحظات">
          <TextArea aria-label="ملاحظات" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} />
        </Collapsible>
      </Card>

      <StickyActions>
        <div className="flex w-full flex-col gap-2">
          <div className="flex items-center justify-between text-sm">
            <span>الإجمالي: <b className="tabular text-base">{formatMoney(computed.total)}</b></span>
            <span className={computed.remaining > 0 ? 'text-danger' : 'text-muted'}>غير مدفوع من الفاتورة: <b className="tabular">{formatMoney(Math.max(0, computed.remaining))}</b></span>
          </div>
          {balanceAfter != null && (
            <p className="text-end text-sm">
              رصيد المورد بعد الفاتورة:{' '}
              {Math.abs(balanceAfter) < 0.005 ? <b>لا شيء</b>
                : balanceAfter > 0 ? <b className="tabular text-danger">علينا له {formatMoney(balanceAfter)}</b>
                : <b className="tabular text-success">لنا عنده {formatMoney(-balanceAfter)}</b>}
            </p>
          )}
          <div className="flex gap-2">
            {!posted && (
              <Button variant="outline" size="lg" className="flex-1" loading={save.isPending && save.variables === false} disabled={save.isPending} onClick={() => void submit(false)}>
                مسودة
              </Button>
            )}
            <Button size="lg" className="flex-[2] whitespace-nowrap" loading={save.isPending && save.variables === true} disabled={save.isPending} onClick={() => void submit(true)}>
              {posted ? 'حفظ التعديل' : 'اعتماد الفاتورة'}
            </Button>
          </div>
        </div>
      </StickyActions>

      {picking && (
        <PickerSheet<ProductListItem>
          title="إضافة منتج للفاتورة"
          placeholder="ابحث بالاسم أو الكود أو الباركود"
          queryKey="purchase-products"
          search={async (q, signal) => (await productsApi.list({ search: q, pageSize: 30 }, signal)).items}
          getKey={(p) => p.id}
          renderItem={(p) => <ProductRow p={p} showCost />}
          onSelect={addProduct}
          onClose={() => setPicking(false)}
          footer={canCreateProducts ? (term: string) => (
            <div className="sticky bottom-0 -mx-4 border-t border-line bg-surface px-4 pt-3">
              <Button block variant="outline" onClick={() => { setPicking(false); setCreating(term); }}>
                <Plus className="size-4" /> {term ? `منتج جديد باسم «${term}»` : 'منتج جديد غير موجود في القائمة'}
              </Button>
            </div>
          ) : undefined}
        />
      )}
      {creating !== null && (
        <QuickProductSheet initialName={creating} onClose={() => setCreating(null)} onCreated={(p) => addProduct(p)} />
      )}
    </div>
  );
}
