import { api, apiDownload, apiUpload, type PagedResult } from './client';

export type StockStatus = 'Available' | 'Low' | 'Out';
export type StockFilter = 'All' | 'Available' | 'Low' | 'Out' | 'NeedsReorder';

export interface Category {
  id: number;
  code: string;
  name: string;
  description: string | null;
  isActive: boolean;
  productCount: number;
  /** «الصنف العام» it belongs to. */
  groupId: number | null;
  groupName: string | null;
}

export interface SaveCategoryInput {
  code?: string | null;
  name: string;
  description?: string | null;
  isActive: boolean;
  groupId?: number | null;
}

/** «الصنف العام»: a general category above the categories («أجهزة كهربائية»، «أدوات منزلية»). */
export interface CategoryGroup {
  id: number;
  name: string;
  categoryCount: number;
  productCount: number;
}

export interface ProductListItem {
  id: number;
  sku: string;
  barcode: string | null;
  name: string;
  displayName: string;
  categoryId: number;
  categoryName: string;
  unitName: string;
  retailPrice: number;
  wholesalePrice: number;
  minSalePrice: number;
  quantity: number;
  reorderLevel: number;
  stockStatus: StockStatus;
  imageFileId: string | null;
  isArchived: boolean;
  averageCost: number | null;
  units?: ProductUnit[] | null;
  /** Its cost is zero (no purchase price entered): the till warns when it is sold. */
  noCost?: boolean;
}

/** Another unit the product is sold/bought in: one of it = `factor` base units (a dozen = 12 pieces). */
export interface ProductUnit {
  id: number;
  unitId: number;
  unitName: string;
  factor: number;
  retailPrice: number;
  wholesalePrice: number;
  minSalePrice: number;
}

export interface SaveProductUnit {
  unitId: number;
  factor: number;
  retailPrice: number;
  wholesalePrice: number;
  minSalePrice: number;
}

export interface ProductRef {
  id: number;
  sku: string;
  displayName: string;
  quantity: number;
  retailPrice: number;
}

export interface ProductDetail {
  id: number;
  sku: string;
  barcode: string | null;
  name: string;
  displayName: string;
  description: string | null;
  categoryId: number;
  categoryName: string;
  brandName: string | null;
  unitId: number;
  unitName: string;
  defaultSupplierId: number | null;
  defaultSupplierName: string | null;
  parentProductId: number | null;
  parentDisplayName: string | null;
  size: string | null;
  color: string | null;
  model: string | null;
  purchasePrice: number | null;
  averageCost: number | null;
  /** Its cost is zero (no purchase price entered). */
  noCost?: boolean;
  wholesalePrice: number;
  retailPrice: number;
  minSalePrice: number;
  reorderLevel: number;
  quantity: number;
  stockStatus: StockStatus;
  stockValue: number | null;
  expectedProfit: number | null;
  marginPercent: number | null;
  suggestedRetailPrice: number | null;
  imageFileId: string | null;
  isArchived: boolean;
  createdAt: string;
  stock: { warehouseId: number; warehouseName: string; quantity: number }[];
  variants: ProductRef[];
  units?: ProductUnit[] | null;
}

export interface SaveProductInput {
  name: string;
  sku?: string | null;
  barcode?: string | null;
  categoryId: number;
  brandName?: string | null;
  unitId: number;
  defaultSupplierId?: number | null;
  parentProductId?: number | null;
  size?: string | null;
  color?: string | null;
  model?: string | null;
  description?: string | null;
  purchasePrice?: number | null;
  wholesalePrice: number;
  retailPrice: number;
  minSalePrice: number;
  reorderLevel: number;
  imageFileId?: string | null;
  openingQuantity?: number | null;
  openingUnitCost?: number | null;
  /** Extra units; leave out to keep the stored ones. */
  units?: SaveProductUnit[] | null;
}

export interface ProductListParams {
  page?: number;
  pageSize?: number;
  search?: string;
  categoryId?: number;
  /** «الصنف العام»: products of every category under it. */
  groupId?: number;
  supplierId?: number;
  stock?: StockFilter;
  archived?: boolean;
  /** Only products with no purchase price entered (needs cost permission). */
  noCost?: boolean;
  /** Only products whose average cost is above their retail price (needs cost permission). */
  costAbovePrice?: boolean;
  /** Retail (piece) price range, inclusive. */
  priceFrom?: number;
  priceTo?: number;
  sort?: ProductSort;
}

