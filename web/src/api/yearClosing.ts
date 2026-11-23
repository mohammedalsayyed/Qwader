import { api } from './client';

export interface ClosedYear {
  id: number;
  year: number;
  closedAt: string;
  closedBy: string | null;
  netProfit: number;
  totalAssets: number;
  totalLiabilities: number;
  totalEquity: number;
  /** The balance sheet's «الفرق المحاسبي» on the year's last day. */
  difference: number;
  reopenedAt: string | null;
  reopenedBy: string | null;
  reopenReason: string | null;
}

export interface YearClosingStatus {
  enabled: boolean;
  firstYear: number;
  /** Every year up to this one is locked (only while enabled). */
  lockedThrough: number | null;
  nextYear: number;
  yearEnded: boolean;
  blockers: string[];
  canClose: boolean;
  history: ClosedYear[];
}

export const yearClosingApi = {
  status: () => api<YearClosingStatus>('/year-closing'),
  setEnabled: (enabled: boolean) => api<YearClosingStatus>('/year-closing/enabled', { method: 'PUT', body: { enabled } }),
  close: (year: number) => api<YearClosingStatus>('/year-closing/close', { method: 'POST', body: { year } }),
  reopen: (year: number, reason: string) => api<YearClosingStatus>(`/year-closing/${year}/reopen`, { method: 'POST', body: { reason } }),
};
