using System.Globalization;
using System.Text.Json;
using Dapper;
using JiMes.Api.Infrastructure.Codes;
using JiMes.Api.Infrastructure.Errors;
using MySqlConnector;

namespace JiMes.Api.Features.Printing.Providers;

/// <summary>
/// 검사성적서 — 대상(품목)별 1장 (§5.1 7). 측정·판정은 검사 공통이라 같은 검사의 대상들은 측정값이 같고 대상 정보만 다르다.
/// 구 <c>InspectionPrintService.BuildPlaceholders</c> 키(기본 12개 + T/C 좌표형)를 그대로 채워 구 양식을 재사용할 수 있게 한다.
/// </summary>
public sealed class InspectionTargetProvider(CommonCodeCache codes, AttachmentReader attachments) : IPrintDataProvider
{
    public string DataSourceCode => "INSPECTION_TARGET";
    public string SourceTable => "inspection_target";
    public bool AllowLegacyGridKeys => true;

    private const int MaxPoints = 10;   // 구 v1~v10 / p1~p10 호환 키 개수 (구 스키마 한계 — 신규 목록 치환자는 제한 없음)

    private sealed class TargetRow
    {
        public long InspectionTargetId { get; init; }
        public int SubNo { get; init; }
        public long CustomerId { get; init; }
        public long PartId { get; init; }
        public decimal? InspectionQty { get; init; }
        public string? SubmitLotNoSnapshot { get; init; }
        public string? CustomerName { get; init; }
        public string? CustomerLot { get; init; }
        public string? PartName { get; init; }
        public string? PartNumber { get; init; }
        public string? Specification { get; init; }
        public string? Model { get; init; }
        public string? Material { get; init; }
        public long InspectionId { get; init; }
        public string InspectionNo { get; init; } = "";
        public string InspectionType { get; init; } = "";
        public DateTime InspectionDate { get; init; }
        public string? Decision { get; init; }
        public long? InspectionStandardVersionId { get; init; }
        public string LotNo { get; init; } = "";
        public DateTime WorkDate { get; init; }
    }

    private sealed class ItemRow
    {
        public long InspectionItemId { get; init; }
        public int SequenceNo { get; init; }
        public string? ItemType { get; init; }
        public string ItemName { get; init; } = "";
        public string? Location { get; init; }
        public string? Result { get; init; }
        public string? Decision { get; init; }
        public string? SpecificationValue { get; init; }
    }

    private sealed class MeasurementRow
    {
        public long InspectionItemId { get; init; }
        public int SampleNo { get; init; }
        public decimal? MeasuredValue { get; init; }
        public string? MeasuredText { get; init; }
    }

    private sealed class CriteriaRow
    {
        public long InspectionCriteriaId { get; init; }
        public int SequenceNo { get; init; }
        public string? ItemType { get; init; }
        public string ItemName { get; init; } = "";
        public string? Location { get; init; }
        public string? SpecificationValue { get; init; }
        public string? ToolName { get; init; }
        public string? TestValue { get; init; }
        public string? Scale { get; init; }
        public decimal? LowerLimit { get; init; }
        public decimal? UpperLimit { get; init; }
        public int? HardnessLimit { get; init; }
        public int SampleCount { get; init; }
        public int TestCount { get; init; }
    }

