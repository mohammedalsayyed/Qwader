using FluentValidation;
using Qwader.Application.Common;
using Qwader.Domain.Purchasing;

namespace Qwader.Application.Purchasing;

/// <summary>ProductUnitId = bought in an extra unit (dozen); null = the base unit. Quantity and cost are per that unit.</summary>
public sealed record PurchaseLineInput(int ProductId, decimal Quantity, decimal UnitCost, decimal LineDiscount = 0, int? ProductUnitId = null);

/// <summary>Part of the payment at purchase from one account.</summary>
public sealed record PurchasePaymentInput(int AccountId, decimal Amount);

/// <param name="Payments">Paid from several accounts (cash and bank together). When given, it replaces PaidAmount/PaymentAccountId
/// (PaidAmount becomes its sum); when null, the single PaidAmount from PaymentAccountId as before.</param>
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
    bool Post,
    IReadOnlyList<PurchasePaymentInput>? Payments = null);

public sealed record PostPurchaseRequest(Guid IdempotencyKey);

/// <summary>Details that move no money or stock.</summary>
public sealed record UpdatePurchaseDetailsRequest(string? Notes, string? SupplierInvoiceNo);

/// <summary>A reason; for a sale void, the day it happened (today by default, or an earlier day).</summary>
public sealed record VoidRequest(string Reason = "", DateOnly? Date = null);

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
    decimal Quantity, decimal UnitCost, decimal LineDiscount, decimal LineTotal, decimal NetUnitCost, decimal ReturnedQuantity,
    int? ProductUnitId = null, decimal UnitFactor = 1);

public sealed record PurchasePaymentDto(int AccountId, string AccountName, decimal Amount);

public sealed record PurchaseReturnSummaryDto(int Id, string Number, DateTime Date, decimal Total, ReturnSettlement Settlement, string? Reason);

public sealed record PurchaseDetailDto(
    int Id, string Number, DateTime Date, PurchaseStatus Status,
    int SupplierId, string SupplierName, string? SupplierInvoiceNo, int WarehouseId, string WarehouseName,
    decimal Subtotal, decimal Discount, decimal Total, decimal PaidAmount, decimal Remaining,
    int? PaymentAccountId, string? PaymentAccountName, string? Notes,
    string? CreatedByName, DateTime CreatedAt, DateTime? PostedAt, DateTime? VoidedAt, string? VoidReason,
    IReadOnlyList<PurchaseLineDto> Lines, IReadOnlyList<PurchaseReturnSummaryDto> Returns,
    /// <summary>Paid at purchase, account by account (an old invoice's single account shows as one row).</summary>
    IReadOnlyList<PurchasePaymentDto> Payments);

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
    /// <summary>Edits a posted purchase: voids it and posts the new one in one transaction (see the service).</summary>
    Task<PurchaseDetailDto> ReplaceAsync(int id, SavePurchaseRequest request, CancellationToken ct = default);
    Task<PurchaseDetailDto> UpdateDetailsAsync(int id, UpdatePurchaseDetailsRequest request, CancellationToken ct = default);
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
        RuleFor(x => x.Discount).GreaterThanOrEqualTo(0).WithMessage("الخصم لا يمكن أن يكون سالبًا")
            .Must(Common.Decimals.Money).WithMessage(Common.Decimals.MoneyMessage);
        RuleFor(x => x.PaidAmount).GreaterThanOrEqualTo(0).WithMessage("المبلغ المدفوع لا يمكن أن يكون سالبًا")
            .Must(Common.Decimals.Money).WithMessage(Common.Decimals.MoneyMessage);
        RuleFor(x => x.PaymentAccountId).NotNull().When(x => x.PaidAmount > 0 && x.Payments is null).WithMessage("اختر الحساب الذي دُفع منه المبلغ");
        RuleForEach(x => x.Payments).ChildRules(p =>
        {
            p.RuleFor(x => x.AccountId).GreaterThan(0).WithMessage("اختر الحساب الذي دُفع منه المبلغ");
            p.RuleFor(x => x.Amount).GreaterThan(0).WithMessage("أدخل مبلغًا أكبر من صفر")
                .Must(Common.Decimals.Money).WithMessage(Common.Decimals.MoneyMessage);
        });
        RuleFor(x => x.Lines).NotEmpty().WithMessage("أضف منتجًا واحدًا على الأقل");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).GreaterThan(0).WithMessage("اختر المنتج");
            line.RuleFor(l => l.Quantity).GreaterThan(0).WithMessage("الكمية يجب أن تكون أكبر من صفر")
                .Must(Common.Decimals.Quantity).WithMessage(Common.Decimals.QuantityMessage);
            line.RuleFor(l => l.UnitCost).GreaterThanOrEqualTo(0).WithMessage("تكلفة الوحدة لا يمكن أن تكون سالبة")
                .Must(Common.Decimals.Money).WithMessage(Common.Decimals.MoneyMessage);
            line.RuleFor(l => l.LineDiscount).GreaterThanOrEqualTo(0).WithMessage("الخصم لا يمكن أن يكون سالبًا")
                .Must(Common.Decimals.Money).WithMessage(Common.Decimals.MoneyMessage);
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
