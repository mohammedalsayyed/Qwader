using FluentValidation;
using Qwader.Application.Common;
using Qwader.Domain.Purchasing;

namespace Qwader.Application.Purchasing;

public sealed record PurchaseLineInput(int ProductId, decimal Quantity, decimal UnitCost, decimal LineDiscount = 0);

public sealed record SavePurchaseRequest(
    Guid IdempotencyKey,
    int SupplierId,
    int? WarehouseId,
    DateOnly? Date,
    string? SupplierInvoiceNo,
    decimal Discount,
    decimal PaidAmount,
    int? PaymentAccountId,
    string? Notes,
    IReadOnlyList<PurchaseLineInput> Lines,
    /// <summary>True posts immediately (stock, supplier balance, payment); false keeps a draft.</summary>
    bool Post);

public sealed record PostPurchaseRequest(Guid IdempotencyKey);

public sealed record VoidRequest(string Reason);

public sealed record ReturnLineInput(int PurchaseInvoiceLineId, decimal Quantity);

public sealed record PurchaseReturnRequest(
    Guid IdempotencyKey,
    DateOnly? Date,
    ReturnSettlement Settlement,
    int? RefundAccountId,
    string? Reason,
    IReadOnlyList<ReturnLineInput> Lines);

public sealed record PurchaseListItemDto(
    int Id, string Number, DateTime Date, int SupplierId, string SupplierName, string? SupplierInvoiceNo,
    PurchaseStatus Status, decimal Total, decimal PaidAmount, decimal ReturnedTotal, int LineCount);

public sealed record PurchaseLineDto(
    int Id, int ProductId, string Sku, string ProductName, string UnitName,
    decimal Quantity, decimal UnitCost, decimal LineDiscount, decimal LineTotal, decimal NetUnitCost, decimal ReturnedQuantity);

public sealed record PurchaseReturnSummaryDto(int Id, string Number, DateTime Date, decimal Total, ReturnSettlement Settlement, string? Reason);

public sealed record PurchaseDetailDto(
    int Id, string Number, DateTime Date, PurchaseStatus Status,
    int SupplierId, string SupplierName, string? SupplierInvoiceNo, int WarehouseId, string WarehouseName,
    decimal Subtotal, decimal Discount, decimal Total, decimal PaidAmount, decimal Remaining,
    int? PaymentAccountId, string? PaymentAccountName, string? Notes,
    string? CreatedByName, DateTime CreatedAt, DateTime? PostedAt, DateTime? VoidedAt, string? VoidReason,
    IReadOnlyList<PurchaseLineDto> Lines, IReadOnlyList<PurchaseReturnSummaryDto> Returns);

public sealed class PurchaseListRequest : PagedRequest
{
    public int? SupplierId { get; set; }
    public PurchaseStatus? Status { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}

public interface IPurchaseService
{
    Task<PagedResult<PurchaseListItemDto>> ListAsync(PurchaseListRequest request, CancellationToken ct = default);
    Task<PurchaseDetailDto> GetAsync(int id, CancellationToken ct = default);
    Task<PurchaseDetailDto> CreateAsync(SavePurchaseRequest request, CancellationToken ct = default);
    Task<PurchaseDetailDto> UpdateDraftAsync(int id, SavePurchaseRequest request, CancellationToken ct = default);
    Task<PurchaseDetailDto> PostAsync(int id, PostPurchaseRequest request, CancellationToken ct = default);
    Task<PurchaseDetailDto> VoidAsync(int id, VoidRequest request, CancellationToken ct = default);
    Task<PurchaseDetailDto> ReturnAsync(int id, PurchaseReturnRequest request, CancellationToken ct = default);
}

public sealed class SavePurchaseRequestValidator : AbstractValidator<SavePurchaseRequest>
{
    public SavePurchaseRequestValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.SupplierId).GreaterThan(0).WithMessage("اختر المورد");
        RuleFor(x => x.SupplierInvoiceNo).MaximumLength(50);
        RuleFor(x => x.Notes).MaximumLength(500);
        RuleFor(x => x.Discount).GreaterThanOrEqualTo(0).WithMessage("الخصم لا يمكن أن يكون سالبًا");
        RuleFor(x => x.PaidAmount).GreaterThanOrEqualTo(0).WithMessage("المبلغ المدفوع لا يمكن أن يكون سالبًا");
        RuleFor(x => x.PaymentAccountId).NotNull().When(x => x.PaidAmount > 0).WithMessage("اختر الحساب الذي دُفع منه المبلغ");
        RuleFor(x => x.Lines).NotEmpty().WithMessage("أضف منتجًا واحدًا على الأقل");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).GreaterThan(0).WithMessage("اختر المنتج");
            line.RuleFor(l => l.Quantity).GreaterThan(0).WithMessage("الكمية يجب أن تكون أكبر من صفر");
            line.RuleFor(l => l.UnitCost).GreaterThanOrEqualTo(0).WithMessage("تكلفة الوحدة لا يمكن أن تكون سالبة");
            line.RuleFor(l => l.LineDiscount).GreaterThanOrEqualTo(0).WithMessage("الخصم لا يمكن أن يكون سالبًا");
        });
    }
}

public sealed class PurchaseReturnRequestValidator : AbstractValidator<PurchaseReturnRequest>
{
    public PurchaseReturnRequestValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.Settlement).IsInEnum();
        RuleFor(x => x.RefundAccountId).NotNull().When(x => x.Settlement == ReturnSettlement.Refund).WithMessage("اختر الحساب الذي استُلم فيه المبلغ");
        RuleFor(x => x.Reason).MaximumLength(300);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("حدد الكميات المرتجعة");
        RuleForEach(x => x.Lines).ChildRules(l => l.RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("الكمية يجب أن تكون أكبر من صفر"));
    }
}

public sealed class VoidRequestValidator : AbstractValidator<VoidRequest>
{
    public VoidRequestValidator() =>
        RuleFor(x => x.Reason).NotEmpty().WithMessage("سبب الإلغاء مطلوب").MaximumLength(300);
}
