using System.Globalization;
using Dapper;
using JiMes.Api.Infrastructure.Codes;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Security;
using MySqlConnector;

namespace JiMes.Api.Features.Printing.Providers;

/// <summary>
/// 수주(입고) 품목 1행 = 공정이동표 1장 / 제품표시 라벨 1장 (구 F_IncomeAddForm·F_IncomeForm — ProcessSheet·ProductLabel, 입고 행마다).
/// 데이터 공급원 SALES_ORDER, 원본 = sales_order_item. "수주번호"(SalesOrderNo) = 현장에서 스캔하는 입고번호 order_item_no (구 IncomeNo).
/// 공정 순서 = 수주 행의 공정 경로(heat_process_operation) — 구는 품목·공정의 subp1~16 문자열.
/// </summary>
public sealed class SalesOrderItemProvider(CommonCodeCache codes) : IPrintDataProvider
{
    public string DataSourceCode => "SALES_ORDER";
    public string MenuKey => MenuKeys.SalesOrder;
    public string SourceTable => "sales_order_item";

    private sealed class ItemRow
    {
        public string OrderItemNo { get; init; } = "";
        public string SalesOrderNo { get; init; } = "";
        public DateTime OrderDate { get; init; }
        public DateTime? DueDate { get; init; }
        public long CustomerId { get; init; }
        public string CustomerName { get; init; } = "";
        public long PartId { get; init; }
        public string? PartCode { get; init; }
        public string? PartName { get; init; }
        public string? PartNumber { get; init; }
        public string? Specification { get; init; }
        public string? Model { get; init; }
        public string? Material { get; init; }
        public decimal OrderQty { get; init; }
        public decimal? OrderWeight { get; init; }
        public decimal? UnitWeight { get; init; }
        public string? Hardness { get; init; }
        public string? CoreHardness { get; init; }
        public string? CaseDepth { get; init; }
        public string? Texture { get; init; }
        public string? HeatProcessName { get; init; }
        public long? HeatProcessVersionId { get; init; }
        public string? CustomerLot { get; init; }
        public string? CoilNo { get; init; }
        public string? CustomerWorkOrderNo { get; init; }
        public int Priority { get; init; }
        public bool IsSeparatelyManaged { get; init; }
        public bool IsRework { get; init; }
        public bool IsReturn { get; init; }
        public string? Remark { get; init; }
    }

    private sealed class StepRow
    {
        public int SequenceNo { get; init; }
        public string UnitProcessName { get; init; } = "";
        public bool IsMainProcess { get; init; }
    }

