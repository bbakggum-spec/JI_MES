using System.Globalization;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using JiMes.Api.Features.Printing.Fixed;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Security;
using QuestPDF.Fluent;
using QuestPDF.Helpers;

namespace JiMes.Api.Features.Export;

public sealed record ExportColumn(string Title, bool Number = false);

public sealed record ExportRequest(string? Title, string? Format, IReadOnlyList<ExportColumn>? Columns, IReadOnlyList<IReadOnlyList<JsonElement>>? Rows);

/// <summary>
/// 목록 내보내기 (구 PrintDoc/ExportHelper — 거래처·품목·검사·월마감 그리드의 엑셀·CSV·PDF, 설계 §29.3).
/// 화면이 보고 있는 목록(필터 적용, 표시 문자열 그대로)을 보내면 파일로 돌려준다. 데이터를 새로 읽지 않으므로 권한 = 로그인
/// (화면을 볼 권한이 있는 사람만 그 행을 갖고 있다).
/// </summary>
public static class ExportEndpoints
{
    private const int MaxRows = 100_000;
    private const int MaxColumns = 80;

    public static void MapExportEndpoints(this IEndpointRouteBuilder app) =>
        app.MapPost("/api/export", (ExportRequest r, TimeProvider time) => Export(r, time.GetLocalNow().DateTime)).WithTags("Export").RequireLogin();

    private static IResult Export(ExportRequest r, DateTime now)
    {
        var columns = r.Columns ?? [];
        var rows = r.Rows ?? [];
        if (columns.Count == 0 || columns.Count > MaxColumns) throw new RequestValidationException("columns", $"열은 1~{MaxColumns}개입니다.");
        if (rows.Count > MaxRows) throw new RequestValidationException("rows", $"한 번에 {MaxRows:#,0}행까지 내보낼 수 있습니다.");
        var title = string.IsNullOrWhiteSpace(r.Title) ? "목록" : r.Title.Trim();
        var safe = string.Concat(title.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var stamp = now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture);
        object? Cell(IReadOnlyList<JsonElement> row, int i) => i >= row.Count ? null : row[i].ValueKind switch
        {
            JsonValueKind.Number => row[i].GetDecimal(),
            JsonValueKind.String => row[i].GetString(),
            JsonValueKind.True => "예",
            JsonValueKind.False => "아니오",
            _ => null,
        };

        switch ((r.Format ?? "XLSX").ToUpperInvariant())
        {
            case "CSV":
            {
                // 엑셀이 한글을 읽도록 UTF-8 BOM (구 SaveDataTableToCsv withBom = true)
                var sb = new StringBuilder();
                sb.AppendLine(string.Join(",", columns.Select(c => Csv(c.Title))));
                foreach (var row in rows)
                    sb.AppendLine(string.Join(",", columns.Select((_, i) => Csv(Text(Cell(row, i))))));
                var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
                return Results.File(bytes, "text/csv; charset=utf-8", $"{safe}_{stamp}.csv");
            }
            case "PDF":
            {
                var fonts = PdfFonts.Pick(["맑은 고딕", "Malgun Gothic", "굴림체"]);
                var pdf = Document.Create(doc => doc.Page(page =>
                {
                    page.Size(columns.Count > 6 ? PageSizes.A4.Landscape() : PageSizes.A4);
                    page.Margin(20);
                    page.DefaultTextStyle(x => x.FontFamily(fonts).FontSize(8));
                    page.Header().Row(row =>
                    {
                        row.RelativeItem().Text(title).FontSize(13).Bold();
                        row.AutoItem().AlignBottom().Text($"{now:yyyy-MM-dd HH:mm} · {rows.Count:#,0}행").FontSize(8).FontColor(Colors.Grey.Darken1);
                    });
                    page.Content().PaddingTop(6).Table(table =>
                    {
                        table.ColumnsDefinition(c => { foreach (var _ in columns) c.RelativeColumn(); });
                        table.Header(h =>
                        {
                            foreach (var c in columns)
                                h.Cell().Border(0.5f).Background(Colors.Grey.Lighten3).Padding(2).AlignCenter().Text(c.Title).Bold();
                        });
                        foreach (var row in rows)
                            for (var i = 0; i < columns.Count; i++)
                            {
                                var cell = table.Cell().Border(0.5f).Padding(2);
                                var value = Cell(row, i);
                                (value is decimal ? cell.AlignRight() : cell).Text(Text(value));
                            }
                    });
                    page.Footer().AlignCenter().Text(t => { t.CurrentPageNumber(); t.Span(" / "); t.TotalPages(); });
                })).GeneratePdf();
                return Results.File(pdf, "application/pdf", $"{safe}_{stamp}.pdf");
            }
            default:
            {
                using var wb = new XLWorkbook();
                var ws = wb.AddWorksheet(safe.Length > 31 ? safe[..31] : safe);
                for (var i = 0; i < columns.Count; i++)
                    ws.Cell(1, i + 1).Value = columns[i].Title;
                ws.Row(1).Style.Font.Bold = true;
                ws.Row(1).Style.Fill.BackgroundColor = XLColor.FromHtml("#EEEEEE");
                for (var r2 = 0; r2 < rows.Count; r2++)
                    for (var i = 0; i < columns.Count; i++)
                    {
                        var cell = ws.Cell(r2 + 2, i + 1);
                        switch (Cell(rows[r2], i))
                        {
                            case decimal d: cell.Value = d; cell.Style.NumberFormat.Format = d == Math.Truncate(d) ? "#,##0" : "#,##0.###"; break;
                            case string s: cell.Value = s; break;
                        }
                    }
                if (rows.Count > 0) ws.Range(1, 1, rows.Count + 1, columns.Count).SetAutoFilter();
                ws.SheetView.FreezeRows(1);
                ws.Columns(1, columns.Count).AdjustToContents(1, Math.Min(rows.Count + 1, 500), 6, 60);
                using var ms = new MemoryStream();
                wb.SaveAs(ms);
                return Results.File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{safe}_{stamp}.xlsx");
            }
        }
    }

    private static string Text(object? v) => v switch
    {
        null => "",
        decimal d => d.ToString(d == Math.Truncate(d) ? "#,##0" : "#,##0.###", CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };

    private static string Csv(string s) => s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
}
