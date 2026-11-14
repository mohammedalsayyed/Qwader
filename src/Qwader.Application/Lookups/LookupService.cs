using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Domain.Catalog;
using Qwader.Domain.Finance;

namespace Qwader.Application.Lookups;

public sealed record LookupItem(int Id, string Name);

public sealed record AccountLookupItem(int Id, string Name, FinancialAccountType Type);

/// <summary>A unit of measure; <see cref="WholeNumbersOnly"/>: no fractions of it (a piece, a set).</summary>
public sealed record UnitLookupItem(int Id, string Name, bool WholeNumbersOnly);

public sealed record LookupsDto(
    IReadOnlyList<UnitLookupItem> Units,
    IReadOnlyList<LookupItem> Warehouses,
    IReadOnlyList<LookupItem> ExpenseCategories,
    IReadOnlyList<LookupItem> Categories,
    IReadOnlyList<AccountLookupItem> Accounts);

public interface ILookupService
{
    Task<LookupsDto> GetAsync(CancellationToken ct = default);
    /// <summary>A unit of measure added from a product form; an existing one with that name is returned (and shown again if hidden).</summary>
    Task<LookupItem> AddUnitAsync(string name, CancellationToken ct = default);
    /// <summary>Whether the unit is counted in whole numbers only.</summary>
    Task<UnitLookupItem> SetUnitWholeNumbersAsync(int id, bool wholeNumbersOnly, CancellationToken ct = default);
}

/// <summary>Small reference lists the client caches once per session.</summary>
public sealed class LookupService(IAppDbContext db, IAuditService audit) : ILookupService
{
    public async Task<LookupItem> AddUnitAsync(string name, CancellationToken ct = default)
    {
        var clean = string.Join(' ', (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (clean.Length is 0 or > 30) throw new ValidationFailedException("name", "اكتب اسم الوحدة (30 حرفًا على الأكثر)");
        var unit = await db.Units.FirstOrDefaultAsync(u => u.Name == clean, ct);
        if (unit is null)
        {
            unit = new Unit { Name = clean };
            db.Units.Add(unit);
            audit.Log("Unit.Create", "Unit", null, null, new { unit.Name });
        }
        unit.IsActive = true;
        await db.SaveChangesAsync(ct);
        return new LookupItem(unit.Id, unit.Name);
    }

    public async Task<UnitLookupItem> SetUnitWholeNumbersAsync(int id, bool wholeNumbersOnly, CancellationToken ct = default)
    {
        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new NotFoundException("الوحدة غير موجودة");
        if (unit.WholeNumbersOnly != wholeNumbersOnly)
        {
            audit.Log("Unit.Update", "Unit", unit.Id.ToString(), new { unit.WholeNumbersOnly }, new { WholeNumbersOnly = wholeNumbersOnly });
            unit.WholeNumbersOnly = wholeNumbersOnly;
            await db.SaveChangesAsync(ct);
        }
        return new UnitLookupItem(unit.Id, unit.Name, unit.WholeNumbersOnly);
    }

    public async Task<LookupsDto> GetAsync(CancellationToken ct = default) => new(
        await db.Units.Where(u => u.IsActive).OrderBy(u => u.Id).Select(u => new UnitLookupItem(u.Id, u.Name, u.WholeNumbersOnly)).ToListAsync(ct),
        await db.Warehouses.Where(w => w.IsActive).OrderByDescending(w => w.IsDefault).ThenBy(w => w.Id)
            .Select(w => new LookupItem(w.Id, w.Name)).ToListAsync(ct),
        await db.ExpenseCategories.Where(c => c.IsActive).OrderBy(c => c.Id).Select(c => new LookupItem(c.Id, c.Name)).ToListAsync(ct),
        await db.Categories.Where(c => c.IsActive).OrderBy(c => c.Name).Select(c => new LookupItem(c.Id, c.Name)).ToListAsync(ct),
        // Names and types only; account numbers are never part of lookups.
        await db.FinancialAccounts.Where(a => a.IsActive).OrderBy(a => a.Type).ThenBy(a => a.Id)
            .Select(a => new AccountLookupItem(a.Id, a.Name, a.Type)).ToListAsync(ct));
}
