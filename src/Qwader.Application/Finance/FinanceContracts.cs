using FluentValidation;
using Qwader.Application.Common;
using Qwader.Domain.Finance;

namespace Qwader.Application.Finance;

// ------------------------------------------------------------------ accounts

public sealed record AccountDto(
    int Id, string Name, FinancialAccountType Type, string? ProviderName, string? HolderName,
    /// <summary>Masked («•••• 1234») unless the caller may view account numbers.</summary>
    string? AccountNumber, decimal OpeningBalance, decimal CurrentBalance, bool IsActive, decimal PendingTransfers);

public sealed record SaveAccountRequest(string Name, FinancialAccountType Type, string? ProviderName, string? HolderName, string? AccountNumber, bool IsActive = true);

/// <summary>Sets the account's opening balance; the difference is posted as a documented «رصيد افتتاحي» movement.</summary>
public sealed record SetOpeningBalanceRequest(decimal OpeningBalance, string? Notes);

public sealed record AccountMovementDto(long Id, DateTime Date, MoneyDirection Direction, decimal Amount, decimal BalanceAfter,
    AccountTransactionCategory Category, string CategoryName, string SourceType, int? SourceId, string? SourceNumber, string? Description, string? UserName);

public sealed record AccountStatementDto(int AccountId, string AccountName, DateOnly? From, DateOnly To, decimal OpeningBalance,
    decimal TotalIn, decimal TotalOut, decimal ClosingBalance, PagedResult<AccountMovementDto> Movements);

public sealed record CreateAccountTransferRequest(Guid IdempotencyKey, int FromAccountId, int ToAccountId, decimal Amount, string? Notes);

public sealed record AccountTransferDto(int Id, string Number, DateTime Date, int FromAccountId, string FromAccountName,
    int ToAccountId, string ToAccountName, decimal Amount, string? Notes, string? CreatedByName);

// ------------------------------------------------------------------ incoming transfers

public sealed record CreateIncomingTransferRequest(
    Guid IdempotencyKey, string SenderName, string? SenderPhone, decimal Amount, string? Channel, int FinancialAccountId,
    string? ReferenceNo, DateOnly? TransferDate, Guid? ProofFileId, int? CustomerId, string? Notes);

public sealed record RejectTransferRequest(string Reason);

public sealed record IncomingTransferDto(
    int Id, string Number, string SenderName, string? SenderPhone, decimal Amount, string? Channel,
    int FinancialAccountId, string AccountName, string? ReferenceNo, DateTime TransferDate, Guid? ProofFileId,
    TransferStatus Status, int? CustomerId, string? CustomerName, int? SaleInvoiceId, string? SaleInvoiceNumber, string? Notes,
    string? RecordedByName, DateTime CreatedAt, string? ReviewedByName, DateTime? ReviewedAt, string? RejectReason,
    int? ReceiptVoucherId, string? ReceiptVoucherNumber);

public sealed class TransferListRequest : PagedRequest
{
    public TransferStatus? Status { get; set; }
    public int? AccountId { get; set; }
}

// ------------------------------------------------------------------ vouchers & expenses

public sealed record CreateReceiptRequest(
    Guid IdempotencyKey, ReceiptPurpose Purpose, int? CustomerId, string? PayerName, string? PayerPhone,
    decimal Amount, int FinancialAccountId, string? Reference, string? Notes, Guid? AttachmentFileId, bool ConfirmDuplicate = false);

public sealed record CreatePaymentRequest(
    Guid IdempotencyKey, PaymentPurpose Purpose, int? SupplierId, string? BeneficiaryName,
    decimal Amount, int FinancialAccountId, string? Reference, string? Notes, Guid? AttachmentFileId, bool ConfirmDuplicate = false);

public sealed record CreateExpenseRequest(
    Guid IdempotencyKey, int ExpenseCategoryId, string Description, decimal Amount, int FinancialAccountId,
    DateOnly? Date, Guid? AttachmentFileId);

public sealed record VoidDocumentRequest(string Reason);

