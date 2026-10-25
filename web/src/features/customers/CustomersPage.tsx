import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Controller, useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { ArrowRight, MessageCircle, Pencil, Phone, Plus, ShoppingCart, Users } from 'lucide-react';
import { customersApi, salesApi, SALE_STATUS, type Customer } from '@/api/sales';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button } from '@/components/ui/Button';
import { Field, Input, Switch, TextArea } from '@/components/ui/Field';
import { Badge, Card, EmptyState, ErrorState, FilterChips, PageHeader, Pagination, SearchInput, Spinner } from '@/components/ui/Feedback';
import { InfoList, Stat, Tabs } from '@/components/ui/Layout';
import { Sheet } from '@/components/ui/Sheet';
import { useToast } from '@/components/ui/Toast';
import { StatementView } from '@/components/finance/StatementView';
import { cn } from '@/lib/cn';
import { applyServerErrors, blankToNull, errorMessage } from '@/lib/forms';
import { formatDate, formatDateTime, formatMoney } from '@/lib/format';
import { useDebouncedValue } from '@/lib/hooks';
import { zOptionalAmount } from '@/lib/numbers';

type Filter = 'all' | 'debt';
const PAGE_SIZE = 30;

export function CustomersPage() {
  const [search, setSearch] = useState('');
  const [filter, setFilter] = useState<Filter>('all');
  const [page, setPage] = useState(1);
  const [creating, setCreating] = useState(false);
  const term = useDebouncedValue(search.trim());

  const query = useQuery({
    queryKey: ['customers', { term, filter, page }],
    queryFn: () => customersApi.list({ search: term, page, pageSize: PAGE_SIZE, withBalance: filter === 'debt' || undefined }),
    placeholderData: keepPreviousData,
  });

  return (
    <>
      <PageHeader title="العملاء" />
      <div className="mb-3 space-y-3">
        <SearchInput value={search} onChange={(v) => { setSearch(v); setPage(1); }} placeholder="ابحث بالاسم أو رقم الجوال" />
        <FilterChips value={filter} onChange={(v) => { setFilter(v); setPage(1); }} options={[{ value: 'all', label: 'الكل' }, { value: 'debt', label: 'عليهم ديون' }]} />
      </div>
      {query.isPending ? (
        <Spinner />
      ) : query.isError ? (
        <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} />
      ) : query.data.items.length === 0 ? (
        <EmptyState icon={<Users className="size-10" />} title={term ? 'لا يوجد عملاء مطابقون' : 'لا يوجد عملاء بعد'}
          description={term ? undefined : 'يُضاف العميل تلقائيًا عند البيع باسمه ورقم جواله، أو من زر «عميل جديد».'} />
      ) : (
        <>
          <ul className={cn('grid gap-2 md:grid-cols-2', query.isPlaceholderData && 'opacity-60')}>
            {query.data.items.filter((c) => !c.isWalkIn).map((c) => (
              <li key={c.id}>
                <Link to={`/customers/${c.id}`} className="flex items-center justify-between gap-3 rounded-card border border-line bg-surface p-4 hover:border-brand-200">
                  <div className="min-w-0">
                    <p className="flex items-center gap-2 font-semibold"><span className="truncate">{c.name}</span>{!c.isActive && <Badge tone="danger">غير فعّال</Badge>}</p>
                    <p className="ltr text-sm text-muted">{c.phone ?? c.code}</p>
                  </div>
                  {c.balance !== 0 && (
                    <div className="shrink-0 text-end">
                      <p className="text-xs text-muted">{c.balance > 0 ? 'عليه' : 'له'}</p>
                      <p className={cn('tabular font-bold', c.balance > 0 && 'text-danger')}>{formatMoney(Math.abs(c.balance))}</p>
                    </div>
                  )}
                </Link>
              </li>
            ))}
          </ul>
          <Pagination page={query.data.page} totalPages={query.data.totalPages} totalCount={query.data.totalCount} pageSize={PAGE_SIZE} onChange={setPage} />
        </>
      )}
      <Button size="lg" onClick={() => setCreating(true)} className="fixed bottom-22 end-4 z-30 rounded-full shadow-lg lg:static lg:mt-4">
        <Plus className="size-5" /> عميل جديد
      </Button>
      {creating && <CustomerSheet customer={null} onClose={() => setCreating(false)} />}
    </>
  );
}

