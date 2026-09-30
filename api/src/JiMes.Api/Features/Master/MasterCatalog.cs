using static JiMes.Api.Features.Master.MasterFieldType;

namespace JiMes.Api.Features.Master;

/// <summary>
/// 단순 기준정보 정의 (5단계 ①). 컬럼·제약은 DDL 과 같아야 한다 (테스트 MasterCatalogTests 가 information_schema 와 대조).
/// 표시명(라벨)은 화면 문구이고, 업무 값(코드 표시명)은 공통코드에서 읽는다.
/// 버전·행렬 구조가 있는 기준정보(품목·작업표준·검사기준)는 전용 화면 (5단계 ②~④).
/// </summary>
public static class MasterCatalog
{
    private static MasterField CodeField(string name, int max = 50) =>
        new(name, "코드") { Required = true, MaxLength = max, Unique = true, Searchable = true, Width = 120 };

    private static MasterField Name(string name, int max = 100) =>
        new(name, "이름") { Required = true, MaxLength = max, Searchable = true };

    private static MasterField Sort() => new("sort_order", "순서", Integer) { Default = 0, Width = 70 };

    private static MasterField Active(string label = "사용") => new("is_active", label, Bool) { Default = true, Width = 70 };

    private static MasterField Remark(string name = "remark", int? max = 255) =>
        new(name, "비고", max is null ? TextArea : Text) { MaxLength = max, InList = false };

