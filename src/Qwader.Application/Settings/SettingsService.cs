using System.Globalization;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Domain.Administration;

namespace Qwader.Application.Settings;

public sealed record SettingsDto(
    string StoreName,
    string? StoreAddress,
    string? StorePhone,
    string? InvoiceFooter,
    decimal DefaultMarginPercent,
    decimal MaxDiscountPercent,
    decimal DefaultReorderLevel,
    bool SaleTransfersPending = true);

public interface ISettingsService
{
    Task<SettingsDto> GetAsync(CancellationToken ct = default);
    Task<SettingsDto> UpdateAsync(SettingsDto request, CancellationToken ct = default);

    /// <summary>
    /// The default «حد التنبيه» only fills in new products; this sets it on every existing product as well.
    /// Returns how many products changed.
    /// </summary>
    Task<int> ApplyDefaultReorderLevelAsync(CancellationToken ct = default);
}

public sealed class SettingsDtoValidator : AbstractValidator<SettingsDto>
{
    public SettingsDtoValidator()
    {
        RuleFor(x => x.StoreName).NotEmpty().WithMessage("اسم المعرض مطلوب").MaximumLength(100);
        RuleFor(x => x.StoreAddress).MaximumLength(200);
        RuleFor(x => x.StorePhone).MaximumLength(30);
        RuleFor(x => x.InvoiceFooter).MaximumLength(300);
        RuleFor(x => x.DefaultMarginPercent).InclusiveBetween(0, 1000).WithMessage("نسبة الربح يجب أن تكون بين 0 و1000");
        RuleFor(x => x.MaxDiscountPercent).InclusiveBetween(0, 100).WithMessage("نسبة الخصم يجب أن تكون بين 0 و100");
        RuleFor(x => x.DefaultReorderLevel).GreaterThanOrEqualTo(0).WithMessage("حد التنبيه لا يمكن أن يكون سالبًا");
    }
}

public sealed class SettingsService(IAppDbContext db, ICurrentUser currentUser, IClock clock, IAuditService audit) : ISettingsService
{
    public static readonly IReadOnlyDictionary<string, string> Defaults = new Dictionary<string, string>
    {
        [SettingKeys.StoreName] = "معرض قويدر للأدوات والأجهزة المنزلية",
        [SettingKeys.StoreAddress] = "",
        [SettingKeys.StorePhone] = "",
        [SettingKeys.InvoiceFooter] = "شكرًا لزيارتكم",
        [SettingKeys.DefaultMarginPercent] = "25",
        [SettingKeys.MaxDiscountPercent] = "10",
        [SettingKeys.LowStockDefault] = "5",
        [SettingKeys.SaleTransfersPending] = "true",
    };

    public async Task<SettingsDto> GetAsync(CancellationToken ct = default)
    {
        var values = await LoadAsync(ct);
        return ToDto(values);
    }

    public async Task<int> ApplyDefaultReorderLevelAsync(CancellationToken ct = default)
    {
        var level = (await GetAsync(ct)).DefaultReorderLevel;
        var changed = await db.Products.Where(p => p.ReorderLevel != level)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ReorderLevel, level), ct);
        audit.Log("Settings.ApplyReorderLevel", "Product", null, null, new { ReorderLevel = level, Products = changed });
        await db.SaveChangesAsync(ct);
        return changed;
    }

    public async Task<SettingsDto> UpdateAsync(SettingsDto request, CancellationToken ct = default)
    {
        var before = await GetAsync(ct);
        var incoming = new Dictionary<string, string>
        {
            [SettingKeys.StoreName] = request.StoreName.Trim(),
            [SettingKeys.StoreAddress] = request.StoreAddress?.Trim() ?? "",
            [SettingKeys.StorePhone] = request.StorePhone?.Trim() ?? "",
            [SettingKeys.InvoiceFooter] = request.InvoiceFooter?.Trim() ?? "",
            [SettingKeys.DefaultMarginPercent] = request.DefaultMarginPercent.ToString(CultureInfo.InvariantCulture),
            [SettingKeys.MaxDiscountPercent] = request.MaxDiscountPercent.ToString(CultureInfo.InvariantCulture),
            [SettingKeys.LowStockDefault] = request.DefaultReorderLevel.ToString(CultureInfo.InvariantCulture),
            [SettingKeys.SaleTransfersPending] = request.SaleTransfersPending ? "true" : "false",
        };

        var existing = await db.Settings.ToDictionaryAsync(s => s.Key, ct);
        foreach (var (key, value) in incoming)
        {
            if (!existing.TryGetValue(key, out var setting))
            {
                setting = new Setting { Key = key };
                db.Settings.Add(setting);
            }
            setting.Value = value;
            setting.UpdatedAt = clock.UtcNow;
            setting.UpdatedById = currentUser.UserId;
        }

        var after = ToDto(incoming);
        audit.Log("Settings.Update", "Settings", null, before, after);
        await db.SaveChangesAsync(ct);
        return after;
    }

    private async Task<Dictionary<string, string>> LoadAsync(CancellationToken ct)
    {
        var values = new Dictionary<string, string>(Defaults);
        foreach (var s in await db.Settings.AsNoTracking().ToListAsync(ct)) values[s.Key] = s.Value;
        return values;
    }

    private static SettingsDto ToDto(IReadOnlyDictionary<string, string> v) => new(
        v[SettingKeys.StoreName],
        NullIfEmpty(v[SettingKeys.StoreAddress]),
        NullIfEmpty(v[SettingKeys.StorePhone]),
        NullIfEmpty(v[SettingKeys.InvoiceFooter]),
        decimal.Parse(v[SettingKeys.DefaultMarginPercent], CultureInfo.InvariantCulture),
        decimal.Parse(v[SettingKeys.MaxDiscountPercent], CultureInfo.InvariantCulture),
        decimal.Parse(v[SettingKeys.LowStockDefault], CultureInfo.InvariantCulture),
        v[SettingKeys.SaleTransfersPending] != "false");

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
