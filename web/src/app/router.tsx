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
import { ComingSoonPage, ForbiddenPage, NotFoundPage } from '@/features/common/Placeholders';
// Pages load on demand so the first visit on a slow connection only downloads what it shows.
const page = <T, K extends keyof T>(load: () => Promise<T>, name: K) =>
  lazy(() => load().then((m) => ({ default: m[name] as ComponentType })));
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

/** Password change page: full-screen while forced, inside the app shell otherwise. */
function PasswordRoute() {
  const { user } = useAuth();
  return user?.mustChangePassword ? <ChangePasswordPage /> : <Navigate to="/account/change-password" replace />;
}

const guarded = (path: string, element: ReactNode, anyOf: Permission[]) => ({
  path,
  element: (
    <RequirePermission anyOf={anyOf}>
      <Suspense fallback={<Spinner />}>{element}</Suspense>
    </RequirePermission>
  ),
});

const soon = (path: string, title: string, phase: string, anyOf: Permission[]) => ({
  path: `${path}/*`,
  element: (
    <RequirePermission anyOf={anyOf}>
      <ComingSoonPage title={title} phase={phase} />
    </RequirePermission>
  ),
});

const PHASE_4 = 'المرحلة الرابعة';
const PHASE_5 = 'المرحلة الخامسة';
const PHASE_6 = 'المرحلة السادسة';

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
          soon('pos', 'نقطة البيع', PHASE_4, [P.SalesCreate]),
          soon('sales', 'فواتير المبيعات', PHASE_4, [P.SalesCreate, P.SalesViewAll]),
          soon('customers', 'العملاء', PHASE_4, [P.CustomersManage, P.SalesCreate]),
          soon('accounts', 'الحسابات المالية', PHASE_5, [P.AccountsView]),
          soon('transfers', 'الحوالات والتحويلات', PHASE_5, [P.TransfersCreate, P.TransfersReview]),
          soon('receipts', 'سندات القبض', PHASE_5, [P.VouchersCreate]),
          soon('payments', 'سندات الصرف', PHASE_5, [P.VouchersCreate]),
          soon('expenses', 'المصروفات', PHASE_5, [P.ExpensesManage]),
          soon('reports', 'التقارير', PHASE_6, [P.ReportsSales, P.ReportsProfit, P.ReportsFinancial]),
          { path: '*', element: <NotFoundPage /> },
        ],
      },
    ],
  },
]);
