using System.Globalization;
using Dapper;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Security;
using MySqlConnector;

namespace JiMes.Api.Features.Printing.Providers;

/// <summary>
/// 마감 1건 + 포함 전표 — 마감내역서 엑셀 양식용 (구 F_MonthlyClosing 은 그리드 엑셀 내보내기만 있었음 → 목록 내보내기·이 양식으로).
/// 공급자(자사)·거래처·마감월·합계와 전표 목록(전표번호·출하일·수량·중량·공급가액·세액·합계).
/// </summary>
public sealed class ShipmentClosingProvider : IPrintDataProvider
{
    public string DataSourceCode => "SHIPMENT_CLOSING";
    public string MenuKey => MenuKeys.SalesClosing;
    public string SourceTable => "shipment_closing";

    private sealed class ClosingRow
    {
        public string ClosingNo { get; init; } = "";
        public long CustomerId { get; init; }
        public string CustomerName { get; init; } = "";
        public string? CustomerBusinessNo { get; init; }
        public DateTime ClosingDate { get; init; }
        public int ClosingYear { get; init; }
        public int ClosingMonth { get; init; }
        public decimal TotalQty { get; init; }
        public decimal TotalWeight { get; init; }
        public decimal TotalAmount { get; init; }
        public string ClosingStatus { get; init; } = "";
        public string? Remark { get; init; }
    }

    private sealed class SlipRow
    {
        public string ShipmentNo { get; init; } = "";
        public DateTime ShipmentDate { get; init; }
        public decimal Qty { get; init; }
        public decimal Weight { get; init; }
        public decimal? SupplyAmount { get; init; }
        public decimal? VatAmount { get; init; }
        public decimal? TotalAmount { get; init; }
        public string? ItemSummary { get; init; }
    }

    public async Task<PrintSource> LoadAsync(MySqlConnection conn, long sourceId, FieldDictionary fields, DateTime issuedAt, CancellationToken ct)
    {
        var c = await conn.QuerySingleOrDefaultAsync<ClosingRow>(
            """
            SELECT x.closing_no, x.customer_id, cu.customer_name, cu.business_no AS customer_business_no, x.closing_date, x.closing_year, x.closing_month,
                   x.total_qty, x.total_weight, x.total_amount, x.closing_status, x.remark
              FROM shipment_closing x JOIN customer cu ON cu.customer_id = x.customer_id
             WHERE x.shipment_closing_id = @sourceId
            """, new { sourceId }) ?? throw new NotFoundException("shipment_closing", sourceId);
        var slips = (await conn.QueryAsync<SlipRow>(
            """
            SELECT s.shipment_no, s.shipment_date,
                   COALESCE((SELECT SUM(si.shipment_qty) FROM shipment_item si WHERE si.shipment_id = s.shipment_id), 0) AS qty,
                   COALESCE((SELECT SUM(si.shipment_weight) FROM shipment_item si WHERE si.shipment_id = s.shipment_id), 0) AS weight,
                   s.supply_amount, s.vat_amount, s.total_amount,
                   (SELECT GROUP_CONCAT(DISTINCT si.part_name_snapshot ORDER BY si.line_no SEPARATOR ', ') FROM shipment_item si WHERE si.shipment_id = s.shipment_id) AS item_summary
              FROM shipment s
             WHERE s.shipment_closing_id = @sourceId AND s.is_deleted = 0
             ORDER BY s.shipment_date, s.shipment_no
            """, new { sourceId })).ToList();
        var company = await conn.QuerySingleOrDefaultAsync<(string? Name, string? BusinessNo, string? CeoName)>(
            "SELECT company_name, business_no, ceo_name FROM company WHERE is_active = 1 ORDER BY company_id LIMIT 1");

        var data = new PrintData();
        string Date(string key, DateTime d) => d.ToString(fields.FormatOf(key) ?? "yyyy-MM-dd", CultureInfo.InvariantCulture);
        data.Values["ClosingNo"] = c.ClosingNo;
        data.Values["ClosingMonth"] = $"{c.ClosingYear}-{c.ClosingMonth:00}";
        data.Values["ClosingDate"] = Date("ClosingDate", c.ClosingDate);
        data.Values["IssueDate"] = Date("IssueDate", issuedAt);
        data.Values["CustomerName"] = c.CustomerName;
        data.Values["CustomerBusinessNo"] = c.CustomerBusinessNo;
        data.Values["SupplierName"] = company.Name;
        data.Values["SupplierBusinessNo"] = company.BusinessNo;
        data.Values["SupplierCeoName"] = company.CeoName;
        data.Values["SlipCount"] = slips.Count;
        data.Values["TotalQty"] = c.TotalQty;
        data.Values["TotalWeight"] = c.TotalWeight;
        data.Values["SupplyAmount"] = slips.Sum(s => s.SupplyAmount ?? 0);
        data.Values["VatAmount"] = slips.Sum(s => s.VatAmount ?? 0);
        data.Values["TotalAmount"] = slips.Sum(s => s.TotalAmount ?? 0);
        data.Values["Remark"] = c.Remark;
        data.Lists["Slips"] = slips.Select((s, i) =>
        {
            var row = PrintData.Row();
            row["No"] = (i + 1).ToString(CultureInfo.InvariantCulture);
            row["ShipmentNo"] = s.ShipmentNo;
            row["ShipmentDate"] = Date("Slips.ShipmentDate", s.ShipmentDate);
            row["Items"] = s.ItemSummary;
            row["Qty"] = s.Qty;
            row["Weight"] = s.Weight;
            row["SupplyAmount"] = s.SupplyAmount;
            row["VatAmount"] = s.VatAmount;
            row["TotalAmount"] = s.TotalAmount;
            return row;
        }).ToList();
        return new PrintSource(data, null, c.CustomerId, c.ClosingNo);
    }
}
