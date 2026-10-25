import { api, type PagedResult } from './client';

export type MovementType =
  | 'OpeningBalance'
  | 'Purchase'
  | 'PurchaseReturn'
  | 'PurchaseVoid'
  | 'Sale'
  | 'SaleReturn'
  | 'SaleVoid'
  | 'Damaged'
  | 'Lost'
  | 'Found'
  | 'CountIncrease'
  | 'CountDecrease';

export const MOVEMENT_LABELS: Record<MovementType, string> = {
  OpeningBalance: 'رصيد افتتاحي',
  Purchase: 'شراء',
  PurchaseReturn: 'مردود مشتريات',
  PurchaseVoid: 'إلغاء شراء',
  Sale: 'بيع',
  SaleReturn: 'مرتجع بيع',
  SaleVoid: 'إلغاء بيع',
  Damaged: 'تالف',
  Lost: 'مفقود',
  Found: 'زيادة / عثور',
  CountIncrease: 'زيادة جرد',
  CountDecrease: 'نقص جرد',
};

export interface StockMovement {
  id: number;
  occurredAt: string;
  productId: number;
  productName: string;
  sku: string;
  type: MovementType;
  quantity: number;
  balanceAfter: number;
  unitCost: number | null;
  sourceType: string;
  sourceId: number | null;
  sourceNumber: string | null;
  notes: string | null;
  userName: string | null;
  warehouseName: string;
}

export interface InventorySummary {
  productCount: number;
  totalQuantity: number;
  stockValue: number | null;
  lowCount: number;
  outCount: number;
}

export type AdjustmentKind = 'Damaged' | 'Lost' | 'Found';
export type StockCountStatus = 'Open' | 'Posted' | 'Cancelled';

export interface StockCountListItem {
  id: number;
  number: string;
  status: StockCountStatus;
  warehouseName: string;
  lineCount: number;
  countedLines: number;
  createdAt: string;
  postedAt: string | null;
  notes: string | null;
}

export interface StockCountLine {
  id: number;
  productId: number;
  sku: string;
  productName: string;
  unitName: string;
  currentQuantity: number;
  expectedQuantity: number | null;
  countedQuantity: number | null;
  difference: number | null;
}

export interface StockCount {
  id: number;
  number: string;
  status: StockCountStatus;
  warehouseId: number;
  warehouseName: string;
  notes: string | null;
  createdAt: string;
  postedAt: string | null;
  lines: StockCountLine[];
}

export const inventoryApi = {
  summary: () => api<InventorySummary>('/inventory/summary'),
  movements: (params: { page?: number; pageSize?: number; productId?: number; type?: MovementType; from?: string; to?: string; search?: string }) =>
    api<PagedResult<StockMovement>>('/inventory/movements', { query: params }),
  adjust: (input: { idempotencyKey: string; productId: number; kind: AdjustmentKind; quantity: number; notes?: string | null }) =>
    api<{ number: string; balanceAfter: number }>('/inventory/adjustments', { method: 'POST', body: input }),
  counts: (params: { page?: number }) => api<PagedResult<StockCountListItem>>('/inventory/counts', { query: params }),
  count: (id: number) => api<StockCount>(`/inventory/counts/${id}`),
  createCount: (input: { categoryId?: number | null; productIds?: number[] | null; notes?: string | null }) =>
    api<StockCount>('/inventory/counts', { method: 'POST', body: input }),
  saveCounts: (id: number, entries: { lineId: number; countedQuantity: number | null }[]) =>
    api<StockCount>(`/inventory/counts/${id}`, { method: 'PUT', body: { entries } }),
  postCount: (id: number) => api<StockCount>(`/inventory/counts/${id}/post`, { method: 'POST' }),
  cancelCount: (id: number) => api<void>(`/inventory/counts/${id}/cancel`, { method: 'POST' }),
};

// ---- Suppliers & purchases ----
export interface SupplierListItem {
  id: number;
  code: string;
  name: string;
  phone: string | null;
  balance: number;
  isActive: boolean;
}

export interface Supplier extends SupplierListItem {
  address: string | null;
  notes: string | null;
  totalPurchases: number;
  totalPayments: number;
  createdAt: string;
}

export interface SaveSupplierInput {
  name: string;
  phone?: string | null;
  address?: string | null;
  notes?: string | null;
  isActive: boolean;
  openingBalance?: number;
}

