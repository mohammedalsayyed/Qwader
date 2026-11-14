using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;

namespace Qwader.Application.Catalog;

/// <summary>Units counted in whole numbers only (<see cref="Domain.Catalog.Unit.WholeNumbersOnly"/>): 0.5 of a piece is refused.</summary>
public static class QuantityRules
{
    /// <summary>
    /// The problems with the given quantities, one sentence each (empty when all are fine). Each quantity is in the unit it was
    /// entered in: the product's own unit, or <c>ProductUnitId</c> (a dozen, a carton). Whole quantities cost no database query.
    /// </summary>
    public static async Task<List<string>> CheckAsync(IAppDbContext db, IEnumerable<(int ProductId, int? ProductUnitId, decimal Quantity)> lines, CancellationToken ct)
    {
        var fractional = lines.Where(l => l.Quantity != decimal.Truncate(l.Quantity)).ToList();
        if (fractional.Count == 0) return [];

        var productIds = fractional.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, Unit = p.Unit.Name, p.Unit.WholeNumbersOnly }).ToDictionaryAsync(p => p.Id, ct);
        var unitIds = fractional.Where(l => l.ProductUnitId is not null).Select(l => l.ProductUnitId!.Value).Distinct().ToList();
        var units = unitIds.Count == 0 ? []
            : await db.ProductUnits.AsNoTracking().Where(u => unitIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Unit.Name, u.Unit.WholeNumbersOnly }).ToDictionaryAsync(u => u.Id, ct);

        var problems = new List<string>();
        foreach (var l in fractional)
        {
            if (!products.TryGetValue(l.ProductId, out var p)) continue;
            var (unit, whole) = l.ProductUnitId is { } id && units.TryGetValue(id, out var u) ? (u.Name, u.WholeNumbersOnly) : (p.Unit, p.WholeNumbersOnly);
            if (whole) problems.Add($"«{p.Name}» يُعدّ بالـ«{unit}» بدون كسور: الكمية {l.Quantity:0.###} غير مقبولة");
        }
        return problems;
    }

    /// <summary>Refuses the request when a quantity is a fraction of a whole-number unit.</summary>
    public static async Task EnsureAsync(IAppDbContext db, IEnumerable<(int ProductId, int? ProductUnitId, decimal Quantity)> lines, CancellationToken ct)
    {
        var problems = await CheckAsync(db, lines, ct);
        if (problems.Count > 0) throw new ValidationFailedException("lines", problems[0]);
    }
}
