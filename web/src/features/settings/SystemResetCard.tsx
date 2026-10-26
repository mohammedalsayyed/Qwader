import { useState } from 'react';
import { useNavigate } from 'react-router';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { TriangleAlert } from 'lucide-react';
import { settingsApi, type SystemResetMode } from '@/api/endpoints';
import { useAuth } from '@/auth/AuthProvider';
import { Button } from '@/components/ui/Button';
import { Field, Input } from '@/components/ui/Field';
import { Card } from '@/components/ui/Feedback';
import { Sheet } from '@/components/ui/Sheet';
import { useToast } from '@/components/ui/Toast';
import { ApiError } from '@/api/client';
import { errorMessage } from '@/lib/forms';
import { cn } from '@/lib/cn';

const CONFIRM_WORD = 'تصفير';

const MODES: { value: SystemResetMode; title: string; removes: string; keeps: string }[] = [
  {
    value: 'TransactionsOnly',
    title: 'مسح الحركات فقط',
    removes: 'المبيعات والمشتريات والمرتجعات والحوالات والسندات والمصروفات والجرد وحركات المخزون. الكميات وأرصدة الحسابات وديون العملاء والموردين ترجع صفر.',
    keeps: 'المنتجات والأصناف والعملاء والموردون والحسابات المالية.',
  },
  {
    value: 'Everything',
    title: 'مسح كل شيء',
    removes: 'كل ما سبق + المنتجات والأصناف وصورها والعملاء والموردون والحسابات المالية (تُنشأ الحسابات الافتراضية من جديد). الترقيم يبدأ من 1.',
    keeps: 'المستخدمون وكلمات المرور والأدوار والإعدادات والوحدات.',
  },
];

/** Admin-only «start over» button: wipes data after the admin picks what to clear, types the word and re-enters their password. */
export function SystemResetCard() {
  const { user } = useAuth();
  const [open, setOpen] = useState(false);
  if (!user?.roles.includes('Admin')) return null;

  return (
    <Card className="space-y-3 border-danger/40 p-5">
      <h2 className="flex items-center gap-2 font-bold text-danger"><TriangleAlert className="size-5" aria-hidden /> تصفير النظام</h2>
      <p className="text-sm text-muted">لبدء العمل من جديد بعد التجربة. المسح نهائي ولا يمكن التراجع عنه — نزّل التقارير التي تحتاجها قبله.</p>
      <Button variant="outline" className="border-danger text-danger" onClick={() => setOpen(true)}>تصفير النظام…</Button>
      {open && <ResetSheet onClose={() => setOpen(false)} />}
    </Card>
  );
}

function ResetSheet({ onClose }: { onClose: () => void }) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [mode, setMode] = useState<SystemResetMode>('TransactionsOnly');
  const [word, setWord] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);

  const reset = useMutation({
    mutationFn: () => settingsApi.reset({ mode, password }),
    onSuccess: () => {
      // Every cached list is now stale.
      queryClient.clear();
      toast.success(mode === 'Everything' ? 'تم مسح كل البيانات، النظام جاهز من البداية' : 'تم مسح الحركات وتصفير الأرصدة والكميات');
      navigate('/');
    },
    onError: (e) => setError((e instanceof ApiError && e.fieldErrors?.password?.[0]) || errorMessage(e)),
  });

  const ready = word.trim() === CONFIRM_WORD && password.length > 0;

  return (
    <Sheet open onClose={onClose} title="تصفير النظام"
      footer={
        <Button variant="danger" block size="lg" disabled={!ready} loading={reset.isPending} onClick={() => { setError(null); reset.mutate(); }}>
          {mode === 'Everything' ? 'مسح كل شيء نهائيًا' : 'مسح الحركات نهائيًا'}
        </Button>
      }>
      <div className="space-y-4">
        <div className="grid gap-2" role="radiogroup" aria-label="ماذا تريد أن تمسح؟">
          {MODES.map((m) => (
            <button key={m.value} type="button" role="radio" aria-checked={mode === m.value} onClick={() => setMode(m.value)}
              className={cn('rounded-xl border p-3 text-start', mode === m.value ? 'border-danger bg-danger/5' : 'border-line')}>
              <span className="block font-semibold">{m.title}</span>
              <span className="mt-1 block text-xs leading-5"><b className="text-danger">يُمسح: </b>{m.removes}</span>
              <span className="mt-1 block text-xs leading-5 text-muted"><b>يبقى: </b>{m.keeps}</span>
            </button>
          ))}
        </div>
        <Field label={`اكتب كلمة «${CONFIRM_WORD}» للتأكيد`}>
          {(id) => <Input id={id} value={word} onChange={(e) => setWord(e.target.value)} autoComplete="off" />}
        </Field>
        <Field label="كلمة المرور الخاصة بك" error={error ?? undefined}>
          {(id, d) => <Input id={id} aria-describedby={d} type="password" autoComplete="current-password" invalid={!!error}
            value={password} onChange={(e) => setPassword(e.target.value)} />}
        </Field>
      </div>
    </Sheet>
  );
}
