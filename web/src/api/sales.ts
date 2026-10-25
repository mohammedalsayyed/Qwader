import { api, type PagedResult } from './client';
import type { ReturnSettlement, Statement } from './stock';

export type PriceTier = 'Retail' | 'Wholesale';
export type SaleStatus = 'Posted' | 'PartiallyReturned' | 'Returned' | 'Voided';
export type ReturnCondition = 'Sellable' | 'Damaged';

export interface SaleListItem {
  id: number;
  number: string;
  date: string;
  customerName: string;
  customerPhone: string | null;
  isWalkIn: boolean;
  total: number;
  paidAmount: number;
  returnedTotal: number;
  status: SaleStatus;
  salespersonName: string;
  itemCount: number;
}

export interface SaleLine {
  id: number;
  productId: number;
  sku: string;
  productName: string;
  unitName: string;
  quantity: number;
  unitPrice: number;
  lineDiscount: number;
  lineTotal: number;
  netTotal: number;
  returnedQuantity: number;
  unitCost: number | null;
  priceOverridden: boolean;
}

export interface Sale {
  id: number;
  number: string;
  date: string;
  status: SaleStatus;
  priceTier: PriceTier;
  customerId: number;
  customerName: string;
  customerPhone: string | null;
  isWalkIn: boolean;
  grossTotal: number;
  lineDiscountTotal: number;
  invoiceDiscount: number;
  total: number;
  paidAmount: number;
  remaining: number;
  returnedTotal: number;
  totalCost: number | null;
  profit: number | null;
  salespersonName: string;
  notes: string | null;
  voidedAt: string | null;
  voidReason: string | null;
  lines: SaleLine[];
  payments: { id: number; accountId: number; accountName: string; amount: number; reference: string | null }[];
  returns: { id: number; number: string; date: string; total: number; settlement: ReturnSettlement; reason: string | null }[];
}

export interface CreateSaleInput {
  idempotencyKey: string;
  priceTier: PriceTier;
  customerId?: number | null;
  customerName?: string | null;
  customerPhone?: string | null;
  invoiceDiscount: number;
  lines: { productId: number; quantity: number; unitPrice: number; lineDiscount: number }[];
  payments: { accountId: number; amount: number; reference?: string | null }[];
  notes?: string | null;
}

export interface SalesSummary {
  from: string;
  to: string;
  ownSalesOnly: boolean;
  invoiceCount: number;
  grossSales: number;
  discounts: number;
  returns: number;
  netSales: number;
  costOfSales: number | null;
  grossProfit: number | null;
  received: number;
  newDebt: number;
  receivedByAccount: { accountId: number; accountName: string; amount: number }[];
}

export const salesApi = {
  create: (input: CreateSaleInput) => api<Sale>('/sales', { method: 'POST', body: input }),
  list: (params: { page?: number; pageSize?: number; search?: string; from?: string; to?: string; status?: SaleStatus; customerId?: number; unpaid?: boolean }) =>
    api<PagedResult<SaleListItem>>('/sales', { query: params }),
  get: (id: number) => api<Sale>(`/sales/${id}`),
  summary: (from?: string, to?: string) => api<SalesSummary>('/sales/summary', { query: { from, to } }),
  return: (
    id: number,
    input: { idempotencyKey: string; settlement: ReturnSettlement; refundAccountId?: number | null; reason?: string | null; lines: { saleInvoiceLineId: number; quantity: number; condition: ReturnCondition }[] },
  ) => api<Sale>(`/sales/${id}/returns`, { method: 'POST', body: input }),
  void: (id: number, reason: string) => api<Sale>(`/sales/${id}/void`, { method: 'POST', body: { reason } }),
};

// ---- Customers ----
export interface CustomerListItem {
  id: number;
  code: string;
  name: string;
  phone: string | null;
  balance: number;
  isActive: boolean;
  isWalkIn: boolean;
}

export interface Customer extends CustomerListItem {
  address: string | null;
  notes: string | null;
  totalSales: number;
  totalReturns: number;
  invoiceCount: number;
  lastPurchaseAt: string | null;
  createdAt: string;
}

export interface SaveCustomerInput {
  name: string;
  phone?: string | null;
  address?: string | null;
  notes?: string | null;
  isActive: boolean;
  openingBalance?: number;
}

export const customersApi = {
  list: (params: { page?: number; pageSize?: number; search?: string; withBalance?: boolean; isActive?: boolean }, signal?: AbortSignal) =>
    api<PagedResult<CustomerListItem>>('/customers', { query: params, signal }),
  get: (id: number) => api<Customer>(`/customers/${id}`),
  statement: (id: number, from?: string, to?: string) => api<Statement>(`/customers/${id}/statement`, { query: { from, to } }),
  create: (input: SaveCustomerInput) => api<Customer>('/customers', { method: 'POST', body: input }),
  update: (id: number, input: SaveCustomerInput) => api<Customer>(`/customers/${id}`, { method: 'PUT', body: input }),
};

export const SALE_STATUS: Record<SaleStatus, { label: string; tone: 'success' | 'warning' | 'neutral' | 'danger' }> = {
  Posted: { label: 'معتمدة', tone: 'success' },
  PartiallyReturned: { label: 'مرتجع جزئي', tone: 'warning' },
  Returned: { label: 'مرتجعة', tone: 'neutral' },
  Voided: { label: 'ملغاة', tone: 'danger' },
};
