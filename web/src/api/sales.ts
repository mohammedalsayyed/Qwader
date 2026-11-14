import type { ProductUnit } from './catalog';
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
  /** Still owed: after later collections and returns credited to the customer. */
  remaining: number;
  soldOffline: boolean;
  /** Sold without internet and broke a rule nobody has reviewed yet. */
  needsReview: boolean;
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
  /** Base units in one of this line's unit (12 for a dozen). */
  unitFactor?: number;
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
  /** A voided invoice brought back (once only). */
  restoredAt: string | null;
  lines: SaleLine[];
  payments: { id: number; accountId: number; accountName: string; amount: number; reference: string | null }[];
  /** Every return, cancelled ones too (voidedAt set): those count for nothing and are shown for the history only. */
  returns: { id: number; number: string; date: string; total: number; settlement: ReturnSettlement; reason: string | null;
    voidedAt: string | null; voidReason: string | null; refundAccountName: string | null }[];
  transfers: { id: number; number: string; amount: number; accountName: string; reference: string | null; status: 'Pending' | 'Confirmed' | 'Rejected' }[];
  soldOffline: boolean;
  /** What an offline sale broke (one problem per line), for a manager to review. */
  reviewNote: string | null;
  reviewedAt: string | null;
  reviewedByName: string | null;
}

export interface CreateSaleInput {
  idempotencyKey: string;
  priceTier: PriceTier;
  customerId?: number | null;
  customerName?: string | null;
  customerPhone?: string | null;
  invoiceDiscount: number;
  /** productUnitId: sold in an extra unit (dozen); null/absent = the base unit. Quantity and price are per that unit. */
  lines: { productId: number; quantity: number; unitPrice: number; lineDiscount: number; productUnitId?: number | null }[];
  payments: { accountId: number; amount: number; reference?: string | null }[];
  notes?: string | null;
  /** Set when the sale was made without internet: when it happened. */
  soldOfflineAt?: string | null;
}

/** What a till keeps on the device to sell without internet. */
export interface OfflineData {
  generatedAt: string;
  maxHours: number;
  products: OfflineProduct[];
  customers: OfflineCustomer[];
  cursor: OfflineCursor | null;
}

/** Where a device's copy stands, so the next poll asks only for what changed after it. */
export interface OfflineCursor {
  movementId: number;
  ledgerEntryId: number;
  at: string;
}

export interface OfflineChanges {
  generatedAt: string;
  maxHours: number;
  cursor: OfflineCursor;
  products: OfflineProduct[];
  removedProductIds: number[];
  customers: OfflineCustomer[];
  removedCustomerIds: number[];
}

export interface OfflineProduct {
  id: number;
  sku: string;
  barcode: string | null;
  displayName: string;
  /** Normalized search text (name, variants, brand, codes). */
  search: string;
  unitName: string;
  imageFileId: string | null;
  quantity: number;
  retailPrice: number;
  wholesalePrice: number;
  minSalePrice: number;
  averageCost: number | null;
  units?: ProductUnit[] | null;
}

export interface OfflineCustomer {
  id: number;
  code: string;
  name: string;
  phone: string | null;
  search: string;
  balance: number;
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
  list: (params: { page?: number; pageSize?: number; search?: string; from?: string; to?: string; status?: SaleStatus; customerId?: number; unpaid?: boolean; needsReview?: boolean }) =>
    api<PagedResult<SaleListItem>>('/sales', { query: params }),
  get: (id: number) => api<Sale>(`/sales/${id}`),
  summary: (from?: string, to?: string) => api<SalesSummary>('/sales/summary', { query: { from, to } }),
  return: (
    id: number,
    input: { idempotencyKey: string; settlement: ReturnSettlement; refundAccountId?: number | null; reason?: string | null; lines: { saleInvoiceLineId: number; quantity: number; condition: ReturnCondition }[];
      /** yyyy-mm-dd; today when left out. */
      date?: string | null },
  ) => api<Sale>(`/sales/${id}/returns`, { method: 'POST', body: input }),
  void: (id: number, reason: string, date?: string | null) => api<Sale>(`/sales/${id}/void`, { method: 'POST', body: { reason, date: date || null } }),
  restore: (id: number, reason: string) => api<Sale>(`/sales/${id}/restore`, { method: 'POST', body: { reason } }),
  voidReturn: (id: number, returnId: number, reason: string) => api<Sale>(`/sales/${id}/returns/${returnId}/void`, { method: 'POST', body: { reason } }),
  offlineData: () => api<OfflineData>('/sales/offline-data'),
  offlineChanges: (c: OfflineCursor) => api<OfflineChanges>('/sales/offline-changes', { query: { movementId: c.movementId, ledgerEntryId: c.ledgerEntryId, at: c.at } }),
  markReviewed: (id: number) => api<Sale>(`/sales/${id}/reviewed`, { method: 'POST' }),
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
  list: (params: { page?: number; pageSize?: number; search?: string; withBalance?: boolean; isActive?: boolean; excludeWalkIn?: boolean }, signal?: AbortSignal) =>
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