const schema = z.object({
  name: z.string().trim().min(1, 'اسم العميل مطلوب').max(150),
  phone: z.string().trim().max(20, 'رقم الجوال طويل جدًا').optional(),
  address: z.string().trim().max(200).optional(),
  notes: z.string().trim().max(500).optional(),
  isActive: z.boolean(),
  openingBalance: zOptionalAmount(),
});
type FormInput = z.input<typeof schema>;
type FormValues = z.output<typeof schema>;

function CustomerSheet({ customer, onClose }: { customer: Customer | null; onClose: () => void }) {
  const toast = useToast();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { register, control, handleSubmit, setError, formState: { errors } } = useForm<FormInput, unknown, FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { name: customer?.name ?? '', phone: customer?.phone ?? '', address: customer?.address ?? '', notes: customer?.notes ?? '', isActive: customer?.isActive ?? true, openingBalance: '' },
  });
  const save = useMutation({
    mutationFn: (v: FormValues) => {
      const input = { name: v.name, phone: blankToNull(v.phone), address: blankToNull(v.address), notes: blankToNull(v.notes), isActive: v.isActive, openingBalance: v.openingBalance ?? 0 };
      return customer ? customersApi.update(customer.id, input) : customersApi.create(input);
    },
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: ['customers'] });
      queryClient.setQueryData(['customer', saved.id], saved);
      toast.success(customer ? 'تم حفظ بيانات العميل' : 'تمت إضافة العميل');
      onClose();
      if (!customer) navigate(`/customers/${saved.id}`);
    },
    onError: (e) => {
      const msg = applyServerErrors(e, setError, ['name', 'phone', 'address', 'notes', 'openingBalance']);
      if (msg) toast.error(msg);
    },
  });
  const onSubmit = handleSubmit((v) => save.mutate(v));

  return (
    <Sheet open onClose={onClose} title={customer ? 'تعديل العميل' : 'عميل جديد'}
      footer={<Button block size="lg" loading={save.isPending} onClick={() => void onSubmit()}>حفظ</Button>}>
      <form onSubmit={onSubmit} noValidate className="space-y-4">
        <Field label="اسم العميل" required error={errors.name?.message}>
          {(id, d) => <Input id={id} aria-describedby={d} invalid={!!errors.name} {...register('name')} />}
        </Field>
        <Field label="رقم الجوال" error={errors.phone?.message}>
          {(id, d) => <Input id={id} aria-describedby={d} type="tel" inputMode="tel" className="ltr" invalid={!!errors.phone} {...register('phone')} />}
        </Field>
        <Field label="العنوان" error={errors.address?.message}>{(id) => <Input id={id} {...register('address')} />}</Field>
        {!customer && (
          <Field label="دين سابق على العميل" error={errors.openingBalance?.message} hint="المبلغ المستحق عليه قبل بدء استخدام النظام">
            {(id, d) => <Input id={id} aria-describedby={d} inputMode="decimal" className="ltr" invalid={!!errors.openingBalance} {...register('openingBalance')} />}
          </Field>
        )}
        <Field label="ملاحظات">{(id) => <TextArea id={id} rows={2} {...register('notes')} />}</Field>
        {customer && <Controller control={control} name="isActive" render={({ field }) => <Switch label="العميل فعّال" checked={field.value} onChange={field.onChange} />} />}
      </form>
    </Sheet>
  );
}

type Tab = 'statement' | 'invoices';

