using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Application.Ledgers;
using Qwader.Application.Settings;
using Qwader.Domain.Administration;
using Qwader.Domain.Catalog;
using Qwader.Domain.Files;
using Qwader.Domain.Identity;
using Qwader.Domain.Inventory;

namespace Qwader.Application.Catalog;

public sealed class ProductService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IDocumentNumberGenerator numbers,
    IInventoryLedger inventory,
    ISettingsService settings,
    IAuditService audit) : IProductService
{
    private bool CanViewCost => currentUser.HasPermission(Permissions.ProductsViewCost);

    /// <summary>Search text: name, variant attributes, brand, SKU and barcode, normalized.</summary>
    public static string BuildSearchText(string name, string? size, string? color, string? model, string? brand, string sku, string? barcode) =>
        ArabicText.Normalize(string.Join(' ', new[] { name, model, color, size, brand, sku, barcode }.Where(s => !string.IsNullOrWhiteSpace(s))));

    /// <summary>Applies the shared search semantics: every word must appear; exact code matches rank first.</summary>
    public static IQueryable<Product> ApplySearch(IQueryable<Product> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return query;
        foreach (var term in ArabicText.Normalize(search).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            query = query.Where(p => p.NormalizedName.Contains(term));
        return query;
    }

    public async Task<PagedResult<ProductListItemDto>> ListAsync(ProductListRequest request, CancellationToken ct = default)
    {
        var query = ApplySearch(db.Products.AsNoTracking().Where(p => p.IsArchived == request.Archived), request.Search);
        if (request.CategoryId is { } categoryId) query = query.Where(p => p.CategoryId == categoryId);
        if (request.SupplierId is { } supplierId) query = query.Where(p => p.DefaultSupplierId == supplierId);

        var withQty = query.Select(p => new { P = p, Qty = p.StockBalances.Sum(s => (decimal?)s.Quantity) ?? 0 });
        withQty = request.Stock switch
        {
            StockFilter.Available => withQty.Where(x => x.Qty > x.P.ReorderLevel),
            StockFilter.Low => withQty.Where(x => x.Qty > 0 && x.Qty <= x.P.ReorderLevel),
            StockFilter.Out => withQty.Where(x => x.Qty <= 0),
            StockFilter.NeedsReorder => withQty.Where(x => x.Qty <= x.P.ReorderLevel),
            _ => withQty,
        };

        var raw = request.Search?.Trim();
        var ordered = string.IsNullOrEmpty(raw)
            ? withQty.OrderBy(x => x.P.Name).ThenBy(x => x.P.Id)
            : withQty.OrderByDescending(x => x.P.Sku == raw || x.P.Barcode == raw).ThenBy(x => x.P.Name).ThenBy(x => x.P.Id);

        var canViewCost = CanViewCost;
        return await ordered.Select(x => new ProductListItemDto(
                x.P.Id, x.P.Sku, x.P.Barcode, x.P.Name,
                Product.BuildDisplayName(x.P.Name, x.P.Size, x.P.Color, x.P.Model),
                x.P.CategoryId, x.P.Category.Name, x.P.Unit.Name,
                x.P.RetailPrice, x.P.WholesalePrice, x.P.MinSalePrice,
                x.Qty, x.P.ReorderLevel,
                x.Qty <= 0 ? StockStatus.Out : x.Qty <= x.P.ReorderLevel ? StockStatus.Low : StockStatus.Available,
                x.P.ImageFileId, x.P.IsArchived,
                canViewCost ? x.P.AverageCost : null))
            .ToPagedAsync(request, ct);
    }

    public async Task<ProductDetailDto> GetAsync(int id, CancellationToken ct = default) =>
        await LoadDetailAsync(p => p.Id == id, ct) ?? throw new NotFoundException("المنتج غير موجود");

    public Task<ProductDetailDto?> FindByCodeAsync(string code, CancellationToken ct = default)
    {
        var c = code.Trim();
        return LoadDetailAsync(p => p.Sku == c || p.Barcode == c, ct);
    }

    private async Task<ProductDetailDto?> LoadDetailAsync(Expression<Func<Product, bool>> predicate, CancellationToken ct)
    {
        var p = await db.Products.AsNoTracking().Where(predicate)
            .Select(p => new
            {
                Product = p,
                CategoryName = p.Category.Name,
                BrandName = p.Brand != null ? p.Brand.Name : null,
                UnitName = p.Unit.Name,
                SupplierName = p.DefaultSupplier != null ? p.DefaultSupplier.Name : null,
                Parent = p.ParentProduct != null ? new { p.ParentProduct.Name, p.ParentProduct.Size, p.ParentProduct.Color, p.ParentProduct.Model } : null,
                Stock = p.StockBalances.Select(s => new StockByWarehouseDto(s.WarehouseId, s.Warehouse.Name, s.Quantity)).ToList(),
            })
            .FirstOrDefaultAsync(ct);
        if (p is null) return null;

        var product = p.Product;
        // Variants hang off the root product; siblings are listed from any member of the family.
        var rootId = product.ParentProductId ?? product.Id;
        var variants = await db.Products.AsNoTracking()
            .Where(v => (v.ParentProductId == rootId || v.Id == rootId) && v.Id != product.Id && !v.IsArchived)
            .OrderBy(v => v.Id)
            .Select(v => new ProductRefDto(v.Id, v.Sku, Product.BuildDisplayName(v.Name, v.Size, v.Color, v.Model),
                v.StockBalances.Sum(s => (decimal?)s.Quantity) ?? 0, v.RetailPrice))
            .ToListAsync(ct);

        var quantity = p.Stock.Sum(s => s.Quantity);
        var status = quantity <= 0 ? StockStatus.Out : quantity <= product.ReorderLevel ? StockStatus.Low : StockStatus.Available;

        decimal? cost = null, value = null, profit = null, margin = null, suggested = null;
        if (CanViewCost)
        {
            cost = product.AverageCost > 0 ? product.AverageCost : product.PurchasePrice;
            value = CostMath.RoundMoney(quantity * product.AverageCost);
            if (cost > 0)
            {
                profit = CostMath.RoundMoney(product.RetailPrice - cost.Value);
                margin = Math.Round(profit.Value / cost.Value * 100, 1);
                suggested = CostMath.SuggestPrice(cost.Value, (await settings.GetAsync(ct)).DefaultMarginPercent);
            }
        }

        return new ProductDetailDto(
            product.Id, product.Sku, product.Barcode, product.Name, product.DisplayName, product.Description,
            product.CategoryId, p.CategoryName, p.BrandName, product.UnitId, p.UnitName,
            product.DefaultSupplierId, p.SupplierName,
            product.ParentProductId, p.Parent is null ? null : Product.BuildDisplayName(p.Parent.Name, p.Parent.Size, p.Parent.Color, p.Parent.Model),
            product.Size, product.Color, product.Model,
            CanViewCost ? product.PurchasePrice : null, CanViewCost ? product.AverageCost : null,
            product.WholesalePrice, product.RetailPrice, product.MinSalePrice, product.ReorderLevel,
            quantity, status, value, profit, margin, suggested,
            product.ImageFileId, product.IsArchived, product.CreatedAt, p.Stock, variants);
    }

    public async Task<ProductDetailDto> CreateAsync(SaveProductRequest request, CancellationToken ct = default)
    {
        if ((request.OpeningQuantity ?? 0) > 0 && !currentUser.HasPermission(Permissions.InventoryAdjust) && !currentUser.HasPermission(Permissions.PurchasesManage))
            throw new ForbiddenException("ليست لديك صلاحية إدخال رصيد افتتاحي للمخزون");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var product = new Product();
        await ApplyAsync(product, request, isNew: true, ct);
        product.Sku = string.IsNullOrWhiteSpace(request.Sku) ? await NextFreeSkuAsync(ct) : request.Sku.Trim();
        await EnsureUniqueCodesAsync(product, ct);
        product.NormalizedName = BuildSearchText(product.Name, product.Size, product.Color, product.Model, request.BrandName, product.Sku, product.Barcode);

        db.Products.Add(product);
        await db.SaveChangesAsync(ct);

        if (request.OpeningQuantity is > 0 and var qty)
        {
            var warehouseId = request.WarehouseId ?? await db.Warehouses.Where(w => w.IsDefault).Select(w => w.Id).FirstAsync(ct);
            var unitCost = request.OpeningUnitCost ?? product.PurchasePrice;
            await inventory.ReceiveAsync(
                new StockEntry(product.Id, warehouseId, qty, StockMovementType.OpeningBalance, "Product", product.Id, product.Sku, "رصيد افتتاحي"),
                unitCost, ct);
        }

        audit.Log("Product.Create", "Product", product.Id.ToString(), null, request);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(product.Id, ct);
    }

    public async Task<ProductDetailDto> UpdateAsync(int id, SaveProductRequest request, CancellationToken ct = default)
    {
        var product = await db.Products.Include(p => p.Brand).FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NotFoundException("المنتج غير موجود");

        var pricesChanged = product.RetailPrice != request.RetailPrice || product.WholesalePrice != request.WholesalePrice
            || product.MinSalePrice != request.MinSalePrice;
        if (pricesChanged && !currentUser.HasPermission(Permissions.ProductsEditPrices))
            throw new ForbiddenException("ليست لديك صلاحية تعديل أسعار البيع");

        var before = Snapshot(product);
        await ApplyAsync(product, request, isNew: false, ct);
        if (!string.IsNullOrWhiteSpace(request.Sku)) product.Sku = request.Sku.Trim();
        await EnsureUniqueCodesAsync(product, ct);
        product.NormalizedName = BuildSearchText(product.Name, product.Size, product.Color, product.Model, request.BrandName, product.Sku, product.Barcode);

        audit.Log(pricesChanged ? "Product.UpdatePrices" : "Product.Update", "Product", id.ToString(), before, Snapshot(product));
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task SetArchivedAsync(int id, bool archived, CancellationToken ct = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("المنتج غير موجود");
        if (product.IsArchived == archived) return;
        product.IsArchived = archived;
        audit.Log(archived ? "Product.Archive" : "Product.Unarchive", "Product", id.ToString());
        await db.SaveChangesAsync(ct);
    }

    public Task<bool> IsCodeTakenAsync(string code, int? exceptId, CancellationToken ct = default)
    {
        var c = code.Trim();
        return db.Products.AnyAsync(p => p.Id != exceptId && (p.Sku == c || p.Barcode == c), ct);
    }

    private async Task ApplyAsync(Product product, SaveProductRequest r, bool isNew, CancellationToken ct)
    {
        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == r.CategoryId, ct)
            ?? throw new ValidationFailedException("categoryId", "الصنف غير موجود");
        if (!category.IsActive && (isNew || product.CategoryId != r.CategoryId))
            throw new ValidationFailedException("categoryId", "الصنف المختار غير فعّال");
        if (!await db.Units.AnyAsync(u => u.Id == r.UnitId, ct))
            throw new ValidationFailedException("unitId", "وحدة القياس غير موجودة");
        if (r.DefaultSupplierId is { } supplierId && !await db.Suppliers.AnyAsync(s => s.Id == supplierId, ct))
            throw new ValidationFailedException("defaultSupplierId", "المورد غير موجود");
        if (r.ImageFileId is { } fileId && fileId != product.ImageFileId
            && !await db.StoredFiles.AnyAsync(f => f.Id == fileId && f.Purpose == FilePurpose.ProductImage, ct))
            throw new ValidationFailedException("imageFileId", "الصورة غير موجودة، أعد رفعها");

        if (r.ParentProductId is { } parentId)
        {
            if (parentId == product.Id) throw new ValidationFailedException("parentProductId", "لا يمكن أن يكون المنتج متغيرًا لنفسه");
            var parent = await db.Products.AsNoTracking().Where(p => p.Id == parentId).Select(p => new { p.ParentProductId }).FirstOrDefaultAsync(ct)
                ?? throw new ValidationFailedException("parentProductId", "المنتج الأصلي غير موجود");
            if (parent.ParentProductId is not null)
                throw new ValidationFailedException("parentProductId", "اختر المنتج الأصلي وليس أحد متغيراته");
            if (!isNew && await db.Products.AnyAsync(p => p.ParentProductId == product.Id, ct))
                throw new ValidationFailedException("parentProductId", "هذا المنتج له متغيرات، فلا يمكن جعله متغيرًا لمنتج آخر");
        }

        product.Name = r.Name.Trim();
        product.Barcode = Clean(r.Barcode);
        product.CategoryId = r.CategoryId;
        product.UnitId = r.UnitId;
        product.DefaultSupplierId = r.DefaultSupplierId;
        product.ParentProductId = r.ParentProductId;
        product.Size = Clean(r.Size);
        product.Color = Clean(r.Color);
        product.Model = Clean(r.Model);
        product.Description = Clean(r.Description);
        product.WholesalePrice = r.WholesalePrice;
        product.RetailPrice = r.RetailPrice;
        product.MinSalePrice = r.MinSalePrice;
        product.ReorderLevel = r.ReorderLevel;
        product.ImageFileId = r.ImageFileId;
        if (r.PurchasePrice is { } purchasePrice && (isNew || CanViewCost)) product.PurchasePrice = purchasePrice;

        product.BrandId = await ResolveBrandAsync(r.BrandName, ct);
    }

    private async Task<int?> ResolveBrandAsync(string? brandName, CancellationToken ct)
    {
        var name = Clean(brandName);
        if (name is null) return null;
        var brand = await db.Brands.FirstOrDefaultAsync(b => b.Name == name, ct);
        if (brand is null)
        {
            brand = new Brand { Name = name };
            db.Brands.Add(brand);
            await db.SaveChangesAsync(ct);
        }
        return brand.Id;
    }

    private async Task EnsureUniqueCodesAsync(Product product, CancellationToken ct)
    {
        if (await db.Products.AnyAsync(p => p.Id != product.Id && (p.Sku == product.Sku || p.Barcode == product.Sku), ct))
            throw new ValidationFailedException("sku", $"الكود «{product.Sku}» مستخدم لمنتج آخر");
        if (product.Barcode is { } barcode && await db.Products.AnyAsync(p => p.Id != product.Id && (p.Barcode == barcode || p.Sku == barcode), ct))
            throw new ValidationFailedException("barcode", $"الباركود «{barcode}» مستخدم لمنتج آخر");
    }

    /// <summary>Auto codes skip any value a user already typed by hand.</summary>
    private async Task<string> NextFreeSkuAsync(CancellationToken ct)
    {
        while (true)
        {
            var sku = await numbers.NextAsync(DocumentType.Product, ct);
            if (!await db.Products.AnyAsync(p => p.Sku == sku || p.Barcode == sku, ct)) return sku;
        }
    }

    private static object Snapshot(Product p) => new
    {
        p.Sku, p.Barcode, p.Name, p.CategoryId, p.UnitId, p.BrandId, p.Size, p.Color, p.Model,
        p.PurchasePrice, p.WholesalePrice, p.RetailPrice, p.MinSalePrice, p.ReorderLevel,
    };

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
