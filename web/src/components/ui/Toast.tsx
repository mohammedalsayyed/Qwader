import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { CheckCircle2, AlertCircle, Info } from 'lucide-react';
import { cn } from '@/lib/cn';

type Kind = 'success' | 'error' | 'info';
interface ToastItem {
  id: number;
  kind: Kind;
  message: string;
}

interface ToastApi {
  success: (message: string) => void;
  error: (message: string) => void;
  info: (message: string) => void;
}

const ToastContext = createContext<ToastApi | null>(null);
let nextId = 1;

export function ToastProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<ToastItem[]>([]);

  const push = useCallback((kind: Kind, message: string) => {
    const id = nextId++;
    setItems((list) => [...list.slice(-2), { id, kind, message }]);
    setTimeout(() => setItems((list) => list.filter((t) => t.id !== id)), kind === 'error' ? 6000 : 3500);
  }, []);

  const api = useMemo<ToastApi>(
    () => ({
      success: (m) => push('success', m),
      error: (m) => push('error', m),
      info: (m) => push('info', m),
    }),
    [push],
  );

  return (
    <ToastContext.Provider value={api}>
      {children}
      {createPortal(
        <div
          className="pointer-events-none fixed inset-x-0 top-3 z-[60] flex flex-col items-center gap-2 px-4"
          aria-live="polite"
        >
          {items.map((t) => (
            <div
              key={t.id}
              role={t.kind === 'error' ? 'alert' : 'status'}
              className={cn(
                'pointer-events-auto flex w-full max-w-md items-start gap-2 rounded-2xl px-4 py-3 text-[15px] font-medium shadow-lg',
                t.kind === 'success' && 'bg-success text-white',
                t.kind === 'error' && 'bg-danger text-white',
                t.kind === 'info' && 'bg-ink text-white',
              )}
            >
              {t.kind === 'success' && <CheckCircle2 className="mt-0.5 size-5 shrink-0" />}
              {t.kind === 'error' && <AlertCircle className="mt-0.5 size-5 shrink-0" />}
              {t.kind === 'info' && <Info className="mt-0.5 size-5 shrink-0" />}
              <span>{t.message}</span>
            </div>
          ))}
        </div>,
        document.body,
      )}
    </ToastContext.Provider>
  );
}

export function useToast() {
  const ctx = useContext(ToastContext);
  if (!ctx) throw new Error('useToast must be used inside ToastProvider');
  return ctx;
}
