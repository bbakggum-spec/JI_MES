using System.Globalization;
using Dapper;
using JiMes.Api.Infrastructure.Codes;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Settings;
using MySqlConnector;

namespace JiMes.Api.Features.Printing.Providers;

/// <summary>
/// 출하 전표 (거래명세표 FIXED 또는 업체 EXCEL 양식). 금액은 전표 저장값 (§15.3 F2).
/// 저장값이 없는 전표(작성 중)는 상세 금액 합과 설정 세율로 계산해 보여준다 — 6단계 출하 서비스가 확정 시 저장.
/// </summary>
public sealed class ShipmentProvider(CommonCodeCache codes, SettingsCache settings) : IPrintDataProvider
{
    public string DataSourceCode => "SHIPMENT";
    public string SourceTable => "shipment";

    private sealed class ShipmentRow
    {
        public string ShipmentNo { get; init; } = "";
        public DateTime ShipmentDate { get; init; }
        public long CustomerId { get; init; }
        public bool PrintSumByPart { get; init; }
        public decimal? SupplyAmount { get; init; }
        public decimal? VatAmount { get; init; }
        public decimal? TotalAmount { get; init; }
        public string? CustomerName { get; init; }
        public string? CustomerBusinessNo { get; init; }
        public string? CustomerCeoName { get; init; }
        public string? CustomerBusinessType { get; init; }
        public string? CustomerBusinessItem { get; init; }
        public string? CustomerAddress { get; init; }
    }

    // 속성 레코드 (기본 생성자 → Dapper 속성 매핑, 합산 출력에서 with 사용)
    private sealed record ItemRow
    {
        public long PartId { get; init; }
        public string? PartName { get; init; }
        public string? PartNumber { get; init; }
        public string? Specification { get; init; }
        public string? Model { get; init; }
        public string? ProcessName { get; init; }
        public decimal ShipmentQty { get; init; }
        public decimal? ShipmentWeight { get; init; }
        public string PriceBasis { get; init; } = "EA";
        public decimal? UnitPrice { get; init; }
        public decimal? Amount { get; init; }
        public string? SubmitLot { get; init; }
        public string? CustomerLot { get; init; }
    }

    private sealed class CompanyRow
    {
        public string CompanyName { get; init; } = "";
        public string? CeoName { get; init; }
        public string? BusinessNo { get; init; }
        public string? BusinessType { get; init; }
        public string? BusinessItem { get; init; }
        public string? Address { get; init; }
        public byte[]? StampImage { get; init; }
    }

