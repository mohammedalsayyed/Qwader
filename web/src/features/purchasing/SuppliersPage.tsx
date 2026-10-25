import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { ArrowRight, Pencil, Phone, Plus, ReceiptText, ShoppingBag, Truck } from 'lucide-react';
import { purchasesApi, suppliersApi, type Supplier } from '@/api/stock';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button } from '@/components/ui/Button';
import { Field, Input, Switch, TextArea } from '@/components/ui/Field';
import { Badge, Card, EmptyState, ErrorState, FilterChips, PageHeader, Pagination, SearchInput, Spinner } from '@/components/ui/Feedback';
import { InfoList, Stat, Tabs } from '@/components/ui/Layout';
import { Sheet } from '@/components/ui/Sheet';
import { useToast } from '@/components/ui/Toast';
import { StatementView } from '@/components/finance/StatementView';
import { applyServerErrors, blankToNull, errorMessage } from '@/lib/forms';
import { formatDate, formatMoney } from '@/lib/format';
import { useDebouncedValue } from '@/lib/hooks';
import { zOptionalAmount } from '@/lib/numbers';
import { PurchaseStatusBadge } from './PurchasesPage';

type Filter = 'active' | 'balance' | 'all';

export function SuppliersPage() {
  const { can } = useAuth();
  const [search, setSearch] = useState('');
  const [filter, setFilter] = useState<Filter>('active');
  const [page, setPage] = useState(1);
  const [creating, setCreating] = useState(false);
  const term = useDebouncedValue(search.trim());

  const query = useQuery({
    queryKey: ['suppliers', { term, filter, page }],
    queryFn: () => suppliersApi.list({ search: term, page, pageSize: 30, isActive: filter === 'all' ? undefined : true, withBalance: filter === 'balance' || undefined }),
    placeholderData: keepPreviousData,
  });

  return (
    <>
      <PageHeader title="الموردون" />
      <div className="mb-3 space-y-3">
        <SearchInput value={search} onChange={(v) => { setSearch(v); setPage(1); }} placeholder="ابحث بالاسم أو الجوال أو الكود" />
        <FilterChips value={filter} onChange={(v) => { setFilter(v); setPage(1); }}
          options={[{ value: 'active', label: 'الفعّالون' }, { value: 'balance', label: 'لهم رصيد' }, { value: 'all', label: 'الكل' }]} />
      </div>
      {query.isPending ? (
        <Spinner />
      ) : query.isError ? (
        <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} />
      ) : query.data.items.length === 0 ? (
        <EmptyState icon={<Truck className="size-10" />} title={term ? 'لا يوجد موردون مطابقون' : 'لا يوجد موردون بعد'} />
      ) : (
        <>
          <ul className="grid gap-2 md:grid-cols-2">
            {query.data.items.map((s) => (
              <li key={s.id}>
                <Link to={`/suppliers/${s.id}`} className="flex items-center justify-between gap-3 rounded-card border border-line bg-surface p-4 hover:border-brand-200">
                  <div className="min-w-0">
                    <p className="flex items-center gap-2 font-semibold"><span className="truncate">{s.name}</span>{!s.isActive && <Badge tone="danger">غير فعّال</Badge>}</p>
                    <p className="ltr text-sm text-muted">{s.phone ?? s.code}</p>
                  </div>
                  <div className="shrink-0 text-end">
                    <p className="text-xs text-muted">{s.balance > 0 ? 'مستحق له' : s.balance < 0 ? 'له علينا' : 'الرصيد'}</p>
                    <p className={`tabular font-bold ${s.balance > 0 ? 'text-danger' : ''}`}>{formatMoney(Math.abs(s.balance))}</p>
                  </div>
                </Link>
              </li>
            ))}
          </ul>
          <Pagination page={query.data.page} totalPages={query.data.totalPages} onChange={setPage} />
        </>
      )}
      {can(P.SuppliersManage) && (
        <Button size="lg" onClick={() => setCreating(true)} className="fixed bottom-fab end-4 z-30 rounded-full shadow-lg lg:static lg:mt-4">
          <Plus className="size-5" /> مورد جديد
        </Button>
      )}
      {creating && <SupplierSheet supplier={null} onClose={() => setCreating(false)} />}
    </>
  );
}

