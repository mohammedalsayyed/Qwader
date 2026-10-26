import { api, apiBlobUrl, type PagedResult } from './client';
import type { AccountType } from './endpoints';

export interface Account {
  id: number;
  name: string;
  type: AccountType;
  providerName: string | null;
  holderName: string | null;
  accountNumber: string | null;
  openingBalance: number;
  currentBalance: number;
  isActive: boolean;
  pendingTransfers: number;
}

export interface SaveAccountInput {
  name: string;
  type: AccountType;
  providerName?: string | null;
  holderName?: string | null;
  accountNumber?: string | null;
  isActive: boolean;
}

export interface AccountMovement {
  id: number;
  date: string;
  direction: 'In' | 'Out';
  amount: number;
  balanceAfter: number;
  category: string;
  categoryName: string;
  sourceType: string;
  sourceId: number | null;
  sourceNumber: string | null;
  description: string | null;
  userName: string | null;
}

export interface AccountStatement {
  accountId: number;
  accountName: string;
  from: string | null;
  to: string;
  openingBalance: number;
  totalIn: number;
  totalOut: number;
  closingBalance: number;
  movements: PagedResult<AccountMovement>;
}

export interface AccountTransfer {
  id: number;
  number: string;
  date: string;
  fromAccountId: number;
  fromAccountName: string;
  toAccountId: number;
  toAccountName: string;
  amount: number;
  notes: string | null;
  createdByName: string | null;
}

export type TransferStatus = 'Pending' | 'Confirmed' | 'Rejected';

export interface IncomingTransfer {
  id: number;
  number: string;
  senderName: string;
  senderPhone: string | null;
  amount: number;
  channel: string | null;
  financialAccountId: number;
  accountName: string;
  referenceNo: string | null;
  transferDate: string;
  proofFileId: string | null;
  status: TransferStatus;
  customerId: number | null;
  customerName: string | null;
  saleInvoiceId: number | null;
  saleInvoiceNumber: string | null;
  notes: string | null;
  recordedByName: string | null;
  createdAt: string;
  reviewedByName: string | null;
  reviewedAt: string | null;
  rejectReason: string | null;
  receiptVoucherId: number | null;
  receiptVoucherNumber: string | null;
}

export type ReceiptPurpose = 'DebtCollection' | 'OtherIncome';
export type PaymentPurpose = 'SupplierPayment' | 'Expense' | 'Other';
export type VoucherStatus = 'Posted' | 'Voided';

export interface Voucher {
  id: number;
  number: string;
  date: string;
  partyName: string;
  partyPhone: string | null;
  customerId: number | null;
  supplierId: number | null;
  amount: number;
  financialAccountId: number;
  accountName: string;
  purpose: string;
  purposeName: string;
  reference: string | null;
  notes: string | null;
  attachmentFileId: string | null;
  status: VoucherStatus;
  createdByName: string | null;
  voidedAt: string | null;
  voidReason: string | null;
  incomingTransferId: number | null;
  saleInvoiceId: number | null;
  expenseCategoryName: string | null;
}

export interface ExpenseItem {
  id: number;
  number: string;
  date: string;
  expenseCategoryId: number;
  categoryName: string;
  description: string;
  amount: number;
  financialAccountId: number;
  accountName: string;
  attachmentFileId: string | null;
  paymentVoucherId: number;
  paymentVoucherNumber: string;
  status: VoucherStatus;
  createdByName: string | null;
}

export interface ExpenseCategory {
  id: number;
  name: string;
  isActive: boolean;
  expenseCount: number;
}

export interface VoucherListParams {
  page?: number;
  pageSize?: number;
  search?: string;
  from?: string;
  to?: string;
  accountId?: number;
  includeVoided?: boolean;
  customerId?: number;
  supplierId?: number;
  categoryId?: number;
}