    public static readonly IReadOnlyList<MasterEntity> All =
    [
        new("company", "company", "자사 정보", "master.company", "company_name",
        [
            new("company_name", "상호") { Required = true, MaxLength = 100 },
            new("ceo_name", "대표자") { MaxLength = 50 },
            new("business_no", "사업자번호") { MaxLength = 20, Unique = true },
            new("business_type", "업태") { MaxLength = 50, InList = false },
            new("business_item", "종목") { MaxLength = 100, InList = false },
            new("phone", "전화") { MaxLength = 50 },
            new("fax", "팩스") { MaxLength = 50, InList = false },
            new("email", "이메일") { MaxLength = 100, InList = false },
            new("address", "주소") { MaxLength = 200 },
            new("address_detail", "상세 주소") { MaxLength = 200, InList = false },
            Active(),
        ])
        {
            OrderBy = "company_id", HasCreatedBy = true, HasUpdatedBy = true,
            Images = [new("stamp_image", "도장", "stamp_file_name")],
            Description = "거래명세표 공급자 정보·도장 (구 F_CompanyForm — 도장 PC 경로 대신 DB 보관)",
        },

        new("customer", "customer", "거래처", "master.customer", "customer_name",
        [
            CodeField("customer_code"),
            Name("customer_name"),
            new("customer_type", "구분", Code) { Required = true, CodeGroup = "CUSTOMER_TYPE", Default = "SALES", Width = 100 },
            new("business_no", "사업자번호") { MaxLength = 20, Searchable = true, Width = 130 },
            new("ceo_name", "대표자") { MaxLength = 50, Width = 100 },
            new("business_type", "업태") { MaxLength = 50, InList = false },
            new("business_item", "종목") { MaxLength = 50, InList = false },
            new("phone", "전화") { MaxLength = 30, Width = 130 },
            new("fax", "팩스") { MaxLength = 30, InList = false },
            new("email", "이메일") { MaxLength = 100, InList = false },
            new("address", "주소") { MaxLength = 255, InList = false },
            new("address_detail", "상세 주소") { MaxLength = 255, InList = false },
            new("closing_day", "마감일", Integer) { Min = 1, Max = 31, Width = 80, Help = "매월 마감일 (말일 = 31)" },
            Remark("remark", null),
            Active(),
        ])
        { OrderBy = "customer_name", HasCreatedBy = true, HasUpdatedBy = true },

        new("equipment_type", "equipment_type", "설비 유형", "master.equipment_type", "equipment_type_name",
        [CodeField("equipment_type_code", 30), Name("equipment_type_name", 50), new("description", "설명") { MaxLength = 255 }, Active()])
        { OrderBy = "equipment_type_name" },

        new("equipment", "equipment", "설비", "master.equipment", "equipment_name",
        [
            new("equipment_type_id", "설비 유형", Lookup) { Lookup = "equipment_type", Required = true, Width = 120 },
            CodeField("equipment_code"),
            Name("equipment_name", 50),
            new("equipment_initial", "이니셜") { MaxLength = 50, Width = 90, Help = "LOT번호에 쓰는 설비 약칭 (예: B01)" },
            new("equipment_no", "번호", Integer) { Width = 70 },
            new("install_date", "설치일", Date) { InList = false },
            new("sort_order", "표시 순서", Integer) { Default = 0, Width = 90, Help = "생산계획 화면 설비 행 순서" },
            Active(),
        ])
        { OrderBy = "sort_order, equipment_code", HasCreatedBy = true, HasUpdatedBy = true },

        new("unit_process", "unit_process", "단위공정", "master.unit_process", "unit_process_name",
        [CodeField("unit_process_code"), Name("unit_process_name"), Sort(), Active()])
        {
            OrderBy = "sort_order, unit_process_code",
            Description = "세척·침탄·템퍼링 등 LOT 단위 공정. 작업표준 화면에서 새 이름 입력으로 자동 등록하지 않음 (구 T4)",
        },

        new("condition_item", "condition_item", "조건 항목", "master.condition_item", "condition_item_name",
        [
            CodeField("condition_item_code"),
            Name("condition_item_name"),
            new("unit_code", "단위") { MaxLength = 20, Width = 80 },
            new("value_type", "값 형식", Code) { Required = true, CodeGroup = "CONDITION_VALUE_TYPE", Default = "NUMBER", Width = 90 },
            Sort(), Active(),
        ])
        { OrderBy = "sort_order, condition_item_code", Description = "작업표준·작업조건 행렬의 행 (온도, 시간, CP …)" },

        new("process_default_time", "process_default_time", "설비 기준시간", "master.process_default_time", "remark",
        [
            new("equipment_type_id", "설비 유형", Lookup) { Lookup = "equipment_type", Width = 120 },
            new("equipment_id", "설비", Lookup) { Lookup = "equipment", Width = 120, Help = "지정하면 설비 유형보다 우선" },
            new("unit_process_id", "단위공정", Lookup) { Lookup = "unit_process", Width = 110, Help = "비우면 모든 단위공정 공통" },
            new("running_time_min", "기준시간(분)", MasterFieldType.Decimal) { Required = true, Min = 1, Width = 110 },
            new("remark", "비고") { MaxLength = 200 },
        ])
        {
            OrderBy = "equipment_type_id, equipment_id, unit_process_id", AllowDelete = true,
            RequireOneOf = ["equipment_type_id", "equipment_id"],
            Description = "작업표준·직전 작업이 없을 때 스케줄 작업시간 (설계 §7 ③). 생산계획 입력창에서도 등록됨",
        },

        new("defect_reason", "defect_reason", "불량 사유", "master.defect_reason", "defect_reason_name",
        [CodeField("defect_reason_code"), Name("defect_reason_name"), new("defect_category", "분류") { MaxLength = 50, Width = 110 }, Sort(), Active()])
        { OrderBy = "sort_order, defect_reason_code" },

        new("instrument", "instrument", "측정기구", "master.instrument", "instrument_name",
        [
            CodeField("instrument_code"),
            Name("instrument_name"),
            new("instrument_type", "종류") { MaxLength = 50, Width = 100 },
            new("serial_no", "시리얼") { MaxLength = 100, Width = 120 },
            new("calibration_cycle_day", "교정 주기(일)", Integer) { Min = 1, Width = 100 },
            new("last_calibrated_date", "최근 교정일", Date) { Width = 110 },
            new("next_calibration_date", "다음 교정일", Date) { Width = 110 },
            Remark("remark", null),
            Active(),
        ])
        { OrderBy = "instrument_code" },

        new("work_shift", "work_shift", "교대", "master.work_shift", "work_shift_name",
        [
            CodeField("work_shift_code", 30),
            Name("work_shift_name", 50),
            new("start_time", "시작", Time) { Required = true, Width = 80 },
            new("end_time", "종료", Time) { Required = true, Width = 80 },
            new("is_next_day_end", "다음날 종료", Bool) { Default = false, Width = 100 },
            new("sort_order", "순서", Integer) { Default = 0, Width = 70, Help = "순서가 가장 앞선 교대의 시작 = 작업일 시작 (없으면 설정 schedule.day_start_time)" },
            Active(),
        ])
        { OrderBy = "sort_order, work_shift_code" },

        new("work_calendar", "work_calendar", "공장 달력", "master.work_calendar", "calendar_date",
        [
            new("calendar_date", "일자", Date) { Required = true, Unique = true, Width = 120 },
            new("day_type", "구분", Code) { Required = true, CodeGroup = "DAY_TYPE", Default = "HOLIDAY", Width = 100 },
            new("remark", "비고") { MaxLength = 255, Searchable = true },
        ])
        {
            OrderBy = "calendar_date DESC", AllowDelete = true,
            Description = "휴일 작업일에는 스케줄 계산이 새 작업을 시작하지 않음 (설계 §7)",
        },

        new("department", "department", "부서", "master.department", "department_name",
        [CodeField("department_code", 30), Name("department_name", 50), Sort(), Active()])
        { OrderBy = "sort_order, department_code" },

        new("job_position", "job_position", "직위", "master.job_position", "job_position_name",
        [CodeField("job_position_code", 30), Name("job_position_name", 50), Sort(), Active()])
        { OrderBy = "sort_order, job_position_code" },

        new("employee", "employee", "사원", "master.employee", "employee_name",
        [
            new("employee_code", "사번") { Required = true, MaxLength = 50, Unique = true, Searchable = true, Width = 100 },
            new("employee_name", "이름") { Required = true, MaxLength = 50, Searchable = true, Width = 100 },
            new("department_id", "부서", Lookup) { Lookup = "department", Width = 100 },
            new("job_position_id", "직위", Lookup) { Lookup = "job_position", Width = 90 },
            new("team_name", "조") { MaxLength = 50, Width = 80 },
            new("mobile_phone", "휴대폰") { MaxLength = 50, Width = 130 },
            new("phone", "전화") { MaxLength = 50, InList = false },
            new("nationality", "국적") { MaxLength = 50, InList = false },
            new("address", "주소") { MaxLength = 100, InList = false },
            new("employment_date", "입사일", Date) { Width = 110 },
            new("resignation_date", "퇴사일", Date) { InList = false },
            new("is_assignment_target", "작업자 배정 대상", Bool) { Default = false, Width = 110 },
            Remark("remark", null),
            Active("재직"),
        ])
        { OrderBy = "employee_name", HasCreatedBy = true, HasUpdatedBy = true },
    ];

    public static MasterEntity Get(string key) =>
        All.FirstOrDefault(e => e.Key == key) ?? throw new KeyNotFoundException($"기준정보 '{key}' 가 없습니다.");
}
