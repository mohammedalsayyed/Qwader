import { lazy, Suspense, type ComponentType, type ReactNode } from 'react';
import { createBrowserRouter, Navigate, Outlet, useLocation } from 'react-router';
import { useAuth } from '@/auth/AuthProvider';
import { OfflineBanner } from '@/app/pwa';
import { P, type Permission } from '@/auth/permissions';
import { AppLayout } from '@/components/layout/AppLayout';
import { ErrorState, Spinner } from '@/components/ui/Feedback';
import { LoginPage } from '@/features/auth/LoginPage';
import { ChangePasswordPage } from '@/features/auth/ChangePasswordPage';
import { DashboardPage } from '@/features/dashboard/DashboardPage';
import { ForbiddenPage, NotFoundPage } from '@/features/common/Placeholders';
// Pages load on demand so the first visit on a slow connection only downloads what it shows.
const page = <T, K extends keyof T>(load: () => Promise<T>, name: K) =>
  lazy(() => load().then((m) => ({ default: m[name] as ComponentType<any> })));
const UsersPage = page(() => import('@/features/users/UsersPage'), 'UsersPage');
const SettingsPage = page(() => import('@/features/settings/SettingsPage'), 'SettingsPage');
const CategoriesPage = page(() => import('@/features/catalog/CategoriesPage'), 'CategoriesPage');
const ProductsPage = page(() => import('@/features/catalog/ProductsPage'), 'ProductsPage');
const ProductDetailPage = page(() => import('@/features/catalog/ProductDetailPage'), 'ProductDetailPage');
const ProductFormPage = page(() => import('@/features/catalog/ProductFormPage'), 'ProductFormPage');
const InventoryPage = page(() => import('@/features/inventory/InventoryPage'), 'InventoryPage');
const StockCountPage = page(() => import('@/features/inventory/StockCountPage'), 'StockCountPage');
const SuppliersPage = page(() => import('@/features/purchasing/SuppliersPage'), 'SuppliersPage');
const SupplierDetailPage = page(() => import('@/features/purchasing/SuppliersPage'), 'SupplierDetailPage');
const PurchasesPage = page(() => import('@/features/purchasing/PurchasesPage'), 'PurchasesPage');
const PurchaseFormPage = page(() => import('@/features/purchasing/PurchaseFormPage'), 'PurchaseFormPage');
const PurchaseDetailPage = page(() => import('@/features/purchasing/PurchaseDetailPage'), 'PurchaseDetailPage');
const PosPage = page(() => import('@/features/pos/PosPage'), 'PosPage');
const NotificationsPage = page(() => import('@/features/auth/NotificationsPage'), 'NotificationsPage');
const SalesPage = page(() => import('@/features/sales/SalesPage'), 'SalesPage');
const SaleDetailPage = page(() => import('@/features/sales/SaleDetailPage'), 'SaleDetailPage');
const CustomersPage = page(() => import('@/features/customers/CustomersPage'), 'CustomersPage');
const CustomerDetailPage = page(() => import('@/features/customers/CustomersPage'), 'CustomerDetailPage');
const AccountsPage = page(() => import('@/features/finance/AccountsPage'), 'AccountsPage');
const AccountDetailPage = page(() => import('@/features/finance/AccountsPage'), 'AccountDetailPage');
const TransfersPage = page(() => import('@/features/finance/TransfersPage'), 'TransfersPage');
const VouchersPage = page(() => import('@/features/finance/VouchersPages'), 'VouchersPage');
const NewVoucherPage = page(() => import('@/features/finance/VouchersPages'), 'NewVoucherPage');
const VoucherDetailPage = page(() => import('@/features/finance/VouchersPages'), 'VoucherDetailPage');
const ExpensesPage = page(() => import('@/features/finance/ExpensesPage'), 'ExpensesPage');
const ReportsPage = page(() => import('@/features/reports/ReportsPage'), 'ReportsPage');
const AuditLogPage = page(() => import('@/features/settings/AuditLogPage'), 'AuditLogPage');


function SessionGate() {
  const { status, user, reload } = useAuth();
  const location = useLocation();

  if (status === 'loading') return <Spinner className="min-h-dvh" />;
  if (status === 'offline')
    return (
      <div className="min-h-dvh">
        <OfflineBanner />
        <div className="pt-16">
          <ErrorState message="تعذر الوصول إلى الخادم. سيتصل النظام تلقائيًا عند عودة الإنترنت، أو اضغط إعادة المحاولة." onRetry={() => void reload()} />
        </div>
      </div>
    );
  if (status === 'anonymous') return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />;
  if (user?.mustChangePassword && location.pathname !== '/account/password') return <Navigate to="/account/password" replace />;
  return <Outlet />;
}

function AnonymousOnly({ children }: { children: ReactNode }) {
  const { status } = useAuth();
  if (status === 'loading') return <Spinner className="min-h-dvh" />;
  if (status === 'authenticated') return <Navigate to="/" replace />;
  return children;
}

