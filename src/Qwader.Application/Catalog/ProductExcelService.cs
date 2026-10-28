using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Qwader.Application.Abstractions;
using Qwader.Application.Common;
using Qwader.Application.Ledgers;
using Qwader.Domain.Administration;
using Qwader.Domain.Catalog;
using Qwader.Domain.Identity;
using Qwader.Domain.Inventory;

namespace Qwader.Application.Catalog;

public sealed record ImportRowError(int Row, string Message);

public sealed record ProductImportResult(int Created, int Updated, IReadOnlyList<ImportRowError> Errors)
{
    public bool Succeeded => Errors.Count == 0;
}

public interface IProductExcelService
{
    Task<byte[]> ExportAsync(ProductListRequest filter, CancellationToken ct = default);
    byte[] Template();
    Task<ProductImportResult> ImportAsync(Stream content, CancellationToken ct = default);
}

public sealed class ProductExcelService(
    IAppDbContext db,
    IExcelService excel,
    ICurrentUser currentUser,
    IDocumentNumberGenerator numbers,
    IInventoryLedger inventory,
    IAuditService audit,
    Settings.ISettingsService settings) : IProductExcelService
{
    private const int MaxRows = 5000;

    private static class H
    {
        public const string Sku = "الكود";
        public const string Barcode = "الباركود";
        public const string Name = "اسم المنتج";
        public const string Category = "الصنف";
        public const string Unit = "الوحدة";
        public const string Brand = "الماركة";
        public const string Size = "المقاس";
        public const string Color = "اللون";
        public const string Model = "الموديل";
        public const string PurchasePrice = "سعر الشراء";
        public const string AverageCost = "متوسط التكلفة";
        public const string Wholesale = "سعر الجملة";
        public const string Retail = "سعر المفرق";
        public const string MinPrice = "أقل سعر بيع";
        public const string ReorderLevel = "حد التنبيه";
        public const string Quantity = "الكمية";
        public const string OpeningQuantity = "الكمية الافتتاحية";
    }

    public async Task<byte[]> ExportAsync(ProductListRequest filter, CancellationToken ct = default)
    {
        var canViewCost = currentUser.HasPermission(Permissions.ProductsViewCost);
        var query = ProductService.ApplySearch(db.Products.AsNoTracking().Where(p => p.IsArchived == filter.Archived), filter.Search);
        if (filter.CategoryId is { } categoryId) query = query.Where(p => p.CategoryId == categoryId);

        var rows = await query.OrderBy(p => p.Category.Name).ThenBy(p => p.Name)
            .Select(p => new
            {
                p.Sku, p.Barcode, p.Name, Category = p.Category.Name, Unit = p.Unit.Name,
                Brand = p.Brand != null ? p.Brand.Name : null, p.Size, p.Color, p.Model,
                p.PurchasePrice, p.AverageCost, p.WholesalePrice, p.RetailPrice, p.MinSalePrice, p.ReorderLevel,
                Quantity = p.StockBalances.Sum(s => (decimal?)s.Quantity) ?? 0,
            })
            .Take(50_000)
            .ToListAsync(ct);

        var columns = new List<ExcelColumn>
        {
            new(H.Sku, 14), new(H.Barcode, 16), new(H.Name, 30), new(H.Category, 18), new(H.Unit, 10), new(H.Brand, 14),
            new(H.Size, 10), new(H.Color, 10), new(H.Model, 12),
        };
        if (canViewCost) columns.AddRange([new(H.PurchasePrice, 12), new(H.AverageCost, 12)]);
        columns.AddRange([new(H.Wholesale, 12), new(H.Retail, 12), new(H.MinPrice, 12), new(H.Quantity, 10), new(H.ReorderLevel, 10)]);

        return excel.Write("المنتجات", columns, rows.Select(r =>
        {
            var cells = new List<object?> { r.Sku, r.Barcode, r.Name, r.Category, r.Unit, r.Brand, r.Size, r.Color, r.Model };
            if (canViewCost) cells.AddRange([r.PurchasePrice, r.AverageCost]);
            cells.AddRange([r.WholesalePrice, r.RetailPrice, r.MinSalePrice, r.Quantity, r.ReorderLevel]);
            return cells.ToArray();
        }));
    }

    public byte[] Template() => excel.Write("المنتجات",
        [
            new(H.Sku, 14), new(H.Barcode, 16), new(H.Name, 30), new(H.Category, 18), new(H.Unit, 10), new(H.Brand, 14),
            new(H.Size, 10), new(H.Color, 10), new(H.Model, 12), new(H.PurchasePrice, 12), new(H.Wholesale, 12),
            new(H.Retail, 12), new(H.MinPrice, 12), new(H.ReorderLevel, 10), new(H.OpeningQuantity, 14),
        ],
        [["", "", "صحن زجاجي 25 سم", "الصحون", "قطعة", "", "25 سم", "شفاف", "", 8, 11, 14, 10, 5, 24]]);

    private sealed record Row(
        int Number, string? Sku, string? Barcode, string Name, string Category, string? Unit, string? Brand,
        string? Size, string? Color, string? Model, decimal? PurchasePrice, decimal? Wholesale, decimal Retail,
        decimal? MinPrice, decimal? ReorderLevel, decimal? OpeningQuantity);

    public async Task<ProductImportResult> ImportAsync(Stream content, CancellationToken ct = default)
    {
        IReadOnlyList<(int RowNumber, IReadOnlyDictionary<string, string?> Cells)> sheet;
        try
        {
            sheet = excel.Read(content);
        }
        catch (Exception)
        {
            return new ProductImportResult(0, 0, [new ImportRowError(0, "تعذر قراءة الملف، تأكد أنه ملف Excel بصيغة xlsx")]);
        }

        if (sheet.Count == 0) return new ProductImportResult(0, 0, [new ImportRowError(0, "الملف لا يحتوي على بيانات")]);
        if (sheet.Count > MaxRows) return new ProductImportResult(0, 0, [new ImportRowError(0, $"الحد الأقصى {MaxRows} صف في المرة الواحدة")]);
        if (!sheet[0].Cells.ContainsKey(H.Name) || !sheet[0].Cells.ContainsKey(H.Retail) || !sheet[0].Cells.ContainsKey(H.Category))
            return new ProductImportResult(0, 0, [new ImportRowError(1, $"يجب أن يحتوي الملف على الأعمدة: {H.Name}، {H.Category}، {H.Retail}. استخدم النموذج الجاهز")]);

        var errors = new List<ImportRowError>();
        var rows = new List<Row>();
        foreach (var (number, cells) in sheet)
        {
            string? Text(string key) => cells.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;
            decimal? Number(string key, string label)
            {
                var text = Text(key);
                if (text is null) return null;
                if (decimal.TryParse(ToAscii(text).Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && value >= 0)
                    return value;
                errors.Add(new ImportRowError(number, $"قيمة «{label}» غير صالحة: {text}"));
                return null;
            }

            var name = Text(H.Name);
            var category = Text(H.Category);
            var retail = Number(H.Retail, H.Retail);
            var row = new Row(number, Text(H.Sku), Text(H.Barcode), name ?? "", category ?? "", Text(H.Unit), Text(H.Brand),
                Text(H.Size), Text(H.Color), Text(H.Model), Number(H.PurchasePrice, H.PurchasePrice), Number(H.Wholesale, H.Wholesale),
                retail ?? 0, Number(H.MinPrice, H.MinPrice), Number(H.ReorderLevel, H.ReorderLevel), Number(H.OpeningQuantity, H.OpeningQuantity));

            if (name is null && category is null && retail is null && row.Sku is null) continue; // blank line
            if (name is null) errors.Add(new ImportRowError(number, "اسم المنتج مطلوب"));
            else if (name.Length > 150) errors.Add(new ImportRowError(number, "اسم المنتج طويل جدًا"));
            if (category is null) errors.Add(new ImportRowError(number, "الصنف مطلوب"));
            if (retail is null or 0) errors.Add(new ImportRowError(number, "سعر المفرق مطلوب"));
            if (row.MinPrice > row.Retail) errors.Add(new ImportRowError(number, "أقل سعر بيع أكبر من سعر المفرق"));
            if (row.Sku?.Contains(' ') == true) errors.Add(new ImportRowError(number, "الكود يجب ألا يحتوي على مسافات"));
            rows.Add(row);
        }

        // Duplicates inside the file.
        foreach (var dup in rows.Where(r => r.Sku is not null).GroupBy(r => r.Sku!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add(new ImportRowError(dup.Skip(1).First().Number, $"الكود «{dup.Key}» مكرر داخل الملف"));
        foreach (var dup in rows.Where(r => r.Barcode is not null).GroupBy(r => r.Barcode!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            errors.Add(new ImportRowError(dup.Skip(1).First().Number, $"الباركود «{dup.Key}» مكرر داخل الملف"));

        var units = await db.Units.AsNoTracking().ToDictionaryAsync(u => u.Name, u => u.Id, ct);
        var defaultUnitId = units.GetValueOrDefault("قطعة", units.Values.FirstOrDefault());
        foreach (var row in rows.Where(r => r.Unit is not null && !units.ContainsKey(r.Unit)))
            errors.Add(new ImportRowError(row.Number, $"وحدة القياس «{row.Unit}» غير معروفة. المتاح: {string.Join("، ", units.Keys)}"));

        var skus = rows.Where(r => r.Sku is not null).Select(r => r.Sku!).ToList();
        var existing = await db.Products.Where(p => skus.Contains(p.Sku)).ToDictionaryAsync(p => p.Sku, StringComparer.OrdinalIgnoreCase, ct);

        var barcodes = rows.Where(r => r.Barcode is not null).Select(r => r.Barcode!).ToList();
        var barcodeOwners = await db.Products.AsNoTracking().Where(p => p.Barcode != null && barcodes.Contains(p.Barcode))
            .Select(p => new { p.Barcode, p.Sku }).ToListAsync(ct);
        foreach (var row in rows.Where(r => r.Barcode is not null))
        {
            var owner = barcodeOwners.FirstOrDefault(o => string.Equals(o.Barcode, row.Barcode, StringComparison.OrdinalIgnoreCase));
            if (owner is not null && !string.Equals(owner.Sku, row.Sku, StringComparison.OrdinalIgnoreCase))
                errors.Add(new ImportRowError(row.Number, $"الباركود «{row.Barcode}» مستخدم للمنتج {owner.Sku}"));
        }

        var canEditPrices = currentUser.HasPermission(Permissions.ProductsEditPrices);
        var canOpenStock = currentUser.HasPermission(Permissions.InventoryAdjust) || currentUser.HasPermission(Permissions.PurchasesManage);
        foreach (var row in rows)
        {
            if (row.Sku is not null && existing.TryGetValue(row.Sku, out var p) && !canEditPrices
                && (p.RetailPrice != row.Retail || (row.Wholesale is { } w && w != p.WholesalePrice) || (row.MinPrice is { } m && m != p.MinSalePrice)))
                errors.Add(new ImportRowError(row.Number, "ليست لديك صلاحية تعديل أسعار المنتجات الموجودة"));
            if (row.OpeningQuantity > 0 && row.Sku is not null && existing.ContainsKey(row.Sku))
                errors.Add(new ImportRowError(row.Number, "لا يمكن إدخال كمية افتتاحية لمنتج موجود؛ استخدم فاتورة شراء أو الجرد"));
            if (row.OpeningQuantity > 0 && !canOpenStock)
                errors.Add(new ImportRowError(row.Number, "ليست لديك صلاحية إدخال رصيد افتتاحي"));
        }

        if (errors.Count > 0) return new ProductImportResult(0, 0, errors.OrderBy(e => e.Row).ToList());

        // A new product without «حد التنبيه» gets the store's default, as when it is added on screen.
        var defaultReorderLevel = (await settings.GetAsync(ct)).DefaultReorderLevel;

        // Everything validated: apply in one transaction.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var categories = await db.Categories.ToDictionaryAsync(c => c.Name, StringComparer.OrdinalIgnoreCase, ct);
        var brands = await db.Brands.ToDictionaryAsync(b => b.Name, StringComparer.OrdinalIgnoreCase, ct);
        var warehouseId = await db.Warehouses.Where(w => w.IsDefault).Select(w => w.Id).FirstAsync(ct);
        int created = 0, updated = 0;
        var openings = new List<(Product Product, decimal Quantity, decimal Cost)>();

        foreach (var row in rows)
        {
            if (!categories.TryGetValue(row.Category, out var category))
            {
                category = new Category
                {
                    Name = row.Category, NormalizedName = ArabicText.Normalize(row.Category),
                    Code = await numbers.NextAsync(DocumentType.Category, ct),
                };
                db.Categories.Add(category);
                categories[row.Category] = category;
            }

            Brand? brand = null;
            if (row.Brand is not null && !brands.TryGetValue(row.Brand, out brand))
            {
                brand = new Brand { Name = row.Brand };
                db.Brands.Add(brand);
                brands[row.Brand] = brand;
            }

            Product? product = null;
            var isNew = row.Sku is null || !existing.TryGetValue(row.Sku, out product);
            if (isNew)
            {
                product = new Product { Sku = row.Sku ?? await NextFreeSkuAsync(ct), PurchasePrice = row.PurchasePrice ?? 0 };
                db.Products.Add(product);
                created++;
            }
            else updated++;

            product!.Name = row.Name;
            product.Barcode = row.Barcode ?? product.Barcode;
            product.Category = category;
            product.Brand = brand ?? product.Brand;
            product.UnitId = row.Unit is null ? (isNew ? defaultUnitId : product.UnitId) : units[row.Unit];
            product.Size = row.Size ?? product.Size;
            product.Color = row.Color ?? product.Color;
            product.Model = row.Model ?? product.Model;
            if (row.PurchasePrice is { } pp) product.PurchasePrice = pp;
            product.RetailPrice = row.Retail;
            product.WholesalePrice = row.Wholesale ?? (isNew ? row.Retail : product.WholesalePrice);
            product.MinSalePrice = row.MinPrice ?? (isNew ? 0 : product.MinSalePrice);
            product.ReorderLevel = row.ReorderLevel ?? (isNew ? defaultReorderLevel : product.ReorderLevel);
            product.NormalizedName = ProductService.BuildSearchText(product.Name, product.Size, product.Color, product.Model,
                brand?.Name ?? row.Brand, product.Sku, product.Barcode);

            if (isNew && row.OpeningQuantity is > 0 and var qty) openings.Add((product, qty, row.PurchasePrice ?? 0));
        }

        await db.SaveChangesAsync(ct);
        foreach (var (product, qty, cost) in openings.OrderBy(o => o.Product.Id))
            await inventory.ReceiveAsync(new StockEntry(product.Id, warehouseId, qty, StockMovementType.OpeningBalance,
                "ProductImport", product.Id, product.Sku, "رصيد افتتاحي (استيراد Excel)"), cost, ct);

        audit.Log("Product.Import", "Product", null, null, new { created, updated });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new ProductImportResult(created, updated, []);
    }

    private async Task<string> NextFreeSkuAsync(CancellationToken ct)
    {
        while (true)
        {
            var sku = await numbers.NextAsync(DocumentType.Product, ct);
            if (!await db.Products.AnyAsync(p => p.Sku == sku || p.Barcode == sku, ct) && !db.Products.Local.Any(p => p.Sku == sku)) return sku;
        }
    }

    private static string ToAscii(string s) => new(s.Select(c => c switch
    {
        >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
        '٫' => '.',
        '٬' => ',',
        _ => c,
    }).ToArray());
}
