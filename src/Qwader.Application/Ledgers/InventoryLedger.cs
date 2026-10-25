using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Domain.Inventory;

namespace Qwader.Application.Ledgers;

public sealed record StockEntry(
    int ProductId,
    int WarehouseId,
    decimal Quantity,
    StockMovementType Type,
    string SourceType,
    int? SourceId,
    string? SourceNumber,
    string? Notes = null);

public sealed record StockPosting(decimal UnitCost, decimal BalanceAfter, decimal AverageCostAfter);

public interface IInventoryLedger
{
    /// <summary>Stock in at <paramref name="unitCost"/>; re-averages the product's cost.</summary>
    Task<StockPosting> ReceiveAsync(StockEntry entry, decimal unitCost, CancellationToken ct = default);

    /// <summary>
    /// Stock out. Leaves at the current average unless <paramref name="atCost"/> is given (purchase returns/voids),
    /// in which case the average is recomputed. Fails without side effects when stock is insufficient.
    /// </summary>
    Task<StockPosting> IssueAsync(StockEntry entry, decimal? atCost = null, CancellationToken ct = default);
}

/// <summary>
/// The only writer of StockBalances, StockMovements and Products.AverageCost. Every call locks the product row
/// (UPDLOCK) for the rest of the caller's transaction, so concurrent postings for one product are serialized.
/// Callers touching several products must post them in ascending ProductId order to avoid deadlocks.
/// </summary>
public sealed class InventoryLedger(IAppDbContext db, ICurrentUser currentUser, IClock clock) : IInventoryLedger
{
    public async Task<StockPosting> ReceiveAsync(StockEntry entry, decimal unitCost, CancellationToken ct = default)
    {
        EnsureTransaction();
        if (entry.Quantity <= 0) throw new ArgumentOutOfRangeException(nameof(entry), "Quantity must be positive.");
        if (unitCost < 0) throw new ArgumentOutOfRangeException(nameof(unitCost));

        var (average, onHand) = await LockProductAsync(entry.ProductId, ct);
        var newAverage = CostMath.AverageAfterReceipt(onHand, average, entry.Quantity, unitCost);

        var balance = (await db.Database.SqlQuery<decimal>($"""
            MERGE StockBalances WITH (HOLDLOCK) AS t
            USING (SELECT {entry.ProductId} AS ProductId, {entry.WarehouseId} AS WarehouseId) AS s
                ON t.ProductId = s.ProductId AND t.WarehouseId = s.WarehouseId
            WHEN MATCHED THEN UPDATE SET Quantity = t.Quantity + {entry.Quantity}
            WHEN NOT MATCHED THEN INSERT (ProductId, WarehouseId, Quantity) VALUES (s.ProductId, s.WarehouseId, {entry.Quantity})
            OUTPUT inserted.Quantity AS Value;
            """).ToListAsync(ct)).Single();

        if (newAverage != average)
            await db.Database.ExecuteSqlAsync($"UPDATE Products SET AverageCost = {newAverage} WHERE Id = {entry.ProductId}", ct);

        AddMovement(entry, entry.Quantity, unitCost, balance, newAverage);
        return new StockPosting(unitCost, balance, newAverage);
    }

    public async Task<StockPosting> IssueAsync(StockEntry entry, decimal? atCost = null, CancellationToken ct = default)
    {
        EnsureTransaction();
        if (entry.Quantity <= 0) throw new ArgumentOutOfRangeException(nameof(entry), "Quantity must be positive.");

        var (average, onHand) = await LockProductAsync(entry.ProductId, ct);

        var balances = await db.Database.SqlQuery<decimal>($"""
            UPDATE StockBalances SET Quantity = Quantity - {entry.Quantity}
            OUTPUT inserted.Quantity AS Value
            WHERE ProductId = {entry.ProductId} AND WarehouseId = {entry.WarehouseId} AND Quantity >= {entry.Quantity};
            """).ToListAsync(ct);

        if (balances.Count == 0)
        {
            var available = await db.StockBalances
                .Where(s => s.ProductId == entry.ProductId && s.WarehouseId == entry.WarehouseId)
                .Select(s => (decimal?)s.Quantity).FirstOrDefaultAsync(ct) ?? 0;
            var product = await db.Products.Where(p => p.Id == entry.ProductId)
                .Select(p => new { p.Name, p.Size, p.Color, p.Model }).FirstAsync(ct);
            var name = Domain.Catalog.Product.BuildDisplayName(product.Name, product.Size, product.Color, product.Model);
            throw new BusinessRuleException($"الكمية المتوفرة من «{name}» هي {available:0.###} فقط، ولا تكفي لخصم {entry.Quantity:0.###}");
        }

        var unitCost = atCost ?? average;
        var newAverage = atCost is null ? average : CostMath.AverageAfterRemovalAtCost(onHand, average, entry.Quantity, atCost.Value);
        if (newAverage != average)
            await db.Database.ExecuteSqlAsync($"UPDATE Products SET AverageCost = {newAverage} WHERE Id = {entry.ProductId}", ct);

        AddMovement(entry, -entry.Quantity, unitCost, balances[0], newAverage);
        return new StockPosting(unitCost, balances[0], newAverage);
    }

    private async Task<(decimal Average, decimal OnHand)> LockProductAsync(int productId, CancellationToken ct)
    {
        var average = (await db.Database.SqlQuery<decimal>(
            $"SELECT AverageCost AS Value FROM Products WITH (UPDLOCK, ROWLOCK) WHERE Id = {productId}").ToListAsync(ct)).SingleOrDefault();
        var onHand = (await db.Database.SqlQuery<decimal>(
            $"SELECT ISNULL(SUM(Quantity), 0) AS Value FROM StockBalances WHERE ProductId = {productId}").ToListAsync(ct)).Single();
        return (average, onHand);
    }

    private void AddMovement(StockEntry entry, decimal signedQuantity, decimal unitCost, decimal balanceAfter, decimal averageAfter) =>
        db.StockMovements.Add(new StockMovement
        {
            ProductId = entry.ProductId,
            WarehouseId = entry.WarehouseId,
            Type = entry.Type,
            Quantity = signedQuantity,
            UnitCost = unitCost,
            BalanceAfter = balanceAfter,
            AverageCostAfter = averageAfter,
            SourceType = entry.SourceType,
            SourceId = entry.SourceId,
            SourceNumber = entry.SourceNumber,
            Notes = entry.Notes,
            OccurredAt = clock.UtcNow,
            UserId = currentUser.UserId,
        });

    private void EnsureTransaction()
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Stock postings must run inside a database transaction.");
    }
}
