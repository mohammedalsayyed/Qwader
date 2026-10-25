using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Application.Ledgers;
using Qwader.Domain.Administration;
using Qwader.Domain.Catalog;
using Qwader.Domain.Identity;
using Qwader.Domain.Inventory;

namespace Qwader.Application.Inventory;

public enum AdjustmentKind
{
    Damaged = 1,
    Lost = 2,
    Found = 3,
}

public sealed record StockAdjustmentRequest(Guid IdempotencyKey, int ProductId, int? WarehouseId, AdjustmentKind Kind, decimal Quantity, string? Notes);

public sealed record StockAdjustmentResult(string Number, decimal BalanceAfter);

public sealed record StockMovementDto(
    long Id,
    DateTime OccurredAt,
    int ProductId,
    string ProductName,
    string Sku,
    StockMovementType Type,
    decimal Quantity,
    decimal BalanceAfter,
    decimal? UnitCost,
    string SourceType,
    int? SourceId,
    string? SourceNumber,
    string? Notes,
    string? UserName,
    string WarehouseName);

public sealed class MovementListRequest : PagedRequest
{
    public int? ProductId { get; set; }
    public StockMovementType? Type { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}

public sealed record InventorySummaryDto(int ProductCount, decimal TotalQuantity, decimal? StockValue, int LowCount, int OutCount);

public sealed record StockCountListItemDto(int Id, string Number, StockCountStatus Status, string WarehouseName, int LineCount, int CountedLines, DateTime CreatedAt, DateTime? PostedAt, string? Notes);

public sealed record StockCountLineDto(int Id, int ProductId, string Sku, string ProductName, string UnitName, decimal CurrentQuantity, decimal? ExpectedQuantity, decimal? CountedQuantity, decimal? Difference);

public sealed record StockCountDto(int Id, string Number, StockCountStatus Status, int WarehouseId, string WarehouseName, string? Notes, DateTime CreatedAt, DateTime? PostedAt, IReadOnlyList<StockCountLineDto> Lines);

public sealed record CreateStockCountRequest(int? WarehouseId, int? CategoryId, int[]? ProductIds, string? Notes);

public sealed record CountEntry(int LineId, decimal? CountedQuantity);

public sealed record SaveCountsRequest(IReadOnlyList<CountEntry> Entries);

public interface IInventoryService
{
    Task<InventorySummaryDto> SummaryAsync(CancellationToken ct = default);
    Task<StockAdjustmentResult> AdjustAsync(StockAdjustmentRequest request, CancellationToken ct = default);
    Task<PagedResult<StockMovementDto>> MovementsAsync(MovementListRequest request, CancellationToken ct = default);
    Task<PagedResult<StockCountListItemDto>> CountsAsync(PagedRequest request, CancellationToken ct = default);
    Task<StockCountDto> GetCountAsync(int id, CancellationToken ct = default);
    Task<StockCountDto> CreateCountAsync(CreateStockCountRequest request, CancellationToken ct = default);
    Task<StockCountDto> SaveCountsAsync(int id, SaveCountsRequest request, CancellationToken ct = default);
    Task<StockCountDto> PostCountAsync(int id, CancellationToken ct = default);
    Task CancelCountAsync(int id, CancellationToken ct = default);
}

public sealed class StockAdjustmentRequestValidator : AbstractValidator<StockAdjustmentRequest>
{
    public StockAdjustmentRequestValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.ProductId).GreaterThan(0).WithMessage("اختر المنتج");
        RuleFor(x => x.Kind).IsInEnum().WithMessage("اختر نوع الحركة");
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("الكمية يجب أن تكون أكبر من صفر");
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.Notes).NotEmpty().When(x => x.Kind == AdjustmentKind.Lost).WithMessage("اذكر سبب أو تفاصيل الفقد");
    }
}

