import { useState, type ReactNode } from 'react';
import { ChevronDown } from 'lucide-react';
import { cn } from '@/lib/cn';

export function Tabs<T extends string>({
  tabs,
  value,
  onChange,
}: {
  tabs: { value: T; label: string; badge?: number }[];
  value: T;
  onChange: (value: T) => void;
}) {
  return (
    <div className="mb-4 flex rounded-xl bg-black/5 p-1" role="tablist">
      {tabs.map((t) => (
        <button
          key={t.value}
          type="button"
          role="tab"
          aria-selected={value === t.value}
          onClick={() => onChange(t.value)}
          className={cn(
            'flex h-10 flex-1 items-center justify-center gap-1.5 rounded-lg px-2 text-sm font-semibold',
            value === t.value ? 'bg-surface shadow-sm' : 'text-muted',
          )}
        >
          {t.label}
          {!!t.badge && <span className="rounded-full bg-danger px-1.5 text-[11px] leading-5 text-white">{t.badge}</span>}
        </button>
      ))}
    </div>
  );
}

/** Optional details hidden by default so short forms stay short. */
export function Collapsible({ title, hint, defaultOpen = false, children }: { title: string; hint?: string; defaultOpen?: boolean; children: ReactNode }) {
  const [open, setOpen] = useState(defaultOpen);
  return (
    <section className="rounded-card border border-line bg-surface">
      <button type="button" onClick={() => setOpen((o) => !o)} aria-expanded={open} className="flex min-h-13 w-full items-center justify-between gap-2 px-5 py-3 text-start">
        <span>
          <span className="block font-bold">{title}</span>
          {hint && <span className="block text-xs text-muted">{hint}</span>}
        </span>
        <ChevronDown className={cn('size-5 shrink-0 text-muted transition', open && 'rotate-180')} />
      </button>
      {open && <div className="space-y-4 border-t border-line px-5 pt-4 pb-5">{children}</div>}
    </section>
  );
}

export function Stat({
  label,
  value,
  tone = 'default',
  hint,
  onClick,
}: {
  label: string;
  value: ReactNode;
  tone?: 'default' | 'success' | 'warning' | 'danger';
  hint?: string;
  onClick?: () => void;
}) {
  const Tag = onClick ? 'button' : 'div';
  return (
    <Tag
      type={onClick ? 'button' : undefined}
      onClick={onClick}
      className={cn('rounded-card border border-line bg-surface p-4 text-start', onClick && 'transition hover:border-brand-200 active:bg-brand-50')}
    >
      <p className="text-xs font-medium text-muted">{label}</p>
      <p
        className={cn(
          'tabular mt-1 text-lg font-bold sm:text-xl',
          tone === 'success' && 'text-success',
          tone === 'warning' && 'text-warning',
          tone === 'danger' && 'text-danger',
        )}
      >
        {value}
      </p>
      {hint && <p className="mt-0.5 text-xs text-muted">{hint}</p>}
    </Tag>
  );
}

/** Label/value rows for detail pages. */
export function InfoList({ items }: { items: ({ label: string; value: ReactNode } | false | null | undefined | '')[] }) {
  return (
    <dl className="divide-y divide-line">
      {items.filter(Boolean).map((item) => {
        const { label, value } = item as { label: string; value: ReactNode };
        return (
          <div key={label} className="flex items-start justify-between gap-4 py-2.5">
            <dt className="shrink-0 text-sm text-muted">{label}</dt>
            <dd className="min-w-0 text-end text-[15px] font-medium break-words">{value ?? '—'}</dd>
          </div>
        );
      })}
    </dl>
  );
}

/** Fixed bottom action bar on phones (above the tab bar), inline on desktop. */
export function StickyActions({ children }: { children: ReactNode }) {
  return (
    <div className="fixed inset-x-0 bottom-nav z-30 border-t border-line bg-surface/95 px-4 py-3 backdrop-blur lg:static lg:mt-4 lg:border-0 lg:bg-transparent lg:p-0 lg:backdrop-blur-none">
      <div className="mx-auto flex max-w-6xl gap-2">{children}</div>
    </div>
  );
}
