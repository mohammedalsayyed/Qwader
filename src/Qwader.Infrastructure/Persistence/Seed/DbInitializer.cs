using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Qwader.Application.Abstractions;
using Qwader.Domain.Administration;
using Qwader.Domain.Catalog;
using Qwader.Domain.Finance;
using Qwader.Domain.Identity;
using Qwader.Domain.Inventory;

namespace Qwader.Infrastructure.Persistence.Seed;

/// <summary>Applies migrations and seeds reference data. Every step is idempotent and safe to run on each start.</summary>
public static class DbInitializer
{
    // Permissions that existed at the last seed. Codes added in later releases are granted to the
    // default roles once; permissions an administrator removed afterwards are not re-added.
    private const string KnownPermissionsKey = "Seed.KnownPermissions";

    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DbInitializer));

        await db.Database.MigrateAsync(ct);

        await SeedRolesAsync(db, ct);
        await SeedAdminAsync(db, sp, logger, ct);
        await SeedReferenceDataAsync(db, ct);
    }

    private static async Task SeedRolesAsync(AppDbContext db, CancellationToken ct)
    {
        var knownSetting = await db.Settings.FirstOrDefaultAsync(s => s.Key == KnownPermissionsKey, ct);
        var known = knownSetting?.Value.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet() ?? [];

        foreach (var seed in SystemRoles.All)
        {
            var role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Name == seed.Name, ct);
            if (role is null)
            {
                role = new AppRole
                {
                    Name = seed.Name,
                    NormalizedName = seed.Name.ToUpperInvariant(),
                    ConcurrencyStamp = Guid.NewGuid().ToString(),
                    DisplayName = seed.DisplayName,
                    Description = seed.Description,
                    IsSystem = true,
                };
                db.Roles.Add(role);
            }

            var grant = role.Name == SystemRoles.Admin
                ? seed.Permissions // the administrator always holds everything
                : role.Id == 0 ? seed.Permissions : seed.Permissions.Where(p => !known.Contains(p));

            foreach (var code in grant.Where(c => role.Permissions.All(p => p.PermissionCode != c)))
                role.Permissions.Add(new RolePermission { PermissionCode = code });
        }

        var all = string.Join(',', Permissions.All.Select(p => p.Code));
        if (knownSetting is null) db.Settings.Add(new Setting { Key = KnownPermissionsKey, Value = all });
        else knownSetting.Value = all;

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedAdminAsync(AppDbContext db, IServiceProvider sp, ILogger logger, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct)) return;

        var config = sp.GetRequiredService<IConfiguration>();
        var userName = config["Seed:AdminUserName"] ?? "admin";
        var password = config["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException(
                "No users exist yet. Provide the first administrator password with user-secrets (Seed:AdminPassword) " +
                "or the Seed__AdminPassword environment variable.");

        var userManager = sp.GetRequiredService<UserManager<AppUser>>();
        var clock = sp.GetRequiredService<IClock>();
        var admin = new AppUser
        {
            UserName = userName,
            FullName = "مدير النظام",
            IsActive = true,
            MustChangePassword = !config.GetValue<bool>("Seed:SkipPasswordChange"),
            CreatedAt = clock.UtcNow,
            LockoutEnabled = true,
        };

        var result = await userManager.CreateAsync(admin, password);
        if (!result.Succeeded)
            throw new InvalidOperationException("Could not create the administrator: " + string.Join("; ", result.Errors.Select(e => e.Description)));

        var adminRole = await db.Roles.FirstAsync(r => r.Name == SystemRoles.Admin, ct);
        db.UserRoles.Add(new AppUserRole { UserId = admin.Id, RoleId = adminRole.Id });
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Created initial administrator '{UserName}'", userName);
    }

    private static async Task SeedReferenceDataAsync(AppDbContext db, CancellationToken ct)
    {
        if (!await db.Units.AnyAsync(ct))
            db.Units.AddRange(new[] { "قطعة", "علبة", "كرتونة", "طقم", "دزينة", "متر", "كيلو", "لتر" }
                .Select(n => new Unit { Name = n }));

        if (!await db.Warehouses.AnyAsync(ct))
            db.Warehouses.Add(new Warehouse { Name = "المعرض الرئيسي", IsDefault = true });

        if (!await db.ExpenseCategories.AnyAsync(ct))
            db.ExpenseCategories.AddRange(new[] { "الإيجار", "الكهرباء", "النقل", "الرواتب", "الصيانة", "الشحن", "مصروفات أخرى" }
                .Select(n => new ExpenseCategory { Name = n }));

        if (!await db.FinancialAccounts.AnyAsync(ct))
            db.FinancialAccounts.AddRange(
                new FinancialAccount { Name = "الصندوق النقدي", Type = FinancialAccountType.Cash },
                new FinancialAccount { Name = "جوال بي", Type = FinancialAccountType.Wallet, ProviderName = "جوال بي" },
                new FinancialAccount { Name = "بنك فلسطين", Type = FinancialAccountType.Bank, ProviderName = "بنك فلسطين" },
                new FinancialAccount { Name = "بنك القدس", Type = FinancialAccountType.Bank, ProviderName = "بنك القدس" },
                new FinancialAccount { Name = "البنك الإسلامي", Type = FinancialAccountType.Bank, ProviderName = "البنك الإسلامي" },
                new FinancialAccount { Name = "بال بي", Type = FinancialAccountType.Wallet, ProviderName = "PalPay" });

        await db.SaveChangesAsync(ct);
    }
}
