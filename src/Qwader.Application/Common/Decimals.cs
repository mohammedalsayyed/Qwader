namespace Qwader.Application.Common;

/// <summary>
/// How many decimals a number may carry: money 2 (agorot), quantities 3. A request with more would be computed with them and
/// then stored rounded, field by field, so a total could stop matching its parts by a cent.
/// </summary>
public static class Decimals
{
    public static bool Money(decimal value) => value == Math.Round(value, 2);
    public static bool Quantity(decimal value) => value == Math.Round(value, 3);
    public const string MoneyMessage = "المبالغ والأسعار بمنزلتين عشريتين على الأكثر";
    public const string QuantityMessage = "الكمية بثلاث منازل عشرية على الأكثر";
}
