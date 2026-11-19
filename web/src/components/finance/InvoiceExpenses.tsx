import { useState } from 'react';
import { Link } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { Plus, Truck } from 'lucide-react';
import { expensesApi } from '@/api/finance';
import { useAuth } from '@/auth/AuthProvider';
import { P } from '@/auth/permissions';
import { Button } from '@/components/ui/Button';
import { Badge, Card } from '@/components/ui/Feedback';
import { NewExpenseSheet, type ExpenseInvoice } from '@/features/finance/ExpensesPage';
import { cn } from '@/lib/cn';
import { formatDate, formatMoney } from '@/lib/format';

/**
 * «مصاريف على الفاتورة»: what was paid for one invoice — transport of a purchase's goods, a sale's delivery or packaging.
 * Each is an ordinary expense (it lowers the profit) with a payment voucher, tied to the invoice; a new one is added here.
 */
export function InvoiceExpenses({ invoice, canAdd }: { invoice: ExpenseInvoice; canAdd: boolean }) {
  const { can } = useAuth();
  const [adding, setAdding] = useState(false);
  const allowed = can(P.ExpensesManage);
  const query = useQuery({
    queryKey: ['expenses', 'invoice', invoice.kind, invoice.id],
    queryFn: () => expensesApi.list(invoice.kind === 'purchase'
      ? { purchaseInvoiceId: invoice.id, pageSize: 50, includeVoided: true } : { saleInvoiceId: invoice.id, pageSize: 50, includeVoided: true }),
    enabled: allowed,
  });
  if (!allowed) return null;
  const items = query.data?.items ?? [];
  const total = items.filter((e) => e.status === 'Posted').reduce((t, e) => t + e.amount, 0);
  if (!items.length && !canAdd) return null;

  return (
    <Card className="space-y-3 p-4 print:hidden">
      <div className="flex items-center justify-between gap-2">
        <h2 className="flex items-center gap-2 font-bold"><Truck className="size-4" /> مصاريف على الفاتورة</h2>
        {items.length > 0 && <span className="tabular font-bold">{formatMoney(total)}</span>}
      </div>
      {items.length > 0 ? (
        <ul className="divide-y divide-line">
          {items.map((e) => (
            <li key={e.id} className="flex items-start justify-between gap-3 py-2 text-sm">
              <div className="min-w-0">
                <p className="font-semibold">{e.categoryName} — {e.description}</p>
                <p className="text-xs text-muted">
                  {formatDate(e.date)} · {e.accountName} · <Link to={`/payments/${e.paymentVoucherId}`} className="ltr inline-block text-brand-700 underline">{e.paymentVoucherNumber}</Link>
                </p>
                {e.status === 'Voided' && <Badge tone="danger">ملغى</Badge>}
              </div>
              <span className={cn('tabular shrink-0 font-semibold', e.status === 'Voided' && 'text-muted line-through')}>{formatMoney(e.amount)}</span>
            </li>
          ))}
        </ul>
      ) : (
        <p className="text-sm text-muted">
          {invoice.kind === 'purchase' ? 'مثل مواصلات البضاعة أو التحميل.' : 'مثل توصيل الطلب أو التغليف.'} يُسجَّل مصروفًا من الحساب، ويظهر هنا وفي المصروفات.
        </p>
      )}
      {canAdd && <Button variant="outline" onClick={() => setAdding(true)}><Plus className="size-4" /> سند صرف لهذه الفاتورة</Button>}
      {adding && <NewExpenseSheet invoice={invoice} onClose={() => setAdding(false)} />}
    </Card>
  );
}
