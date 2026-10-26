import { api, apiDownload } from './client';

export interface NamedAmount {
  name: string;
  amount: number;
}

export interface PeriodReport {
  from: string;
  to: string;
  invoiceCount: number;
  grossSales: number;
  discounts: number;
  returns: number;
  netSales: number;
  averageInvoice: number;
  costOfSales: number | null;
  damagedReturnsCost: number | null;
  grossProfit: number | null;
  inventoryLosses: number | null;
  expenses: number | null;
  netOperatingProfit: number | null;
  /** Stock found / counted over, at cost: shown apart, not profit. */
  inventoryGains: number | null;
  /** Cost of goods sold + inventory losses + operating expenses (profit = netSales − totalExpenses). */
  totalExpenses: number | null;
  expensesByCategory: NamedAmount[] | null;
  cashIn: number | null;
  cashOut: number | null;
  netCashFlow: number | null;
  inByCategory: NamedAmount[] | null;
  outByCategory: NamedAmount[] | null;
  accounts: { accountId: number; name: string; in: number; out: number; closingBalance: number }[] | null;
  newDebt: number | null;
  collections: number | null;
  pendingTransferCount: number;
  pendingTransferAmount: number | null;
  receivables: number | null;
  payables: number | null;
  inventoryValue: number | null;
}

export interface DailyRow {
  day: string;
  invoices: number;
  netSales: number;
  grossProfit: number | null;
  expenses: number | null;
  received: number | null;
}

export interface ProductRow {
  productId: number;
  sku: string;
  name: string;
  categoryName: string;
  quantity: number;
  revenue: number;
  cost: number | null;
  profit: number | null;
  marginPercent: number | null;
}

export interface GroupRow {
  id: number;
  name: string;
  count: number;
  quantity: number;
  revenue: number;
  cost: number | null;
  profit: number | null;
}

export interface BalanceRow {
  id: number;
  code: string;
  name: string;
  phone: string | null;
  balance: number;
  lastActivity: string | null;
}

export interface InventoryValueRow {
  categoryId: number;
  categoryName: string;
  productCount: number;
  quantity: number;
  value: number;
}

export type ProductSort = 'Revenue' | 'Quantity' | 'Profit';
type Range = { from?: string; to?: string };

export const reportsApi = {
  period: (r: Range) => api<PeriodReport>('/reports/period', { query: r }),
  daily: (r: Range) => api<DailyRow[]>('/reports/daily', { query: r }),
  products: (r: Range, sort: ProductSort, take = 15) => api<ProductRow[]>('/reports/products', { query: { ...r, sort, take } }),
  categories: (r: Range) => api<GroupRow[]>('/reports/categories', { query: r }),
  salespeople: (r: Range) => api<GroupRow[]>('/reports/salespeople', { query: r }),
  receivables: () => api<BalanceRow[]>('/reports/receivables'),
  payables: () => api<BalanceRow[]>('/reports/payables'),
  inventoryValue: () => api<InventoryValueRow[]>('/reports/inventory-value'),
  exportExcel: (report: string, r: Range) => apiDownload(`/reports/${report}/export`, r, `${report}.xlsx`),
};
