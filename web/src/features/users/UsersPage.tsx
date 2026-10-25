import { useDeferredValue, useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Plus, UserRound } from 'lucide-react';
import { usersApi, type User } from '@/api/endpoints';
import { Button } from '@/components/ui/Button';
import { Badge, Card, EmptyState, ErrorState, FilterChips, PageHeader, Pagination, SearchInput, Spinner } from '@/components/ui/Feedback';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';
import { formatDateTime } from '@/lib/format';
import { RolesTab } from './RolesTab';
import { UserSheet } from './UserSheet';

type Tab = 'users' | 'roles';
type StatusFilter = 'all' | 'active' | 'inactive';

export function UsersPage() {
  const [tab, setTab] = useState<Tab>('users');

  return (
    <>
      <PageHeader title="المستخدمون والصلاحيات" />
      <div className="mb-4 grid grid-cols-2 rounded-xl bg-black/5 p-1" role="tablist">
        {(
          [
            ['users', 'المستخدمون'],
            ['roles', 'الأدوار والصلاحيات'],
          ] as const
        ).map(([key, label]) => (
          <button
            key={key}
            role="tab"
            aria-selected={tab === key}
            onClick={() => setTab(key)}
            className={cn('h-10 rounded-lg text-sm font-semibold', tab === key ? 'bg-surface shadow-sm' : 'text-muted')}
          >
            {label}
          </button>
        ))}
      </div>
      {tab === 'users' ? <UsersTab /> : <RolesTab />}
    </>
  );
}

function UsersTab() {
  const [search, setSearch] = useState('');
  const [status, setStatus] = useState<StatusFilter>('active');
  const [page, setPage] = useState(1);
  const [editing, setEditing] = useState<User | 'new' | null>(null);
  const deferredSearch = useDeferredValue(search.trim());

  const query = useQuery({
    queryKey: ['users', { search: deferredSearch, status, page }],
    queryFn: () =>
      usersApi.list({ search: deferredSearch, page, pageSize: 20, isActive: status === 'all' ? undefined : status === 'active' }),
    placeholderData: keepPreviousData,
  });

  return (
    <>
      <div className="mb-3 space-y-3">
        <SearchInput
          value={search}
          onChange={(v) => {
            setSearch(v);
            setPage(1);
          }}
          placeholder="ابحث بالاسم أو اسم المستخدم أو الجوال"
        />
        <FilterChips
          value={status}
          onChange={(v) => {
            setStatus(v);
            setPage(1);
          }}
          options={[
            { value: 'active', label: 'الفعّالون' },
            { value: 'inactive', label: 'المعطّلون' },
            { value: 'all', label: 'الكل' },
          ]}
        />
      </div>

      {query.isPending ? (
        <Spinner />
      ) : query.isError ? (
        <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} />
      ) : query.data.items.length === 0 ? (
        <EmptyState icon={<UserRound className="size-10" />} title="لا يوجد مستخدمون مطابقون" />
      ) : (
        <>
          <ul className="grid gap-2 md:grid-cols-2">
            {query.data.items.map((u) => (
              <li key={u.id}>
                <button type="button" onClick={() => setEditing(u)} className="w-full text-start">
                  <Card className="flex items-start gap-3 p-4 transition hover:border-brand-200">
                    <span className="flex size-11 shrink-0 items-center justify-center rounded-full bg-brand-50 text-lg font-bold text-brand-700">
                      {u.fullName.trim()[0]}
                    </span>
                    <span className="min-w-0 flex-1">
                      <span className="flex items-center justify-between gap-2">
                        <span className="truncate font-semibold">{u.fullName}</span>
                        {!u.isActive && <Badge tone="danger">معطّل</Badge>}
                      </span>
                      <span className="ltr block truncate text-sm text-muted">{u.userName}</span>
                      <span className="mt-2 flex flex-wrap gap-1">
                        {u.roles.map((r) => (
                          <Badge key={r.name} tone="brand">
                            {r.displayName}
                          </Badge>
                        ))}
                      </span>
                      <span className="mt-2 block text-xs text-muted">آخر دخول: {formatDateTime(u.lastLoginAt)}</span>
                    </span>
                  </Card>
                </button>
              </li>
            ))}
          </ul>
          <Pagination page={query.data.page} totalPages={query.data.totalPages} onChange={setPage} />
        </>
      )}

      {/* Fixed add button above the bottom bar on phones */}
      <Button
        size="lg"
        onClick={() => setEditing('new')}
        className="fixed bottom-22 end-4 z-30 rounded-full shadow-lg lg:static lg:mt-4"
      >
        <Plus className="size-5" /> مستخدم جديد
      </Button>

      {editing && <UserSheet user={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </>
  );
}
