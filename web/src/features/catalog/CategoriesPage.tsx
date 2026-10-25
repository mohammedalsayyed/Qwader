import { useState } from 'react';
import { Link } from 'react-router';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm, Controller } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { FolderTree, Plus } from 'lucide-react';
import { categoriesApi, type Category } from '@/api/catalog';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button } from '@/components/ui/Button';
import { Field, Input, Switch, TextArea } from '@/components/ui/Field';
import { Badge, Card, EmptyState, ErrorState, FilterChips, PageHeader, Pagination, SearchInput, Spinner } from '@/components/ui/Feedback';
import { Sheet } from '@/components/ui/Sheet';
import { useToast } from '@/components/ui/Toast';
import { applyServerErrors, blankToNull, errorMessage } from '@/lib/forms';
import { useDebouncedValue } from '@/lib/hooks';

type Status = 'active' | 'inactive' | 'all';

export function CategoriesPage() {
  const { can } = useAuth();
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<Status>('active');
  const [page, setPage] = useState(1);
  const [editing, setEditing] = useState<Category | 'new' | null>(null);
  const term = useDebouncedValue(search.trim());

  const query = useQuery({
    queryKey: ['categories', { term, status, page }],
    queryFn: () => categoriesApi.list({ search: term, page, pageSize: 30, isActive: status === 'all' ? undefined : status === 'active' }),
    placeholderData: keepPreviousData,
  });

  return (
    <>
      <PageHeader title="الأصناف" subtitle="المجموعات الرئيسية التي تندرج تحتها المنتجات" />
      <div className="mb-3 space-y-3">
        <SearchInput value={search} onChange={(v) => { setSearch(v); setPage(1); }} placeholder="ابحث باسم الصنف أو الكود" />
        <FilterChips value={status} onChange={(v) => { setStatus(v); setPage(1); }}
          options={[{ value: 'active', label: 'الفعّالة' }, { value: 'inactive', label: 'غير الفعّالة' }, { value: 'all', label: 'الكل' }]} />
      </div>

      {query.isPending ? (
        <Spinner />
      ) : query.isError ? (
        <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} />
      ) : query.data.items.length === 0 ? (
        <EmptyState icon={<FolderTree className="size-10" />} title={term ? 'لا توجد أصناف مطابقة' : 'لا توجد أصناف بعد'}
          description={term ? undefined : 'أضف أول صنف مثل «الصحون» أو «الأجهزة الكهربائية»'} />
      ) : (
        <>
          <ul className="grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
            {query.data.items.map((c) => (
              <li key={c.id}>
                <Card className="flex items-center gap-3 p-4">
                  <Link to={`/products?categoryId=${c.id}`} className="min-w-0 flex-1">
                    <p className="flex items-center gap-2 font-semibold">
                      <span className="truncate">{c.name}</span>
                      {!c.isActive && <Badge tone="danger">غير فعّال</Badge>}
                    </p>
                    <p className="text-sm text-muted">
                      <span className="ltr inline-block">{c.code}</span> · {c.productCount} منتج
                    </p>
                  </Link>
                  {can(P.ProductsManage) && (
                    <Button variant="outline" size="sm" onClick={() => setEditing(c)}>تعديل</Button>
                  )}
                </Card>
              </li>
            ))}
          </ul>
          <Pagination page={query.data.page} totalPages={query.data.totalPages} onChange={setPage} />
        </>
      )}

      {can(P.ProductsManage) && (
        <Button size="lg" onClick={() => setEditing('new')} className="fixed bottom-22 end-4 z-30 rounded-full shadow-lg lg:static lg:mt-4">
          <Plus className="size-5" /> صنف جديد
        </Button>
      )}
      {editing && <CategorySheet category={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </>
  );
}

const schema = z.object({
  name: z.string().trim().min(1, 'اسم الصنف مطلوب').max(100),
  code: z.string().trim().max(30).regex(/^\S*$/, 'الكود يجب ألا يحتوي على مسافات').optional(),
  description: z.string().max(500).optional(),
  isActive: z.boolean(),
});
type FormValues = z.infer<typeof schema>;

export function CategorySheet({ category, onClose, onSaved }: { category: Category | null; onClose: () => void; onSaved?: (c: Category) => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const { register, control, handleSubmit, setError, formState: { errors } } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { name: category?.name ?? '', code: category?.code ?? '', description: category?.description ?? '', isActive: category?.isActive ?? true },
  });

  const save = useMutation({
    mutationFn: (v: FormValues) => {
      const input = { name: v.name, code: blankToNull(v.code), description: blankToNull(v.description), isActive: v.isActive };
      return category ? categoriesApi.update(category.id, input) : categoriesApi.create(input);
    },
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: ['categories'] });
      void queryClient.invalidateQueries({ queryKey: ['lookups'] });
      toast.success(category ? 'تم حفظ الصنف' : 'تمت إضافة الصنف');
      onSaved?.(saved);
      onClose();
    },
    onError: (e) => {
      const msg = applyServerErrors(e, setError, ['name', 'code', 'description']);
      if (msg) toast.error(msg);
    },
  });
  const onSubmit = handleSubmit((v) => save.mutate(v));

  return (
    <Sheet open onClose={onClose} title={category ? 'تعديل الصنف' : 'صنف جديد'}
      footer={<Button block size="lg" loading={save.isPending} onClick={() => void onSubmit()}>{category ? 'حفظ' : 'إضافة الصنف'}</Button>}>
      <form onSubmit={onSubmit} noValidate className="space-y-4">
        <Field label="اسم الصنف" required error={errors.name?.message}>
          {(id, d) => <Input id={id} aria-describedby={d} placeholder="مثال: الصحون" invalid={!!errors.name} {...register('name')} />}
        </Field>
        <Field label="كود الصنف" error={errors.code?.message} hint="اتركه فارغًا ليُنشأ تلقائيًا">
          {(id, d) => <Input id={id} aria-describedby={d} className="ltr" invalid={!!errors.code} {...register('code')} />}
        </Field>
        <Field label="الوصف" error={errors.description?.message}>
          {(id, d) => <TextArea id={id} aria-describedby={d} rows={2} {...register('description')} />}
        </Field>
        {category && (
          <Controller control={control} name="isActive"
            render={({ field }) => <Switch label="الصنف فعّال" description="الصنف غير الفعّال لا يظهر عند إضافة منتجات جديدة" checked={field.value} onChange={field.onChange} />} />
        )}
      </form>
    </Sheet>
  );
}