const schema = z.object({
  name: z.string().trim().min(1, 'اسم المورد مطلوب').max(150),
  phone: z.string().trim().max(20, 'رقم الجوال طويل جدًا').optional(),
  address: z.string().trim().max(200).optional(),
  notes: z.string().trim().max(500).optional(),
  isActive: z.boolean(),
  openingBalance: zOptionalAmount(),
});
type FormInput = z.input<typeof schema>;
type FormValues = z.output<typeof schema>;

export function SupplierSheet({ supplier, onClose }: { supplier: Supplier | null; onClose: () => void }) {
  const toast = useToast();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { register, control, handleSubmit, setError, formState: { errors } } = useForm<FormInput, unknown, FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { name: supplier?.name ?? '', phone: supplier?.phone ?? '', address: supplier?.address ?? '', notes: supplier?.notes ?? '', isActive: supplier?.isActive ?? true, openingBalance: '' },
  });

  const save = useMutation({
    mutationFn: (v: FormValues) => {
      const input = { name: v.name, phone: blankToNull(v.phone), address: blankToNull(v.address), notes: blankToNull(v.notes), isActive: v.isActive, openingBalance: v.openingBalance ?? 0 };
      return supplier ? suppliersApi.update(supplier.id, input) : suppliersApi.create(input);
    },
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: ['suppliers'] });
      queryClient.setQueryData(['supplier', saved.id], saved);
      toast.success(supplier ? 'تم حفظ بيانات المورد' : 'تمت إضافة المورد');
      onClose();
      if (!supplier) navigate(`/suppliers/${saved.id}`);
    },
    onError: (e) => {
      const msg = applyServerErrors(e, setError, ['name', 'phone', 'address', 'notes', 'openingBalance']);
      if (msg) toast.error(msg);
    },
  });
  const onSubmit = handleSubmit((v) => save.mutate(v));

  return (
    <Sheet open onClose={onClose} title={supplier ? 'تعديل المورد' : 'مورد جديد'}
      footer={<Button block size="lg" loading={save.isPending} onClick={() => void onSubmit()}>حفظ</Button>}>
      <form onSubmit={onSubmit} noValidate className="space-y-4">
        <Field label="اسم المورد" required error={errors.name?.message}>
          {(id, d) => <Input id={id} aria-describedby={d} invalid={!!errors.name} {...register('name')} />}
        </Field>
        <Field label="رقم الجوال" error={errors.phone?.message}>
          {(id, d) => <Input id={id} aria-describedby={d} type="tel" inputMode="tel" className="ltr" {...register('phone')} />}
        </Field>
        <Field label="العنوان" error={errors.address?.message}>{(id) => <Input id={id} {...register('address')} />}</Field>
        {!supplier && (
          <Field label="رصيد افتتاحي مستحق للمورد" error={errors.openingBalance?.message} hint="المبلغ الذي يدين به المعرض للمورد قبل بدء استخدام النظام">
            {(id, d) => <Input id={id} aria-describedby={d} inputMode="decimal" className="ltr" invalid={!!errors.openingBalance} {...register('openingBalance')} />}
          </Field>
        )}
        <Field label="ملاحظات" error={errors.notes?.message}>{(id) => <TextArea id={id} rows={2} {...register('notes')} />}</Field>
        {supplier && (
          <Controller control={control} name="isActive" render={({ field }) => <Switch label="المورد فعّال" checked={field.value} onChange={field.onChange} />} />
        )}
      </form>
    </Sheet>
  );
}

type DetailTab = 'statement' | 'purchases';