    public async Task<PrintSource> LoadAsync(MySqlConnection conn, long sourceId, FieldDictionary fields, DateTime issuedAt, CancellationToken ct)
    {
        var t = await conn.QuerySingleOrDefaultAsync<TargetRow>(
            """
            SELECT t.inspection_target_id, t.sub_no, t.customer_id, soi.part_id, t.inspection_qty, t.submit_lot_no_snapshot,
                   COALESCE(t.customer_name_snapshot, c.customer_name) AS customer_name,
                   COALESCE(t.customer_lot_snapshot, soi.customer_lot) AS customer_lot,
                   COALESCE(t.part_name_snapshot, soi.part_name_snapshot, p.part_name) AS part_name,
                   COALESCE(t.part_number_snapshot, soi.part_number_snapshot, p.part_number) AS part_number,
                   COALESCE(t.specification_snapshot, soi.specification_snapshot, p.specification) AS specification,
                   COALESCE(t.model_snapshot, soi.model_snapshot, p.model) AS model,
                   COALESCE(t.material_snapshot, soi.material_snapshot, p.material) AS material,
                   i.inspection_id, i.inspection_no, i.inspection_type, i.inspection_date, i.decision, i.inspection_standard_version_id,
                   w.lot_no, w.work_date
              FROM inspection_target t
              JOIN inspection i         ON i.inspection_id = t.inspection_id AND i.is_deleted = 0
              JOIN production_work w    ON w.production_work_id = i.production_work_id
              JOIN sales_order_item soi ON soi.sales_order_item_id = t.sales_order_item_id
              JOIN part p               ON p.part_id = soi.part_id
              JOIN customer c           ON c.customer_id = t.customer_id
             WHERE t.inspection_target_id = @sourceId
            """, new { sourceId }) ?? throw new NotFoundException("inspection_target", sourceId);

        var data = new PrintData();
        string Date(string key, DateTime d) => d.ToString(fields.FormatOf(key) ?? "yyyy-MM-dd", CultureInfo.InvariantCulture);
        data.Values["InspectionNo"] = t.InspectionNo;
        data.Values["InspectionType"] = codes.GetName("INSPECTION_TYPE", t.InspectionType);
        data.Values["InspectionDate"] = Date("InspectionDate", t.InspectionDate);
        data.Values["WorkDate"] = Date("WorkDate", t.WorkDate);
        data.Values["IssueDate"] = Date("IssueDate", issuedAt);
        data.Values["LotNo"] = t.LotNo;
        data.Values["ConvertLot"] = t.SubmitLotNoSnapshot;
        data.Values["CustomerName"] = t.CustomerName;
        data.Values["CustomerLot"] = t.CustomerLot;
        data.Values["PartName"] = t.PartName;
        data.Values["PartNumber"] = t.PartNumber;
        data.Values["Specification"] = t.Specification;
        data.Values["Model"] = t.Model;
        data.Values["Material"] = t.Material;
        data.Values["ChargeQt"] = t.InspectionQty;
        data.Values["Decision"] = t.Decision is null ? null : codes.GetName("DECISION", t.Decision);

        var items = (await conn.QueryAsync<ItemRow>(
            """
            SELECT ii.inspection_item_id, ii.sequence_no, ii.item_type, ii.item_name, ii.location, ii.result, ii.decision,
                   ic.specification_value
              FROM inspection_item ii LEFT JOIN inspection_criteria ic ON ic.inspection_criteria_id = ii.inspection_criteria_id
             WHERE ii.inspection_id = @InspectionId ORDER BY ii.sequence_no
            """, new { t.InspectionId })).ToList();
        var measurements = (await conn.QueryAsync<MeasurementRow>(
            """
            SELECT m.inspection_item_id, m.sample_no, m.measured_value, m.measured_text
              FROM inspection_measurement m JOIN inspection_item ii ON ii.inspection_item_id = m.inspection_item_id
             WHERE ii.inspection_id = @InspectionId
            """, new { t.InspectionId })).ToLookup(m => m.InspectionItemId);

        var measurementRows = data.Lists["Measurements"] = [];
        foreach (var item in items)
        {
            var row = PrintData.Row();
            row["Seq"] = (decimal)item.SequenceNo;
            row["ItemType"] = ItemTypeName(item.ItemType);
            row["Item"] = item.ItemName;
            row["Location"] = item.Location;
            row["Spec"] = item.SpecificationValue;
            row["Result"] = item.Result;
            row["Decision"] = item.Decision is null ? null : codes.GetName("DECISION", item.Decision);
            foreach (var m in measurements[item.InspectionItemId])
                row[$"P{m.SampleNo}"] = m.MeasuredText ?? m.MeasuredValue?.ToString("0.###", CultureInfo.InvariantCulture);
            measurementRows.Add(row);
        }

        // 구 좌표형 키 T{탭}_{행}_… (탭 = 검사 항목 유형 공통코드 attr result_prefix)
        foreach (var group in items.GroupBy(i => Prefix(i.ItemType, "result_prefix")).Where(g => g.Key is not null))
        {
            var r = 0;
            foreach (var item in group)
            {
                var p = $"{group.Key}_{++r}_";
                var row = measurementRows[items.IndexOf(item)];
                data.Values[p + "Item"] = item.ItemName;
                data.Values[p + "Location"] = item.Location;
                data.Values[p + "SampleNo"] = measurements[item.InspectionItemId].Count().ToString(CultureInfo.InvariantCulture);
                for (var s = 1; s <= MaxPoints; s++)
                    data.Values[$"{p}P{s}"] = row.GetValueOrDefault($"P{s}");
                data.Values[p + "Result"] = item.Result;
                data.Values[p + "Decision"] = row["Decision"];
            }
        }

        // 구 좌표형 키 C{탭}_{행}_… — 판정 기준 (검사가 쓴 기준 버전)
        if (t.InspectionStandardVersionId is { } versionId)
        {
            var criteria = (await conn.QueryAsync<CriteriaRow>(
                """
                SELECT inspection_criteria_id, sequence_no, item_type, item_name, location, specification_value, tool_name, test_value,
                       scale, lower_limit, upper_limit, hardness_limit, sample_count, test_count
                  FROM inspection_criteria WHERE inspection_standard_version_id = @versionId ORDER BY sequence_no
                """, new { versionId })).ToList();
            var points = (await conn.QueryAsync<(long CriteriaId, int PointNo, string? Label)>(
                """
                SELECT CAST(p.inspection_criteria_id AS SIGNED), p.point_no, p.point_label
                  FROM inspection_criteria_point p JOIN inspection_criteria c ON c.inspection_criteria_id = p.inspection_criteria_id
                 WHERE c.inspection_standard_version_id = @versionId
                """, new { versionId })).ToLookup(x => x.CriteriaId);
            foreach (var group in criteria.GroupBy(c => Prefix(c.ItemType, "criteria_prefix")).Where(g => g.Key is not null))
            {
                var r = 0;
                foreach (var c in group)
                {
                    var p = $"{group.Key}_{++r}_";
                    data.Values[p + "Item"] = c.ItemName;
                    data.Values[p + "Location"] = c.Location;
                    data.Values[p + "Spec"] = c.SpecificationValue;
                    data.Values[p + "Tool"] = c.ToolName;
                    data.Values[p + "TestValue"] = c.TestValue;
                    data.Values[p + "Sample"] = c.SampleCount.ToString(CultureInfo.InvariantCulture);
                    data.Values[p + "TestCount"] = c.TestCount.ToString(CultureInfo.InvariantCulture);
                    data.Values[p + "Min"] = c.LowerLimit?.ToString("0.###", CultureInfo.InvariantCulture);
                    data.Values[p + "Max"] = c.UpperLimit?.ToString("0.###", CultureInfo.InvariantCulture);
                    data.Values[p + "HardnessLimit"] = c.HardnessLimit is > 0 ? c.HardnessLimit.Value.ToString(CultureInfo.InvariantCulture) : null;
                    data.Values[p + "Scale"] = c.Scale;
                    foreach (var point in points[(long)c.InspectionCriteriaId])
                        data.Values[$"{p}P{point.PointNo}"] = point.Label;
                }
            }
        }

        foreach (var (key, kind) in new[] { ("HardnessChart", "HARDNESS_CHART"), ("StructurePhoto", "STRUCTURE_PHOTO") })
        {
            if (await attachments.ReadFirstAsync(conn, "inspection", t.InspectionId, kind) is { } image)
                data.Images[key] = image;
        }

        return new PrintSource(data, t.PartId, t.CustomerId, $"{t.InspectionNo}-{t.SubNo}");
    }

    public Task OnIssuedAsync(MySqlConnection conn, MySqlTransaction tx, long sourceId, long printTemplateId, DateTime issuedAt) =>
        conn.ExecuteAsync(
            """
            UPDATE inspection_target
               SET report_issued_at = @issuedAt, report_issue_count = report_issue_count + 1, print_template_id = @printTemplateId
             WHERE inspection_target_id = @sourceId
            """, new { sourceId, printTemplateId, issuedAt }, tx);

    /// <summary>검사 항목 유형 = 공통코드 INSPECTION_ITEM_TYPE (코드 또는 표시명으로 저장된 구 데이터 모두 허용)</summary>
    private CommonCodeRow? ItemType(string? value) =>
        value is null ? null
            : codes.GetGroup("INSPECTION_ITEM_TYPE").Codes.FirstOrDefault(c =>
                string.Equals(c.Code, value, StringComparison.OrdinalIgnoreCase) || c.CodeName == value);

    private string? ItemTypeName(string? value) => ItemType(value)?.CodeName ?? value;

    private string? Prefix(string? itemType, string attr)
    {
        var json = ItemType(itemType)?.AttrJson;
        if (json is null) return null;
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(attr, out var v) ? v.GetString() : null;
    }
}
