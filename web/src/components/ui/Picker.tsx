import { useState, type ReactNode } from 'react';
import { useQuery, keepPreviousData } from '@tanstack/react-query';
import { ChevronDown, X } from 'lucide-react';
import { cn } from '@/lib/cn';
import { useDebouncedValue } from '@/lib/hooks';
import { errorMessage } from '@/lib/forms';
import { Sheet } from './Sheet';
import { EmptyState, ErrorState, SearchInput, Spinner } from './Feedback';

interface PickerProps<T> {
  /** Text shown in the closed field; null shows the placeholder. */
  valueLabel: string | null;
  placeholder: string;
  title: string;
  queryKey: string;
  search: (q: string, signal: AbortSignal) => Promise<T[]>;
  getKey: (item: T) => string | number;
  renderItem: (item: T) => ReactNode;
  onSelect: (item: T) => void;
  onClear?: () => void;
  invalid?: boolean;
  id?: string;
  describedBy?: string;
  /** Extra content under the results; as a function it receives the typed text, e.g. «إضافة صنف جديد باسم …». */
  footer?: ReactNode | ((query: string, select: (item: T) => void) => ReactNode);
}

/** Searchable select: a field that opens a sheet with server-side search as you type. */
export function Picker<T>(props: PickerProps<T>) {
  const [open, setOpen] = useState(false);
  return (
    <>
      <div className="relative">
        <button
          id={props.id}
          type="button"
          aria-describedby={props.describedBy}
          aria-invalid={props.invalid || undefined}
          onClick={() => setOpen(true)}
          className={cn(
            'flex h-12 w-full items-center justify-between gap-2 rounded-xl border bg-surface ps-3.5 pe-3 text-start',
            props.invalid ? 'border-danger' : 'border-line',
          )}
        >
          <span className={cn('truncate', !props.valueLabel && 'text-muted/70')}>{props.valueLabel ?? props.placeholder}</span>
          {!(props.valueLabel && props.onClear) && <ChevronDown className="size-5 shrink-0 text-muted" />}
        </button>
        {props.valueLabel && props.onClear && (
          <button
            type="button"
            aria-label="إزالة الاختيار"
            onClick={props.onClear}
            className="absolute end-1 top-1/2 flex size-10 -translate-y-1/2 items-center justify-center rounded-lg text-muted hover:bg-black/5"
          >
            <X className="size-4" />
          </button>
        )}
      </div>
      {open && (
        <PickerSheet
          {...props}
          onSelect={(item) => {
            props.onSelect(item);
            setOpen(false);
          }}
          onClose={() => setOpen(false)}
        />
      )}
    </>
  );
}

export function PickerSheet<T>({
  title,
  placeholder,
  queryKey,
  search,
  getKey,
  renderItem,
  onSelect,
  onClose,
  footer,
}: Omit<PickerProps<T>, 'valueLabel'> & { onClose: () => void }) {
  const [q, setQ] = useState('');
  const term = useDebouncedValue(q.trim(), 250);
  const results = useQuery({
    queryKey: ['picker', queryKey, term],
    queryFn: ({ signal }) => search(term, signal),
    placeholderData: keepPreviousData,
  });

  return (
    <Sheet open onClose={onClose} title={title} size="lg">
      <div className="sticky top-0 z-10 -mx-4 bg-surface px-4 pb-3">
        <SearchInput value={q} onChange={setQ} placeholder={placeholder} autoFocus />
      </div>
      {results.isPending ? (
        <Spinner />
      ) : results.isError ? (
        <ErrorState message={errorMessage(results.error)} onRetry={() => void results.refetch()} />
      ) : results.data.length === 0 ? (
        <EmptyState title={term ? `لا توجد نتائج لـ «${term}»` : 'لا توجد عناصر'} />
      ) : (
        <ul className={cn('divide-y divide-line', results.isFetching && 'opacity-60')}>
          {results.data.map((item) => (
            <li key={getKey(item)}>
              <button type="button" onClick={() => onSelect(item)} className="w-full py-3 text-start hover:bg-canvas">
                {renderItem(item)}
              </button>
            </li>
          ))}
        </ul>
      )}
      {typeof footer === 'function' ? footer(q.trim(), onSelect) : footer}
    </Sheet>
  );
}
