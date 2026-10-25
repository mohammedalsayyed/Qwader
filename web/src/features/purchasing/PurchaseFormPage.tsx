import { useMemo, useState } from 'react';
import { useNavigate, useParams, useSearchParams } from 'react-router';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowRight, Plus, Trash2 } from 'lucide-react';
import type { ProductListItem } from '@/api/catalog';
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
import { formatMoney, localToday } from '@/lib/format';
import { useIdempotencyKey, useUnsavedChangesWarning } from '@/lib/hooks';
import { parseNumber, round2 } from '@/lib/numbers';
import { useLookups } from '@/lib/queries';

interface Line {
  key: string;
  productId: number;
  name: string;
  unitName: string;
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
  if (existing.data && existing.data.status !== 'Draft') return <ErrorState message="لا يمكن تعديل فاتورة معتمدة أو ملغاة" />;

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
  const [notes, setNotes] = useState(existing?.notes ?? '');
  const [discount, setDiscount] = useState(existing?.discount ? String(existing.discount) : '');
  const [paid, setPaid] = useState(existing?.paidAmount ? String(existing.paidAmount) : '');
  const [accountId, setAccountId] = useState(existing?.paymentAccountId ? String(existing.paymentAccountId) : '');
  const [lines, setLines] = useState<Line[]>(() =>
    existing?.lines.map((l) => ({
      key: crypto.randomUUID(), productId: l.productId, name: l.productName, unitName: l.unitName,
      quantity: String(l.quantity), unitCost: String(l.unitCost), lineDiscount: l.lineDiscount ? String(l.lineDiscount) : '',
    })) ?? []);
  const [picking, setPicking] = useState(false);
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
    const paidN = parseNumber(paid) ?? 0;
    return { rows, subtotal, total, remaining: round2(total - (Number.isNaN(paidN) ? 0 : paidN)) };
  }, [lines, discount, paid]);

  const update = (k: string, patch: Partial<Line>) => {
    setTouched(true);
    setLines((ls) => ls.map((l) => (l.key === k ? { ...l, ...patch } : l)));
  };

  const addProduct = (p: ProductListItem) => {
    setPicking(false);
    setTouched(true);
    setErrors((e) => ({ ...e, lines: '' }));
    setLines((ls) => {
      const found = ls.find((l) => l.productId === p.id);
      if (found) return ls.map((l) => (l === found ? { ...l, quantity: String((parseNumber(l.quantity) || 0) + 1) } : l));
      return [...ls, { key: crypto.randomUUID(), productId: p.id, name: p.displayName, unitName: p.unitName, quantity: '1', unitCost: p.averageCost ? String(p.averageCost) : '', lineDiscount: '' }];
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
    const paidN = parseNumber(paid) ?? 0;
    if (Number.isNaN(paidN) || paidN < 0) e.paid = 'المبلغ غير صالح';
    else if (paidN > computed.total) e.paid = 'المدفوع أكبر من إجمالي الفاتورة';
    else if (paidN > 0 && !accountId) e.account = 'اختر الحساب الذي دُفع منه';
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
        paidAmount: parseNumber(paid) ?? 0,
        paymentAccountId: (parseNumber(paid) ?? 0) > 0 ? Number(accountId) : null,
        notes: notes.trim() || null,
        lines: lines.map((l) => ({ productId: l.productId, quantity: parseNumber(l.quantity)!, unitCost: parseNumber(l.unitCost)!, lineDiscount: parseNumber(l.lineDiscount) ?? 0 })),
        post,
      };
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
      toast.success(post ? `تم اعتماد الفاتورة ${saved.number} وإضافة البضاعة للمخزون` : `تم حفظ المسودة ${saved.number}`);
      navigate(`/purchases/${saved.id}`, { replace: true });
    },
    onError: (e) => {
      if (e instanceof ApiError && e.fieldErrors) setErrors(Object.fromEntries(Object.entries(e.fieldErrors).map(([k, v]) => [k === 'paymentAccountId' ? 'account' : k === 'paidAmount' ? 'paid' : k === 'supplierId' ? 'supplier' : k, v[0]])));
      toast.error(errorMessage(e));
    },
  });

  const submit = async (post: boolean) => {
    const problem = validate();
    if (problem) return toast.error(problem);
    if (post && !(await confirm({
      title: 'اعتماد فاتورة الشراء',
      message: `سيُضاف ${lines.length} بند إلى المخزون بإجمالي ${formatMoney(computed.total)}${computed.remaining > 0 ? `، ويُسجَّل ${formatMoney(computed.remaining)} مستحقًا للمورد` : ''}. لا يمكن تعديل الفاتورة بعد الاعتماد.`,
      confirmLabel: 'اعتماد',
    }))) return;
    save.mutate(post);
  };

  return (
    <div className="mx-auto max-w-3xl space-y-4 pb-[calc(8rem+env(safe-area-inset-bottom))] lg:pb-0">
      <button type="button" onClick={() => navigate(-1)} className="flex h-10 items-center gap-1 text-sm text-muted">
        <ArrowRight className="size-4" /> رجوع
      </button>
      <PageHeader title={existing ? `تعديل المسودة ${existing.number}` : 'فاتورة شراء جديدة'} />

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
          <Button variant="secondary" onClick={() => setPicking(true)}><Plus className="size-4" /> إضافة منتج</Button>
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
                  <div className="grid grid-cols-3 gap-2">
                    <Field label={`الكمية (${l.unitName})`} error={errors[`q${l.key}`]}>
                      {(id) => <Input id={id} inputMode="decimal" className="ltr" invalid={!!errors[`q${l.key}`]} value={l.quantity} onChange={(e) => update(l.key, { quantity: e.target.value })} />}
                    </Field>
                    <Field label="تكلفة الوحدة" error={errors[`c${l.key}`]}>
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
          <Field label="خصم على الفاتورة" error={errors.discount}>
            {(id) => <Input id={id} inputMode="decimal" className="ltr" invalid={!!errors.discount} value={discount} onChange={(e) => { setDiscount(e.target.value); setTouched(true); }} />}
          </Field>
          <Field label="المبلغ المدفوع الآن" error={errors.paid}>
            {(id) => (
              <div className="flex gap-1">
                <Input id={id} inputMode="decimal" className="ltr" invalid={!!errors.paid} value={paid} onChange={(e) => { setPaid(e.target.value); setTouched(true); }} />
                <Button variant="outline" size="sm" className="shrink-0 px-2" onClick={() => setPaid(String(computed.total))}>الكل</Button>
              </div>
            )}
          </Field>
        </div>
        {(parseNumber(paid) ?? 0) > 0 && (
          <Field label="دُفع من حساب" required error={errors.account}>
            {(id) => (
              <Select id={id} invalid={!!errors.account} value={accountId} onChange={(e) => { setAccountId(e.target.value); setErrors((x) => ({ ...x, account: '' })); }}>
                <option value="">اختر الحساب…</option>
                {lookups.data?.accounts.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}
              </Select>
            )}
          </Field>
        )}
        <Collapsible title="ملاحظات">
          <TextArea aria-label="ملاحظات" rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} />
        </Collapsible>
      </Card>

      <StickyActions>
        <div className="flex w-full flex-col gap-2">
          <div className="flex items-center justify-between text-sm">
            <span>الإجمالي: <b className="tabular text-base">{formatMoney(computed.total)}</b></span>
            <span className={computed.remaining > 0 ? 'text-danger' : 'text-muted'}>المتبقي للمورد: <b className="tabular">{formatMoney(Math.max(0, computed.remaining))}</b></span>
          </div>
          <div className="flex gap-2">
            <Button variant="outline" size="lg" className="flex-1" loading={save.isPending && save.variables === false} disabled={save.isPending} onClick={() => void submit(false)}>
              مسودة
            </Button>
            <Button size="lg" className="flex-[2] whitespace-nowrap" loading={save.isPending && save.variables === true} disabled={save.isPending} onClick={() => void submit(true)}>
              اعتماد الفاتورة
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
        />
      )}
    </div>
  );
}
