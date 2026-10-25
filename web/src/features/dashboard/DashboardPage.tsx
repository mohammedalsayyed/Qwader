import { Link, useNavigate } from 'react-router';
import { useQuery } from '@tanstack/react-query';
import { searchApi } from '@/api/catalog';
import { salesApi } from '@/api/sales';
import { Stat } from '@/components/ui/Layout';
import { InstallBanner } from '@/app/pwa';
import {
  BarChart3,
  HandCoins,
  PackagePlus,
  ReceiptText,
  Search,
  ShoppingCart,
  Wallet,
  Warehouse,
  type LucideIcon,
} from 'lucide-react';
import { useAuth } from '@/auth/AuthProvider';
import { P, type Permission } from '@/auth/permissions';
import { formatDate, formatMoney } from '@/lib/format';

interface QuickAction {
  to: string;
  label: string;
  icon: LucideIcon;
  anyOf: Permission[];
  primary?: boolean;
}

// Ordered by how often the store uses them; «بيع جديد» is the primary action.
const QUICK_ACTIONS: QuickAction[] = [
  { to: '/pos', label: 'بيع جديد', icon: ShoppingCart, anyOf: [P.SalesCreate], primary: true },
  { to: '/products?focus=1', label: 'البحث عن منتج', icon: Search, anyOf: [P.ProductsView] },
  { to: '/products?add=1', label: 'إضافة منتج', icon: PackagePlus, anyOf: [P.ProductsManage] },
  { to: '/receipts/new', label: 'سند قبض', icon: HandCoins, anyOf: [P.VouchersCreate] },
  { to: '/payments/new', label: 'سند صرف', icon: ReceiptText, anyOf: [P.VouchersCreate] },
  { to: '/expenses/new', label: 'إضافة مصروف', icon: Wallet, anyOf: [P.ExpensesManage] },
  { to: '/inventory', label: 'عرض المخزون', icon: Warehouse, anyOf: [P.InventoryView] },
  { to: '/reports', label: 'التقارير', icon: BarChart3, anyOf: [P.ReportsSales, P.ReportsProfit, P.ReportsFinancial] },
];

export function DashboardPage() {
  const { user, can } = useAuth();
  const actions = QUICK_ACTIONS.filter((a) => can(...a.anyOf));
  const navigate = useNavigate();
  const stockAlerts = can(P.InventoryView, P.ProductsView);
  const notifications = useQuery({ queryKey: ['notifications'], queryFn: searchApi.notifications, enabled: stockAlerts });
  const [primary, ...rest] = actions;

  return (
    <div className="space-y-5">
      <div>
        <p className="text-sm text-muted">{formatDate(new Date())}</p>
        <h1 className="text-xl font-bold sm:text-2xl">أهلًا {user?.fullName}</h1>
      </div>

      <InstallBanner />

      {can(P.SalesCreate, P.SalesViewAll, P.ReportsSales) && <TodayStats />}

      {primary?.primary && (
        <Link
          to={primary.to}
          className="flex h-18 items-center justify-center gap-3 rounded-2xl bg-brand-700 text-lg font-bold text-white shadow-md active:bg-brand-900"
        >
          <primary.icon className="size-7" aria-hidden />
          {primary.label}
        </Link>
      )}

      <section aria-labelledby="quick-actions">
        <h2 id="quick-actions" className="mb-2 text-sm font-semibold text-muted">
          إجراءات سريعة
        </h2>
        <div className="grid grid-cols-3 gap-2 sm:grid-cols-4 lg:grid-cols-7">
          {(primary?.primary ? rest : actions).map((a) => (
            <Link
              key={a.to}
              to={a.to}
              className="flex min-h-24 flex-col items-center justify-center gap-2 rounded-2xl border border-line bg-surface p-2 text-center text-[13px] font-semibold leading-tight hover:border-brand-200 active:bg-brand-50"
            >
              <a.icon className="size-7 text-brand-700" aria-hidden />
              {a.label}
            </Link>
          ))}
        </div>
      </section>

      {stockAlerts && notifications.data && (
        <div className="grid grid-cols-2 gap-2">
          <Stat label="أوشكت على النفاد" value={notifications.data.lowStock} tone={notifications.data.lowStock ? 'warning' : 'default'}
            hint="منتج تحت حد التنبيه" onClick={() => navigate('/products?stock=Low')} />
          <Stat label="نفدت من المخزون" value={notifications.data.outOfStock} tone={notifications.data.outOfStock ? 'danger' : 'default'}
            hint="منتج رصيده صفر" onClick={() => navigate('/products?stock=Out')} />
        </div>
      )}

    </div>
  );
}

/** Today's figures straight from posted invoices and account movements. Staff without report access see their own sales. */
function TodayStats() {
  const navigate = useNavigate();
  const summary = useQuery({ queryKey: ['sales-summary', 'today'], queryFn: () => salesApi.summary(), refetchInterval: 60_000 });
  if (summary.isError) return null;
  const s = summary.data;
  const value = (v: number | null | undefined) => (s ? formatMoney(v ?? 0) : '…');

  return (
    <section aria-labelledby="today">
      <h2 id="today" className="mb-2 text-sm font-semibold text-muted">
        اليوم {s?.ownSalesOnly && '— مبيعاتك أنت'}
      </h2>
      <div className="grid grid-cols-2 gap-2 lg:grid-cols-4">
        <Stat label="مبيعات اليوم (صافي)" value={value(s?.netSales)} onClick={() => navigate('/sales')}
          hint={s && s.returns > 0 ? `بعد مرتجعات ${formatMoney(s.returns)}` : undefined} />
        <Stat label="عدد الفواتير" value={s ? s.invoiceCount : '…'} onClick={() => navigate('/sales')} />
        <Stat label="المبالغ المستلمة" value={value(s?.received)}
          hint={s?.receivedByAccount.slice(0, 2).map((a) => `${a.accountName} ${formatMoney(a.amount)}`).join(' · ') || undefined} />
        {s?.grossProfit != null ? (
          <Stat label="ربح اليوم" value={value(s.grossProfit)} tone={s.grossProfit < 0 ? 'danger' : 'success'} hint="قبل المصروفات" />
        ) : (
          <Stat label="ديون جديدة اليوم" value={value(s?.newDebt)} tone={s && s.newDebt > 0 ? 'warning' : 'default'} />
        )}
      </div>
    </section>
  );
}