function RequirePermission({ anyOf, children }: { anyOf: Permission[]; children: ReactNode }) {
  const { can } = useAuth();
  return can(...anyOf) ? children : <ForbiddenPage />;
}

/**
 * The forced, full-screen password change. Once it is done there is nothing to do here: go home.
 * (Right after saving, the guard can still see the old session for a moment and land here again;
 * sending users to the in-app change page made a successful change look like a failed one.)
 */
function PasswordRoute() {
  const { user } = useAuth();
  return user?.mustChangePassword ? <ChangePasswordPage /> : <Navigate to="/" replace />;
}

const guarded = (path: string, element: ReactNode, anyOf: Permission[]) => ({
  path,
  element: (
    <RequirePermission anyOf={anyOf}>
      <Suspense fallback={<Spinner />}>{element}</Suspense>
    </RequirePermission>
  ),
});


export const router = createBrowserRouter([
  {
    path: '/login',
    element: (
      <AnonymousOnly>
        <LoginPage />
      </AnonymousOnly>
    ),
  },
  {
    element: <SessionGate />,
    children: [
      { path: '/account/password', element: <PasswordRoute /> },
      {
        element: <AppLayout />,
        children: [
          { index: true, element: <DashboardPage /> },
          { path: 'account/change-password', element: <ChangePasswordPage /> },
          { path: 'account/notifications', element: <NotificationsPage /> },
          {
            path: 'users',
            element: (
              <RequirePermission anyOf={[P.UsersManage]}>
                <UsersPage />
              </RequirePermission>
            ),
          },
          {
            path: 'settings',
            element: (
              <RequirePermission anyOf={[P.SettingsManage]}>
                <SettingsPage />
              </RequirePermission>
            ),
          },
          guarded('categories', <CategoriesPage />, [P.ProductsView]),
          guarded('products', <ProductsPage />, [P.ProductsView]),
          guarded('products/new', <ProductFormPage />, [P.ProductsManage]),
          guarded('products/:id', <ProductDetailPage />, [P.ProductsView]),
          guarded('products/:id/edit', <ProductFormPage />, [P.ProductsManage]),
          guarded('inventory', <InventoryPage />, [P.InventoryView]),
          guarded('inventory/counts/:id', <StockCountPage />, [P.InventoryCount]),
          guarded('suppliers', <SuppliersPage />, [P.SuppliersManage, P.PurchasesView]),
          guarded('suppliers/:id', <SupplierDetailPage />, [P.SuppliersManage, P.PurchasesView]),
          guarded('purchases', <PurchasesPage />, [P.PurchasesView]),
          guarded('purchases/new', <PurchaseFormPage />, [P.PurchasesManage]),
          guarded('purchases/:id', <PurchaseDetailPage />, [P.PurchasesView]),
          guarded('purchases/:id/edit', <PurchaseFormPage />, [P.PurchasesManage]),
          guarded('pos', <PosPage />, [P.SalesCreate]),
          guarded('sales', <SalesPage />, [P.SalesCreate, P.SalesViewAll]),
          guarded('sales/:id', <SaleDetailPage />, [P.SalesCreate, P.SalesViewAll]),
          guarded('customers', <CustomersPage />, [P.CustomersManage, P.SalesCreate]),
          guarded('customers/:id', <CustomerDetailPage />, [P.CustomersManage, P.SalesCreate]),
          guarded('accounts', <AccountsPage />, [P.AccountsView, P.AccountsManage]),
          guarded('accounts/:id', <AccountDetailPage />, [P.AccountsView, P.AccountsManage]),
          guarded('transfers', <TransfersPage />, [P.TransfersCreate, P.TransfersReview]),
          guarded('receipts', <VouchersPage kind="receipt" />, [P.VouchersCreate, P.ReportsFinancial]),
          guarded('receipts/new', <NewVoucherPage kind="receipt" />, [P.VouchersCreate]),
          guarded('receipts/:id', <VoucherDetailPage kind="receipt" />, [P.VouchersCreate, P.ReportsFinancial]),
          guarded('payments', <VouchersPage kind="payment" />, [P.VouchersCreate, P.ReportsFinancial]),
          guarded('payments/new', <NewVoucherPage kind="payment" />, [P.VouchersCreate]),
          guarded('payments/:id', <VoucherDetailPage kind="payment" />, [P.VouchersCreate, P.ReportsFinancial]),
          guarded('expenses', <ExpensesPage />, [P.ExpensesManage]),
          guarded('audit', <AuditLogPage />, [P.AuditView]),
          guarded('reports', <ReportsPage />, [P.ReportsSales, P.ReportsProfit, P.ReportsFinancial]),
          { path: '*', element: <NotFoundPage /> },
        ],
      },
    ],
  },
]);
