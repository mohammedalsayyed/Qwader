using System.Globalization;
using ClosedXML.Excel;
using Qwader.Application.Abstractions;

namespace Qwader.Infrastructure.Services;

/// <summary>Right-to-left Arabic workbooks with a bold, frozen header row.</summary>
public sealed class ExcelService : IExcelService
{
    public byte[] Write(string sheetName, IReadOnlyList<ExcelColumn> columns, IEnumerable<object?[]> rows)
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
                    case decimal d: cell.Value = d; cell.Style.NumberFormat.Format = "#,##0.##"; break;
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

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
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
