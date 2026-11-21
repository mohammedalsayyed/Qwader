import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react';
import { Eye, EyeOff, LockKeyhole } from 'lucide-react';
import { authApi } from '@/api/endpoints';
import { Button } from '@/components/ui/Button';
import { Field, Input } from '@/components/ui/Field';
import { Sheet } from '@/components/ui/Sheet';
import { useToast } from '@/components/ui/Toast';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';

/** How long the profits stay shown after the password (then hidden again by themselves). */
const SHOWN_FOR_MS = 10 * 60 * 1000;

interface ProfitLock {
  /** The profits are shown now (the password was given in the last few minutes). */
  shown: boolean;
  /** Asks for the password, then shows the profits. */
  show: () => void;
  /** Hides them again at once. */
  hide: () => void;
}

const Context = createContext<ProfitLock | null>(null);

/**
 * Profits are hidden on screen even for those allowed to see them (a customer or a colleague may be looking): a profit shows
 * as ••••• with an eye; the user's own password shows every profit for ten minutes. Nothing is stored: a reload hides them.
 */
export function ProfitLockProvider({ children }: { children: ReactNode }) {
  const toast = useToast();
  const [until, setUntil] = useState(0);
  const [asking, setAsking] = useState(false);
  const [now, setNow] = useState(() => Date.now());
  const shown = until > now;

  // Hide again when the time is up.
  useEffect(() => {
    if (!shown) return;
    const timer = setTimeout(() => setNow(Date.now()), until - Date.now() + 50);
    return () => clearTimeout(timer);
  }, [shown, until]);

  const hide = useCallback(() => { setUntil(0); setNow(Date.now()); }, []);
  const value = useMemo<ProfitLock>(() => ({ shown, show: () => setAsking(true), hide }), [shown, hide]);

  return (
    <Context.Provider value={value}>
      {children}
      {shown && (
        <button type="button" onClick={hide}
          className="fixed bottom-fab start-4 z-30 flex items-center gap-1.5 rounded-full bg-gray-900/85 px-3 py-2 text-xs font-semibold text-white shadow-lg lg:bottom-4 lg:start-auto lg:end-4 print:hidden">
          <EyeOff className="size-4" /> إخفاء الأرباح
        </button>
      )}
      {asking && (
        <PasswordSheet onClose={() => setAsking(false)}
          onConfirmed={() => {
            setAsking(false);
            setUntil(Date.now() + SHOWN_FOR_MS);
            setNow(Date.now());
            toast.info('الأرباح ظاهرة لمدة 10 دقائق');
          }} />
      )}
    </Context.Provider>
  );
}

export function useProfitLock(): ProfitLock {
  return useContext(Context) ?? { shown: true, show: () => undefined, hide: () => undefined };
}

/** A profit figure: as is when the profits are shown, otherwise ••••• with an eye that asks for the password. */
export function Profit({ children, className }: { children: ReactNode; className?: string }) {
  const { shown, show } = useProfitLock();
  if (shown) return <>{children}</>;
  return (
    <button type="button" onClick={(e) => { e.preventDefault(); e.stopPropagation(); show(); }}
      className={cn('inline-flex items-center gap-1 align-middle text-muted hover:text-brand-700 print:hidden', className)}
      aria-label="إظهار الأرباح (يحتاج كلمة المرور)">
      <span className="tracking-widest">•••••</span>
      <Eye className="size-4 shrink-0" />
    </button>
  );
}

/** A whole part made of profits (a chart, the balance sheet): hidden behind one button until the password is given. */
export function ProfitGate({ children, title = 'الأرباح مخفية' }: { children: ReactNode; title?: string }) {
  const { shown, show } = useProfitLock();
  if (shown) return <>{children}</>;
  return (
    <div className="flex flex-col items-center gap-3 rounded-card border border-dashed border-line bg-surface p-8 text-center print:hidden">
      <LockKeyhole className="size-8 text-muted" />
      <p className="font-semibold">{title}</p>
      <p className="text-sm text-muted">لإظهارها أدخل كلمة المرور الخاصة بك. تبقى ظاهرة 10 دقائق.</p>
      <Button variant="secondary" onClick={show}><Eye className="size-4" /> إظهار الأرباح</Button>
    </div>
  );
}

function PasswordSheet({ onClose, onConfirmed }: { onClose: () => void; onConfirmed: () => void }) {
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async () => {
    if (!password) return setError('اكتب كلمة المرور');
    setBusy(true);
    try {
      await authApi.confirmPassword(password);
      onConfirmed();
    } catch (e) {
      setError(errorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Sheet open onClose={onClose} title="إظهار الأرباح"
      footer={<Button block size="lg" loading={busy} onClick={() => void submit()}><Eye className="size-5" /> إظهار</Button>}>
      <div className="space-y-3">
        <p className="text-sm text-muted">الأرباح مخفية عن الشاشة. أدخل كلمة المرور الخاصة بك لإظهارها لمدة 10 دقائق.</p>
        <Field label="كلمة المرور" error={error ?? undefined}>
          {(id, d) => (
            <Input id={id} aria-describedby={d} type="password" autoComplete="current-password" autoFocus value={password} invalid={!!error}
              onChange={(e) => { setPassword(e.target.value); setError(null); }}
              onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); void submit(); } }} />
          )}
        </Field>
      </div>
    </Sheet>
  );
}
