import { useEffect, useRef, type KeyboardEvent as ReactKeyboardEvent, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { X } from 'lucide-react';
import { cn } from '@/lib/cn';
import { IconButton } from './Button';
import { useOptionalConfirm } from './Confirm';

interface SheetProps {
  open: boolean;
  onClose: () => void;
  title: string;
  children: ReactNode;
  /** Sticky action bar; stays reachable above the phone keyboard. */
  footer?: ReactNode;
  size?: 'md' | 'lg';
  /** The form inside holds input that is not saved: closing asks first instead of throwing it away. */
  dirty?: boolean;
  /** Enter in a field saves (see {@link enterSaves}). Off where Enter must not commit, such as completing a sale. */
  submitOnEnter?: boolean;
}

/**
 * Enter in a one-line field saves the sheet: its form is submitted, or else the footer's main (primary/accent) button is pressed.
 * Never a red (danger) button, never from a multi-line text, a checkbox or a button, and not when the field handled Enter itself.
 */
export function enterSaves(e: ReactKeyboardEvent, container: HTMLElement | null, footer: HTMLElement | null): boolean {
  if (e.key !== 'Enter' || e.defaultPrevented || e.shiftKey || e.ctrlKey || e.altKey || e.metaKey || e.nativeEvent.isComposing) return false;
  const target = e.target as HTMLElement;
  if (!(target instanceof HTMLInputElement) || !container?.contains(target)) return false;
  if (['checkbox', 'radio', 'button', 'submit', 'file', 'reset'].includes(target.type)) return false;
  if (target.form && container.contains(target.form)) {
    e.preventDefault();
    target.form.requestSubmit();
    return true;
  }
  const main = footer?.querySelector<HTMLButtonElement>('button[data-variant="primary"]:not(:disabled), button[data-variant="accent"]:not(:disabled)');
  if (!main) return false;
  e.preventDefault();
  main.click();
  return true;
}

/** Open sheets, newest last: Escape closes only the top one (a confirmation over a form, a scanner over a product). */
const openSheets: symbol[] = [];

/** Bottom sheet on phones, centered dialog from the `sm` breakpoint up. */
export function Sheet({ open, onClose, title, children, footer, size = 'md', dirty = false, submitOnEnter = true }: SheetProps) {
  const panelRef = useRef<HTMLDivElement>(null);
  const footerRef = useRef<HTMLElement>(null);
  const confirm = useOptionalConfirm();
  const latest = useRef({ onClose, dirty });
  latest.current = { onClose, dirty };

  const requestClose = async () => {
    if (latest.current.dirty && confirm
      && !(await confirm({ title: 'تجاهل التغييرات؟', message: 'البيانات التي أدخلتها لم تُحفظ وستضيع إذا أغلقت النافذة.', confirmLabel: 'تجاهل وإغلاق', danger: true })))
      return;
    latest.current.onClose();
  };

  useEffect(() => {
    if (!open) return;
    const id = Symbol('sheet');
    openSheets.push(id);
    const previous = document.activeElement as HTMLElement | null;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && openSheets[openSheets.length - 1] === id) void requestClose();
    };
    document.addEventListener('keydown', onKey);
    document.body.style.overflow = 'hidden';
    // A field that asked for focus (a search box) keeps it; otherwise the sheet itself takes it.
    if (!panelRef.current?.contains(document.activeElement)) panelRef.current?.focus();
    return () => {
      openSheets.splice(openSheets.indexOf(id), 1);
      document.removeEventListener('keydown', onKey);
      if (openSheets.length === 0) document.body.style.overflow = '';
      previous?.focus();
    };
    // requestClose reads the latest props through a ref.
  }, [open]);

  if (!open) return null;

  return createPortal(
    <div className="fixed inset-0 z-50 flex items-end justify-center sm:items-center sm:p-4">
      <div className="absolute inset-0 bg-black/40" onClick={() => void requestClose()} aria-hidden />
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-label={title}
        tabIndex={-1}
        onKeyDown={submitOnEnter ? (e) => { enterSaves(e, panelRef.current, footerRef.current); } : undefined}
        className={cn(
          'relative flex max-h-[92dvh] w-full flex-col rounded-t-3xl bg-surface shadow-xl outline-none sm:rounded-3xl',
          size === 'lg' ? 'sm:max-w-2xl' : 'sm:max-w-lg',
        )}
      >
        <div className="mx-auto mt-2 h-1.5 w-10 rounded-full bg-gray-300 sm:hidden" aria-hidden />
        <header className="flex items-center justify-between gap-2 px-4 pb-2 pt-2 sm:pt-4">
          <h2 className="text-lg font-bold">{title}</h2>
          <IconButton label="إغلاق" onClick={() => void requestClose()}>
            <X className="size-5" />
          </IconButton>
        </header>
        <div className="flex-1 overflow-y-auto overscroll-contain px-4 pb-4">{children}</div>
        {footer && <footer ref={footerRef} className="pb-safe border-t border-line bg-surface px-4 pt-3 pb-3 sm:rounded-b-3xl">{footer}</footer>}
      </div>
    </div>,
    document.body,
  );
}