export type ProductSort = 'Newest' | 'PriceAsc' | 'PriceDesc' | 'Name' | 'QuantityDesc' | 'QuantityAsc';

export interface ImportResult {
  created: number;
  updated: number;
  /** The stock count recording «الكمية الفعلية» differences of existing products. */
  stockCountNumber?: string | null;
  stockChanged?: number;
  categoriesAdded?: number;
  unitsAdded?: number;
  errors: { row: number; message: string }[];
  succeeded: boolean;
}

export const categoryGroupsApi = {
  list: () => api<CategoryGroup[]>('/category-groups'),
  create: (name: string) => api<CategoryGroup>('/category-groups', { method: 'POST', body: { name } }),
  rename: (id: number, name: string) => api<CategoryGroup>(`/category-groups/${id}`, { method: 'PUT', body: { name } }),
  remove: (id: number) => api<void>(`/category-groups/${id}`, { method: 'DELETE' }),
};

export const categoriesApi = {
  list: (params: { page?: number; pageSize?: number; search?: string; isActive?: boolean; groupId?: number }) =>
    api<PagedResult<Category>>('/categories', { query: params }),
  create: (input: SaveCategoryInput) => api<Category>('/categories', { method: 'POST', body: input }),
  update: (id: number, input: SaveCategoryInput) => api<Category>(`/categories/${id}`, { method: 'PUT', body: input }),
};

export const productsApi = {
  /** New selling prices for several products at once; returns how many changed. */
  updatePrices: (items: { productId: number; retailPrice: number; wholesalePrice: number; minSalePrice: number }[]) =>
    api<number>('/products/prices', { method: 'POST', body: { items } }),
  list: (params: ProductListParams, signal?: AbortSignal) =>
    api<PagedResult<ProductListItem>>('/products', { query: { ...params }, signal }),
  get: (id: number) => api<ProductDetail>(`/products/${id}`),
  byCode: (code: string) => api<ProductDetail>(`/products/by-code/${encodeURIComponent(code)}`),
  codeAvailable: (code: string, exceptId?: number) =>
    api<{ available: boolean }>('/products/code-available', { query: { code, exceptId } }),
  create: (input: SaveProductInput) => api<ProductDetail>('/products', { method: 'POST', body: input }),
  update: (id: number, input: SaveProductInput) => api<ProductDetail>(`/products/${id}`, { method: 'PUT', body: input }),
  archive: (id: number) => api<void>(`/products/${id}/archive`, { method: 'POST' }),
  unarchive: (id: number) => api<void>(`/products/${id}/unarchive`, { method: 'POST' }),
  exportExcel: (params: ProductListParams) => apiDownload('/products/export', { ...params }, 'products.xlsx'),
  template: () => apiDownload('/products/import-template', undefined, 'products-template.xlsx'),
  importExcel: (file: File) => {
    const form = new FormData();
    form.append('file', file);
    return apiUpload<ImportResult>('/products/import', form);
  },
};

export type FilePurpose = 'ProductImage' | 'TransferProof' | 'VoucherAttachment' | 'ExpenseAttachment' | 'AccountLogo';

export const filesApi = {
  upload: (file: Blob, name: string, purpose: FilePurpose) => {
    const form = new FormData();
    form.append('file', file, name);
    return apiUpload<{ id: string; url: string; contentType: string; size: number }>('/files', form, { purpose });
  },
  url: (id: string) => `/api/files/${id}`,
};

// ---- Search & notifications ----
export type SearchKind = 'Product' | 'Category' | 'Supplier' | 'PurchaseInvoice' | 'Customer' | 'SaleInvoice' | 'IncomingTransfer' | 'ReceiptVoucher' | 'PaymentVoucher';

export interface SearchHit {
  kind: SearchKind;
  id: number;
  title: string;
  subtitle: string | null;
  code: string | null;
  price: number | null;
  quantity: number | null;
  imageFileId: string | null;
}

export interface Notifications {
  lowStock: number;
  outOfStock: number;
  draftPurchases: number;
  pendingTransfers: number;
}

export const searchApi = {
  search: (q: string, signal?: AbortSignal) => api<{ query: string; hits: SearchHit[] }>('/search', { query: { q }, signal }),
  notifications: () => api<Notifications>('/notifications'),
};
