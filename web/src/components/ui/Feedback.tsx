import type { ReactNode } from 'react';
import { ChevronLeft, ChevronRight, Loader2, Search, WifiOff, X } from 'lucide-react';
import { cn } from '@/lib/cn';
import { Button } from './Button';

export function Spinner({ label = 'جارٍ التحميل…', className }: { label?: string; className?: string }) {
  return (
    <div role="status" className={cn('flex items-center justify-center gap-2 py-10 text-muted', className)}>
      <Loader2 className="size-5 animate-spin" aria-hidden />
      <span>{label}</span>
    </div>
  );
}

export function EmptyState({ icon, title, description, action }: { icon?: ReactNode; title: string; description?: string; action?: ReactNode }) {
  return (
    <div className="flex flex-col items-center gap-2 px-4 py-12 text-center">
      {icon && <div className="mb-1 text-muted">{icon}</div>}
      <p className="text-base font-semibold">{title}</p>
      {description && <p className="max-w-sm text-sm text-muted">{description}</p>}
      {action && <div className="mt-3">{action}</div>}
    </div>
  );
}

export function ErrorState({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <EmptyState
      icon={<WifiOff className="size-8" />}
      title="تعذر تحميل البيانات"
      description={message}
      action={onRetry && <Button variant="outline" onClick={onRetry}>إعادة المحاولة</Button>}
    />
  );
}

export function PageHeader({ title, subtitle, actions }: { title: string; subtitle?: string; actions?: ReactNode }) {
  return (
    <div className="mb-4 flex flex-wrap items-end justify-between gap-3">
      <div className="min-w-0">
        <h1 className="text-xl font-bold sm:text-2xl">{title}</h1>
        {subtitle && <p className="mt-0.5 text-sm text-muted">{subtitle}</p>}
      </div>
      {actions && <div className="flex gap-2">{actions}</div>}
    </div>
  );
}

export function Card({ className, children }: { className?: string; children: ReactNode }) {
  return <div className={cn('rounded-card border border-line bg-surface', className)}>{children}</div>;
}

type Tone = 'neutral' | 'success' | 'warning' | 'danger' | 'brand';
const tones: Record<Tone, string> = {
  neutral: 'bg-gray-100 text-gray-700',
  success: 'bg-green-50 text-green-800',
  warning: 'bg-amber-50 text-amber-800',
  danger: 'bg-red-50 text-red-800',
  brand: 'bg-brand-50 text-brand-800',
};

export function Badge({ tone = 'neutral', children }: { tone?: Tone; children: ReactNode }) {
  return <span className={cn('inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-semibold', tones[tone])}>{children}</span>;
}

export function SearchInput({
  value,
  onChange,
  placeholder = 'بحث…',
  autoFocus,
}: {
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  autoFocus?: boolean;
}) {
  return (
    <div className="relative">
      <Search className="pointer-events-none absolute start-3.5 top-1/2 size-5 -translate-y-1/2 text-muted" aria-hidden />
      <input
        type="search"
        value={value}
        autoFocus={autoFocus}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        aria-label={placeholder}
        className="h-12 w-full rounded-xl border border-line bg-surface ps-11 pe-11 focus:border-brand-500 focus:outline-none focus:ring-2 focus:ring-brand-500/20 [&::-webkit-search-cancel-button]:hidden"
      />
      {value && (
        <button
          type="button"
          aria-label="مسح البحث"
          onClick={() => onChange('')}
          className="absolute end-1 top-1/2 flex size-10 -translate-y-1/2 items-center justify-center rounded-lg text-muted hover:bg-black/5"
        >
          <X className="size-4" />
        </button>
      )}
    </div>
  );
}

/** Page numbers to show: first, last, and a window around the current page, with gaps marked by null. */
function pageWindow(page: number, total: number): (number | null)[] {
  const pages = new Set([1, total, page - 1, page, page + 1].filter((p) => p >= 1 && p <= total));
  const sorted = [...pages].sort((a, b) => a - b);
  const out: (number | null)[] = [];
  sorted.forEach((p, i) => {
    if (i > 0 && p - sorted[i - 1] > 1) out.push(null);
    out.push(p);
  });
  return out;
}

export function Pagination({
  page,
  totalPages,
  onChange,
  totalCount,
  pageSize,
}: {
  page: number;
  totalPages: number;
  onChange: (page: number) => void;
  totalCount?: number;
  pageSize?: number;
}) {
  if (totalPages <= 1) {
    return totalCount ? <p className="tabular mt-3 text-center text-sm text-muted">{totalCount} نتيجة</p> : null;
  }
  const go = (p: number) => {
    onChange(p);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  };
  const from = pageSize ? (page - 1) * pageSize + 1 : null;
  const to = pageSize && totalCount ? Math.min(page * pageSize, totalCount) : null;

  return (
    <nav className="mt-4 space-y-2" aria-label="الصفحات">
      <div className="flex items-center justify-center gap-1">
        {/* In RTL «السابق» points right. */}
        <button type="button" aria-label="الصفحة السابقة" disabled={page <= 1} onClick={() => go(page - 1)}
          className="flex size-11 items-center justify-center rounded-xl border border-line bg-surface disabled:opacity-40">
          <ChevronRight className="size-5" />
        </button>
        {pageWindow(page, totalPages).map((p, i) =>
          p === null ? (
            <span key={`gap-${i}`} className="w-6 text-center text-muted">…</span>
          ) : (
            <button key={p} type="button" onClick={() => go(p)} aria-current={p === page ? 'page' : undefined}
              aria-label={`صفحة ${p}`}
              className={cn('tabular flex h-11 min-w-11 items-center justify-center rounded-xl px-2 text-[15px] font-semibold',
                p === page ? 'bg-brand-700 text-white' : 'border border-line bg-surface hover:bg-canvas')}>
              {p}
            </button>
          ),
        )}
        <button type="button" aria-label="الصفحة التالية" disabled={page >= totalPages} onClick={() => go(page + 1)}
          className="flex size-11 items-center justify-center rounded-xl border border-line bg-surface disabled:opacity-40">
          <ChevronLeft className="size-5" />
        </button>
      </div>
      {from && to && totalCount ? (
        <p className="tabular text-center text-sm text-muted">عرض {from}–{to} من {totalCount}</p>
      ) : (
        <p className="tabular text-center text-sm text-muted">صفحة {page} من {totalPages}</p>
      )}
    </nav>
  );
}

export function FilterChips<T extends string>({
  options,
  value,
  onChange,
}: {
  options: { value: T; label: string }[];
  value: T;
  onChange: (value: T) => void;
}) {
  return (
    <div className="-mx-4 flex gap-2 overflow-x-auto px-4 pb-1 [scrollbar-width:none]" role="radiogroup">
      {options.map((o) => (
        <button
          key={o.value}
          type="button"
          role="radio"
          aria-checked={value === o.value}
          onClick={() => onChange(o.value)}
          className={cn(
            'h-10 shrink-0 rounded-full border px-4 text-sm font-medium transition',
            value === o.value ? 'border-brand-700 bg-brand-700 text-white' : 'border-line bg-surface text-ink',
          )}
        >
          {o.label}
        </button>
      ))}
    </div>
  );
}
