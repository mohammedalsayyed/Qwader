using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Catalog;
using Qwader.Application.Common;
using Qwader.Domain.Catalog;
using Qwader.Domain.Identity;
using Qwader.Domain.Purchasing;

namespace Qwader.Application.Search;

public enum SearchResultKind
{
    Product = 1,
    Category = 2,
    Supplier = 3,
    PurchaseInvoice = 4,
    Customer = 5,
    SaleInvoice = 6,
    IncomingTransfer = 7,
    ReceiptVoucher = 8,
    PaymentVoucher = 9,
}

/// <summary>One hit, already shaped for a compact result row.</summary>
public sealed record SearchHit(
    SearchResultKind Kind,
    int Id,
    string Title,
    string? Subtitle,
    string? Code,
    decimal? Price,
    decimal? Quantity,
    Guid? ImageFileId);

public sealed record SearchResult(string Query, IReadOnlyList<SearchHit> Hits);

public sealed record NotificationsDto(int LowStock, int OutOfStock, int DraftPurchases, int PendingTransfers);

public interface ISearchService
{
    Task<SearchResult> SearchAsync(string query, int perKind = 6, CancellationToken ct = default);
    Task<NotificationsDto> NotificationsAsync(CancellationToken ct = default);
}

/// <summary>
/// Global search across the sections the caller may see. Each kind is queried server-side with a small limit;
/// new kinds (customers, invoices, vouchers, transfers) join here as their modules are built.
/// </summary>
public sealed class SearchService(IAppDbContext db, ICurrentUser currentUser) : ISearchService
{
    public async Task<SearchResult> SearchAsync(string query, int perKind = 6, CancellationToken ct = default)
    {
        var raw = query.Trim();
        var normalized = ArabicText.Normalize(raw);
        if (normalized.Length == 0) return new SearchResult(raw, []);
        perKind = Math.Clamp(perKind, 1, 20);
        var hits = new List<SearchHit>();

        if (currentUser.HasPermission(Permissions.ProductsView))
        {
            var products = ProductService.ApplySearch(db.Products.AsNoTracking().Where(p => !p.IsArchived), raw);
            hits.AddRange(await products
                .OrderByDescending(p => p.Sku == raw || p.Barcode == raw)
                .ThenByDescending(p => p.NormalizedName.StartsWith(normalized))
                .ThenBy(p => p.Name)
                .Take(perKind)
                .Select(p => new SearchHit(SearchResultKind.Product, p.Id,
                    Product.BuildDisplayName(p.Name, p.Size, p.Color, p.Model), p.Category.Name, p.Sku, p.RetailPrice,
                    p.StockBalances.Sum(s => (decimal?)s.Quantity) ?? 0, p.ImageFileId))
                .ToListAsync(ct));

            hits.AddRange(await db.Categories.AsNoTracking()
                .Where(c => c.NormalizedName.Contains(normalized) || c.Code == raw)
                .OrderBy(c => c.Name).Take(3)
                .Select(c => new SearchHit(SearchResultKind.Category, c.Id, c.Name, null, c.Code, null,
                    c.Products.Count(p => !p.IsArchived), null))
                .ToListAsync(ct));
        }

        if (currentUser.HasPermission(Permissions.SuppliersManage) || currentUser.HasPermission(Permissions.PurchasesView))
        {
            hits.AddRange(await db.Suppliers.AsNoTracking()
                .Where(s => s.NormalizedName.Contains(normalized) || s.Phone!.Contains(raw) || s.Code == raw)
                .OrderBy(s => s.Name).Take(3)
                .Select(s => new SearchHit(SearchResultKind.Supplier, s.Id, s.Name, s.Phone, s.Code, s.Balance, null, null))
                .ToListAsync(ct));
        }

        if (currentUser.HasPermission(Permissions.CustomersManage) || currentUser.HasPermission(Permissions.SalesCreate))
        {
            var phone = Customers.PhoneNumbers.Normalize(raw);
            hits.AddRange(await db.Customers.AsNoTracking()
                .Where(c => !c.IsWalkIn && (c.NormalizedName.Contains(normalized) || c.Code == raw || (phone != null && phone.Length >= 4 && c.Phone!.Contains(phone))))
                .OrderBy(c => c.Name).Take(4)
                .Select(c => new SearchHit(SearchResultKind.Customer, c.Id, c.Name, c.Phone, c.Code, c.Balance, null, null))
                .ToListAsync(ct));
        }

        if ((currentUser.HasPermission(Permissions.SalesCreate) || currentUser.HasPermission(Permissions.SalesViewAll)) && raw.Length >= 3)
        {
            var sales = db.SaleInvoices.AsNoTracking().Where(s => s.Number.EndsWith(raw) || s.Number == raw);
            if (!currentUser.HasPermission(Permissions.SalesViewAll) && !currentUser.HasPermission(Permissions.ReportsSales))
                sales = sales.Where(s => s.SalespersonId == currentUser.UserId);
            hits.AddRange(await sales.OrderByDescending(s => s.Id).Take(3)
                .Select(s => new SearchHit(SearchResultKind.SaleInvoice, s.Id, s.Number, s.CustomerName, null, s.Total, null, null))
                .ToListAsync(ct));
        }

        if (currentUser.HasPermission(Permissions.TransfersCreate) || currentUser.HasPermission(Permissions.TransfersReview))
        {
            hits.AddRange(await db.IncomingTransfers.AsNoTracking()
                .Where(t => t.NormalizedSenderName.Contains(normalized) || t.ReferenceNo == raw || t.Number.EndsWith(raw))
                .OrderByDescending(t => t.Id).Take(3)
                .Select(t => new SearchHit(SearchResultKind.IncomingTransfer, t.Id, t.SenderName, t.FinancialAccount.Name, t.ReferenceNo, t.Amount, null, null))
                .ToListAsync(ct));
        }

        if ((currentUser.HasPermission(Permissions.VouchersCreate) || currentUser.HasPermission(Permissions.ReportsFinancial)) && raw.Length >= 3)
        {
            hits.AddRange(await db.ReceiptVouchers.AsNoTracking().Where(v => v.Number.EndsWith(raw) || v.Number == raw)
                .OrderByDescending(v => v.Id).Take(2)
                .Select(v => new SearchHit(SearchResultKind.ReceiptVoucher, v.Id, v.Number, v.PayerName, null, v.Amount, null, null)).ToListAsync(ct));
            hits.AddRange(await db.PaymentVouchers.AsNoTracking().Where(v => v.Number.EndsWith(raw) || v.Number == raw)
                .OrderByDescending(v => v.Id).Take(2)
                .Select(v => new SearchHit(SearchResultKind.PaymentVoucher, v.Id, v.Number, v.BeneficiaryName, null, v.Amount, null, null)).ToListAsync(ct));
        }

        if (currentUser.HasPermission(Permissions.PurchasesView) && raw.Length >= 3)
        {
            hits.AddRange(await db.PurchaseInvoices.AsNoTracking()
                .Where(p => p.Number.EndsWith(raw) || p.Number == raw || p.SupplierInvoiceNo == raw)
                .OrderByDescending(p => p.Id).Take(3)
                .Select(p => new SearchHit(SearchResultKind.PurchaseInvoice, p.Id, p.Number, p.Supplier.Name, p.SupplierInvoiceNo, p.Total, null, null))
                .ToListAsync(ct));
        }

        return new SearchResult(raw, hits);
    }

    public async Task<NotificationsDto> NotificationsAsync(CancellationToken ct = default)
    {
        int low = 0, @out = 0, drafts = 0;
        if (currentUser.HasPermission(Permissions.InventoryView) || currentUser.HasPermission(Permissions.ProductsView))
        {
            var stock = db.Products.AsNoTracking().Where(p => !p.IsArchived)
                .Select(p => new { p.ReorderLevel, Qty = p.StockBalances.Sum(s => (decimal?)s.Quantity) ?? 0 });
            low = await stock.CountAsync(x => x.Qty > 0 && x.Qty <= x.ReorderLevel, ct);
            @out = await stock.CountAsync(x => x.Qty <= 0, ct);
        }
        if (currentUser.HasPermission(Permissions.PurchasesManage))
            drafts = await db.PurchaseInvoices.CountAsync(p => p.Status == PurchaseStatus.Draft, ct);
        var pendingTransfers = currentUser.HasPermission(Permissions.TransfersReview)
            ? await db.IncomingTransfers.CountAsync(t => t.Status == Domain.Finance.TransferStatus.Pending, ct)
            : 0;
        return new NotificationsDto(low, @out, drafts, pendingTransfers);
    }
}
