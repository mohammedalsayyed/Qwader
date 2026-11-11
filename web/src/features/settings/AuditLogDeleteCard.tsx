import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ScrollText } from 'lucide-react';
import { settingsApi } from '@/api/endpoints';
import { ApiError } from '@/api/client';
import { useAuth } from '@/auth/AuthProvider';
import { Button } from '@/components/ui/Button';
import { Field, Input } from '@/components/ui/Field';
import { Card } from '@/components/ui/Feedback';
import { Sheet } from '@/components/ui/Sheet';
import { useToast } from '@/components/ui/Toast';
import { errorMessage } from '@/lib/forms';
import { formatDate, localToday } from '@/lib/format';

/** Admin-only: delete the audit log of a period (both days included), confirmed with the admin's password. The deletion itself stays logged. */
export function AuditLogDeleteCard() {
  const { user } = useAuth();
  const [open, setOpen] = useState(false);
  if (!user?.roles.includes('Admin')) return null;

  return (
    <Card className="space-y-3 p-5">
      <h2 className="flex items-center gap-2 font-bold"><ScrollText className="size-5" aria-hidden /> حذف سجل التدقيق</h2>
      <p className="text-sm text-muted">
        لتخفيف السجل القديم: يُحذف ما سُجّل بين تاريخين. لا يمس أي فاتورة أو حركة أو رصيد، ويبقى في السجل سطر يوضح من حذف وأي فترة.
      </p>
      <Button variant="outline" onClick={() => setOpen(true)}>حذف فترة من السجل…</Button>
      {open && <DeleteSheet onClose={() => setOpen(false)} />}
    </Card>
  );
}

function DeleteSheet({ onClose }: { onClose: () => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const today = localToday();
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);

  const remove = useMutation({
    mutationFn: () => settingsApi.deleteAuditLog({ from, to, password }),
    onSuccess: ({ deleted }) => {
      void queryClient.invalidateQueries({ queryKey: ['audit'] });
      toast.success(deleted > 0 ? `تم حذف ${deleted} عملية من سجل التدقيق` : 'لا توجد عمليات مسجلة في هذه الفترة');
      onClose();
    },
    onError: (e) => setError((e instanceof ApiError && (e.fieldErrors?.password?.[0] ?? e.fieldErrors?.to?.[0])) || errorMessage(e)),
  });

  const rangeError = from && to && to < from ? 'تاريخ النهاية قبل تاريخ البداية' : null;
  const ready = !!from && !!to && !rangeError && password.length > 0;

  return (
    <Sheet open onClose={onClose} title="حذف فترة من سجل التدقيق" dirty={!!from || !!to || !!password}
      footer={
        <Button variant="danger" block size="lg" disabled={!ready} loading={remove.isPending} onClick={() => { setError(null); remove.mutate(); }}>
          {from && to ? `حذف السجل من ${formatDate(from)} إلى ${formatDate(to)}` : 'حذف'}
        </Button>
      }>
      <div className="space-y-4">
        <div className="grid grid-cols-2 gap-3">
          <Field label="من تاريخ" required>
            {(id) => <Input id={id} type="date" max={today} value={from} onChange={(e) => { setFrom(e.target.value); setError(null); }} />}
          </Field>
          <Field label="إلى تاريخ" required error={rangeError ?? undefined}>
            {(id) => <Input id={id} type="date" max={today} value={to} onChange={(e) => { setTo(e.target.value); setError(null); }} />}
          </Field>
        </div>
        <p className="rounded-xl bg-amber-50 p-3 text-sm text-amber-900">
          الحذف نهائي ويشمل اليومين المحددين. البيانات نفسها (الفواتير، الحركات، الأرصدة) لا تتأثر — يُحذف السجل فقط.
        </p>
        <Field label="كلمة مرورك للتأكيد" required>
          {(id) => <Input id={id} type="password" autoComplete="current-password" value={password}
            onChange={(e) => { setPassword(e.target.value); setError(null); }} />}
        </Field>
        {error && <p role="alert" className="rounded-xl bg-red-50 p-3 text-sm text-danger">{error}</p>}
      </div>
    </Sheet>
  );
}
