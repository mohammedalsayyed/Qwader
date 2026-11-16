import { useState } from 'react';
import { Link } from 'react-router';
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useForm, Controller } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import { FolderTree, Layers, Pencil, Plus, Trash2 } from 'lucide-react';
import { categoriesApi, categoryGroupsApi, type Category, type CategoryGroup } from '@/api/catalog';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button } from '@/components/ui/Button';
import { Field, Input, Select, Switch, TextArea } from '@/components/ui/Field';
import { useConfirm } from '@/components/ui/Confirm';
import { Badge, Card, EmptyState, ErrorState, FilterChips, PageHeader, Pagination, SearchInput, Spinner } from '@/components/ui/Feedback';
import { Sheet } from '@/components/ui/Sheet';
import { useToast } from '@/components/ui/Toast';
import { cn } from '@/lib/cn';
import { applyServerErrors, blankToNull, errorMessage } from '@/lib/forms';
import { useDebouncedValue } from '@/lib/hooks';

type Status = 'active' | 'inactive' | 'all';

export function CategoriesPage() {
  const { can } = useAuth();
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<Status>('active');
  const [page, setPage] = useState(1);
  const [editing, setEditing] = useState<Category | 'new' | null>(null);
  const [groupId, setGroupId] = useState<number | undefined>();
  const term = useDebouncedValue(search.trim());

  const query = useQuery({
    queryKey: ['categories', { term, status, page, groupId }],
    queryFn: () => categoriesApi.list({ search: term, page, pageSize: 30, isActive: status === 'all' ? undefined : status === 'active', groupId }),
    placeholderData: keepPreviousData,
  });

  return (
    <>
      <PageHeader title="الأصناف" subtitle="المجموعات الرئيسية التي تندرج تحتها المنتجات" />
      <GroupsBar selected={groupId} onSelect={(id) => { setGroupId(id); setPage(1); }} />
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
                      {c.groupName && <> · <span className="font-medium text-brand-700">{c.groupName}</span></>}
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
        <Button size="lg" onClick={() => setEditing('new')} className="fixed bottom-fab end-4 z-30 rounded-full shadow-lg lg:static lg:mt-4">
          <Plus className="size-5" /> صنف جديد
        </Button>
      )}
      {editing && <CategorySheet category={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </>
  );
}

/**
 * «الأصناف العامة»: the general categories above the categories. Choosing one shows its categories; with the products
 * permission they are added, renamed and removed here (removing one leaves its categories without one).
 */
function GroupsBar({ selected, onSelect }: { selected?: number; onSelect: (id: number | undefined) => void }) {
  const { can } = useAuth();
  const manage = can(P.ProductsManage);
  const toast = useToast();
  const confirm = useConfirm();
  const queryClient = useQueryClient();
  const groups = useQuery({ queryKey: ['category-groups'], queryFn: categoryGroupsApi.list });
  const [editing, setEditing] = useState<CategoryGroup | 'new' | null>(null);
  const refresh = () => {
    for (const k of ['category-groups', 'categories', 'lookups', 'inventory-summary']) void queryClient.invalidateQueries({ queryKey: [k] });
  };
  const remove = useMutation({
    mutationFn: (g: CategoryGroup) => categoryGroupsApi.remove(g.id),
    onSuccess: (_, g) => { refresh(); if (selected === g.id) onSelect(undefined); toast.success(`حُذف «${g.name}»`); },
    onError: (e) => toast.error(errorMessage(e)),
  });
  const list = groups.data ?? [];
  if (!manage && list.length === 0) return null;

  return (
    <Card className="mb-4 p-3">
      <div className="mb-2 flex items-center justify-between gap-2">
        <h2 className="flex items-center gap-1.5 font-bold"><Layers className="size-4 text-brand-700" /> الأصناف العامة</h2>
        {manage && <Button size="sm" variant="outline" onClick={() => setEditing('new')}><Plus className="size-4" /> صنف عام</Button>}
      </div>
      {list.length === 0 ? (
        <p className="text-sm text-muted">مثل «أجهزة كهربائية» و«أدوات منزلية». أضفها، ثم اختر لكل صنف صنفه العام من «تعديل».</p>
      ) : (
        <div className="flex flex-wrap gap-2">
          <button type="button" onClick={() => onSelect(undefined)}
            className={cn('h-9 rounded-full border px-3 text-sm font-semibold', selected == null ? 'border-brand-700 bg-brand-700 text-white' : 'border-line bg-surface')}>
            الكل
          </button>
          {list.map((g) => (
            <span key={g.id} className={cn('inline-flex h-9 items-center gap-1 rounded-full border ps-3 text-sm',
              selected === g.id ? 'border-brand-700 bg-brand-50' : 'border-line bg-surface', !manage && 'pe-3')}>
              <button type="button" className="font-semibold" onClick={() => onSelect(selected === g.id ? undefined : g.id)}>
                {g.name} <span className="font-normal text-muted">· {g.categoryCount} صنف · {g.productCount} منتج</span>
              </button>
              {manage && (
                <>
                  <button type="button" aria-label={`تعديل اسم ${g.name}`} className="grid size-8 place-items-center text-muted hover:text-ink" onClick={() => setEditing(g)}>
                    <Pencil className="size-3.5" />
                  </button>
                  <button type="button" aria-label={`حذف ${g.name}`} className="grid size-8 place-items-center text-muted hover:text-danger"
                    onClick={async () => {
                      if (await confirm({ title: `حذف «${g.name}»`, message: g.categoryCount ? `أصنافه (${g.categoryCount}) تبقى كما هي، بدون صنف عام.` : 'لا أصناف تحته.', confirmLabel: 'حذف', danger: true }))
                        remove.mutate(g);
                    }}>
                    <Trash2 className="size-3.5" />
                  </button>
                </>
              )}
            </span>
          ))}
        </div>
      )}
      {editing && <GroupSheet group={editing === 'new' ? null : editing} onClose={() => setEditing(null)} onSaved={refresh} />}
    </Card>
  );
}

