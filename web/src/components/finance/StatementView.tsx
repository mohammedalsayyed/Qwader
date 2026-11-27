import { useState } from 'react';
import { Link } from 'react-router';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { FileSpreadsheet, Printer } from 'lucide-react';
import type { Statement } from '@/api/stock';
import { Button } from '@/components/ui/Button';
import { DateRangeFilter, type DateRangeValue } from '@/components/ui/DateRange';
import { EmptyState, ErrorState, Spinner } from '@/components/ui/Feedback';
import { cn } from '@/lib/cn';
import { errorMessage } from '@/lib/forms';
import { formatDate, formatMoney } from '@/lib/format';

function sourceLink(sourceType: string, sourceId: number | null): string | null {
  if (!sourceId) return null;
  if (sourceType === 'PurchaseInvoice') return `/purchases/${sourceId}`;
  if (sourceType === 'SaleInvoice') return `/sales/${sourceId}`;
  return null;
}

/** Running-balance statement for any party (supplier now; customers and accounts later). */
export function StatementView({
  queryKey,
  load,
  debitLabel,
  creditLabel,
  balanceHint,
  increases = 'credit',
  exportExcel,
  exportExcelSheet,
}: {
  queryKey: unknown[];
  load: (from?: string, to?: string) => Promise<Statement>;
  debitLabel: string;
  creditLabel: string;
  balanceHint?: string;
  /** The side that adds to the balance: a customer's purchases (debit), what the store owes a supplier (credit). */
  increases?: 'debit' | 'credit';
  /** «Excel»: the statement of the period chosen as a workbook. */
  exportExcel?: (from?: string, to?: string) => Promise<void>;
  /** A second file: everything on one printed sheet under the store's name. */
  exportExcelSheet?: (from?: string, to?: string) => Promise<void>;
}) {
  const [exporting, setExporting] = useState<'book' | 'sheet' | null>(null);
  const run = async (kind: 'book' | 'sheet', exporter: (from?: string, to?: string) => Promise<void>) => {
    setExporting(kind);
    setExportError(null);
    try { await exporter(range.from, range.to); } catch (e) { setExportError(errorMessage(e)); } finally { setExporting(null); }
  };
  const [exportError, setExportError] = useState<string | null>(null);
  const [range, setRange] = useState<DateRangeValue>({ preset: 'all' });
  const query = useQuery({
    queryKey: [...queryKey, range.from, range.to],
    queryFn: () => load(range.from, range.to),
    placeholderData: keepPreviousData,
  });

  return (
    <div className="space-y-3">
      <div className="space-y-2 print:hidden">
        <DateRangeFilter value={range} onChange={setRange} />
        {(exportExcel || exportExcelSheet) && (
          <div className="flex flex-wrap gap-2">
            {exportExcelSheet && (
              <Button variant="outline" size="sm" loading={exporting === 'sheet'} onClick={() => void run('sheet', exportExcelSheet)}>
                <FileSpreadsheet className="size-4" /> Excel (ورقة واحدة للطباعة)
              </Button>
            )}
            {exportExcel && (
              <Button variant="outline" size="sm" loading={exporting === 'book'} onClick={() => void run('book', exportExcel)}>
                <FileSpreadsheet className="size-4" /> Excel (عدة أوراق)
              </Button>
            )}
          </div>
        )}
        {exportError && <p role="alert" className="text-sm text-danger">{exportError}</p>}
      </div>
      {query.isPending ? (
        <Spinner />
      ) : query.isError ? (
        <ErrorState message={errorMessage(query.error)} onRetry={() => void query.refetch()} />
      ) : (
        <div className={cn('space-y-3', query.isFetching && 'opacity-60')}>
          <h2 className="hidden text-lg font-bold print:block">
            كشف حساب: {query.data.partyName} {query.data.from ? `من ${formatDate(query.data.from)}` : ''} إلى {formatDate(query.data.to)}
          </h2>
          <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
            <Summary label="الرصيد الافتتاحي" value={query.data.openingBalance} />
            <Summary label={debitLabel} value={query.data.totalDebit} />
            <Summary label={creditLabel} value={query.data.totalCredit} />
            <Summary label="الرصيد الختامي" value={query.data.closingBalance} strong />
          </div>
          {balanceHint && <p className="text-xs text-muted">{balanceHint}</p>}

          {query.data.lines.length === 0 ? (
            <EmptyState title="لا توجد حركات في هذه الفترة" />
          ) : (
            <ul className="divide-y divide-line rounded-card border border-line bg-surface">
              {query.data.lines.map((l, i) => {
                const link = sourceLink(l.sourceType, l.sourceId);
                return (
                  <li key={i} className="flex items-start justify-between gap-3 p-3 break-inside-avoid">
                    <div className="min-w-0">
                      <p className="font-semibold">{l.type}</p>
                      <p className="text-xs text-muted">
                        {formatDate(l.date)}
                        {l.reference && <> · {link ? <Link to={link} className="ltr inline-block text-brand-700 underline">{l.reference}</Link> : <span className="ltr inline-block">{l.reference}</span>}</>}
                      </p>
                      {l.description && <p className="text-sm">{l.description}</p>}
                    </div>
                    <div className="shrink-0 text-end">
                      {/* What adds to the balance shows «+», what takes from it «−». */}
                      {(increases === 'credit' ? l.credit : l.debit) > 0 && (
                        <p className="tabular font-semibold text-danger">+{formatMoney(increases === 'credit' ? l.credit : l.debit)}</p>
                      )}
                      {(increases === 'credit' ? l.debit : l.credit) > 0 && (
                        <p className="tabular font-semibold text-success">−{formatMoney(increases === 'credit' ? l.debit : l.credit)}</p>
                      )}
                      <p className="tabular text-xs text-muted">الرصيد: {formatMoney(l.balance)}</p>
                    </div>
                  </li>
                );
              })}
            </ul>
          )}
          <Button variant="outline" className="print:hidden" onClick={() => window.print()}>
            <Printer className="size-4" /> طباعة الكشف
          </Button>
        </div>
      )}
    </div>
  );
}

function Summary({ label, value, strong }: { label: string; value: number; strong?: boolean }) {
  return (
    <div className="rounded-xl border border-line bg-surface p-3">
      <p className="text-xs text-muted">{label}</p>
      <p className={cn('tabular mt-0.5', strong ? 'text-lg font-bold' : 'font-semibold')}>{formatMoney(value)}</p>
    </div>
  );
}
