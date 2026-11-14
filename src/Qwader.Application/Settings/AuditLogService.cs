using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;

namespace Qwader.Application.Settings;

/// <param name="EntityLabel">What the entry is about, readable: «P-00402 · صحن …», an invoice number, a customer's name. Null when unknown.</param>
public sealed record AuditLogDto(long Id, DateTime OccurredAt, string? UserName, string Action, string ActionName, string EntityType,
    string? EntityId, string? OldValues, string? NewValues, string? Reason, string? IpAddress, string? EntityLabel = null);

public sealed class AuditLogRequest : PagedRequest
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    /// <summary>Prefix filter, e.g. «Sale» or «Transfer».</summary>
    public string? Area { get; set; }
}

public interface IAuditLogService
{
    Task<PagedResult<AuditLogDto>> ListAsync(AuditLogRequest request, CancellationToken ct = default);
}

public sealed class AuditLogService(IAppDbContext db, IClock clock) : IAuditLogService
{
    private static readonly Dictionary<string, string> Names = new()
    {
        ["Auth.Login"] = "تسجيل دخول",
        ["Auth.ChangePassword"] = "تغيير كلمة المرور",
        ["User.Create"] = "إضافة مستخدم",
        ["User.Update"] = "تعديل مستخدم",
        ["User.ResetPassword"] = "إعادة تعيين كلمة مرور",
        ["Role.Create"] = "إضافة دور",
        ["Role.Update"] = "تعديل صلاحيات دور",
        ["Role.Delete"] = "حذف دور",
        ["Settings.Update"] = "تعديل الإعدادات",
        ["Settings.ApplyReorderLevel"] = "تطبيق حد التنبيه على كل المنتجات",
        ["System.Reset"] = "تصفير النظام",
        ["System.Backup"] = "تنزيل نسخة احتياطية",
        ["AuditLog.Delete"] = "حذف من سجل التدقيق",
        ["Product.Create"] = "إضافة منتج",
        ["Product.Update"] = "تعديل منتج",
        ["Product.UpdatePrices"] = "تعديل أسعار منتج",
        ["Product.Archive"] = "أرشفة منتج",
        ["Product.Unarchive"] = "إلغاء أرشفة منتج",
        ["Product.Import"] = "استيراد منتجات",
        ["Product.CostCorrection"] = "تصحيح تكلفة منتج",
        ["Inventory.Adjust"] = "حركة مخزون يدوية",
        ["StockCount.Post"] = "اعتماد جرد",
        ["StockCount.Cancel"] = "إلغاء جرد",
        ["Purchase.CreateAndPost"] = "فاتورة شراء معتمدة",
        ["Purchase.CreateDraft"] = "مسودة شراء",
        ["Purchase.Post"] = "اعتماد فاتورة شراء",
        ["Purchase.Void"] = "إلغاء فاتورة شراء",
        ["Purchase.Return"] = "مردود مشتريات",
        ["Sale.Create"] = "فاتورة بيع",
        ["Sale.PriceOverride"] = "بيع بأقل من الحد الأدنى أو التكلفة",
        ["Sale.DiscountOverLimit"] = "خصم فوق الحد المسموح",
        ["Sale.Reviewed"] = "مراجعة فاتورة بدون إنترنت",
        ["Customer.Create"] = "إضافة عميل",
        ["Customer.Update"] = "تعديل عميل",
        ["Supplier.Create"] = "إضافة مورد",
        ["Supplier.Update"] = "تعديل مورد",
        ["Category.Create"] = "إضافة صنف",
        ["Unit.Create"] = "إضافة وحدة قياس",
        ["Category.Update"] = "تعديل صنف",
        ["Expense.Create"] = "إضافة مصروف",
        ["Expense.Void"] = "إلغاء مصروف",
        ["Payroll.AddEmployee"] = "إضافة عامل للرواتب",
        ["Payroll.UpdateEmployee"] = "تعديل بيانات عامل",
        ["Payroll.RemoveEmployee"] = "حذف عامل من الرواتب",
        ["Payroll.Entry"] = "سلفة أو خصم أو مكافأة",
        ["Payroll.VoidEntry"] = "إلغاء سلفة أو خصم أو مكافأة",
        ["Payroll.Settle"] = "مخالصة راتب",
        ["Payroll.VoidSettlement"] = "إلغاء مخالصة راتب",
        ["Sale.Return"] = "مرتجع مبيعات",
        ["Sale.Void"] = "إلغاء فاتورة بيع",
        ["Sale.Restore"] = "استعادة فاتورة بيع ملغاة",
        ["Sale.ReturnVoid"] = "إلغاء مرتجع بيع",
        ["Account.TransferVoid"] = "إلغاء تحويل بين حسابات",
        ["Unit.Update"] = "تعديل وحدة قياس",
        ["Account.Create"] = "إضافة حساب مالي",
        ["Account.Update"] = "تعديل حساب مالي",
        ["Account.SetOpeningBalance"] = "تعديل رصيد افتتاحي",
        ["Account.Transfer"] = "تحويل بين حسابات",
        ["Transfer.Create"] = "تسجيل حوالة",
        ["Transfer.Confirm"] = "تأكيد حوالة",
        ["Transfer.Reject"] = "رفض حوالة",
        ["Voucher.Receipt"] = "سند قبض",
        ["Voucher.Payment"] = "سند صرف",
        ["Voucher.VoidReceipt"] = "إلغاء سند قبض",
        ["Voucher.VoidPayment"] = "إلغاء سند صرف",
    };

