import { Link, useNavigate } from 'react-router';
import { useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { DateRangeFilter, resolveRange, type DateRangeValue, type RangePreset } from '@/components/ui/DateRange';
import { searchApi } from '@/api/catalog';
import { reportsApi, type CashSummary } from '@/api/reports';
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
import { LatestNotifications } from '@/features/notifications/NotificationFeed';
import { P, type Permission } from '@/auth/permissions';
import { formatDate, formatMoney, localToday } from '@/lib/format';
import { Profit } from '@/features/profit/ProfitLock';

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
  { to: '/expenses?new=1', label: 'إضافة مصروف', icon: Wallet, anyOf: [P.ExpensesManage] },
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
          className="flex h-18 items-center justify-center gap-3 rounded-2xl bg-accent-600 text-lg font-bold text-white shadow-md hover:bg-accent-700 active:bg-accent-700"
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

      <LatestNotifications />

      {stockAlerts && notifications.data && (
        <div className="grid grid-cols-2 gap-2">
          <Stat label="أوشكت على النفاد" value={notifications.data.lowStock} tone={notifications.data.lowStock ? 'warning' : 'default'}
            hint="منتج تحت حد التنبيه" onClick={() => navigate('/products?stock=Low')} />
          <Stat label="نفدت من المخزون" value={notifications.data.outOfStock} tone={notifications.data.outOfStock ? 'danger' : 'default'}
            hint="منتج رصيده صفر" onClick={() => navigate('/products?stock=Out')} />
          {notifications.data.pendingTransfers > 0 && (
            <Stat label="حوالات بانتظار التحقق" value={notifications.data.pendingTransfers} tone="warning" hint="اضغط للمراجعة" onClick={() => navigate('/transfers')} />
          )}
        </div>
      )}

    </div>
  );
}

/**
 * Today's figures. With profit reports: المبيعات (الإيرادات) → إجمالي الربح (− سعر الشراء) → المصروفات → صافي الربح.
 * Staff without report access see their own sales only.
 */
const PERIODS: RangePreset[] = ['today', 'yesterday', 'last7', 'thisMonth', 'custom', 'all'];
const PERIOD_TITLES: Partial<Record<RangePreset, string>> = {
  today: 'اليوم', yesterday: 'أمس', last7: 'آخر 7 أيام', thisMonth: 'هذا الشهر', custom: 'الفترة المحددة', all: 'منذ البداية',
};

/** «الكل»: the reports accept up to 10 years, which covers everything this store has recorded. */
function rangeBounds(range: DateRangeValue): { from?: string; to?: string } {
  if (range.preset !== 'all') return { from: range.from, to: range.to };
  const today = localToday();
  const from = new Date(new Date(`${today}T00:00:00Z`).getTime() - 3650 * 86_400_000).toISOString().slice(0, 10);
  return { from, to: today };
}

function TodayStats() {
  const navigate = useNavigate();
  const { can } = useAuth();
  // Profit figures need «عرض الأرباح» (and a report permission to read the period).
  const withProfit = can(P.ProfitView) && can(P.ReportsSales, P.ReportsProfit, P.ReportsFinancial);
  // «اليوم» unless the user picks another period; not remembered, so the home screen always opens on today.
  const [range, setRange] = useState<DateRangeValue>({ preset: 'today', ...resolveRange('today') });
  const { from, to } = rangeBounds(range);
  const ready = range.preset !== 'custom' || (!!from && !!to);
  const summary = useQuery({ queryKey: ['sales-summary', from, to], queryFn: () => salesApi.summary(from, to), refetchInterval: 60_000, enabled: ready,
    placeholderData: keepPreviousData });
  const period = useQuery({ queryKey: ['report-period', from, to], queryFn: () => reportsApi.period({ from, to }), refetchInterval: 60_000,
    enabled: withProfit && ready, placeholderData: keepPreviousData });
  if (summary.isError) return null;
  const s = summary.data;
  const r = withProfit ? period.data : undefined;
  const value = (v: number | null | undefined) => (s ? formatMoney(v ?? 0) : '…');
  const money = (v: number | null | undefined) => (r ? formatMoney(v ?? 0) : '…');
  const tone = (v: number | null | undefined) => (v != null && v < 0 ? 'danger' : 'success');

  return (
    <section aria-labelledby="today">
      <h2 id="today" className="mb-2 text-sm font-semibold text-muted">
        {PERIOD_TITLES[range.preset]} {s?.ownSalesOnly && '— مبيعاتك أنت'}
      </h2>
      <div className="mb-3">
        <DateRangeFilter value={range} onChange={setRange} presets={PERIODS} />
      </div>
      {withProfit && (
        <div className="mb-2 grid grid-cols-2 gap-2 lg:grid-cols-4">
          <Stat label="المبيعات (الإيرادات)" value={money(r?.netSales)} onClick={() => navigate('/sales')}
            hint={r ? `${r.invoiceCount} فاتورة${r.returns > 0 ? ` · بعد مرتجعات ${formatMoney(r.returns)}` : ''}` : undefined} />
          <Stat label="إجمالي الربح" value={<Profit>{money(r?.grossProfit)}</Profit>} tone={tone(r?.grossProfit)} hint="المبيعات − سعر الشراء" />
          <Stat label="المصروفات" value={money(r?.totalExpenses ?? r?.expenses)} tone={r && (r.totalExpenses ?? 0) > 0 ? 'warning' : 'default'}
            hint="رواتب، أعطال، مصاريف تشغيلية" onClick={() => navigate('/expenses')} />
          <Stat label="صافي الربح" value={<Profit>{money(r?.netOperatingProfit)}</Profit>} tone={tone(r?.netOperatingProfit)} hint="إجمالي الربح − المصروفات"
            onClick={() => navigate('/reports')} />
        </div>
      )}
      <div className="grid grid-cols-2 gap-2 lg:grid-cols-4">
        {!withProfit && (
          <Stat label="المبيعات (الإيرادات)" value={value(s?.netSales)} onClick={() => navigate('/sales')}
            hint={s && s.returns > 0 ? `بعد مرتجعات ${formatMoney(s.returns)}` : undefined} />
        )}
        <Stat label="عدد الفواتير" value={s ? s.invoiceCount : '…'} onClick={() => navigate('/sales')} />
        <Stat label="المبالغ المستلمة" value={value(s?.received)}
          hint={s?.receivedByAccount.slice(0, 2).map((a) => `${a.accountName} ${formatMoney(a.amount)}`).join(' · ') || undefined} />
        <Stat label={range.preset === 'today' ? 'ديون جديدة اليوم' : 'ديون جديدة'} value={value(s?.newDebt)} tone={s && s.newDebt > 0 ? 'warning' : 'default'} />
      </div>
      {can(P.DashboardCashFlow) && <CashFlow from={from} to={to} ready={ready} />}
    </section>
  );
}