    public async Task<PrintSource> LoadAsync(MySqlConnection conn, long sourceId, FieldDictionary fields, DateTime issuedAt, CancellationToken ct)
    {
        var i = await conn.QuerySingleOrDefaultAsync<ItemRow>(
            """
            SELECT soi.order_item_no, so.sales_order_no, so.order_date, so.due_date, so.customer_id, c.customer_name, soi.part_id, p.part_code,
                   COALESCE(soi.part_name_snapshot, p.part_name) AS part_name, COALESCE(soi.part_number_snapshot, p.part_number) AS part_number,
                   COALESCE(soi.specification_snapshot, p.specification) AS specification, COALESCE(soi.model_snapshot, p.model) AS model,
                   COALESCE(soi.material_snapshot, p.material) AS material, soi.order_qty, soi.order_weight, COALESCE(soi.unit_weight, p.unit_weight) AS unit_weight,
                   soi.hardness_snapshot AS hardness, soi.core_hardness_snapshot AS core_hardness, soi.case_depth_snapshot AS case_depth,
                   soi.texture_snapshot AS texture, COALESCE(soi.heat_process_name_snapshot, h.heat_process_name) AS heat_process_name,
                   soi.heat_process_version_id, soi.customer_lot, soi.coil_no, soi.customer_work_order_no, soi.priority,
                   soi.is_separately_managed, soi.is_rework, soi.is_return, soi.remark
              FROM sales_order_item soi
              JOIN sales_order so ON so.sales_order_id = soi.sales_order_id AND so.is_deleted = 0
              JOIN customer c ON c.customer_id = so.customer_id
              JOIN part p ON p.part_id = soi.part_id
              LEFT JOIN heat_process_version hv ON hv.heat_process_version_id = soi.heat_process_version_id
              LEFT JOIN heat_process h ON h.heat_process_id = hv.heat_process_id
             WHERE soi.sales_order_item_id = @sourceId
            """, new { sourceId }) ?? throw new NotFoundException("sales_order_item", sourceId);
        var steps = i.HeatProcessVersionId is { } versionId
            ? (await conn.QueryAsync<StepRow>(
                """
                SELECT o.sequence_no, u.unit_process_name, o.is_main_process
                  FROM heat_process_operation o JOIN unit_process u ON u.unit_process_id = o.unit_process_id
                 WHERE o.heat_process_version_id = @versionId ORDER BY o.sequence_no
                """, new { versionId })).ToList()
            : [];

        var data = new PrintData();
        string Date(string key, DateTime d) => d.ToString(fields.FormatOf(key) ?? "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var priority = i.Priority.ToString(CultureInfo.InvariantCulture);
        data.Values["SalesOrderNo"] = i.OrderItemNo;
        data.Values["OrderBundleNo"] = i.SalesOrderNo;
        data.Values["OrderDate"] = Date("OrderDate", i.OrderDate);
        data.Values["DueDate"] = i.DueDate is { } due ? Date("DueDate", due) : null;
        data.Values["IssueDate"] = Date("IssueDate", issuedAt);
        data.Values["CustomerName"] = i.CustomerName;
        data.Values["PartCode"] = i.PartCode;
        data.Values["PartName"] = i.PartName;
        data.Values["PartNumber"] = i.PartNumber;
        data.Values["Specification"] = i.Specification;
        data.Values["Model"] = i.Model;
        data.Values["Material"] = i.Material;
        data.Values["Qty"] = i.OrderQty;
        data.Values["Weight"] = i.OrderWeight;
        data.Values["UnitWeight"] = i.UnitWeight;
        data.Values["Hardness"] = i.Hardness;
        data.Values["CoreHardness"] = i.CoreHardness;
        data.Values["CaseDepth"] = i.CaseDepth;
        data.Values["Texture"] = i.Texture;
        data.Values["HeatProcess"] = i.HeatProcessName;
        data.Values["CustomerLot"] = i.CustomerLot;
        data.Values["CoilNo"] = i.CoilNo;
        data.Values["CustomerWorkOrderNo"] = i.CustomerWorkOrderNo;
        data.Values["Priority"] = priority;
        data.Values["PriorityName"] = codes.GetName("PRIORITY", priority);
        data.Values["PriorityColor"] = codes.GetGroup("PRIORITY").Codes.FirstOrDefault(c => c.Code == priority)?.AttrJson is { } attr
            && System.Text.Json.JsonDocument.Parse(attr).RootElement.TryGetProperty("color", out var color) ? color.GetString() : null;
        // 구 공정이동표 특기사항 "[Grade] 별도관리" — 신규는 별도관리·재작업·반입 표시
        data.Values["SpecialNote"] = string.Join(" ", new[]
        {
            i.IsSeparatelyManaged ? "[별도관리]" : null, i.IsRework ? "[재작업]" : null, i.IsReturn ? "[반입]" : null,
        }.OfType<string>()) is { Length: > 0 } flags ? flags : null;
        data.Values["Remark"] = i.Remark;
        data.Lists["Processes"] = steps.Select(s =>
        {
            var row = PrintData.Row();
            row["Seq"] = s.SequenceNo.ToString(CultureInfo.InvariantCulture);
            row["UnitProcess"] = s.UnitProcessName;
            row["IsMain"] = s.IsMainProcess ? "주" : null;
            return row;
        }).ToList();
        return new PrintSource(data, i.PartId, i.CustomerId, i.OrderItemNo);
    }
}
