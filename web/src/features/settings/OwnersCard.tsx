import { useEffect, useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Plus, X } from 'lucide-react';
import { settingsApi } from '@/api/endpoints';
import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Field';
import { Card } from '@/components/ui/Feedback';
import { useToast } from '@/components/ui/Toast';
import { errorMessage } from '@/lib/forms';
import { useStoreSettings } from '@/lib/queries';

/** «أصحاب العمل»: the owners' names; «مصاريف صاحب العمل» are recorded and totalled per owner. Saved on their own. */
export function OwnersCard() {
  const toast = useToast();
  const queryClient = useQueryClient();
  const settings = useStoreSettings();
  const [names, setNames] = useState<string[]>([]);
  const [typed, setTyped] = useState('');
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { if (settings.data) setNames(settings.data.owners ?? []); }, [settings.data]);
  const saved = settings.data?.owners ?? [];
  const changed = names.join('\n') !== saved.join('\n');

  const save = useMutation({
    mutationFn: () => settingsApi.setOwners(names),
    onSuccess: (list) => {
      queryClient.setQueryData(['settings'], (s: object | undefined) => (s ? { ...s, owners: list } : s));
      void queryClient.invalidateQueries({ queryKey: ['settings'] });
      toast.success('تم حفظ أصحاب العمل');
    },
    onError: (e) => setError(errorMessage(e)),
  });

  const add = () => {
    const n = typed.trim();
    if (!n) return;
    if (names.includes(n)) return setError('الاسم موجود');
    setNames([...names, n]);
    setTyped('');
    setError(null);
  };

  return (
    <Card className="space-y-3 p-5">
      <h2 className="font-bold">أصحاب العمل</h2>
      <p className="text-sm text-muted">
        أسماء أصحاب المعرض. «مصاريف صاحب العمل» (المسحوبات الشخصية) تُسجَّل باسم أحدهم، وتظهر لكل واحد مجاميعه. اتركها فارغة إن كان صاحب العمل واحدًا.
      </p>
      {names.length > 0 && (
        <ul className="flex flex-wrap gap-2">
          {names.map((n) => (
            <li key={n} className="flex items-center gap-1 rounded-full border border-line bg-canvas py-1 pe-1 ps-3 text-sm font-semibold">
              {n}
              <button type="button" aria-label={`حذف ${n}`} className="rounded-full p-1 text-muted hover:bg-black/5 hover:text-danger"
                onClick={() => setNames(names.filter((x) => x !== n))}><X className="size-4" /></button>
            </li>
          ))}
        </ul>
      )}
      <div className="flex gap-2">
        <Input aria-label="اسم صاحب عمل" placeholder="مثلًا: معتصم" value={typed} maxLength={60}
          onChange={(e) => { setTyped(e.target.value); setError(null); }}
          onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); add(); } }} />
        <Button variant="outline" className="shrink-0" onClick={add}><Plus className="size-4" /> إضافة</Button>
      </div>
      {error && <p role="alert" className="text-sm text-danger">{error}</p>}
      <p className="text-xs text-muted">حذف اسم لا يحذف مصاريفه السابقة؛ تبقى باسمه في القائمة.</p>
      <Button disabled={!changed} loading={save.isPending} onClick={() => save.mutate()}>حفظ أصحاب العمل</Button>
    </Card>
  );
}
