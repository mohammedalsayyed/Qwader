import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Plus, ShieldCheck, Trash2 } from 'lucide-react';
import { rolesApi, type Role } from '@/api/endpoints';
import { Button } from '@/components/ui/Button';
import { Checkbox, Field, Input } from '@/components/ui/Field';
import { Badge, Card, ErrorState, Spinner } from '@/components/ui/Feedback';
import { Sheet } from '@/components/ui/Sheet';
import { useConfirm } from '@/components/ui/Confirm';
import { useToast } from '@/components/ui/Toast';
import { errorMessage } from '@/lib/forms';

export function RolesTab() {
  const roles = useQuery({ queryKey: ['roles'], queryFn: rolesApi.list });
  const [editing, setEditing] = useState<Role | 'new' | null>(null);

  if (roles.isPending) return <Spinner />;
  if (roles.isError) return <ErrorState message={errorMessage(roles.error)} onRetry={() => void roles.refetch()} />;

  return (
    <>
      <ul className="grid gap-2 md:grid-cols-2">
        {roles.data.map((r) => (
          <li key={r.id}>
            <button type="button" className="w-full text-start" onClick={() => setEditing(r)}>
              <Card className="p-4 transition hover:border-brand-200">
                <div className="flex items-center justify-between gap-2">
                  <span className="flex items-center gap-2 font-semibold">
                    <ShieldCheck className="size-5 text-brand-700" /> {r.displayName}
                  </span>
                  {r.isSystem && <Badge>أساسي</Badge>}
                </div>
                {r.description && <p className="mt-1 text-sm text-muted">{r.description}</p>}
                <p className="mt-2 text-xs text-muted">
                  {r.permissions.length} صلاحية · {r.userCount} مستخدم
                </p>
              </Card>
            </button>
          </li>
        ))}
      </ul>
      <Button variant="outline" block className="mt-3" onClick={() => setEditing('new')}>
        <Plus className="size-4" /> دور جديد
      </Button>
      {editing && <RoleSheet role={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </>
  );
}

function RoleSheet({ role, onClose }: { role: Role | null; onClose: () => void }) {
  const toast = useToast();
  const confirm = useConfirm();
  const queryClient = useQueryClient();
  const catalog = useQuery({ queryKey: ['permission-catalog'], queryFn: rolesApi.permissions, staleTime: Infinity });

  const [displayName, setDisplayName] = useState(role?.displayName ?? '');
  const [description, setDescription] = useState(role?.description ?? '');
  const [selected, setSelected] = useState<Set<string>>(new Set(role?.permissions ?? []));
  const [nameError, setNameError] = useState<string>();
  const locked = role?.name === 'Admin';

  const done = (message: string) => {
    void queryClient.invalidateQueries({ queryKey: ['roles'] });
    toast.success(message);
    onClose();
  };

  const save = useMutation({
    mutationFn: () => {
      const input = { displayName: displayName.trim(), description: description.trim() || null, permissions: [...selected] };
      return role ? rolesApi.update(role.id, input) : rolesApi.create(input);
    },
    onSuccess: () => done(role ? 'تم حفظ صلاحيات الدور' : 'تمت إضافة الدور'),
    onError: (e) => toast.error(errorMessage(e)),
  });

  const remove = useMutation({
    mutationFn: () => rolesApi.remove(role!.id),
    onSuccess: () => done('تم حذف الدور'),
    onError: (e) => toast.error(errorMessage(e)),
  });

  const toggle = (code: string, on: boolean) =>
    setSelected((s) => {
      const next = new Set(s);
      if (on) next.add(code);
      else next.delete(code);
      return next;
    });

  const submit = () => {
    if (!displayName.trim()) {
      setNameError('اسم الدور مطلوب');
      return;
    }
    save.mutate();
  };

  return (
    <Sheet
      open
      size="lg"
      onClose={onClose}
      title={role ? `صلاحيات: ${role.displayName}` : 'دور جديد'}
      footer={
        <div className="flex gap-2">
          <Button block size="lg" loading={save.isPending} disabled={locked} onClick={submit}>
            حفظ
          </Button>
          {role && !role.isSystem && (
            <Button
              variant="outline"
              size="lg"
              aria-label="حذف الدور"
              loading={remove.isPending}
              onClick={async () => {
                if (await confirm({ title: 'حذف الدور', message: `هل تريد حذف دور «${role.displayName}»؟`, confirmLabel: 'حذف', danger: true }))
                  remove.mutate();
              }}
            >
              <Trash2 className="size-5 text-danger" />
            </Button>
          )}
        </div>
      }
    >
      <div className="space-y-4">
        {locked && <p className="rounded-xl bg-amber-50 p-3 text-sm text-amber-800">دور مدير النظام يملك جميع الصلاحيات دائمًا ولا يمكن تقليصها.</p>}
        <Field label="اسم الدور" required error={nameError}>
          {(id, d) => (
            <Input
              id={id}
              aria-describedby={d}
              value={displayName}
              disabled={locked}
              invalid={!!nameError}
              onChange={(e) => {
                setDisplayName(e.target.value);
                setNameError(undefined);
              }}
            />
          )}
        </Field>
        <Field label="الوصف">
          {(id) => <Input id={id} value={description} disabled={locked} onChange={(e) => setDescription(e.target.value)} />}
        </Field>

        {catalog.isPending ? (
          <Spinner />
        ) : (
          catalog.data?.map((g) => {
            const all = g.permissions.every((p) => selected.has(p.code));
            return (
              <fieldset key={g.group} className="rounded-2xl border border-line p-3">
                <legend className="px-1 text-sm font-bold">{g.group}</legend>
                <Checkbox
                  label="تحديد الكل"
                  checked={all}
                  disabled={locked}
                  onChange={(on) => g.permissions.forEach((p) => toggle(p.code, on))}
                />
                <div className="border-t border-line pt-1">
                  {g.permissions.map((p) => (
                    <Checkbox key={p.code} label={p.name} checked={selected.has(p.code)} disabled={locked} onChange={(on) => toggle(p.code, on)} />
                  ))}
                </div>
              </fieldset>
            );
          })
        )}
      </div>
    </Sheet>
  );
}