export function SupplierDetailPage() {
  const id = Number(useParams().id);
  const { can } = useAuth();
  const navigate = useNavigate();
  const [tab, setTab] = useState<DetailTab>('statement');
  const [editing, setEditing] = useState(false);
  const supplier = useQuery({ queryKey: ['supplier', id], queryFn: () => suppliersApi.get(id) });

  if (supplier.isPending) return <Spinner />;
  if (supplier.isError) return <ErrorState message={errorMessage(supplier.error)} onRetry={() => void supplier.refetch()} />;
  const s = supplier.data;

  return (
    <div className="space-y-4">
      <button type="button" onClick={() => navigate('/suppliers')} className="flex h-10 items-center gap-1 text-sm text-muted">
        <ArrowRight className="size-4" /> الموردون
      </button>
      <div className="flex items-start justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold">{s.name}</h1>
          <p className="ltr text-sm text-muted">{s.code}</p>
        </div>
        <div className="flex gap-2">
          {s.phone && <a href={`tel:${s.phone}`}><Button variant="outline" aria-label="اتصال"><Phone className="size-4" /></Button></a>}
          {can(P.SuppliersManage) && <Button variant="outline" onClick={() => setEditing(true)}><Pencil className="size-4" /> تعديل</Button>}
        </div>
      </div>

      <div className="grid grid-cols-2 gap-2 lg:grid-cols-3">
        <Stat label={s.balance >= 0 ? 'المستحق للمورد' : 'رصيد لصالحنا'} value={formatMoney(Math.abs(s.balance))} tone={s.balance > 0 ? 'danger' : 'default'} />
        <Stat label="صافي المشتريات" value={formatMoney(s.totalPurchases)} />
        <Stat label="إجمالي المدفوع" value={formatMoney(s.totalPayments)} />
      </div>

      {can(P.VouchersCreate) && s.balance > 0 && (
        <Link to={`/payments/new?supplierId=${s.id}`} className="block">
          <Button block variant="secondary"><ReceiptText className="size-4" /> دفعة للمورد (سند صرف)</Button>
        </Link>
      )}
      {can(P.PurchasesManage) && (
        <Link to={`/purchases/new?supplierId=${s.id}`} className="block">
          <Button block variant="secondary"><ShoppingBag className="size-4" /> فاتورة شراء جديدة من هذا المورد</Button>
        </Link>
      )}

      {(s.address || s.notes) && (
        <Card className="px-4"><InfoList items={[s.address && { label: 'العنوان', value: s.address }, s.notes && { label: 'ملاحظات', value: s.notes }]} /></Card>
      )}

      <Tabs value={tab} onChange={setTab} tabs={[{ value: 'statement', label: 'كشف الحساب' }, { value: 'purchases', label: 'فواتير الشراء' }]} />
      {tab === 'statement' ? (
        <StatementView queryKey={['supplier-statement', id]} load={(from, to) => suppliersApi.statement(id, from, to)}
          debitLabel="مدين (سداد/مردود)" creditLabel="دائن (مشتريات)" balanceHint="الموجب = مستحق للمورد" />
      ) : (
        <SupplierPurchases supplierId={id} />
      )}

      {editing && <SupplierSheet supplier={s} onClose={() => setEditing(false)} />}
    </div>
  );
}

function SupplierPurchases({ supplierId }: { supplierId: number }) {
  const [page, setPage] = useState(1);
  const query = useQuery({
    queryKey: ['purchases', { supplierId, page }],
    queryFn: () => purchasesApi.list({ supplierId, page, pageSize: 20 }),
    placeholderData: keepPreviousData,
  });
  if (query.isPending) return <Spinner />;
  if (query.isError) return <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} />;
  if (query.data.items.length === 0) return <EmptyState title="لا توجد فواتير شراء لهذا المورد" />;
  return (
    <>
      <ul className="divide-y divide-line rounded-card border border-line bg-surface">
        {query.data.items.map((p) => (
          <li key={p.id}>
            <Link to={`/purchases/${p.id}`} className="flex items-center justify-between gap-3 p-3">
              <div>
                <p className="ltr font-semibold">{p.number}</p>
                <p className="text-xs text-muted">{formatDate(p.date)}</p>
              </div>
              <div className="flex items-center gap-2">
                <span className="tabular font-bold">{formatMoney(p.total)}</span>
                <PurchaseStatusBadge status={p.status} />
              </div>
            </Link>
          </li>
        ))}
      </ul>
      <Pagination page={query.data.page} totalPages={query.data.totalPages} onChange={setPage} />
    </>
  );
}