public sealed record VoucherDto(
    int Id, string Number, DateTime Date, string PartyName, string? PartyPhone, int? CustomerId, int? SupplierId,
    decimal Amount, int FinancialAccountId, string AccountName, string Purpose, string PurposeName,
    string? Reference, string? Notes, Guid? AttachmentFileId, VoucherStatus Status, string? CreatedByName,
    DateTime? VoidedAt, string? VoidReason, int? IncomingTransferId, int? SaleInvoiceId, string? ExpenseCategoryName);

public sealed record ExpenseDto(
    int Id, string Number, DateTime Date, int ExpenseCategoryId, string CategoryName, string Description, decimal Amount,
    int FinancialAccountId, string AccountName, Guid? AttachmentFileId, int PaymentVoucherId, string PaymentVoucherNumber,
    VoucherStatus Status, string? CreatedByName);

public sealed class VoucherListRequest : PagedRequest
{
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int? AccountId { get; set; }
    public decimal? MinAmount { get; set; }
    public decimal? MaxAmount { get; set; }
    public bool? IncludeVoided { get; set; }
    public int? CustomerId { get; set; }
    public int? SupplierId { get; set; }
    public int? CategoryId { get; set; }
}

public sealed record SaveExpenseCategoryRequest(string Name, bool IsActive = true);

public sealed record ExpenseCategoryDto(int Id, string Name, bool IsActive, int ExpenseCount);

public static class FinanceNames
{
    public static string Category(AccountTransactionCategory c) => c switch
    {
        AccountTransactionCategory.SaleReceipt => "مقبوضات مبيعات",
        AccountTransactionCategory.DebtCollection => "تحصيل دين",
        AccountTransactionCategory.OtherIncome => "إيراد آخر",
        AccountTransactionCategory.SupplierPayment => "دفعة لمورد",
        AccountTransactionCategory.SupplierRefund => "استرداد من مورد",
        AccountTransactionCategory.Expense => "مصروف",
        AccountTransactionCategory.SaleRefund => "استرداد لعميل",
        AccountTransactionCategory.OtherPayment => "صرف آخر",
        AccountTransactionCategory.TransferIn => "تحويل وارد من حساب",
        AccountTransactionCategory.TransferOut => "تحويل صادر إلى حساب",
        AccountTransactionCategory.Reversal => "قيد عكسي",
        AccountTransactionCategory.OpeningBalance => "رصيد افتتاحي",
        _ => c.ToString(),
    };

    public static string Receipt(ReceiptPurpose p) => p == ReceiptPurpose.DebtCollection ? "تحصيل دين من عميل" : "إيراد آخر";

    public static string Payment(PaymentPurpose p) => p switch
    {
        PaymentPurpose.SupplierPayment => "دفعة لمورد",
        PaymentPurpose.Expense => "مصروف",
        _ => "صرف آخر",
    };
}

// ------------------------------------------------------------------ validators

public sealed class SaveAccountRequestValidator : AbstractValidator<SaveAccountRequest>
{
    public SaveAccountRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("اسم الحساب مطلوب").MaximumLength(100);
        RuleFor(x => x.Type).IsInEnum().WithMessage("اختر نوع الحساب");
        RuleFor(x => x.ProviderName).MaximumLength(100);
        RuleFor(x => x.HolderName).MaximumLength(100);
        RuleFor(x => x.AccountNumber).MaximumLength(50);
    }
}

public sealed class SetOpeningBalanceRequestValidator : AbstractValidator<SetOpeningBalanceRequest>
{
    public SetOpeningBalanceRequestValidator()
    {
        RuleFor(x => x.OpeningBalance).GreaterThanOrEqualTo(0).WithMessage("الرصيد الافتتاحي لا يمكن أن يكون سالبًا");
        RuleFor(x => x.Notes).MaximumLength(300);
    }
}

public sealed class CreateAccountTransferRequestValidator : AbstractValidator<CreateAccountTransferRequest>
{
    public CreateAccountTransferRequestValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("المبلغ يجب أن يكون أكبر من صفر");
        RuleFor(x => x.ToAccountId).NotEqual(x => x.FromAccountId).WithMessage("اختر حسابين مختلفين");
        RuleFor(x => x.Notes).MaximumLength(300);
    }
}