export interface StatementLine {
  date: string;
  type: string;
  reference: string | null;
  sourceType: string;
  sourceId: number | null;
  description: string | null;
  debit: number;
  credit: number;
  balance: number;
}

export interface Statement {
  partyId: number;
  partyName: string;
  from: string | null;
  to: string;
  openingBalance: number;
  totalDebit: number;
  totalCredit: number;
  closingBalance: number;
  lines: StatementLine[];
}

export type PurchaseStatus = 'Draft' | 'Posted' | 'Voided';
export type ReturnSettlement = 'ReduceBalance' | 'Refund';

export interface PurchaseListItem {
  id: number;
  number: string;
  date: string;
  supplierId: number;
  supplierName: string;
  supplierInvoiceNo: string | null;
  status: PurchaseStatus;
  total: number;
  paidAmount: number;
  returnedTotal: number;
  lineCount: number;
}

export interface PurchaseLine {
  id: number;
  productId: number;
  sku: string;
  productName: string;
  unitName: string;
  quantity: number;
  unitCost: number;
  lineDiscount: number;
  lineTotal: number;
  netUnitCost: number;
  returnedQuantity: number;
}

export interface Purchase {
  id: number;
  number: string;
  date: string;
  status: PurchaseStatus;
  supplierId: number;
  supplierName: string;
  supplierInvoiceNo: string | null;
  warehouseId: number;
  warehouseName: string;
  subtotal: number;
  discount: number;
  total: number;
  paidAmount: number;
  remaining: number;
  paymentAccountId: number | null;
  paymentAccountName: string | null;
  notes: string | null;
  createdByName: string | null;
  createdAt: string;
  postedAt: string | null;
  voidedAt: string | null;
  voidReason: string | null;
  lines: PurchaseLine[];
  returns: { id: number; number: string; date: string; total: number; settlement: ReturnSettlement; reason: string | null }[];
}

export interface SavePurchaseInput {
  idempotencyKey: string;
  supplierId: number;
  date?: string | null;
  supplierInvoiceNo?: string | null;
  discount: number;
  paidAmount: number;
  paymentAccountId?: number | null;
  notes?: string | null;
  lines: { productId: number; quantity: number; unitCost: number; lineDiscount: number }[];
  post: boolean;
}

export const suppliersApi = {
  list: (params: { page?: number; pageSize?: number; search?: string; isActive?: boolean; withBalance?: boolean }, signal?: AbortSignal) =>
    api<PagedResult<SupplierListItem>>('/suppliers', { query: params, signal }),
  get: (id: number) => api<Supplier>(`/suppliers/${id}`),
  statement: (id: number, from?: string, to?: string) => api<Statement>(`/suppliers/${id}/statement`, { query: { from, to } }),
  create: (input: SaveSupplierInput) => api<Supplier>('/suppliers', { method: 'POST', body: input }),
  update: (id: number, input: SaveSupplierInput) => api<Supplier>(`/suppliers/${id}`, { method: 'PUT', body: input }),
};

export const purchasesApi = {
  list: (params: { page?: number; pageSize?: number; search?: string; supplierId?: number; status?: PurchaseStatus; from?: string; to?: string }) =>
    api<PagedResult<PurchaseListItem>>('/purchases', { query: params }),
  get: (id: number) => api<Purchase>(`/purchases/${id}`),
  create: (input: SavePurchaseInput) => api<Purchase>('/purchases', { method: 'POST', body: input }),
  updateDraft: (id: number, input: SavePurchaseInput) => api<Purchase>(`/purchases/${id}`, { method: 'PUT', body: input }),
  post: (id: number, idempotencyKey: string) => api<Purchase>(`/purchases/${id}/post`, { method: 'POST', body: { idempotencyKey } }),
  void: (id: number, reason: string) => api<Purchase>(`/purchases/${id}/void`, { method: 'POST', body: { reason } }),
  return: (
    id: number,
    input: { idempotencyKey: string; settlement: ReturnSettlement; refundAccountId?: number | null; reason?: string | null; lines: { purchaseInvoiceLineId: number; quantity: number }[] },
  ) => api<Purchase>(`/purchases/${id}/returns`, { method: 'POST', body: input }),
};
