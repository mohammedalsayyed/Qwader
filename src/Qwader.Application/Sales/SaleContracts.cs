using FluentValidation;
using Qwader.Application.Common;
using Qwader.Domain.Purchasing;
using Qwader.Domain.Sales;

namespace Qwader.Application.Sales;

public sealed record SaleLineInput(int ProductId, decimal Quantity, decimal UnitPrice, decimal LineDiscount = 0);

/// <summary>Pending = a transfer the customer says they sent but nobody has verified yet: it stays as debt until confirmed.</summary>
public sealed record SalePaymentInput(int AccountId, decimal Amount, string? Reference = null, bool Pending = false);

public sealed record CreateSaleRequest(
    Guid IdempotencyKey,
    PriceTier PriceTier,
    /// <summary>An existing customer; or leave null and give name + phone to find/create one; or neither for «زبون نقدي».</summary>
    int? CustomerId,
    string? CustomerName,
    string? CustomerPhone,
    decimal InvoiceDiscount,
    IReadOnlyList<SaleLineInput> Lines,
    IReadOnlyList<SalePaymentInput> Payments,
    string? Notes);

public sealed record SaleReturnLineInput(int SaleInvoiceLineId, decimal Quantity, ReturnCondition Condition = ReturnCondition.Sellable);

public sealed record SaleReturnRequest(
    Guid IdempotencyKey,
    ReturnSettlement Settlement,
    int? RefundAccountId,
    string? Reason,
    IReadOnlyList<SaleReturnLineInput> Lines);

public sealed record SaleListItemDto(
    int Id, string Number, DateTime Date, string CustomerName, string? CustomerPhone, bool IsWalkIn,
    decimal Total, decimal PaidAmount, decimal ReturnedTotal, SaleStatus Status, string SalespersonName, int ItemCount);

public sealed record SaleLineDto(
    int Id, int ProductId, string Sku, string ProductName, string UnitName, decimal Quantity, decimal UnitPrice, decimal LineDiscount,
    decimal LineTotal, decimal NetTotal, decimal ReturnedQuantity, decimal? UnitCost, bool PriceOverridden);

public sealed record SalePaymentDto(int Id, int AccountId, string AccountName, decimal Amount, string? Reference);

public sealed record SalePendingTransferDto(int Id, string Number, decimal Amount, string AccountName, string? Reference, Domain.Finance.TransferStatus Status);

public sealed record SaleReturnSummaryDto(int Id, string Number, DateTime Date, decimal Total, ReturnSettlement Settlement, string? Reason);

public sealed record SaleDetailDto(
    int Id, string Number, DateTime Date, SaleStatus Status, PriceTier PriceTier,
    int CustomerId, string CustomerName, string? CustomerPhone, bool IsWalkIn,
    decimal GrossTotal, decimal LineDiscountTotal, decimal InvoiceDiscount, decimal Total, decimal PaidAmount, decimal Remaining,
    decimal ReturnedTotal, decimal? TotalCost, decimal? Profit,
    string SalespersonName, string? Notes, DateTime? VoidedAt, string? VoidReason,
    IReadOnlyList<SaleLineDto> Lines, IReadOnlyList<SalePaymentDto> Payments, IReadOnlyList<SaleReturnSummaryDto> Returns,
    IReadOnlyList<SalePendingTransferDto> Transfers);

public sealed class SaleListRequest : PagedRequest
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public SaleStatus? Status { get; set; }
    public int? CustomerId { get; set; }
    /// <summary>Only invoices with money still owed at the till.</summary>
    public bool? Unpaid { get; set; }
}

public sealed record AccountAmountDto(int AccountId, string AccountName, decimal Amount);

public sealed record SalesSummaryDto(
    DateOnly From, DateOnly To, bool OwnSalesOnly,
    int InvoiceCount, decimal GrossSales, decimal Discounts, decimal Returns, decimal NetSales,
    decimal? CostOfSales, decimal? GrossProfit,
    decimal Received, decimal NewDebt, IReadOnlyList<AccountAmountDto> ReceivedByAccount);

public interface ISaleService
{
    Task<SaleDetailDto> CreateAsync(CreateSaleRequest request, CancellationToken ct = default);
    Task<PagedResult<SaleListItemDto>> ListAsync(SaleListRequest request, CancellationToken ct = default);
    Task<SaleDetailDto> GetAsync(int id, CancellationToken ct = default);
    Task<SaleDetailDto> ReturnAsync(int id, SaleReturnRequest request, CancellationToken ct = default);
    Task<SaleDetailDto> VoidAsync(int id, Purchasing.VoidRequest request, CancellationToken ct = default);
    Task<SalesSummaryDto> SummaryAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default);
}

public sealed class CreateSaleRequestValidator : AbstractValidator<CreateSaleRequest>
{
    public CreateSaleRequestValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.PriceTier).IsInEnum();
        RuleFor(x => x.Lines).NotEmpty().WithMessage("أضف منتجًا واحدًا على الأقل للفاتورة");
        RuleFor(x => x.Lines.Count).LessThanOrEqualTo(300).WithMessage("عدد البنود كبير جدًا لفاتورة واحدة");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.ProductId).GreaterThan(0);
            l.RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("الكمية يجب أن تكون أكبر من صفر");
            l.RuleFor(x => x.UnitPrice).GreaterThanOrEqualTo(0).WithMessage("السعر لا يمكن أن يكون سالبًا");
            l.RuleFor(x => x.LineDiscount).GreaterThanOrEqualTo(0).WithMessage("الخصم لا يمكن أن يكون سالبًا");
        });
        RuleFor(x => x.InvoiceDiscount).GreaterThanOrEqualTo(0).WithMessage("الخصم لا يمكن أن يكون سالبًا");
        RuleForEach(x => x.Payments).ChildRules(p =>
        {
            p.RuleFor(x => x.AccountId).GreaterThan(0).WithMessage("اختر الحساب المستلم");
            p.RuleFor(x => x.Amount).GreaterThan(0).WithMessage("مبلغ الدفعة يجب أن يكون أكبر من صفر");
            p.RuleFor(x => x.Reference).MaximumLength(100);
        });
        RuleFor(x => x.CustomerName).MaximumLength(150);
        RuleFor(x => x.CustomerPhone).MaximumLength(20);
        RuleFor(x => x.CustomerName).NotEmpty().When(x => x.CustomerId is null && !string.IsNullOrWhiteSpace(x.CustomerPhone))
            .WithMessage("اكتب اسم العميل");
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class SaleReturnRequestValidator : AbstractValidator<SaleReturnRequest>
{
    public SaleReturnRequestValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.Settlement).IsInEnum();
        RuleFor(x => x.RefundAccountId).NotNull().When(x => x.Settlement == ReturnSettlement.Refund)
            .WithMessage("اختر الحساب الذي سيُدفع منه المبلغ المسترد");
        RuleFor(x => x.Reason).MaximumLength(300);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("حدد المنتجات والكميات المرتجعة");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("الكمية يجب أن تكون أكبر من صفر");
            l.RuleFor(x => x.Condition).IsInEnum();
        });
    }
}