public sealed class CreateIncomingTransferRequestValidator : AbstractValidator<CreateIncomingTransferRequest>
{
    public CreateIncomingTransferRequestValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.SenderName).NotEmpty().WithMessage("اسم صاحب الحوالة مطلوب").MaximumLength(150);
        RuleFor(x => x.SenderPhone).MaximumLength(20);
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("المبلغ يجب أن يكون أكبر من صفر");
        RuleFor(x => x.Channel).MaximumLength(100);
        RuleFor(x => x.FinancialAccountId).GreaterThan(0).WithMessage("اختر الحساب المستلم");
        RuleFor(x => x.ReferenceNo).MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class RejectTransferRequestValidator : AbstractValidator<RejectTransferRequest>
{
    public RejectTransferRequestValidator() => RuleFor(x => x.Reason).NotEmpty().WithMessage("سبب الرفض مطلوب").MaximumLength(300);
}

public sealed class CreateReceiptRequestValidator : AbstractValidator<CreateReceiptRequest>
{
    public CreateReceiptRequestValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.Purpose).IsInEnum();
        RuleFor(x => x.CustomerId).NotNull().When(x => x.Purpose == ReceiptPurpose.DebtCollection).WithMessage("اختر العميل");
        RuleFor(x => x.PayerName).NotEmpty().When(x => x.CustomerId is null).WithMessage("اسم الدافع مطلوب");
        RuleFor(x => x.PayerName).MaximumLength(150);
        RuleFor(x => x.PayerPhone).MaximumLength(20);
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("المبلغ يجب أن يكون أكبر من صفر");
        RuleFor(x => x.FinancialAccountId).GreaterThan(0).WithMessage("اختر الحساب المستلم");
        RuleFor(x => x.Reference).MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class CreatePaymentRequestValidator : AbstractValidator<CreatePaymentRequest>
{
    public CreatePaymentRequestValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.Purpose).Must(p => p is PaymentPurpose.SupplierPayment or PaymentPurpose.Other)
            .WithMessage("لتسجيل مصروف استخدم شاشة المصروفات");
        RuleFor(x => x.SupplierId).NotNull().When(x => x.Purpose == PaymentPurpose.SupplierPayment).WithMessage("اختر المورد");
        RuleFor(x => x.BeneficiaryName).NotEmpty().When(x => x.SupplierId is null).WithMessage("اسم المستفيد مطلوب");
        RuleFor(x => x.BeneficiaryName).MaximumLength(150);
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("المبلغ يجب أن يكون أكبر من صفر");
        RuleFor(x => x.FinancialAccountId).GreaterThan(0).WithMessage("اختر الحساب الذي ستخرج منه الأموال");
        RuleFor(x => x.Reference).MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class CreateExpenseRequestValidator : AbstractValidator<CreateExpenseRequest>
{
    public CreateExpenseRequestValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.ExpenseCategoryId).GreaterThan(0).WithMessage("اختر نوع المصروف");
        RuleFor(x => x.Description).NotEmpty().WithMessage("وصف المصروف مطلوب").MaximumLength(300);
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("المبلغ يجب أن يكون أكبر من صفر");
        RuleFor(x => x.FinancialAccountId).GreaterThan(0).WithMessage("اختر الحساب الذي دُفع منه");
    }
}

public sealed class VoidDocumentRequestValidator : AbstractValidator<VoidDocumentRequest>
{
    public VoidDocumentRequestValidator() => RuleFor(x => x.Reason).NotEmpty().WithMessage("سبب الإلغاء مطلوب").MaximumLength(300);
}

public sealed class SaveExpenseCategoryRequestValidator : AbstractValidator<SaveExpenseCategoryRequest>
{
    public SaveExpenseCategoryRequestValidator() => RuleFor(x => x.Name).NotEmpty().WithMessage("اسم النوع مطلوب").MaximumLength(50);
}