    public async Task<PrintSource> LoadAsync(MySqlConnection conn, long sourceId, FieldDictionary fields, DateTime issuedAt, CancellationToken ct)
    {
        var s = await conn.QuerySingleOrDefaultAsync<ShipmentRow>(
            """
            SELECT s.shipment_no, s.shipment_date, s.customer_id, s.print_sum_by_part, s.supply_amount, s.vat_amount, s.total_amount,
                   COALESCE(s.customer_name_snapshot, c.customer_name) AS customer_name,
                   COALESCE(s.customer_business_no_snapshot, c.business_no) AS customer_business_no,
                   COALESCE(s.customer_ceo_name_snapshot, c.ceo_name) AS customer_ceo_name,
                   COALESCE(s.customer_business_type_snapshot, c.business_type) AS customer_business_type,
                   COALESCE(s.customer_business_item_snapshot, c.business_item) AS customer_business_item,
                   TRIM(CONCAT_WS(' ', COALESCE(s.customer_address_snapshot, c.address), COALESCE(s.customer_address_detail_snapshot, c.address_detail))) AS customer_address
              FROM shipment s JOIN customer c ON c.customer_id = s.customer_id
             WHERE s.shipment_id = @sourceId AND s.is_deleted = 0
            """, new { sourceId }) ?? throw new NotFoundException("shipment", sourceId);

        var items = (await conn.QueryAsync<ItemRow>(
            """
            SELECT soi.part_id,
                   COALESCE(si.part_name_snapshot, soi.part_name_snapshot, p.part_name) AS part_name,
                   COALESCE(si.part_number_snapshot, soi.part_number_snapshot, p.part_number) AS part_number,
                   COALESCE(si.specification_snapshot, soi.specification_snapshot, p.specification) AS specification,
                   COALESCE(soi.model_snapshot, p.model) AS model,
                   soi.heat_process_name_snapshot AS process_name,
                   si.shipment_qty, si.shipment_weight, si.price_basis_snapshot AS price_basis, si.unit_price_snapshot AS unit_price,
                   si.amount, si.submit_lot_no_snapshot AS submit_lot, si.customer_lot_snapshot AS customer_lot
              FROM shipment_item si
              JOIN sales_order_item soi ON soi.sales_order_item_id = si.sales_order_item_id
              JOIN part p               ON p.part_id = soi.part_id
             WHERE si.shipment_id = @sourceId
             ORDER BY si.line_no
            """, new { sourceId })).ToList();

        var company = await conn.QueryFirstOrDefaultAsync<CompanyRow>(
            """
            SELECT company_name, ceo_name, business_no, business_type, business_item,
                   TRIM(CONCAT_WS(' ', address, address_detail)) AS address, stamp_image
              FROM company WHERE is_active = 1 ORDER BY company_id LIMIT 1
            """);

        var data = new PrintData();
        data.Values["ShipmentNo"] = s.ShipmentNo;
        data.Values["ShipmentDate"] = s.ShipmentDate.ToString(fields.FormatOf("ShipmentDate") ?? "yyyy-MM-dd", CultureInfo.GetCultureInfo("ko-KR"));
        data.Values["SupplierName"] = company?.CompanyName;
        data.Values["SupplierBusinessNo"] = company?.BusinessNo;
        data.Values["SupplierCeoName"] = company?.CeoName;
        data.Values["SupplierAddress"] = company?.Address;
        data.Values["SupplierBusinessType"] = company?.BusinessType;
        data.Values["SupplierBusinessItem"] = company?.BusinessItem;
        data.Values["CustomerName"] = s.CustomerName;
        data.Values["CustomerBusinessNo"] = s.CustomerBusinessNo;
        data.Values["CustomerCeoName"] = s.CustomerCeoName;
        data.Values["CustomerAddress"] = s.CustomerAddress;
        data.Values["CustomerBusinessType"] = s.CustomerBusinessType;
        data.Values["CustomerBusinessItem"] = s.CustomerBusinessItem;
        if (company?.StampImage is { Length: > 0 } stamp)
            data.Images["Stamp"] = stamp;

        // 품목 합산 출력 (구 OutputSheet.MergeByPart: 품목 + 단가 기준)
        var rows = s.PrintSumByPart
            ? items.GroupBy(i => (i.PartId, i.UnitPrice)).Select(g => g.First() with
              {
                  ShipmentQty = g.Sum(x => x.ShipmentQty),
                  ShipmentWeight = g.Sum(x => x.ShipmentWeight ?? 0),
                  Amount = g.Sum(x => x.Amount ?? 0),
                  SubmitLot = string.Join(", ", g.Select(x => x.SubmitLot).Where(x => x is not null).Distinct()),
              }).ToList()
            : items;

        var vatRate = settings.GetDecimal(SettingKeys.SalesVatRate);
        var list = data.Lists["Items"] = [];
        var no = 0;
        foreach (var i in rows)
        {
            var row = PrintData.Row();
            row["No"] = (decimal)++no;
            row["PartName"] = i.PartName;
            row["PartNumber"] = i.PartNumber;
            row["Specification"] = i.Specification;
            row["Model"] = i.Model;
            row["ProcessName"] = i.ProcessName;
            row["Qty"] = i.ShipmentQty;
            row["Weight"] = i.ShipmentWeight;
            row["PriceUnit"] = codes.GetName("PRICE_BASIS", i.PriceBasis);
            row["UnitPrice"] = i.UnitPrice;
            row["Amount"] = i.Amount;
            row["Vat"] = i.Amount is { } a ? Round(a * vatRate) : null;   // 행 세액은 표시용 (합계 세액은 저장값)
            row["SubmitLot"] = i.SubmitLot;
            row["CustomerLot"] = i.CustomerLot;
            list.Add(row);
        }

        var supply = s.SupplyAmount ?? items.Sum(i => i.Amount ?? 0);
        var vat = s.VatAmount ?? Round(supply * vatRate);
        data.Values["SupplyAmount"] = supply;
        data.Values["VatAmount"] = vat;
        data.Values["TotalAmount"] = s.TotalAmount ?? supply + vat;

        return new PrintSource(data, null, s.CustomerId, s.ShipmentNo);
    }

    /// <summary>금액 반올림 방식 = 설정 sales.amount_rounding (ROUND / FLOOR / CEIL), 원 단위</summary>
    private decimal Round(decimal amount) => settings.GetString(SettingKeys.SalesAmountRounding) switch
    {
        "FLOOR" => Math.Floor(amount),
        "CEIL" => Math.Ceiling(amount),
        _ => Math.Round(amount, 0, MidpointRounding.AwayFromZero),
    };
}
