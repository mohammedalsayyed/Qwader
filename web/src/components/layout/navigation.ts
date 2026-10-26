import {
  ArrowLeftRight,
  BarChart3,
  Boxes,
  FileText,
  FolderTree,
  HandCoins,
  Home,
  Landmark,
  Package,
  Receipt,
  ReceiptText,
  ScrollText,
  Settings,
  ShieldCheck,
  ShoppingCart,
  Truck,
  Users,
  Wallet,
  Warehouse,
  type LucideIcon,
} from 'lucide-react';
import { P, type Permission } from '@/auth/permissions';

export interface NavItem {
  to: string;
  label: string;
  icon: LucideIcon;
  /** Visible when the user holds any of these; empty = every signed-in user. */
  anyOf: Permission[];
  group: 'daily' | 'stock' | 'finance' | 'admin';
}

export const NAV_GROUP_LABELS: Record<NavItem['group'], string> = {
  daily: 'العمل اليومي',
  stock: 'المنتجات والمخزون',
  finance: 'المالية',
  admin: 'الإدارة',
};

export const NAV_ITEMS: NavItem[] = [
  { to: '/', label: 'لوحة التحكم', icon: Home, anyOf: [], group: 'daily' },
  { to: '/pos', label: 'نقطة البيع', icon: ShoppingCart, anyOf: [P.SalesCreate], group: 'daily' },
  { to: '/sales', label: 'فواتير المبيعات', icon: FileText, anyOf: [P.SalesCreate, P.SalesViewAll], group: 'daily' },
  { to: '/customers', label: 'العملاء', icon: Users, anyOf: [P.CustomersManage, P.SalesCreate], group: 'daily' },
  { to: '/transfers', label: 'الحوالات والتحويلات', icon: ArrowLeftRight, anyOf: [P.TransfersCreate, P.TransfersReview], group: 'daily' },

  { to: '/categories', label: 'الأصناف', icon: FolderTree, anyOf: [P.ProductsView], group: 'stock' },
  { to: '/products', label: 'المنتجات', icon: Package, anyOf: [P.ProductsView], group: 'stock' },
  { to: '/inventory', label: 'إدارة المخزون', icon: Warehouse, anyOf: [P.InventoryView], group: 'stock' },
  { to: '/purchases', label: 'المشتريات', icon: Boxes, anyOf: [P.PurchasesView], group: 'stock' },
  { to: '/suppliers', label: 'الموردون', icon: Truck, anyOf: [P.SuppliersManage, P.PurchasesView], group: 'stock' },

  { to: '/accounts', label: 'الحسابات المالية', icon: Landmark, anyOf: [P.AccountsView], group: 'finance' },
  { to: '/receipts', label: 'سندات القبض', icon: HandCoins, anyOf: [P.VouchersCreate], group: 'finance' },
  { to: '/payments', label: 'سندات الصرف', icon: ReceiptText, anyOf: [P.VouchersCreate], group: 'finance' },
  { to: '/expenses', label: 'المصروفات', icon: Wallet, anyOf: [P.ExpensesManage], group: 'finance' },
  { to: '/reports', label: 'التقارير', icon: BarChart3, anyOf: [P.ReportsSales, P.ReportsProfit, P.ReportsFinancial], group: 'finance' },

  { to: '/users', label: 'المستخدمون والصلاحيات', icon: ShieldCheck, anyOf: [P.UsersManage], group: 'admin' },
  { to: '/settings', label: 'الإعدادات', icon: Settings, anyOf: [P.SettingsManage], group: 'admin' },
  { to: '/audit', label: 'سجل التدقيق', icon: ScrollText, anyOf: [P.AuditView], group: 'admin' },
];

/** The four fixed tabs of the phone bottom bar (the fifth is «المزيد»). */
export const BOTTOM_TABS: { to: string; label: string; icon: LucideIcon; anyOf: Permission[] }[] = [
  { to: '/', label: 'الرئيسية', icon: Home, anyOf: [] },
  { to: '/pos', label: 'المبيعات', icon: Receipt, anyOf: [P.SalesCreate] },
  { to: '/products', label: 'المنتجات', icon: Package, anyOf: [P.ProductsView] },
  { to: '/inventory', label: 'المخزون', icon: Warehouse, anyOf: [P.InventoryView] },
];
