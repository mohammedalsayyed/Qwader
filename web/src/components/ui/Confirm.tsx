import { createContext, useCallback, useContext, useRef, useState, type ReactNode } from 'react';
import { Button } from './Button';
import { Sheet } from './Sheet';

interface ConfirmOptions {
  title: string;
  message: string;
  confirmLabel?: string;
  danger?: boolean;
}

type ConfirmFn = (options: ConfirmOptions) => Promise<boolean>;
const ConfirmContext = createContext<ConfirmFn | null>(null);

/** Promise-based confirmation for sensitive actions: `if (await confirm({...})) …` */
export function ConfirmProvider({ children }: { children: ReactNode }) {
  const [options, setOptions] = useState<ConfirmOptions | null>(null);
  const resolver = useRef<(value: boolean) => void>(undefined);

  const confirm = useCallback<ConfirmFn>((opts) => {
    setOptions(opts);
    return new Promise<boolean>((resolve) => {
      resolver.current = resolve;
    });
  }, []);

  const close = (value: boolean) => {
    resolver.current?.(value);
    setOptions(null);
  };

  return (
    <ConfirmContext.Provider value={confirm}>
      {children}
      <Sheet
        open={!!options}
        onClose={() => close(false)}
        title={options?.title ?? ''}
        footer={
          <div className="flex gap-2">
            <Button block variant={options?.danger ? 'danger' : 'primary'} onClick={() => close(true)}>
              {options?.confirmLabel ?? 'تأكيد'}
            </Button>
            <Button block variant="outline" onClick={() => close(false)}>
              تراجع
            </Button>
          </div>
        }
      >
        <p className="text-[15px] leading-7 text-muted">{options?.message}</p>
      </Sheet>
    </ConfirmContext.Provider>
  );
}

export function useConfirm() {
  const ctx = useContext(ConfirmContext);
  if (!ctx) throw new Error('useConfirm must be used inside ConfirmProvider');
  return ctx;
}
