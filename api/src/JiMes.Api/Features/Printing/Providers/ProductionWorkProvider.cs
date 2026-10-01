using System.Globalization;
using Dapper;
using JiMes.Api.Infrastructure.Codes;
using JiMes.Api.Infrastructure.Errors;
using JiMes.Api.Infrastructure.Security;
using MySqlConnector;

namespace JiMes.Api.Features.Printing.Providers;

/// <summary>
/// 작업 LOT 1건 — 작업일보 (구 F_GasForm → WorkDailySheet), 그 밖에 작업지시서·LOT 라벨 엑셀 양식.
/// 로트 정보 / 투입(수주별) / 분할(트레이·장입·추출) / 작업표준(확정 표준) / 작업조건(LOT 조건) / 검사 / 불량 / 특기사항.
/// 검사 내역 = 이 LOT 을 입력한 검사의 항목·측정값 (구 라인검사 hx·cx·dx·tx 는 11단계 라인검사에서).
/// </summary>
public sealed class ProductionWorkProvider(CommonCodeCache codes) : IPrintDataProvider
{
    public string DataSourceCode => "PRODUCTION_WORK";
    public string MenuKey => MenuKeys.ProductionWork;
    public string SourceTable => "production_work";

    private sealed class WorkRow
    {
        public string LotNo { get; init; } = "";
        public DateTime WorkDate { get; init; }
        public string Status { get; init; } = "";
        public string? EquipmentName { get; init; }
        public string? UnitProcessName { get; init; }
        public string? WorkerName { get; init; }
        public DateTime? ActualStartAt { get; init; }
        public DateTime? ActualEndAt { get; init; }
        public decimal? ExpectedDurationMin { get; init; }
        public decimal? ActualDurationMin { get; init; }
        public bool IsRework { get; init; }
        public bool IsMainProcess { get; init; }
        public string? SubmitLotNo { get; init; }
        public string? Marking { get; init; }
        public string? HeatProcessName { get; init; }
        public long? StandardVersionId { get; init; }
        public string? Remark { get; init; }
    }

    private sealed class InputRow
    {
        public long PartId { get; init; }
        public long CustomerId { get; init; }
        public string OrderItemNo { get; init; } = "";
        public string? CustomerName { get; init; }
        public string? PartName { get; init; }
        public string? PartNumber { get; init; }
        public string? Specification { get; init; }
        public string? Model { get; init; }
        public string? CustomerLot { get; init; }
        public decimal InputQty { get; init; }
        public decimal? InputWeight { get; init; }
        public decimal DefectQty { get; init; }
        public string? TrayMark { get; init; }
        public DateTime? LoadedAt { get; init; }
        public DateTime? UnloadedAt { get; init; }
        public bool IsStandardBasis { get; init; }
        public string? MainLotNo { get; init; }
    }

    private sealed class InspectionRow
    {
        public long InspectionItemId { get; init; }
        public string InspectionNo { get; init; } = "";
        public string? Decision { get; init; }
        public string ItemName { get; init; } = "";
        public string? Location { get; init; }
        public string? Specification { get; init; }
        public string? ItemDecision { get; init; }
        public string? Result { get; init; }
    }

    private sealed class DefectRow
    {
        public decimal DefectQty { get; init; }
        public string? ReasonName { get; init; }
        public string? Remark { get; init; }
        public string Status { get; init; } = "";
        public string? Decision { get; init; }
        public string? OrderItemNo { get; init; }
    }

    private sealed class MatrixStep { public int SequenceNo { get; init; } public string StepName { get; init; } = ""; }
    private sealed class MatrixItem { public long ConditionItemId { get; init; } public string ConditionItemName { get; init; } = ""; public string? UnitCode { get; init; } }
    private sealed class MatrixCell { public int? StepNo { get; init; } public long ConditionItemId { get; init; } public string? Value { get; init; } }