public sealed class InventoryService(
    IAppDbContext db,
    IInventoryLedger ledger,
    IDocumentNumberGenerator numbers,
    IIdempotencyService idempotency,
    ICurrentUser currentUser,
    IClock clock,
    IAuditService audit) : IInventoryService
{
    private const string AdjustOperation = "Inventory.Adjust";

    public async Task<InventorySummaryDto> SummaryAsync(CancellationToken ct = default)
    {
        var rows = db.Products.AsNoTracking().Where(p => !p.IsArchived)
            .Select(p => new { p.ReorderLevel, p.AverageCost, Qty = p.StockBalances.Sum(s => (decimal?)s.Quantity) ?? 0 });

        var totals = await rows.GroupBy(_ => 1).Select(g => new
        {
            Count = g.Count(),
            Qty = g.Sum(x => x.Qty),
            Value = g.Sum(x => x.Qty * x.AverageCost),
            Low = g.Count(x => x.Qty > 0 && x.Qty <= x.ReorderLevel),
            Out = g.Count(x => x.Qty <= 0),
        }).FirstOrDefaultAsync(ct);

        return totals is null
            ? new InventorySummaryDto(0, 0, currentUser.HasPermission(Permissions.ProductsViewCost) ? 0 : null, 0, 0)
            : new InventorySummaryDto(totals.Count, totals.Qty,
                currentUser.HasPermission(Permissions.ProductsViewCost) ? CostMath.RoundMoney(totals.Value) : null, totals.Low, totals.Out);
    }

    public async Task<StockAdjustmentResult> AdjustAsync(StockAdjustmentRequest request, CancellationToken ct = default)
    {
        if (await idempotency.FindAsync(request.IdempotencyKey, AdjustOperation, ct) is { } done)
        {
            var previous = await db.StockMovements.AsNoTracking().Where(m => m.SourceType == "Adjustment" && m.SourceNumber == done.ResultId)
                .Select(m => m.BalanceAfter).FirstAsync(ct);
            return new StockAdjustmentResult(done.ResultId!, previous);
        }

        var product = await db.Products.AsNoTracking().Where(p => p.Id == request.ProductId).Select(p => new { p.Id, p.IsArchived }).FirstOrDefaultAsync(ct)
            ?? throw new ValidationFailedException("productId", "المنتج غير موجود");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var warehouseId = request.WarehouseId ?? await DefaultWarehouseAsync(ct);
        var number = await numbers.NextAsync(DocumentType.StockAdjustment, ct);
        var notes = request.Notes?.Trim();

        StockPosting posting = request.Kind switch
        {
            AdjustmentKind.Damaged => await ledger.IssueAsync(new StockEntry(product.Id, warehouseId, request.Quantity, StockMovementType.Damaged, "Adjustment", null, number, notes), ct: ct),
            AdjustmentKind.Lost => await ledger.IssueAsync(new StockEntry(product.Id, warehouseId, request.Quantity, StockMovementType.Lost, "Adjustment", null, number, notes), ct: ct),
            // Found stock re-enters at the current average so it does not distort cost.
            AdjustmentKind.Found => await ReceiveAtAverageAsync(new StockEntry(product.Id, warehouseId, request.Quantity, StockMovementType.Found, "Adjustment", null, number, notes), ct),
            _ => throw new ValidationFailedException("kind", "نوع الحركة غير معروف"),
        };

        idempotency.Record(request.IdempotencyKey, AdjustOperation, "StockAdjustment", number);
        audit.Log("Inventory.Adjust", "Product", product.Id.ToString(), null, new { number, request.Kind, request.Quantity, notes });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new StockAdjustmentResult(number, posting.BalanceAfter);
    }

    public async Task<PagedResult<StockMovementDto>> MovementsAsync(MovementListRequest request, CancellationToken ct = default)
    {
        var query = db.StockMovements.AsNoTracking();
        if (request.ProductId is { } productId) query = query.Where(m => m.ProductId == productId);
        if (request.Type is { } type) query = query.Where(m => m.Type == type);
        if (request.From is not null || request.To is not null)
        {
            var (start, end) = clock.LocalDaysToUtcRange(request.From ?? DateOnly.MinValue.AddYears(2000), request.To ?? clock.LocalToday);
            query = query.Where(m => m.OccurredAt >= start && m.OccurredAt < end);
        }
        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim();
            query = query.Where(m => m.SourceNumber == s || m.Product.Sku == s);
        }

        var canViewCost = currentUser.HasPermission(Permissions.ProductsViewCost);
        var users = db.Users;
        return await query.OrderByDescending(m => m.Id)
            .Select(m => new StockMovementDto(
                m.Id, m.OccurredAt, m.ProductId,
                Product.BuildDisplayName(m.Product.Name, m.Product.Size, m.Product.Color, m.Product.Model), m.Product.Sku,
                m.Type, m.Quantity, m.BalanceAfter, canViewCost ? m.UnitCost : null,
                m.SourceType, m.SourceId, m.SourceNumber, m.Notes,
                users.Where(u => u.Id == m.UserId).Select(u => u.FullName).FirstOrDefault(),
                m.Warehouse.Name))
            .ToPagedAsync(request, ct);
    }

    // ---------- Stock counts ----------

    public async Task<PagedResult<StockCountListItemDto>> CountsAsync(PagedRequest request, CancellationToken ct = default) =>
        await db.StockCounts.AsNoTracking().OrderByDescending(c => c.Id)
            .Select(c => new StockCountListItemDto(c.Id, c.Number, c.Status, c.Warehouse.Name, c.Lines.Count,
                c.Lines.Count(l => l.CountedQuantity != null), c.CreatedAt, c.PostedAt, c.Notes))
            .ToPagedAsync(request, ct);

    public async Task<StockCountDto> GetCountAsync(int id, CancellationToken ct = default)
    {
        var count = await db.StockCounts.AsNoTracking().Where(c => c.Id == id)
            .Select(c => new { c.Id, c.Number, c.Status, c.WarehouseId, WarehouseName = c.Warehouse.Name, c.Notes, c.CreatedAt, c.PostedAt })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("الجرد غير موجود");

        var lines = await db.StockCountLines.AsNoTracking().Where(l => l.StockCountId == id)
            .OrderBy(l => l.Product.Name)
            .Select(l => new
            {
                l.Id, l.ProductId, l.Product.Sku,
                Name = Product.BuildDisplayName(l.Product.Name, l.Product.Size, l.Product.Color, l.Product.Model),
                Unit = l.Product.Unit.Name,
                Current = l.Product.StockBalances.Where(s => s.WarehouseId == count.WarehouseId).Sum(s => (decimal?)s.Quantity) ?? 0,
                l.ExpectedQuantity, l.CountedQuantity,
            })
            .ToListAsync(ct);

        return new StockCountDto(count.Id, count.Number, count.Status, count.WarehouseId, count.WarehouseName, count.Notes, count.CreatedAt, count.PostedAt,
            lines.Select(l =>
            {
                var expected = l.ExpectedQuantity ?? l.Current;
                return new StockCountLineDto(l.Id, l.ProductId, l.Sku, l.Name, l.Unit, l.Current, l.ExpectedQuantity, l.CountedQuantity,
                    l.CountedQuantity is { } counted ? counted - expected : null);
            }).ToList());
    }

    public async Task<StockCountDto> CreateCountAsync(CreateStockCountRequest request, CancellationToken ct = default)
    {
        var products = db.Products.Where(p => !p.IsArchived);
        if (request.ProductIds is { Length: > 0 } ids) products = products.Where(p => ids.Contains(p.Id));
        else if (request.CategoryId is { } categoryId) products = products.Where(p => p.CategoryId == categoryId);
        var productIds = await products.Select(p => p.Id).ToListAsync(ct);
        if (productIds.Count == 0) throw new BusinessRuleException("لا توجد منتجات مطابقة لإنشاء الجرد");
        if (productIds.Count > 3000) throw new BusinessRuleException("عدد المنتجات كبير جدًا لجرد واحد؛ اختر صنفًا محددًا");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var count = new StockCount
        {
            Number = await numbers.NextAsync(DocumentType.StockCount, ct),
            WarehouseId = request.WarehouseId ?? await DefaultWarehouseAsync(ct),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
        };
        foreach (var productId in productIds) count.Lines.Add(new StockCountLine { ProductId = productId });
        db.StockCounts.Add(count);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetCountAsync(count.Id, ct);
    }

    public async Task<StockCountDto> SaveCountsAsync(int id, SaveCountsRequest request, CancellationToken ct = default)
    {
        var count = await db.StockCounts.Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("الجرد غير موجود");
        if (count.Status != StockCountStatus.Open) throw new BusinessRuleException("هذا الجرد مغلق ولا يمكن تعديله");

        foreach (var entry in request.Entries)
        {
            if (entry.CountedQuantity < 0) throw new ValidationFailedException("entries", "الكمية المعدودة لا يمكن أن تكون سالبة");
            var line = count.Lines.FirstOrDefault(l => l.Id == entry.LineId) ?? throw new ValidationFailedException("entries", "بند جرد غير موجود");
            line.CountedQuantity = entry.CountedQuantity;
        }
        await db.SaveChangesAsync(ct);
        return await GetCountAsync(id, ct);
    }

    public async Task<StockCountDto> PostCountAsync(int id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var count = await db.StockCounts.Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("الجرد غير موجود");
        if (count.Status != StockCountStatus.Open) throw new BusinessRuleException("تم اعتماد هذا الجرد أو إلغاؤه من قبل");
        if (count.Lines.All(l => l.CountedQuantity is null)) throw new BusinessRuleException("لم يتم إدخال أي كمية معدودة");

        foreach (var line in count.Lines.Where(l => l.CountedQuantity is not null).OrderBy(l => l.ProductId))
        {
            // Expected = the system balance at the moment of posting (sales during the count are already reflected).
            var expected = await db.StockBalances.Where(s => s.ProductId == line.ProductId && s.WarehouseId == count.WarehouseId)
                .Select(s => (decimal?)s.Quantity).FirstOrDefaultAsync(ct) ?? 0;
            line.ExpectedQuantity = expected;
            var diff = line.CountedQuantity!.Value - expected;
            if (diff > 0)
                await ReceiveAtAverageAsync(new StockEntry(line.ProductId, count.WarehouseId, diff, StockMovementType.CountIncrease, "StockCount", count.Id, count.Number), ct);
            else if (diff < 0)
                await ledger.IssueAsync(new StockEntry(line.ProductId, count.WarehouseId, -diff, StockMovementType.CountDecrease, "StockCount", count.Id, count.Number), ct: ct);
        }

        count.Status = StockCountStatus.Posted;
        count.PostedAt = clock.UtcNow;
        audit.Log("StockCount.Post", "StockCount", id.ToString(), null, new { count.Number });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetCountAsync(id, ct);
    }

    public async Task CancelCountAsync(int id, CancellationToken ct = default)
    {
        var count = await db.StockCounts.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("الجرد غير موجود");
        if (count.Status != StockCountStatus.Open) throw new BusinessRuleException("لا يمكن إلغاء جرد معتمد");
        count.Status = StockCountStatus.Cancelled;
        audit.Log("StockCount.Cancel", "StockCount", id.ToString());
        await db.SaveChangesAsync(ct);
    }

    private async Task<StockPosting> ReceiveAtAverageAsync(StockEntry entry, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().Where(p => p.Id == entry.ProductId).Select(p => new { p.AverageCost, p.PurchasePrice }).FirstAsync(ct);
        return await ledger.ReceiveAsync(entry, product.AverageCost > 0 ? product.AverageCost : product.PurchasePrice, ct);
    }

    private Task<int> DefaultWarehouseAsync(CancellationToken ct) => db.Warehouses.Where(w => w.IsDefault).Select(w => w.Id).FirstAsync(ct);
}
