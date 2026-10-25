namespace Qwader.Domain.Identity;

public sealed record PermissionInfo(string Code, string Name, string Group);

/// <summary>The full catalog of permissions. Codes are stored in RolePermissions and emitted as JWT claims.</summary>
public static class Permissions
{
    public const string ClaimType = "perm";

    public const string ProductsView = "Products.View";
    public const string ProductsViewCost = "Products.ViewCost";
    public const string ProductsManage = "Products.Manage";
    public const string ProductsEditPrices = "Products.EditPrices";

    public const string InventoryView = "Inventory.View";
    public const string InventoryAdjust = "Inventory.Adjust";
    public const string InventoryCount = "Inventory.Count";

    public const string PurchasesView = "Purchases.View";
    public const string PurchasesManage = "Purchases.Manage";
    public const string SuppliersManage = "Suppliers.Manage";

    public const string SalesCreate = "Sales.Create";
    public const string SalesViewAll = "Sales.ViewAll";
    public const string SalesDiscount = "Sales.Discount";
    public const string SalesPriceOverride = "Sales.PriceOverride";
    public const string SalesReturn = "Sales.Return";
    public const string SalesVoid = "Sales.Void";
    public const string CustomersManage = "Customers.Manage";

    public const string TransfersCreate = "Transfers.Create";
    public const string TransfersReview = "Transfers.Review";
    public const string VouchersCreate = "Vouchers.Create";
    public const string VouchersVoid = "Vouchers.Void";
    public const string ExpensesManage = "Expenses.Manage";
    public const string AccountsView = "Accounts.View";
    public const string AccountsManage = "Accounts.Manage";
    public const string AccountsViewNumbers = "Accounts.ViewNumbers";

    public const string ReportsSales = "Reports.Sales";
    public const string ReportsProfit = "Reports.Profit";
    public const string ReportsFinancial = "Reports.Financial";

    public const string UsersManage = "Users.Manage";
    public const string SettingsManage = "Settings.Manage";
    public const string AuditView = "Audit.View";

    public static readonly IReadOnlyList<PermissionInfo> All =
    [
        new(ProductsView, "عرض المنتجات والأصناف", "المنتجات"),
        new(ProductsViewCost, "عرض أسعار التكلفة", "المنتجات"),
        new(ProductsManage, "إضافة وتعديل المنتجات والأصناف", "المنتجات"),
        new(ProductsEditPrices, "تعديل أسعار البيع", "المنتجات"),

        new(InventoryView, "عرض المخزون وحركاته", "المخزون"),
        new(InventoryAdjust, "تسجيل التالف والمفقود وتعديل المخزون", "المخزون"),
        new(InventoryCount, "الجرد وتسوية المخزون", "المخزون"),

        new(PurchasesView, "عرض فواتير الشراء", "المشتريات"),
        new(PurchasesManage, "إنشاء واعتماد فواتير الشراء والمردودات", "المشتريات"),
        new(SuppliersManage, "إدارة الموردين", "المشتريات"),

        new(SalesCreate, "البيع وإنشاء الفواتير", "المبيعات"),
        new(SalesViewAll, "عرض فواتير جميع الموظفين", "المبيعات"),
        new(SalesDiscount, "تنفيذ الخصومات", "المبيعات"),
        new(SalesPriceOverride, "البيع بأقل من الحد الأدنى أو التكلفة", "المبيعات"),
        new(SalesReturn, "تسجيل مرتجعات المبيعات", "المبيعات"),
        new(SalesVoid, "إلغاء الفواتير", "المبيعات"),
        new(CustomersManage, "إدارة العملاء", "المبيعات"),

        new(TransfersCreate, "تسجيل الحوالات", "المالية"),
        new(TransfersReview, "تأكيد ورفض الحوالات", "المالية"),
        new(VouchersCreate, "إنشاء سندات القبض والصرف", "المالية"),
        new(VouchersVoid, "إلغاء السندات", "المالية"),
        new(ExpensesManage, "إدارة المصروفات", "المالية"),
        new(AccountsView, "عرض الحسابات المالية وأرصدتها", "المالية"),
        new(AccountsManage, "إدارة الحسابات المالية والتحويل بينها", "المالية"),
        new(AccountsViewNumbers, "عرض أرقام الحسابات والمحافظ كاملة", "المالية"),

        new(ReportsSales, "تقارير المبيعات", "التقارير"),
        new(ReportsProfit, "تقارير الأرباح والتكلفة", "التقارير"),
        new(ReportsFinancial, "التقارير المالية والذمم", "التقارير"),

        new(UsersManage, "إدارة المستخدمين والأدوار", "الإدارة"),
        new(SettingsManage, "إدارة الإعدادات", "الإدارة"),
        new(AuditView, "عرض سجل التدقيق", "الإدارة"),
    ];

