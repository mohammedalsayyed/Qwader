import { api } from './client';

export type PayPeriod = 'Daily' | 'Weekly' | 'Monthly';
export type PayrollEntryType = 'Advance' | 'Deduction' | 'Bonus';
type DocStatus = 'Posted' | 'Voided';

export const PAY_PERIOD: Record<PayPeriod, { label: string; unit: string }> = {
  Daily: { label: 'يومي', unit: 'يوم' },
  Weekly: { label: 'أسبوعي', unit: 'أسبوع' },
  Monthly: { label: 'شهري', unit: 'شهر' },
};

export const ENTRY_TYPE: Record<PayrollEntryType, { label: string; tone: 'warning' | 'danger' | 'success' }> = {
  Advance: { label: 'سلفة', tone: 'warning' },
  Deduction: { label: 'خصم', tone: 'danger' },
  Bonus: { label: 'مكافأة', tone: 'success' },
};

export interface EmployeeListItem {
  id: number;
  userId: number;
  fullName: string;
  phone: string | null;
  payPeriod: PayPeriod;
  rate: number;
  isActive: boolean;
  openAdvances: number;
  openDeductions: number;
  openBonuses: number;
  lastPaidTo: string | null;
}

export interface PayrollEntry {
  id: number;
  type: PayrollEntryType;
  date: string;
  amount: number;
  notes: string | null;
  accountName: string | null;
  expenseNumber: string | null;
  /** Left over from a settlement whose advances were larger than the salary. */
  carried: boolean;
  settlementId: number | null;
  settlementNumber: string | null;
  status: DocStatus;
  voidReason: string | null;
  createdByName: string | null;
}

export interface PayrollSettlement {
  id: number;
  number: string;
  date: string;
  periodFrom: string;
  periodTo: string;
  payPeriod: PayPeriod;
  units: number;
  rate: number;
  gross: number;
  bonuses: number;
  deductions: number;
  advances: number;
  net: number;
  carriedAdvance: number;
  accountName: string | null;
  expenseNumber: string | null;
  notes: string | null;
  status: DocStatus;
  voidReason: string | null;
  createdByName: string | null;
}

export interface Employee extends Omit<EmployeeListItem, never> {
  userName: string;
  startDate: string | null;
  notes: string | null;
  entries: PayrollEntry[];
  settlements: PayrollSettlement[];
}

export interface PayrollCandidate {
  userId: number;
  fullName: string;
  userName: string;
  phone: string | null;
}

export interface SaveEmployeeInput {
  userId: number;
  phone: string | null;
  payPeriod: PayPeriod;
  rate: number;
  startDate: string | null;
  notes: string | null;
  isActive: boolean;
}

export const payrollApi = {
  list: () => api<EmployeeListItem[]>('/payroll/employees'),
  candidates: () => api<PayrollCandidate[]>('/payroll/candidates'),
  get: (id: number) => api<Employee>(`/payroll/employees/${id}`),
  /** The signed-in user's own record; undefined when they are not on the payroll. */
  mine: () => api<Employee | undefined>('/payroll/me'),
  create: (input: SaveEmployeeInput) => api<Employee>('/payroll/employees', { method: 'POST', body: input }),
  update: (id: number, input: SaveEmployeeInput) => api<Employee>(`/payroll/employees/${id}`, { method: 'PUT', body: input }),
  addEntry: (id: number, input: { idempotencyKey: string; type: PayrollEntryType; amount: number; date?: string | null; financialAccountId?: number | null; notes?: string | null }) =>
    api<Employee>(`/payroll/employees/${id}/entries`, { method: 'POST', body: input }),
  voidEntry: (id: number, reason: string) => api<Employee>(`/payroll/entries/${id}/void`, { method: 'POST', body: { reason } }),
  settle: (id: number, input: { idempotencyKey: string; periodFrom: string; periodTo: string; units: number; financialAccountId?: number | null; notes?: string | null }) =>
    api<Employee>(`/payroll/employees/${id}/settlements`, { method: 'POST', body: input }),
  voidSettlement: (id: number, reason: string) => api<Employee>(`/payroll/settlements/${id}/void`, { method: 'POST', body: { reason } }),
};
