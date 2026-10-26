import { api, apiDownload, type AuthResponse, type CurrentUser, type PagedResult } from './client';

// ---- Auth ----
export const authApi = {
  login: (userName: string, password: string) =>
    api<AuthResponse>('/auth/login', { method: 'POST', body: { userName, password } }),
  logout: () => api<void>('/auth/logout', { method: 'POST' }),
  me: () => api<CurrentUser>('/auth/me'),
  changePassword: (currentPassword: string, newPassword: string) =>
    api<void>('/auth/change-password', { method: 'POST', body: { currentPassword, newPassword } }),
};

// ---- Users & roles ----
export interface RoleRef {
  name: string;
  displayName: string;
}

export interface User {
  id: number;
  userName: string;
  fullName: string;
  phoneNumber: string | null;
  isActive: boolean;
  roles: RoleRef[];
  createdAt: string;
  lastLoginAt: string | null;
}

export interface Role {
  id: number;
  name: string;
  displayName: string;
  description: string | null;
  isSystem: boolean;
  permissions: string[];
  userCount: number;
}

export interface PermissionGroup {
  group: string;
  permissions: { code: string; name: string; group: string }[];
}

export interface CreateUserInput {
  userName: string;
  fullName: string;
  phoneNumber?: string | null;
  password: string;
  roles: string[];
}

export interface UpdateUserInput {
  fullName: string;
  phoneNumber?: string | null;
  isActive: boolean;
  roles: string[];
}

export interface SaveRoleInput {
  displayName: string;
  description?: string | null;
  permissions: string[];
}

export const usersApi = {
  list: (params: { page?: number; pageSize?: number; search?: string; isActive?: boolean }) =>
    api<PagedResult<User>>('/users', { query: params }),
  get: (id: number) => api<User>(`/users/${id}`),
  create: (input: CreateUserInput) => api<User>('/users', { method: 'POST', body: input }),
  update: (id: number, input: UpdateUserInput) => api<User>(`/users/${id}`, { method: 'PUT', body: input }),
  resetPassword: (id: number, newPassword: string) =>
    api<void>(`/users/${id}/reset-password`, { method: 'POST', body: { newPassword } }),
};

export const rolesApi = {
  list: () => api<Role[]>('/roles'),
  permissions: () => api<PermissionGroup[]>('/roles/permissions'),
  create: (input: SaveRoleInput) => api<Role>('/roles', { method: 'POST', body: input }),
  update: (id: number, input: SaveRoleInput) => api<Role>(`/roles/${id}`, { method: 'PUT', body: input }),
  remove: (id: number) => api<void>(`/roles/${id}`, { method: 'DELETE' }),
};

// ---- Settings & lookups ----
export interface Settings {
  storeName: string;
  storeAddress: string | null;
  storePhone: string | null;
  invoiceFooter: string | null;
  defaultMarginPercent: number;
  maxDiscountPercent: number;
  defaultReorderLevel: number;
  saleTransfersPending: boolean;
}

export interface LookupItem {
  id: number;
  name: string;
}

export type AccountType = 'Cash' | 'Bank' | 'Wallet';

export interface Lookups {
  units: LookupItem[];
  warehouses: LookupItem[];
  expenseCategories: LookupItem[];
  categories: LookupItem[];
  accounts: (LookupItem & { type: AccountType })[];
}

export const settingsApi = {
  get: () => api<Settings>('/settings'),
  update: (input: Settings) => api<Settings>('/settings', { method: 'PUT', body: input }),
  backup: () => apiDownload('/settings/backup', undefined, 'qwader-backup.zip'),
  reset: (input: { mode: SystemResetMode; password: string }) =>
    api<{ mode: SystemResetMode; deletedRows: number }>('/settings/reset', { method: 'POST', body: input }),
};

export type SystemResetMode = 'TransactionsOnly' | 'Everything';

export const lookupsApi = {
  get: () => api<Lookups>('/lookups'),
};
