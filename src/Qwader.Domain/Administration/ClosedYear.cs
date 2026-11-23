namespace Qwader.Domain.Administration;

/// <summary>
/// «إقفال السنة»: a year whose books are closed. While the feature is on (setting Finance.YearClosing), nothing dated in a closed
/// year can be recorded, edited, voided or restored; its documents stay readable and its reports unchanged. The figures of the
/// balance sheet on its last day are kept as they were at closing. Years close in order and only the last one reopens; a
/// reopened row stays (ReopenedAt) so the history shows who reopened it and why.
/// </summary>
public class ClosedYear
{
    public int Id { get; set; }
    public int Year { get; set; }
    public DateTime ClosedAt { get; set; }
    public int? ClosedById { get; set; }
    public decimal NetProfit { get; set; }
    public decimal TotalAssets { get; set; }
    public decimal TotalLiabilities { get; set; }
    public decimal TotalEquity { get; set; }
    /// <summary>The balance sheet's «الفرق المحاسبي» on the year's last day (0 when the books agree).</summary>
    public decimal Difference { get; set; }
    /// <summary>The whole balance sheet of the year's last day, as JSON.</summary>
    public string Snapshot { get; set; } = string.Empty;
    public DateTime? ReopenedAt { get; set; }
    public int? ReopenedById { get; set; }
    public string? ReopenReason { get; set; }
}
