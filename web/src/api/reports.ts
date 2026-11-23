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
  /** Discounts suppliers gave on whole purchase invoices («خصم مكتسب من الموردين»): added to the net profit. */
  purchaseDiscounts?: number | null;
  expensesByCategory: NamedAmount[] | null;
  cashIn: number | null;
  cashOut: number | null;
  netCashFlow: number | null;
  inByCategory: NamedAmount[] | null;
  outByCategory: NamedAmount[] | null;
  /** openingBalance + in − out = closingBalance. */
  accounts: { accountId: number; name: string; in: number; out: number; closingBalance: number; openingBalance: number }[] | null;
  newDebt: number | null;
  collections: number | null;
  pendingTransferCount: number;
  pendingTransferAmount: number | null;
  receivables: number | null;
  payables: number | null;
  inventoryValue: number | null;
}

export interface BalanceLine {
  name: string;
  amount: number;
  /** Where the figure comes from in the app. */
  link: string | null;
}

export interface BalanceSection {
  title: string;
  lines: BalanceLine[];
  total: number;
}

/** «الميزانية العمومية» at the end of a day; difference = assets − liabilities − equity, shown as it is. */
export interface BalanceSheet {
  asOf: string;
  yearStart: string;
  assets: BalanceSection[];
  totalAssets: number;
  liabilities: BalanceSection[];
  totalLiabilities: number;
  equity: BalanceSection[];
  totalEquity: number;
  difference: number;
  yearProfit: BalanceLine[];
  notRecorded: string[];
}

/** «مدفوعة» · «جزئي» · «آجل» · «بانتظار التحقق» (what is left is a transfer not confirmed yet). */
export type InvoiceStatus = 'مدفوعة' | 'جزئي' | 'آجل' | 'بانتظار التحقق';

export interface InvoicePaymentPart {
  account: string;
  amount: number;
}

export interface InvoiceReportRow {
  id: number;
  number: string;
  date: string;
  customerName: string;
  isWalkIn: boolean;
  salesperson: string;
  total: number;
  returned: number;
  paid: number;
  remaining: number;
  pendingTransfers: number;
  status: InvoiceStatus;
  /** Null without «عرض الأرباح». */
  profit: number | null;
  payments: InvoicePaymentPart[];
}

export type InvoiceReportFilter = 'All' | 'Owed' | 'Paid' | 'Pending';

/** Totals over the whole period, totals of what the filter/search kept (matched…), and one page of rows. */
export interface InvoiceReport {
  from: string;
  to: string;
  count: number;
  total: number;
  returned: number;
  paid: number;
  remaining: number;
  pendingTransfers: number;
  profit: number | null;
  byAccount: InvoicePaymentPart[];
  matched: number;
  matchedTotal: number;
  matchedReturned: number;
  matchedPaid: number;
  matchedRemaining: number;
  matchedProfit: number | null;
  rows: InvoiceReportRow[];
  page: number;
  pageSize: number;
  totalPages: number;
}

export type InvoiceReportQuery = Range & { filter?: InvoiceReportFilter; search?: string; page?: number; pageSize?: number };

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
  /** Quantity × average cost. */
  value: number;
  /** Quantity × retail price. */
  saleValue?: number;
  /** Its general category («الصنف العام»); null for none. */
  groupId?: number | null;
  groupName?: string | null;
}

export type ProductSort = 'Revenue' | 'Quantity' | 'Profit';
type Range = { from?: string; to?: string };

/** «حركة الفلوس»: storeNet = received + otherIncome − supplierPayments − expenses − otherPayments; remaining = storeNet − ownerWithdrawals. */
export interface CashSummary {
  from: string;
  to: string;
  received: number;
  otherIncome: number;
  supplierPayments: number;
  expenses: number;
  otherPayments: number;
  storeNet: number;
  ownerWithdrawals: number;
  remaining: number;
  capitalAdded: number;
}

export const reportsApi = {
  cashSummary: (r: Range) => api<CashSummary>('/reports/cash-summary', { query: r }),
  period: (r: Range) => api<PeriodReport>('/reports/period', { query: r }),
  invoices: (q: InvoiceReportQuery) => api<InvoiceReport>('/reports/invoices', { query: q }),
  /** Every invoice the filter and search keep (not one page). */
  exportInvoices: (q: InvoiceReportQuery) => apiDownload('/reports/invoices/export', q, 'invoices.xlsx'),
  balanceSheet: (date?: string) => api<BalanceSheet>('/reports/balance-sheet', { query: { date } }),
  daily: (r: Range) => api<DailyRow[]>('/reports/daily', { query: r }),
  products: (r: Range, sort: ProductSort, take = 15, categoryId?: number) => api<ProductRow[]>('/reports/products', { query: { ...r, sort, take, categoryId } }),
  categories: (r: Range) => api<GroupRow[]>('/reports/categories', { query: r }),
  salespeople: (r: Range) => api<GroupRow[]>('/reports/salespeople', { query: r }),
  receivables: () => api<BalanceRow[]>('/reports/receivables'),
  payables: () => api<BalanceRow[]>('/reports/payables'),
  inventoryValue: () => api<InventoryValueRow[]>('/reports/inventory-value'),
  exportExcel: (report: string, r: Range) => apiDownload(`/reports/${report}/export`, r, `${report}.xlsx`),
};
