using System.Globalization;
using ClosedXML.Excel;
using Qwader.Application.Abstractions;

namespace Qwader.Infrastructure.Services;

/// <summary>Right-to-left Arabic workbooks with a bold, frozen header row.</summary>
public sealed class ExcelService : IExcelService
{
    private static void SetValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null: break;
            case decimal d: cell.Value = d; cell.Style.NumberFormat.Format = d == decimal.Truncate(d) ? "#,##0" : "#,##0.00"; break;
            case int i: cell.Value = i; break;
            case double dbl: cell.Value = dbl; break;
            case DateTime dt: cell.Value = dt; cell.Style.DateFormat.Format = dt.TimeOfDay == TimeSpan.Zero ? "yyyy-mm-dd" : "yyyy-mm-dd hh:mm"; break;
            case DateOnly day: cell.Value = day.ToDateTime(TimeOnly.MinValue); cell.Style.DateFormat.Format = "yyyy-mm-dd"; break;
            case string s: cell.SetValue(s); cell.Style.NumberFormat.Format = "@"; break;
            default: cell.Value = Convert.ToString(value, CultureInfo.InvariantCulture); break;
        }
    }

    private static readonly XLColor Navy = XLColor.FromHtml("#0B3B6F");
    private static readonly XLColor Teal = XLColor.FromHtml("#D5EEEB");
    private static readonly XLColor Stripe = XLColor.FromHtml("#F7F9FB");
    private static readonly XLColor Line = XLColor.FromHtml("#C9D2DC");

    public byte[] WriteSheets(IReadOnlyList<ExcelSheet> sheets)
    {
        using var workbook = new XLWorkbook();
        foreach (var spec in sheets)
        {
            var sheet = workbook.Worksheets.Add(spec.Name);
            sheet.RightToLeft = true;
            var width = Math.Max(1, spec.Widths.Count);
            for (var c = 0; c < spec.Widths.Count; c++) sheet.Column(c + 1).Width = spec.Widths[c];
            var r = 1;
            var striped = 0;
            foreach (var row in spec.Rows)
            {
                if (row.Kind is ExcelRowKind.Title or ExcelRowKind.Note or ExcelRowKind.Brand or ExcelRowKind.Subtitle or ExcelRowKind.Section)
                {
                    var range = sheet.Range(r, 1, r, width).Merge();
                    SetValue(range.FirstCell(), row.Cells.FirstOrDefault());
                    range.Style.Alignment.WrapText = true;
                    range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                    switch (row.Kind)
                    {
                        case ExcelRowKind.Brand:
                            range.Style.Font.Bold = true;
                            range.Style.Font.FontSize = 22;
                            range.Style.Font.FontColor = Navy;
                            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            sheet.Row(r).Height = 36;
                            break;
                        case ExcelRowKind.Subtitle:
                            range.Style.Font.FontColor = XLColor.FromHtml("#4B5563");
                            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            break;
                        case ExcelRowKind.Section:
                            range.Style.Font.Bold = true;
                            range.Style.Font.FontSize = 13;
                            range.Style.Font.FontColor = Navy;
                            range.Style.Border.BottomBorder = XLBorderStyleValues.Medium;
                            range.Style.Border.BottomBorderColor = Navy;
                            sheet.Row(r).Height = 24;
                            break;
                        case ExcelRowKind.Title:
                            range.Style.Font.Bold = true;
                            range.Style.Font.FontSize = spec.Report ? 12 : 13;
                            range.Style.Fill.BackgroundColor = Navy;
                            range.Style.Font.FontColor = XLColor.White;
                            sheet.Row(r).Height = 22;
                            break;
                        default:
                            range.Style.Font.FontColor = XLColor.FromHtml("#6B7280");
                            range.Style.Font.Italic = spec.Report;
                            break;
                    }
                    striped = 0;
                }
                else
                {
                    var count = Math.Min(row.Cells.Count, width);
                    for (var c = 0; c < count; c++) SetValue(sheet.Cell(r, c + 1), row.Cells[c]);
                    if (row.Kind != ExcelRowKind.Blank && count > 0)
                    {
                        // A table row spans the columns it fills (a report's tables have different widths) or the whole sheet.
                        var range = sheet.Range(r, 1, r, spec.Report ? count : width);
                        switch (row.Kind)
                        {
                            case ExcelRowKind.Header:
                                range.Style.Font.Bold = true;
                                range.Style.Fill.BackgroundColor = Teal;
                                range.Style.Font.FontColor = Navy;
                                striped = 0;
                                break;
                            case ExcelRowKind.Total:
                                range.Style.Font.Bold = true;
                                range.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF1F5");
                                range.Style.Border.TopBorder = XLBorderStyleValues.Thin;
                                break;
                            case ExcelRowKind.Highlight:
                                range.Style.Font.Bold = true;
                                range.Style.Font.FontSize = 13;
                                range.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFF4D6");
                                range.Style.Font.FontColor = XLColor.FromHtml("#7A4B00");
                                sheet.Row(r).Height = 22;
                                break;
                            default:
                                if (spec.Report && striped++ % 2 == 1) range.Style.Fill.BackgroundColor = Stripe;
                                break;
                        }
                        if (spec.Report)
                        {
                            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                            range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                            range.Style.Border.OutsideBorderColor = Line;
                            range.Style.Border.InsideBorderColor = Line;
                            range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                            if (sheet.Row(r).Height < 18) sheet.Row(r).Height = 18;
                        }
                    }
                    if (row.Kind == ExcelRowKind.Blank) striped = 0;
                }
                r++;
            }
            sheet.PageSetup.PageOrientation = XLPageOrientation.Portrait;
            sheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
            sheet.PageSetup.FitToPages(1, 0);
            if (spec.Report)
            {
                sheet.ShowGridLines = false;
                sheet.PageSetup.CenterHorizontally = true;
                sheet.PageSetup.Margins.Left = 0.4;
                sheet.PageSetup.Margins.Right = 0.4;
                sheet.PageSetup.Margins.Top = 0.5;
                sheet.PageSetup.Margins.Bottom = 0.75;
                sheet.PageSetup.Margins.Footer = 0.3;
                sheet.PageSetup.Footer.Center.AddText("صفحة &P من &N");
            }
        }
        workbook.Worksheet(1).SetTabActive();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public byte[] Write(string sheetName, IReadOnlyList<ExcelColumn> columns, IEnumerable<object?[]> rows, string? listsSheet = null)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(sheetName);
        sheet.RightToLeft = true;

        for (var c = 0; c < columns.Count; c++)
        {
            var cell = sheet.Cell(1, c + 1);
            cell.Value = columns[c].Header;
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#D5EEEB");
            sheet.Column(c + 1).Width = columns[c].Width;
        }

        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Length && c < columns.Count; c++)
            {
                var cell = sheet.Cell(r, c + 1);
                switch (row[c])
                {
                    case null: break;
                    // "#,##0.##" shows a whole number as «938.» in Excel, so whole numbers get a format without the point.
                    case decimal d: cell.Value = d; cell.Style.NumberFormat.Format = d == decimal.Truncate(d) ? "#,##0" : "#,##0.##"; break;
                    case int i: cell.Value = i; break;
                    case double dbl: cell.Value = dbl; break;
                    case DateTime dt: cell.Value = dt; cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm"; break;
                    // Codes such as barcodes must stay text so Excel does not turn them into numbers.
                    case string s: cell.SetValue(s); cell.Style.NumberFormat.Format = "@"; break;
                    default: cell.Value = Convert.ToString(row[c], CultureInfo.InvariantCulture); break;
                }
            }
            r++;
        }

        sheet.SheetView.FreezeRows(1);
        if (r > 2) sheet.Range(1, 1, r - 1, columns.Count).SetAutoFilter();

        // Columns with a fixed set of values (categories, units…) get a drop-down fed from a second sheet, so the list can be long.
        // Typing another value stays allowed (a new category or unit is created on import). The sheet is shown when named.
        var lastRow = Math.Max(r + 2000, 5000);
        IXLWorksheet? lists = null;
        for (var c = 0; c < columns.Count; c++)
        {
            if (columns[c].Choices is not { Count: > 0 } choices) continue;
            if (lists is null)
            {
                lists = workbook.Worksheets.Add(listsSheet ?? "القوائم");
                lists.RightToLeft = true;
            }
            var listColumn = lists.LastColumnUsed()?.ColumnNumber() + 1 ?? 1;
            var title = lists.Cell(1, listColumn);
            title.Value = columns[c].Header;
            title.Style.Font.Bold = true;
            title.Style.Fill.BackgroundColor = XLColor.FromHtml("#D5EEEB");
            lists.Column(listColumn).Width = Math.Max(columns[c].Width, 18);
            for (var i = 0; i < choices.Count; i++) lists.Cell(i + 2, listColumn).SetValue(choices[i]);
            var validation = sheet.Range(2, c + 1, lastRow, c + 1).CreateDataValidation();
            validation.List(lists.Range(2, listColumn, choices.Count + 1, listColumn), true);
            validation.ShowErrorMessage = false;
        }
        if (lists is not null && listsSheet is null) lists.Visibility = XLWorksheetVisibility.Hidden;
        sheet.SetTabActive();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> ReadColumns(Stream content, string sheetName)
    {
        using var workbook = new XLWorkbook(content);
        if (!workbook.TryGetWorksheet(sheetName, out var sheet) || sheet.RangeUsed() is not { } used) return new Dictionary<string, IReadOnlyList<string>>();
        var result = new Dictionary<string, IReadOnlyList<string>>();
        for (var c = 1; c <= used.LastColumn().ColumnNumber(); c++)
        {
            var header = sheet.Cell(1, c).GetString().Trim();
            if (header.Length == 0) continue;
            result[header] = Enumerable.Range(2, Math.Max(0, used.LastRow().RowNumber() - 1))
                .Select(r => sheet.Cell(r, c).GetString().Trim()).Where(v => v.Length > 0).ToList();
        }
        return result;
    }

    public IReadOnlyList<(int RowNumber, IReadOnlyDictionary<string, string?> Cells)> Read(Stream content)
    {
        using var workbook = new XLWorkbook(content);
        var sheet = workbook.Worksheets.First();
        var used = sheet.RangeUsed();
        if (used is null) return [];

        var headerRow = used.FirstRow().RowNumber();
        var lastColumn = used.LastColumn().ColumnNumber();
        var headers = new Dictionary<int, string>();
        for (var c = 1; c <= lastColumn; c++)
        {
            var header = sheet.Cell(headerRow, c).GetString().Trim();
            if (header.Length > 0) headers[c] = header;
        }

        var result = new List<(int, IReadOnlyDictionary<string, string?>)>();
        for (var r = headerRow + 1; r <= used.LastRow().RowNumber(); r++)
        {
            var cells = new Dictionary<string, string?>();
            foreach (var (c, header) in headers)
            {
                var cell = sheet.Cell(r, c);
                cells[header] = cell.DataType == XLDataType.Number
                    ? cell.GetDouble().ToString(CultureInfo.InvariantCulture)
                    : cell.GetString();
            }
            result.Add((r, cells));
        }
        return result;
    }
}