/**
 * «حركة الفلوس»: money that really came in and went out in the period — not profit (credit sales are not in it until
 * collected, and goods bought are still on the shelf). المقبوض − المشتريات − مصاريف المحل = صافي المحل؛ − المسحوبات الشخصية = الباقي.
 */
function CashFlow({ from, to, ready }: { from?: string; to?: string; ready: boolean }) {
  const navigate = useNavigate();
  const q = useQuery({ queryKey: ['cash-summary', from, to], queryFn: () => reportsApi.cashSummary({ from, to }), refetchInterval: 60_000, enabled: ready,
    placeholderData: keepPreviousData });
  if (q.isError) return null;
  const c: CashSummary | undefined = q.data;
  const m = (v: number | undefined) => (c ? formatMoney(v ?? 0) : '…');
  const row = (label: string, v: number | undefined, sign: '+' | '−' | '', hint?: string, to?: string) => (
    <li key={label}>
      <button type="button" disabled={!to} onClick={() => to && navigate(to)}
        className="flex w-full items-center justify-between gap-3 px-4 py-2.5 text-start enabled:hover:bg-brand-50 disabled:cursor-default">
        <span>
          <span className="font-semibold">{label}</span>
          {hint && <span className="block text-xs text-muted">{hint}</span>}
        </span>
        <span className="tabular shrink-0 font-semibold" dir="ltr">{sign && <span className="text-muted">{sign} </span>}{m(v)}</span>
      </button>
    </li>
  );
  const total = (label: string, v: number | undefined, hint: string) => (
    <li className={`flex items-center justify-between gap-3 px-4 py-3 ${v != null && v < 0 ? 'bg-danger/10 text-danger' : 'bg-success/10 text-success'}`}>
      <span>
        <span className="font-bold">{label}</span>
        <span className="block text-xs opacity-80">{hint}</span>
      </span>
      <span className="tabular shrink-0 text-lg font-bold" dir="ltr">{m(v)}</span>
    </li>
  );

  return (
    <div className="mt-3 overflow-hidden rounded-card border border-line bg-surface">
      <h3 className="border-b border-line px-4 py-2.5 text-sm font-bold">حركة الفلوس</h3>
      <ul className="divide-y divide-line text-sm">
        {row('المقبوض من المبيعات', c?.received, '', 'نقدي وتحصيل ديون، بعد المرتجعات', '/reports/invoices')}
        {!!c?.otherIncome && row('إيرادات أخرى', c.otherIncome, '+')}
        {row('المدفوع على المشتريات', c?.supplierPayments, '−', 'للموردين، بعد ما رجّعوه', '/purchases')}
        {row('مصاريف المحل', c?.expenses, '−', 'رواتب وسلف ومصاريف تشغيل', '/expenses')}
        {!!c?.otherPayments && row('مدفوعات أخرى', c.otherPayments, '−', undefined, '/payments')}
        {total('صافي المحل', c?.storeNet, 'اللي جابه المحل بهالفترة')}
        {row('المسحوبات الشخصية', c?.ownerWithdrawals, '−', 'مصاريف صاحب العمل', '/owner-expenses')}
        {total('الباقي', c?.remaining, 'الفرق بالصندوق والبنوك بهالفترة')}
      </ul>
      {!!c?.capitalAdded && (
        <p className="border-t border-line px-4 py-2 text-xs text-muted">مش محسوب فيها: رأس مال مضاف {formatMoney(c.capitalAdded)}</p>
      )}
    </div>
  );
}