function GroupSheet({ group, onClose, onSaved }: { group: CategoryGroup | null; onClose: () => void; onSaved: () => void }) {
  const toast = useToast();
  const [name, setName] = useState(group?.name ?? '');
  const [error, setError] = useState<string>();
  const save = useMutation({
    mutationFn: () => (group ? categoryGroupsApi.rename(group.id, name.trim()) : categoryGroupsApi.create(name.trim())),
    onSuccess: (g) => { onSaved(); toast.success(group ? 'تم حفظ الاسم' : `أُضيف «${g.name}»`); onClose(); },
    onError: (e) => setError(errorMessage(e)),
  });
  const submit = () => (name.trim() ? save.mutate() : setError('اكتب اسم الصنف العام'));
  return (
    <Sheet open onClose={onClose} dirty={name !== (group?.name ?? '')} title={group ? 'تعديل الصنف العام' : 'صنف عام جديد'}
      footer={<Button block size="lg" loading={save.isPending} onClick={submit}>{group ? 'حفظ' : 'إضافة'}</Button>}>
      <Field label="اسم الصنف العام" required error={error}>
        {(id) => <Input id={id} value={name} maxLength={100} placeholder="مثال: أجهزة كهربائية" invalid={!!error} onChange={(e) => { setName(e.target.value); setError(undefined); }} />}
      </Field>
    </Sheet>
  );
}

const schema = z.object({
  name: z.string().trim().min(1, 'اسم الصنف مطلوب').max(100),
  code: z.string().trim().max(30).regex(/^\S*$/, 'الكود يجب ألا يحتوي على مسافات').optional(),
  description: z.string().max(500).optional(),
  isActive: z.boolean(),
  groupId: z.string(),
});
type FormValues = z.infer<typeof schema>;

export function CategorySheet({ category, onClose, onSaved }: { category: Category | null; onClose: () => void; onSaved?: (c: Category) => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const { register, control, handleSubmit, setError, formState: { errors, isDirty } } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { name: category?.name ?? '', code: category?.code ?? '', description: category?.description ?? '', isActive: category?.isActive ?? true,
      groupId: category?.groupId ? String(category.groupId) : '' },
  });
  const groups = useQuery({ queryKey: ['category-groups'], queryFn: categoryGroupsApi.list });

  const save = useMutation({
    mutationFn: (v: FormValues) => {
      const input = { name: v.name, code: blankToNull(v.code), description: blankToNull(v.description), isActive: v.isActive, groupId: v.groupId ? Number(v.groupId) : null };
      return category ? categoriesApi.update(category.id, input) : categoriesApi.create(input);
    },
    onSuccess: (saved) => {
      void queryClient.invalidateQueries({ queryKey: ['categories'] });
      void queryClient.invalidateQueries({ queryKey: ['lookups'] });
      void queryClient.invalidateQueries({ queryKey: ['category-groups'] });
      void queryClient.invalidateQueries({ queryKey: ['inventory-summary'] });
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
    <Sheet open onClose={onClose} dirty={isDirty} title={category ? 'تعديل الصنف' : 'صنف جديد'}
      footer={<Button block size="lg" loading={save.isPending} onClick={() => void onSubmit()}>{category ? 'حفظ' : 'إضافة الصنف'}</Button>}>
      <form onSubmit={onSubmit} noValidate className="space-y-4">
        <Field label="اسم الصنف" required error={errors.name?.message}>
          {(id, d) => <Input id={id} aria-describedby={d} placeholder="مثال: الصحون" invalid={!!errors.name} {...register('name')} />}
        </Field>
        {(groups.data?.length ?? 0) > 0 && (
          <Field label="الصنف العام" hint="منتجات هذا الصنف تُحسب تحته في الفلاتر والمخزون">
            {(id, d) => (
              <Select id={id} aria-describedby={d} {...register('groupId')}>
                <option value="">— بدون صنف عام —</option>
                {groups.data!.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
              </Select>
            )}
          </Field>
        )}
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
