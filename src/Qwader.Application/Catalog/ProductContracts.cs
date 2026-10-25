using FluentValidation;
using Qwader.Application.Common;

namespace Qwader.Application.Catalog;

public enum StockStatus
{
    Available = 1,
    Low = 2,
    Out = 3,
}

public enum StockFilter
{
    All = 0,
    Available = 1,
    Low = 2,
    Out = 3,
    /// <summary>Low or out — the reorder list.</summary>
    NeedsReorder = 4,
}

public sealed record ProductListItemDto(
    int Id,
    string Sku,
    string? Barcode,
    string Name,
    string DisplayName,
    int CategoryId,
    string CategoryName,
    string UnitName,
    decimal RetailPrice,
    decimal WholesalePrice,
    decimal MinSalePrice,
    decimal Quantity,
    decimal ReorderLevel,
    StockStatus StockStatus,
    Guid? ImageFileId,
    bool IsArchived,
    /// <summary>Null unless the caller may view costs.</summary>
    decimal? AverageCost);

public sealed class ProductListRequest : PagedRequest
{
    public int? CategoryId { get; set; }
    public int? SupplierId { get; set; }
    public StockFilter Stock { get; set; } = StockFilter.All;
    public bool Archived { get; set; }
}

public sealed record StockByWarehouseDto(int WarehouseId, string WarehouseName, decimal Quantity);

public sealed record ProductRefDto(int Id, string Sku, string DisplayName, decimal Quantity, decimal RetailPrice);

public sealed record ProductDetailDto(
    int Id,
    string Sku,
    string? Barcode,
    string Name,
    string DisplayName,
    string? Description,
    int CategoryId,
    string CategoryName,
    string? BrandName,
    int UnitId,
    string UnitName,
    int? DefaultSupplierId,
    string? DefaultSupplierName,
    int? ParentProductId,
    string? ParentDisplayName,
    string? Size,
    string? Color,
    string? Model,
    decimal? PurchasePrice,
    decimal? AverageCost,
    decimal WholesalePrice,
    decimal RetailPrice,
    decimal MinSalePrice,
    decimal ReorderLevel,
    decimal Quantity,
    StockStatus StockStatus,
    decimal? StockValue,
    decimal? ExpectedProfit,
    decimal? MarginPercent,
    decimal? SuggestedRetailPrice,
    Guid? ImageFileId,
    bool IsArchived,
    DateTime CreatedAt,
    IReadOnlyList<StockByWarehouseDto> Stock,
    IReadOnlyList<ProductRefDto> Variants);

public sealed record SaveProductRequest(
    string Name,
    string? Sku,
    string? Barcode,
    int CategoryId,
    string? BrandName,
    int UnitId,
    int? DefaultSupplierId,
    int? ParentProductId,
    string? Size,
    string? Color,
    string? Model,
    string? Description,
    /// <summary>Null keeps the stored value (callers without cost permission never see it).</summary>
    decimal? PurchasePrice,
    decimal WholesalePrice,
    decimal RetailPrice,
    decimal MinSalePrice,
    decimal ReorderLevel,
    Guid? ImageFileId,
    /// <summary>Create only: opening stock entered with the product.</summary>
    decimal? OpeningQuantity = null,
    decimal? OpeningUnitCost = null,
    int? WarehouseId = null);

public sealed class SaveProductRequestValidator : AbstractValidator<SaveProductRequest>
{
    public SaveProductRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("اسم المنتج مطلوب").MaximumLength(150).WithMessage("اسم المنتج طويل جدًا");
        RuleFor(x => x.Sku).MaximumLength(50).WithMessage("الكود طويل جدًا").Matches(@"^\S*$").WithMessage("الكود يجب ألا يحتوي على مسافات");
        RuleFor(x => x.Barcode).MaximumLength(50).Matches(@"^\S*$").WithMessage("الباركود يجب ألا يحتوي على مسافات");
        RuleFor(x => x.CategoryId).GreaterThan(0).WithMessage("اختر الصنف");
        RuleFor(x => x.UnitId).GreaterThan(0).WithMessage("اختر وحدة القياس");
        RuleFor(x => x.BrandName).MaximumLength(100);
        RuleFor(x => x.Size).MaximumLength(50);
        RuleFor(x => x.Color).MaximumLength(50);
        RuleFor(x => x.Model).MaximumLength(50);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.PurchasePrice).GreaterThanOrEqualTo(0).WithMessage("سعر الشراء لا يمكن أن يكون سالبًا");
        RuleFor(x => x.RetailPrice).GreaterThan(0).WithMessage("سعر البيع بالمفرق مطلوب");
        RuleFor(x => x.WholesalePrice).GreaterThanOrEqualTo(0).WithMessage("سعر الجملة لا يمكن أن يكون سالبًا");
        RuleFor(x => x.MinSalePrice).GreaterThanOrEqualTo(0).WithMessage("أقل سعر بيع لا يمكن أن يكون سالبًا")
            .LessThanOrEqualTo(x => x.RetailPrice).WithMessage("أقل سعر بيع يجب ألا يتجاوز سعر المفرق");
        RuleFor(x => x.ReorderLevel).GreaterThanOrEqualTo(0).WithMessage("حد التنبيه لا يمكن أن يكون سالبًا");
        RuleFor(x => x.OpeningQuantity).GreaterThanOrEqualTo(0).WithMessage("الكمية الافتتاحية لا يمكن أن تكون سالبة");
        RuleFor(x => x.OpeningUnitCost).GreaterThanOrEqualTo(0).WithMessage("التكلفة لا يمكن أن تكون سالبة");
    }
}

public interface IProductService
{
    Task<PagedResult<ProductListItemDto>> ListAsync(ProductListRequest request, CancellationToken ct = default);
    Task<ProductDetailDto> GetAsync(int id, CancellationToken ct = default);
    Task<ProductDetailDto?> FindByCodeAsync(string code, CancellationToken ct = default);
    Task<ProductDetailDto> CreateAsync(SaveProductRequest request, CancellationToken ct = default);
    Task<ProductDetailDto> UpdateAsync(int id, SaveProductRequest request, CancellationToken ct = default);
    Task SetArchivedAsync(int id, bool archived, CancellationToken ct = default);
    Task<bool> IsCodeTakenAsync(string code, int? exceptId, CancellationToken ct = default);
}