    public async Task<PagedResult<AuditLogDto>> ListAsync(AuditLogRequest request, CancellationToken ct = default)
    {
        var query = db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(request.Area)) query = query.Where(a => a.Action.StartsWith(request.Area));
        if (request.From is not null || request.To is not null)
        {
            var (start, end) = clock.LocalDaysToUtcRange(request.From ?? new DateOnly(2000, 1, 1), request.To ?? clock.LocalToday);
            query = query.Where(a => a.OccurredAt >= start && a.OccurredAt < end);
        }
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim();
            query = query.Where(a => a.UserName!.Contains(s) || a.NewValues!.Contains(s) || a.Reason!.Contains(s) || a.EntityId == s);
        }

        var page = await query.OrderByDescending(a => a.Id).ToPagedAsync(request, ct);
        var labels = await LabelsAsync(page.Items, ct);
        return new PagedResult<AuditLogDto>(
            page.Items.Select(a => new AuditLogDto(a.Id, a.OccurredAt, a.UserName, a.Action, Names.GetValueOrDefault(a.Action, a.Action), a.EntityType,
                a.EntityId, a.OldValues, a.NewValues, a.Reason, a.IpAddress,
                int.TryParse(a.EntityId, out var id) ? labels.GetValueOrDefault((a.EntityType, id)) : null)).ToList(),
            page.Page, page.PageSize, page.TotalCount);
    }

    /// <summary>
    /// Readable names for the page's entries, looked up now (a later rename shows the new name). Nothing is stored or changed;
    /// a deleted record simply keeps showing its number.
    /// </summary>
    private async Task<Dictionary<(string Type, int Id), string>> LabelsAsync(IReadOnlyList<Domain.Administration.AuditLog> items, CancellationToken ct)
    {
        var ids = items.Where(a => int.TryParse(a.EntityId, out _)).GroupBy(a => a.EntityType)
            .ToDictionary(g => g.Key, g => g.Select(a => int.Parse(a.EntityId!)).Distinct().ToList());
        var labels = new Dictionary<(string, int), string>();
        async Task Add(string type, Func<List<int>, Task<Dictionary<int, string>>> load)
        {
            if (!ids.TryGetValue(type, out var list)) return;
            foreach (var (id, label) in await load(list)) labels[(type, id)] = label;
        }

        await Add("Product", async l => (await db.Products.AsNoTracking().Where(p => l.Contains(p.Id))
                .Select(p => new { p.Id, p.Sku, p.Name, p.Size, p.Color, p.Model }).ToListAsync(ct))
            .ToDictionary(p => p.Id, p => $"{p.Sku} · {Domain.Catalog.Product.BuildDisplayName(p.Name, p.Size, p.Color, p.Model)}"));
        await Add("SaleInvoice", l => db.SaleInvoices.AsNoTracking().Where(x => l.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => $"{x.Number} · {x.CustomerName}", ct));
        await Add("PurchaseInvoice", l => db.PurchaseInvoices.AsNoTracking().Where(x => l.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => $"{x.Number} · {x.Supplier.Name}", ct));
        await Add("ReceiptVoucher", l => db.ReceiptVouchers.AsNoTracking().Where(x => l.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Number, ct));
        await Add("PaymentVoucher", l => db.PaymentVouchers.AsNoTracking().Where(x => l.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Number, ct));
        await Add("IncomingTransfer", l => db.IncomingTransfers.AsNoTracking().Where(x => l.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Number, ct));
        await Add("Expense", l => db.Expenses.AsNoTracking().Where(x => l.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Number, ct));
        await Add("StockCount", l => db.StockCounts.AsNoTracking().Where(x => l.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Number, ct));
        await Add("Customer", l => db.Customers.AsNoTracking().Where(x => l.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => $"{x.Code} · {x.Name}", ct));
        await Add("Supplier", l => db.Suppliers.AsNoTracking().Where(x => l.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => $"{x.Code} · {x.Name}", ct));
        await Add("Category", l => db.Categories.AsNoTracking().Where(x => l.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct));
        await Add("Unit", l => db.Units.AsNoTracking().Where(x => l.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct));
        await Add("FinancialAccount", l => db.FinancialAccounts.AsNoTracking().Where(x => l.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, ct));
        await Add("User", l => db.Users.AsNoTracking().Where(x => l.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.FullName, ct));
        return labels;
    }
}
