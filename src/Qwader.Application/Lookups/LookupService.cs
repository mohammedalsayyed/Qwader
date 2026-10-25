using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Domain.Finance;

namespace Qwader.Application.Lookups;

public sealed record LookupItem(int Id, string Name);

public sealed record AccountLookupItem(int Id, string Name, FinancialAccountType Type);

public sealed record LookupsDto(
    IReadOnlyList<LookupItem> Units,
    IReadOnlyList<LookupItem> Warehouses,
    IReadOnlyList<LookupItem> ExpenseCategories,
    IReadOnlyList<LookupItem> Categories,
    IReadOnlyList<AccountLookupItem> Accounts);

public interface ILookupService
{
    Task<LookupsDto> GetAsync(CancellationToken ct = default);
}

/// <summary>Small reference lists the client caches once per session.</summary>
public sealed class LookupService(IAppDbContext db) : ILookupService
{
    public async Task<LookupsDto> GetAsync(CancellationToken ct = default) => new(
        await db.Units.Where(u => u.IsActive).OrderBy(u => u.Id).Select(u => new LookupItem(u.Id, u.Name)).ToListAsync(ct),
        await db.Warehouses.Where(w => w.IsActive).OrderByDescending(w => w.IsDefault).ThenBy(w => w.Id)
            .Select(w => new LookupItem(w.Id, w.Name)).ToListAsync(ct),
        await db.ExpenseCategories.Where(c => c.IsActive).OrderBy(c => c.Id).Select(c => new LookupItem(c.Id, c.Name)).ToListAsync(ct),
        await db.Categories.Where(c => c.IsActive).OrderBy(c => c.Name).Select(c => new LookupItem(c.Id, c.Name)).ToListAsync(ct),
        // Names and types only; account numbers are never part of lookups.
        await db.FinancialAccounts.Where(a => a.IsActive).OrderBy(a => a.Type).ThenBy(a => a.Id)
            .Select(a => new AccountLookupItem(a.Id, a.Name, a.Type)).ToListAsync(ct));
}