export function CustomerDetailPage() {
  const id = Number(useParams().id);
  const { can } = useAuth();
  const navigate = useNavigate();
  const [tab, setTab] = useState<Tab>('invoices');
  const [editing, setEditing] = useState(false);
  const query = useQuery({ queryKey: ['customer', id], queryFn: () => customersApi.get(id) });

  if (query.isPending) return <Spinner />;
  if (query.isError) return <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} />;
  const c = query.data;
  const intlPhone = c.phone ? (c.phone.startsWith('0') ? `970${c.phone.slice(1)}` : c.phone.replace('+', '')) : null;

  return (
    <div className="space-y-4">
      <button type="button" onClick={() => navigate('/customers')} className="flex h-10 items-center gap-1 text-sm text-muted">
        <ArrowRight className="size-4" /> العملاء
      </button>
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <h1 className="text-xl font-bold">{c.name}</h1>
          <p className="ltr text-sm text-muted">{c.phone ?? c.code}</p>
        </div>
        <div className="flex shrink-0 gap-2">
          {c.phone && <a href={`tel:${c.phone}`}><Button variant="outline" aria-label="اتصال"><Phone className="size-4" /></Button></a>}
          {intlPhone && <a href={`https://wa.me/${intlPhone}`} target="_blank" rel="noopener"><Button variant="outline" aria-label="واتساب"><MessageCircle className="size-4" /></Button></a>}
          {can(P.CustomersManage) && !c.isWalkIn && <Button variant="outline" onClick={() => setEditing(true)}><Pencil className="size-4" /> تعديل</Button>}
        </div>
      </div>

      <div className="grid grid-cols-2 gap-2 lg:grid-cols-4">
        <Stat label={c.balance >= 0 ? 'المستحق عليه' : 'رصيد له'} value={formatMoney(Math.abs(c.balance))} tone={c.balance > 0 ? 'danger' : 'default'} />
        <Stat label="إجمالي المشتريات" value={formatMoney(c.totalSales - c.totalReturns)} hint={c.totalReturns ? `بعد مرتجعات ${formatMoney(c.totalReturns)}` : undefined} />
        <Stat label="عدد الفواتير" value={c.invoiceCount} />
        <Stat label="آخر شراء" value={c.lastPurchaseAt ? formatDate(c.lastPurchaseAt) : '—'} />
      </div>

      {can(P.SalesCreate) && (
        <Link to="/pos" className="block"><Button block variant="secondary"><ShoppingCart className="size-4" /> بيع جديد</Button></Link>
      )}
      {(c.address || c.notes) && <Card className="px-4"><InfoList items={[c.address && { label: 'العنوان', value: c.address }, c.notes && { label: 'ملاحظات', value: c.notes }]} /></Card>}

      <Tabs value={tab} onChange={setTab} tabs={[{ value: 'invoices', label: 'الفواتير' }, { value: 'statement', label: 'كشف الحساب' }]} />
      {tab === 'statement' ? (
        <StatementView queryKey={['customer-statement', id]} load={(from, to) => customersApi.statement(id, from, to)}
          debitLabel="مدين (مشتريات)" creditLabel="دائن (دفعات/مرتجعات)" balanceHint="الموجب = مستحق على العميل" />
      ) : (
        <CustomerInvoices customerId={id} />
      )}
      {editing && <CustomerSheet customer={c} onClose={() => setEditing(false)} />}
    </div>
  );
}

function CustomerInvoices({ customerId }: { customerId: number }) {
  const [page, setPage] = useState(1);
  const query = useQuery({
    queryKey: ['sales', { customerId, page }],
    queryFn: () => salesApi.list({ customerId, page, pageSize: 20 }),
    placeholderData: keepPreviousData,
  });
  if (query.isPending) return <Spinner />;
  if (query.isError) return <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} />;
  if (query.data.items.length === 0) return <EmptyState title="لا توجد فواتير لهذا العميل" />;
  return (
    <>
      <ul className="divide-y divide-line rounded-card border border-line bg-surface">
        {query.data.items.map((s) => (
          <li key={s.id}>
            <Link to={`/sales/${s.id}`} className="flex items-center justify-between gap-3 p-3">
              <div>
                <p className="ltr font-semibold">{s.number}</p>
                <p className="text-xs text-muted">{formatDateTime(s.date)}{s.total > s.paidAmount && s.status !== 'Voided' && <span className="text-danger"> · آجل {formatMoney(s.total - s.paidAmount)}</span>}</p>
              </div>
              <div className="flex items-center gap-2">
                <span className="tabular font-bold">{formatMoney(s.total)}</span>
                <Badge tone={SALE_STATUS[s.status].tone}>{SALE_STATUS[s.status].label}</Badge>
              </div>
            </Link>
          </li>
        ))}
      </ul>
      <Pagination page={query.data.page} totalPages={query.data.totalPages} onChange={setPage} />
    </>
  );
}