    public async Task<PrintSource> LoadAsync(MySqlConnection conn, long sourceId, FieldDictionary fields, DateTime issuedAt, CancellationToken ct)
    {
        var w = await conn.QuerySingleOrDefaultAsync<WorkRow>(
            """
            SELECT w.lot_no, w.work_date, w.status, COALESCE(e.equipment_name, w.equipment_name_snapshot) AS equipment_name,
                   COALESCE(u.unit_process_name, w.unit_process_name_snapshot) AS unit_process_name,
                   COALESCE(emp.employee_name, w.worker_name_snapshot) AS worker_name, w.actual_start_at, w.actual_end_at,
                   w.expected_duration_min, w.actual_duration_min, w.is_rework, w.is_main_process, w.submit_lot_no, w.marking,
                   COALESCE(h.heat_process_name, w.heat_process_name_snapshot) AS heat_process_name, w.standard_version_id, w.remark
              FROM production_work w
              LEFT JOIN equipment e ON e.equipment_id = w.equipment_id
              LEFT JOIN unit_process u ON u.unit_process_id = w.unit_process_id
              LEFT JOIN employee emp ON emp.employee_id = w.worker_employee_id
              LEFT JOIN heat_process_version hv ON hv.heat_process_version_id = w.heat_process_version_id
              LEFT JOIN heat_process h ON h.heat_process_id = hv.heat_process_id
             WHERE w.production_work_id = @sourceId AND w.is_deleted = 0
            """, new { sourceId }) ?? throw new NotFoundException("production_work", sourceId);
        var inputs = (await conn.QueryAsync<InputRow>(
            """
            SELECT soi.part_id, so.customer_id, soi.order_item_no, COALESCE(i.customer_name_snapshot, c.customer_name) AS customer_name,
                   COALESCE(i.part_name_snapshot, soi.part_name_snapshot, p.part_name) AS part_name, COALESCE(i.part_number_snapshot, soi.part_number_snapshot, p.part_number) AS part_number,
                   COALESCE(i.specification_snapshot, soi.specification_snapshot, p.specification) AS specification, COALESCE(i.model_snapshot, soi.model_snapshot, p.model) AS model,
                   COALESCE(i.customer_lot_snapshot, soi.customer_lot) AS customer_lot, i.input_qty, i.input_weight,
                   COALESCE((SELECT SUM(d.defect_qty) FROM defect_occurrence d WHERE d.production_work_input_id = i.production_work_input_id
                              AND d.is_deleted = 0 AND d.status <> 'CANCELLED'), 0) AS defect_qty,
                   i.tray_mark, i.loaded_at, i.unloaded_at, i.is_standard_basis, mw.lot_no AS main_lot_no
              FROM production_work_input i
              JOIN sales_order_item soi ON soi.sales_order_item_id = i.sales_order_item_id
              JOIN sales_order so ON so.sales_order_id = soi.sales_order_id
              JOIN customer c ON c.customer_id = so.customer_id
              JOIN part p ON p.part_id = soi.part_id
              LEFT JOIN production_work mw ON mw.production_work_id = i.main_work_id
             WHERE i.production_work_id = @sourceId AND i.status <> 'CANCELLED'
             ORDER BY i.production_work_input_id
            """, new { sourceId })).ToList();

        var data = new PrintData();
        string Date(string key, DateTime d, string fallback) => d.ToString(fields.FormatOf(key) ?? fallback, CultureInfo.InvariantCulture);
        string? Time(DateTime? d) => d?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        data.Values["LotNo"] = w.LotNo;
        data.Values["WorkDate"] = Date("WorkDate", w.WorkDate, "yyyy-MM-dd");
        data.Values["IssueDate"] = Date("IssueDate", issuedAt, "yyyy-MM-dd");
        data.Values["StatusName"] = codes.GetName("WORK_STATUS", w.Status);
        data.Values["EquipmentName"] = w.EquipmentName;
        data.Values["UnitProcessName"] = w.UnitProcessName;
        data.Values["HeatProcessName"] = w.HeatProcessName;
        data.Values["WorkerName"] = w.WorkerName;
        data.Values["StartedAt"] = Time(w.ActualStartAt);
        data.Values["EndedAt"] = Time(w.ActualEndAt);
        data.Values["ExpectedHours"] = w.ExpectedDurationMin is { } e ? Math.Round(e / 60, 1) : null;
        data.Values["ActualHours"] = w.ActualDurationMin is { } a ? Math.Round(a / 60, 1) : null;
        data.Values["IsRework"] = w.IsRework ? "예" : "아니오";
        data.Values["IsMainProcess"] = w.IsMainProcess ? "주공정" : null;
        data.Values["MainLotNo"] = w.IsMainProcess ? w.LotNo : inputs.Select(i => i.MainLotNo).FirstOrDefault(m => m is not null);
        data.Values["SubmitLotNo"] = w.SubmitLotNo;
        data.Values["Marking"] = w.Marking;
        data.Values["Remark"] = w.Remark;
        data.Values["TotalQty"] = inputs.Sum(i => i.InputQty);
        data.Values["TotalWeight"] = inputs.Sum(i => i.InputWeight ?? 0);
        // 표준 확정 품목 (혼적 LOT) — 첫 투입 품목의 요구사항을 대표로
        var basis = inputs.FirstOrDefault(i => i.IsStandardBasis) ?? inputs.FirstOrDefault();
        data.Values["PartName"] = basis?.PartName;
        data.Values["CustomerName"] = basis?.CustomerName;

        data.Lists["Inputs"] = inputs.Select((i, n) =>
        {
            var row = PrintData.Row();
            row["No"] = (n + 1).ToString(CultureInfo.InvariantCulture);
            row["SalesOrderNo"] = i.OrderItemNo;
            row["CustomerName"] = i.CustomerName;
            row["PartName"] = i.PartName;
            row["PartNumber"] = i.PartNumber;
            row["Specification"] = i.Specification;
            row["Model"] = i.Model;
            row["CustomerLot"] = i.CustomerLot;
            row["Qty"] = i.InputQty;
            row["DefectQty"] = i.DefectQty;
            row["Weight"] = i.InputWeight;
            return row;
        }).ToList();
        data.Lists["Splits"] = inputs.Where(i => i.TrayMark is not null || i.LoadedAt is not null).Select(i =>
        {
            var row = PrintData.Row();
            row["Tray"] = i.TrayMark;
            row["Qty"] = i.InputQty;
            row["LoadedAt"] = i.LoadedAt?.ToString("HH:mm", CultureInfo.InvariantCulture);
            row["UnloadedAt"] = i.UnloadedAt?.ToString("HH:mm", CultureInfo.InvariantCulture);
            return row;
        }).ToList();

        // 작업표준 (확정한 표준 Version) / 작업조건 (LOT 확정 조건)
        if (w.StandardVersionId is { } versionId)
        {
            var steps = await conn.QueryAsync<MatrixStep>(
                "SELECT sequence_no, step_name FROM standard_version_step WHERE standard_version_id = @versionId ORDER BY sequence_no", new { versionId });
            var items = await conn.QueryAsync<MatrixItem>(
                """
                SELECT i.condition_item_id, c.condition_item_name, c.unit_code FROM standard_version_item i
                  JOIN condition_item c ON c.condition_item_id = i.condition_item_id WHERE i.standard_version_id = @versionId ORDER BY i.sequence_no
                """, new { versionId });
            var cells = await conn.QueryAsync<MatrixCell>(
                "SELECT step_no, condition_item_id, condition_value AS value FROM standard_condition WHERE standard_version_id = @versionId", new { versionId });
            ConditionMatrix.Fill(data, "Standard", Steps(steps), Items(items), Cells(cells));
        }
        else ConditionMatrix.Fill(data, "Standard", [], [], []);
        var conditionCells = (await conn.QueryAsync<(long ItemId, string ItemName, string? Unit, int? ItemSeq, int? StepNo, string? StepName, string? Value)>(
            """
            SELECT CAST(c.condition_item_id AS SIGNED), i.condition_item_name, i.unit_code, c.item_sequence_no, c.step_sequence_no, c.step_name_snapshot,
                   COALESCE(c.actual_value, c.set_value)
              FROM production_work_condition c JOIN condition_item i ON i.condition_item_id = c.condition_item_id
             WHERE c.production_work_id = @sourceId
            """, new { sourceId })).ToList();
        ConditionMatrix.Fill(data, "Conditions",
            conditionCells.Where(c => c.StepNo is not null).GroupBy(c => c.StepNo!.Value).OrderBy(g => g.Key)
                .Select(g => new ConditionMatrix.Step(g.Key, g.Select(c => c.StepName).FirstOrDefault(n => n is not null) ?? $"#{g.Key}")).ToList(),
            conditionCells.GroupBy(c => c.ItemId).OrderBy(g => g.Min(c => c.ItemSeq ?? int.MaxValue)).ThenBy(g => g.First().ItemName)
                .Select(g => new ConditionMatrix.Item(g.Key, g.First().ItemName, g.First().Unit)).ToList(),
            conditionCells.Select(c => new ConditionMatrix.Cell(c.StepNo, c.ItemId, c.Value)).ToList());

        // 검사 내역 — 이 LOT 을 입력한 확정 전·후 검사의 항목 (측정값은 공백 구분)
        var inspections = (await conn.QueryAsync<InspectionRow>(
            """
            SELECT ii.inspection_item_id, i.inspection_no, i.decision, ii.item_name, ii.location, cr.specification_value AS specification,
                   ii.decision AS item_decision, ii.result
              FROM inspection i
              JOIN inspection_item ii ON ii.inspection_id = i.inspection_id
              LEFT JOIN inspection_criteria cr ON cr.inspection_criteria_id = ii.inspection_criteria_id
             WHERE i.production_work_id = @sourceId AND i.is_deleted = 0 AND i.status <> 'CANCELLED'
             ORDER BY i.inspection_date, i.inspection_id, ii.sequence_no
            """, new { sourceId })).ToList();
        var measurements = (await conn.QueryAsync<(long ItemId, decimal? Value, string? Text)>(
            """
            SELECT CAST(m.inspection_item_id AS SIGNED), m.measured_value, m.measured_text FROM inspection_measurement m
              JOIN inspection_item ii ON ii.inspection_item_id = m.inspection_item_id
              JOIN inspection i ON i.inspection_id = ii.inspection_id
             WHERE i.production_work_id = @sourceId AND i.is_deleted = 0 ORDER BY m.inspection_item_id, m.sample_no
            """, new { sourceId })).ToLookup(m => m.ItemId);
        data.Lists["Inspections"] = inspections.Select(r =>
        {
            var row = PrintData.Row();
            row["InspectionNo"] = r.InspectionNo;
            row["Item"] = string.IsNullOrEmpty(r.Location) ? r.ItemName : $"{r.ItemName} ({r.Location})";
            row["Specification"] = r.Specification;
            row["Values"] = string.Join("  ", measurements[r.InspectionItemId].Select(m => m.Text ?? m.Value?.ToString("0.###", CultureInfo.InvariantCulture)));
            row["Result"] = r.Result;
            row["Decision"] = r.ItemDecision is null ? null : codes.GetName("DECISION", r.ItemDecision);
            return row;
        }).ToList();

        var defects = await conn.QueryAsync<DefectRow>(
            """
            SELECT d.defect_qty, r.defect_reason_name AS reason_name, d.remark, d.status, d.decision, soi.order_item_no
              FROM defect_occurrence d
              JOIN sales_order_item soi ON soi.sales_order_item_id = d.sales_order_item_id
              LEFT JOIN defect_reason r ON r.defect_reason_id = d.defect_reason_id
             WHERE d.production_work_id = @sourceId AND d.is_deleted = 0 AND d.status <> 'CANCELLED'
             ORDER BY d.defect_occurrence_id
            """, new { sourceId });
        data.Lists["Defects"] = defects.Select(d =>
        {
            var row = PrintData.Row();
            row["SalesOrderNo"] = d.OrderItemNo;
            row["Qty"] = d.DefectQty;
            row["Detail"] = d.Remark;
            row["Reason"] = d.ReasonName;
            row["Decision"] = d.Decision is null ? codes.GetName("DEFECT_STATUS", d.Status) : codes.GetName("DEFECT_ACTION", d.Decision);
            return row;
        }).ToList();
        data.Values["DefectQty"] = defects.Sum(d => d.DefectQty);

        return new PrintSource(data, basis?.PartId, basis?.CustomerId, w.LotNo);
    }

    private static List<ConditionMatrix.Step> Steps(IEnumerable<MatrixStep> s) => s.Select(x => new ConditionMatrix.Step(x.SequenceNo, x.StepName)).ToList();
    private static List<ConditionMatrix.Item> Items(IEnumerable<MatrixItem> s) => s.Select(x => new ConditionMatrix.Item(x.ConditionItemId, x.ConditionItemName, x.UnitCode)).ToList();
    private static List<ConditionMatrix.Cell> Cells(IEnumerable<MatrixCell> s) => s.Select(x => new ConditionMatrix.Cell(x.StepNo, x.ConditionItemId, x.Value)).ToList();
}