    public static bool Exists(string code) => All.Any(p => p.Code == code);
}

/// <summary>Seeded roles and their default permissions.</summary>
public static class SystemRoles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Sales = "Sales";
    public const string Storekeeper = "Storekeeper";
    public const string Accountant = "Accountant";
    public const string Viewer = "Viewer";

    public sealed record RoleSeed(string Name, string DisplayName, string Description, string[] Permissions);

    public static readonly IReadOnlyList<RoleSeed> All =
    [
        new(Admin, "مدير النظام", "صلاحيات كاملة على النظام", Identity.Permissions.All.Select(p => p.Code).ToArray()),
        new(Manager, "مدير المعرض", "كل العمليات اليومية والمالية والتقارير",
            Identity.Permissions.All.Select(p => p.Code)
                .Where(c => c is not Identity.Permissions.UsersManage and not Identity.Permissions.SettingsManage)
                .ToArray()),
        new(Sales, "موظف مبيعات", "البيع وتسجيل الحوالات وسندات القبض",
        [
            Identity.Permissions.ProductsView, Identity.Permissions.InventoryView,
            Identity.Permissions.SalesCreate, Identity.Permissions.SalesDiscount, Identity.Permissions.SalesReturn,
            Identity.Permissions.CustomersManage, Identity.Permissions.TransfersCreate, Identity.Permissions.VouchersCreate,
            Identity.Permissions.AccountsView,
        ]),
        new(Storekeeper, "أمين مخزن", "المنتجات والمخزون والجرد واستلام المشتريات",
        [
            Identity.Permissions.ProductsView, Identity.Permissions.ProductsViewCost, Identity.Permissions.ProductsManage,
            Identity.Permissions.InventoryView, Identity.Permissions.InventoryAdjust, Identity.Permissions.InventoryCount,
            Identity.Permissions.PurchasesView, Identity.Permissions.PurchasesManage, Identity.Permissions.SuppliersManage,
        ]),
        new(Accountant, "محاسب", "السندات والمصروفات والحسابات والتقارير المالية",
        [
            Identity.Permissions.ProductsView, Identity.Permissions.ProductsViewCost, Identity.Permissions.InventoryView,
            Identity.Permissions.PurchasesView, Identity.Permissions.SalesViewAll, Identity.Permissions.CustomersManage,
            Identity.Permissions.SuppliersManage, Identity.Permissions.TransfersCreate, Identity.Permissions.TransfersReview,
            Identity.Permissions.VouchersCreate, Identity.Permissions.VouchersVoid, Identity.Permissions.ExpensesManage,
            Identity.Permissions.AccountsView, Identity.Permissions.AccountsManage, Identity.Permissions.AccountsViewNumbers,
            Identity.Permissions.ReportsSales, Identity.Permissions.ReportsProfit, Identity.Permissions.ReportsFinancial,
        ]),
        new(Viewer, "عرض فقط", "الاطلاع على المنتجات والمخزون وتقارير المبيعات",
        [
            Identity.Permissions.ProductsView, Identity.Permissions.InventoryView, Identity.Permissions.SalesViewAll,
            Identity.Permissions.ReportsSales,
        ]),
    ];
}
