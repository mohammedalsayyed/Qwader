// Mirrors Qwader.Domain.Identity.Permissions. The server enforces these; the client only hides what would be refused.
export const P = {
  ProductsView: 'Products.View',
  ProductsViewCost: 'Products.ViewCost',
  ProductsManage: 'Products.Manage',
  ProductsEditPrices: 'Products.EditPrices',
  InventoryView: 'Inventory.View',
  InventoryAdjust: 'Inventory.Adjust',
  InventoryCount: 'Inventory.Count',
  PurchasesView: 'Purchases.View',
  PurchasesManage: 'Purchases.Manage',
  SuppliersManage: 'Suppliers.Manage',
  SalesCreate: 'Sales.Create',
  SalesViewAll: 'Sales.ViewAll',
  SalesWholesale: 'Sales.Wholesale',
  SalesDiscount: 'Sales.Discount',
  SalesPriceOverride: 'Sales.PriceOverride',
  SalesReturn: 'Sales.Return',
  SalesVoid: 'Sales.Void',
  CustomersManage: 'Customers.Manage',
  TransfersCreate: 'Transfers.Create',
  TransfersReview: 'Transfers.Review',
  VouchersCreate: 'Vouchers.Create',
  VouchersVoid: 'Vouchers.Void',
  ExpensesManage: 'Expenses.Manage',
  PayrollManage: 'Payroll.Manage',
  AccountsView: 'Accounts.View',
  AccountsManage: 'Accounts.Manage',
  AccountsViewNumbers: 'Accounts.ViewNumbers',
  ReportsSales: 'Reports.Sales',
  ReportsProfit: 'Reports.Profit',
  ReportsFinancial: 'Reports.Financial',
  /** «تقرير فواتير المبيعات», its own permission. */
  ReportsInvoices: 'Reports.Invoices',
  /** «حركة الفلوس» on the home screen, its own permission. */
  DashboardCashFlow: 'Dashboard.CashFlow',
  /** Any profit anywhere (the sale's, a product's expected profit, reports, dashboard, balance sheet). */
  ProfitView: 'Profit.View',
  UsersManage: 'Users.Manage',
  SettingsManage: 'Settings.Manage',
  AuditView: 'Audit.View',
} as const;

export type Permission = (typeof P)[keyof typeof P];
