namespace Qwader.Api.Infrastructure;

/// <summary>Arabic names for request fields, used in the validation messages a rule does not word itself.</summary>
public static class FieldNames
{
    public static readonly IReadOnlyDictionary<string, string> Arabic = new Dictionary<string, string>
    {
        ["IdempotencyKey"] = "مفتاح العملية",
        ["Purpose"] = "نوع السند",
        ["Amount"] = "المبلغ",
        ["Date"] = "التاريخ",
        ["Quantity"] = "الكمية",
        ["UnitPrice"] = "السعر",
        ["Price"] = "السعر",
        ["Reason"] = "السبب",
        ["Notes"] = "الملاحظات",
        ["Name"] = "الاسم",
        ["Phone"] = "رقم الجوال",
        ["AccountId"] = "الحساب",
        ["FinancialAccountId"] = "الحساب",
        ["FromAccountId"] = "الحساب المحوَّل منه",
        ["ToAccountId"] = "الحساب المحوَّل إليه",
        ["CustomerId"] = "العميل",
        ["SupplierId"] = "المورد",
        ["ProductId"] = "المنتج",
        ["CategoryId"] = "الصنف",
        ["UnitId"] = "الوحدة",
        ["PriceTier"] = "نوع السعر",
        ["Settlement"] = "التسوية",
        ["Condition"] = "حالة البضاعة",
        ["Lines"] = "البنود",
        ["Payments"] = "الدفعات",
        ["Type"] = "النوع",
        ["Status"] = "الحالة",
    };
}
