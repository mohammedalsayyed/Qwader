using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;

namespace Qwader.Application.Settings;

public sealed record AuditLogDto(long Id, DateTime OccurredAt, string? UserName, string Action, string ActionName, string EntityType,
    string? EntityId, string? OldValues, string? NewValues, string? Reason, string? IpAddress);

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
        ["System.Reset"] = "تصفير النظام",
        ["Product.Create"] = "إضافة منتج",
        ["Product.Update"] = "تعديل منتج",
        ["Product.UpdatePrices"] = "تعديل أسعار منتج",
        ["Product.Archive"] = "أرشفة منتج",
        ["Product.Unarchive"] = "إلغاء أرشفة منتج",
        ["Product.Import"] = "استيراد منتجات",
        ["Inventory.Adjust"] = "حركة مخزون يدوية",
        ["StockCount.Post"] = "اعتماد جرد",
        ["StockCount.Cancel"] = "إلغاء جرد",
        ["Purchase.CreateAndPost"] = "فاتورة شراء معتمدة",
        ["Purchase.CreateDraft"] = "مسودة شراء",
        ["Purchase.Post"] = "اعتماد فاتورة شراء",
        ["Purchase.Void"] = "إلغاء فاتورة شراء",
        ["Purchase.Return"] = "مردود مشتريات",
        ["Sale.PriceOverride"] = "بيع بأقل من الحد الأدنى",
        ["Sale.Return"] = "مرتجع مبيعات",
        ["Sale.Void"] = "إلغاء فاتورة بيع",
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
        return new PagedResult<AuditLogDto>(
            page.Items.Select(a => new AuditLogDto(a.Id, a.OccurredAt, a.UserName, a.Action, Names.GetValueOrDefault(a.Action, a.Action), a.EntityType,
                a.EntityId, a.OldValues, a.NewValues, a.Reason, a.IpAddress)).ToList(),
            page.Page, page.PageSize, page.TotalCount);
    }
}
