using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Application.Reports;
using Qwader.Domain.Administration;
using Qwader.Domain.Finance;
using Qwader.Domain.Purchasing;

namespace Qwader.Application.Settings;

public sealed record ClosedYearDto(int Id, int Year, DateTime ClosedAt, string? ClosedBy, decimal NetProfit, decimal TotalAssets,
    decimal TotalLiabilities, decimal TotalEquity, decimal Difference, DateTime? ReopenedAt, string? ReopenedBy, string? ReopenReason);

/// <param name="LockedThrough">The last closed year (every year up to it is locked), when the feature is on.</param>
/// <param name="NextYear">The year that would be closed next.</param>
/// <param name="Blockers">What has to be finished in <paramref name="NextYear"/> before it can be closed.</param>
public sealed record YearClosingStatusDto(bool Enabled, int FirstYear, int? LockedThrough, int NextYear, bool YearEnded,
    IReadOnlyList<string> Blockers, bool CanClose, IReadOnlyList<ClosedYearDto> History);

public sealed record SetYearClosingRequest(bool Enabled);
/// <param name="Year">Typed by the user to confirm (must be the year that closes next).</param>
public sealed record CloseYearRequest(int Year);
public sealed record ReopenYearRequest(string Reason);

public interface IYearClosingService
{
    Task<YearClosingStatusDto> StatusAsync(CancellationToken ct = default);
    Task<YearClosingStatusDto> SetEnabledAsync(SetYearClosingRequest request, CancellationToken ct = default);
    Task<YearClosingStatusDto> CloseAsync(CloseYearRequest request, CancellationToken ct = default);
    Task<YearClosingStatusDto> ReopenAsync(int year, ReopenYearRequest request, CancellationToken ct = default);
}