export const accountsApi = {
  list: (includeInactive = false) => api<Account[]>('/financial-accounts', { query: { includeInactive } }),
  get: (id: number) => api<Account>(`/financial-accounts/${id}`),
  create: (input: SaveAccountInput) => api<Account>('/financial-accounts', { method: 'POST', body: input }),
  update: (id: number, input: SaveAccountInput) => api<Account>(`/financial-accounts/${id}`, { method: 'PUT', body: input }),
  setOpening: (id: number, openingBalance: number, notes?: string | null) =>
    api<Account>(`/financial-accounts/${id}/opening-balance`, { method: 'POST', body: { openingBalance, notes } }),
  statement: (id: number, params: { from?: string; to?: string; page?: number; pageSize?: number }) =>
    api<AccountStatement>(`/financial-accounts/${id}/statement`, { query: params }),
  transfers: (page = 1) => api<PagedResult<AccountTransfer>>('/account-transfers', { query: { page, pageSize: 20 } }),
  transfer: (input: { idempotencyKey: string; fromAccountId: number; toAccountId: number; amount: number; notes?: string | null }) =>
    api<AccountTransfer>('/account-transfers', { method: 'POST', body: input }),
};

export const transfersApi = {
  list: (params: { page?: number; pageSize?: number; search?: string; status?: TransferStatus; accountId?: number }) =>
    api<PagedResult<IncomingTransfer>>('/incoming-transfers', { query: params }),
  get: (id: number) => api<IncomingTransfer>(`/incoming-transfers/${id}`),
  create: (input: {
    idempotencyKey: string; senderName: string; senderPhone?: string | null; amount: number; channel?: string | null; financialAccountId: number;
    referenceNo?: string | null; transferDate?: string | null; proofFileId?: string | null; customerId?: number | null; notes?: string | null;
  }) => api<IncomingTransfer>('/incoming-transfers', { method: 'POST', body: input }),
  confirm: (id: number, acceptCredit = false) =>
    api<IncomingTransfer>(`/incoming-transfers/${id}/confirm`, { method: 'POST', query: acceptCredit ? { acceptCredit: true } : undefined }),
  reject: (id: number, reason: string) => api<IncomingTransfer>(`/incoming-transfers/${id}/reject`, { method: 'POST', body: { reason } }),
};

export const receiptsApi = {
  list: (params: VoucherListParams) => api<PagedResult<Voucher>>('/receipt-vouchers', { query: { ...params } }),
  get: (id: number) => api<Voucher>(`/receipt-vouchers/${id}`),
  create: (input: {
    idempotencyKey: string; purpose: ReceiptPurpose; customerId?: number | null; payerName?: string | null; payerPhone?: string | null;
    amount: number; financialAccountId: number; reference?: string | null; notes?: string | null; attachmentFileId?: string | null; confirmDuplicate?: boolean;
  }) => api<Voucher>('/receipt-vouchers', { method: 'POST', body: input }),
  void: (id: number, reason: string) => api<Voucher>(`/receipt-vouchers/${id}/void`, { method: 'POST', body: { reason } }),
};

export const paymentsApi = {
  list: (params: VoucherListParams) => api<PagedResult<Voucher>>('/payment-vouchers', { query: { ...params } }),
  get: (id: number) => api<Voucher>(`/payment-vouchers/${id}`),
  create: (input: {
    idempotencyKey: string; purpose: PaymentPurpose; supplierId?: number | null; beneficiaryName?: string | null;
    amount: number; financialAccountId: number; reference?: string | null; notes?: string | null; attachmentFileId?: string | null; confirmDuplicate?: boolean;
  }) => api<Voucher>('/payment-vouchers', { method: 'POST', body: input }),
  void: (id: number, reason: string) => api<Voucher>(`/payment-vouchers/${id}/void`, { method: 'POST', body: { reason } }),
};

export const expensesApi = {
  list: (params: VoucherListParams) => api<PagedResult<ExpenseItem>>('/expenses', { query: { ...params } }),
  create: (input: { idempotencyKey: string; expenseCategoryId: number; description: string; amount: number; financialAccountId: number; date?: string | null; attachmentFileId?: string | null }) =>
    api<ExpenseItem>('/expenses', { method: 'POST', body: input }),
  void: (id: number, reason: string) => api<ExpenseItem>(`/expenses/${id}/void`, { method: 'POST', body: { reason } }),
  categories: () => api<ExpenseCategory[]>('/expenses/categories'),
  saveCategory: (id: number | null, input: { name: string; isActive: boolean }) =>
    id ? api<ExpenseCategory>(`/expenses/categories/${id}`, { method: 'PUT', body: input }) : api<ExpenseCategory>('/expenses/categories', { method: 'POST', body: input }),
};

/** Private files (transfer proofs, attachments) need the auth header, so <img> cannot load them directly. */
export const fetchPrivateFile = (id: string) => apiBlobUrl(`/files/${id}`);
