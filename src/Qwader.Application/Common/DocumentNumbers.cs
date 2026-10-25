using Qwader.Domain.Administration;

namespace Qwader.Application.Common;

public static class DocumentNumbers
{
    public static string Prefix(DocumentType type) => type switch
    {
        DocumentType.SaleInvoice => "INV",
        DocumentType.SaleReturn => "SR",
        DocumentType.PurchaseInvoice => "PUR",
        DocumentType.PurchaseReturn => "PR",
        DocumentType.ReceiptVoucher => "RV",
        DocumentType.PaymentVoucher => "PV",
        DocumentType.IncomingTransfer => "TR",
        DocumentType.AccountTransfer => "AT",
        DocumentType.Expense => "EXP",
        DocumentType.StockCount => "SC",
        DocumentType.StockAdjustment => "ADJ",
        DocumentType.Customer => "C",
        DocumentType.Supplier => "S",
        DocumentType.Product => "P",
        DocumentType.Category => "CAT",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };

    /// <summary>Master-data codes (customers, suppliers) are not reset yearly.</summary>
    public static bool IsYearly(DocumentType type) =>
        type is not (DocumentType.Customer or DocumentType.Supplier or DocumentType.Product or DocumentType.Category);

    public static string Format(DocumentType type, int year, long number) =>
        IsYearly(type) ? $"{Prefix(type)}-{year}-{number:D6}" : $"{Prefix(type)}-{number:D5}";
}