/// <summary>
/// «إقفال السنة» (off by default). Closing a year keeps its balance sheet on its last day and locks it: nothing dated in it can be
/// recorded, edited, voided or restored any more (the check is in the database context's save, so it covers every screen).
/// Nothing is moved or deleted: balances simply carry on into the next year, and its documents and reports stay as they were.
/// </summary>
public sealed class YearClosingService(IAppDbContext db, ICurrentUser currentUser, IClock clock, IAuditService audit, IBalanceSheetService sheets)
    : IYearClosingService
{
    private async Task<bool> EnabledAsync(CancellationToken ct) =>
        await db.Settings.AsNoTracking().Where(s => s.Key == SettingKeys.YearClosing).Select(s => s.Value).FirstOrDefaultAsync(ct) == "true";

    private async Task<int?> LockedThroughAsync(CancellationToken ct) =>
        await db.ClosedYears.AsNoTracking().Where(y => y.ReopenedAt == null).MaxAsync(y => (int?)y.Year, ct);

    public async Task<YearClosingStatusDto> StatusAsync(CancellationToken ct = default)
    {
        var enabled = await EnabledAsync(ct);
        var first = await db.Users.MinAsync(u => (DateTime?)u.CreatedAt, ct) is { } created ? clock.ToLocal(created).Year : clock.LocalToday.Year;
        var locked = await LockedThroughAsync(ct);
        var next = Math.Max(first, (locked ?? first - 1) + 1);
        var ended = next < clock.LocalToday.Year;

        var blockers = new List<string>();
        if (ended)
        {
            var (_, end) = clock.LocalDaysToUtcRange(new DateOnly(next, 12, 31), new DateOnly(next, 12, 31));
            var pending = await db.IncomingTransfers.CountAsync(t => t.Status == TransferStatus.Pending && t.TransferDate < end, ct);
            if (pending > 0) blockers.Add($"{pending} حوالة بانتظار التحقق من سنة {next} أو قبلها: أكّدها أو ارفضها أولًا");
            var drafts = await db.PurchaseInvoices.CountAsync(p => p.Status == PurchaseStatus.Draft && p.Date < end, ct);
            if (drafts > 0) blockers.Add($"{drafts} فاتورة شراء مسودة بتاريخ في سنة {next} أو قبلها: اعتمدها أو احذفها أولًا");
        }

        var rows = await db.ClosedYears.AsNoTracking().OrderByDescending(y => y.Id).ToListAsync(ct);
        var userIds = rows.SelectMany(r => new[] { r.ClosedById, r.ReopenedById }).OfType<int>().Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        string? Name(int? id) => id is { } i ? names.GetValueOrDefault(i) : null;
        var history = rows.Select(r => new ClosedYearDto(r.Id, r.Year, r.ClosedAt, Name(r.ClosedById), r.NetProfit, r.TotalAssets, r.TotalLiabilities,
            r.TotalEquity, r.Difference, r.ReopenedAt, Name(r.ReopenedById), r.ReopenReason)).ToList();

        return new YearClosingStatusDto(enabled, first, enabled ? locked : null, next, ended, blockers, enabled && ended && blockers.Count == 0, history);
    }

    public async Task<YearClosingStatusDto> SetEnabledAsync(SetYearClosingRequest request, CancellationToken ct = default)
    {
        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Key == SettingKeys.YearClosing, ct);
        var before = setting?.Value == "true";
        if (setting is null) db.Settings.Add(setting = new Setting { Key = SettingKeys.YearClosing });
        setting.Value = request.Enabled ? "true" : "false";
        setting.UpdatedAt = clock.UtcNow;
        setting.UpdatedById = currentUser.UserId;
        if (before != request.Enabled) audit.Log("YearClosing.Toggle", "Settings", null, new { Enabled = before }, new { request.Enabled });
        await db.SaveChangesAsync(ct);
        return await StatusAsync(ct);
    }

    public async Task<YearClosingStatusDto> CloseAsync(CloseYearRequest request, CancellationToken ct = default)
    {
        var status = await StatusAsync(ct);
        if (!status.Enabled) throw new BusinessRuleException("إقفال السنة غير مفعّل في الإعدادات");
        if (request.Year != status.NextYear)
            throw new ValidationFailedException("year", $"السنة التي تُقفل الآن هي {status.NextYear} (تُقفل السنوات بالترتيب)");
        if (!status.YearEnded) throw new BusinessRuleException($"سنة {status.NextYear} لم تنتهِ بعد: تُقفل بعد 31/12/{status.NextYear}");
        if (status.Blockers.Count > 0) throw new BusinessRuleException(string.Join("؛ ", status.Blockers));

        BalanceSheetDto sheet;
        try
        {
            sheet = await sheets.AsOfAsync(new DateOnly(request.Year, 12, 31), ct);
        }
        catch (ForbiddenException)
        {
            throw new ForbiddenException("إقفال السنة يحتاج صلاحيات التقارير المالية وعرض الأرباح وعرض التكلفة (لحفظ ميزانية آخر يوم)");
        }

        await using var tx = await TxScope.BeginAsync(db, ct);
        if (await LockedThroughAsync(ct) is { } locked && locked >= request.Year) throw new BusinessRuleException($"سنة {request.Year} مقفلة من قبل");
        var row = new ClosedYear
        {
            Year = request.Year,
            ClosedAt = clock.UtcNow,
            ClosedById = currentUser.UserId,
            NetProfit = sheet.YearProfit.LastOrDefault()?.Amount ?? 0,
            TotalAssets = sheet.TotalAssets,
            TotalLiabilities = sheet.TotalLiabilities,
            TotalEquity = sheet.TotalEquity,
            Difference = sheet.Difference,
            Snapshot = JsonSerializer.Serialize(sheet),
        };
        db.ClosedYears.Add(row);
        audit.Log("YearClosing.Close", "ClosedYear", request.Year.ToString(), null,
            new { row.Year, row.NetProfit, row.TotalAssets, row.TotalLiabilities, row.TotalEquity, row.Difference });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await StatusAsync(ct);
    }

    public async Task<YearClosingStatusDto> ReopenAsync(int year, ReopenYearRequest request, CancellationToken ct = default)
    {
        var reason = request.Reason?.Trim() ?? "";
        if (reason.Length < 3) throw new ValidationFailedException("reason", "اكتب سبب إعادة فتح السنة");
        var locked = await LockedThroughAsync(ct);
        if (locked != year)
            throw new BusinessRuleException(locked is null ? "لا توجد سنة مقفلة" : $"يُعاد فتح آخر سنة مقفلة فقط ({locked})");
        var row = await db.ClosedYears.Where(y => y.Year == year && y.ReopenedAt == null).OrderByDescending(y => y.Id).FirstAsync(ct);
        row.ReopenedAt = clock.UtcNow;
        row.ReopenedById = currentUser.UserId;
        row.ReopenReason = reason;
        audit.Log("YearClosing.Reopen", "ClosedYear", year.ToString(), null, new { Year = year }, reason);
        await db.SaveChangesAsync(ct);
        return await StatusAsync(ct);
    }
}
