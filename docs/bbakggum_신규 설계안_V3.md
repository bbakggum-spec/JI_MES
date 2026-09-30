# bbakggum DB 구조개편 설계안 V3.8

| 항목 | 내용 |
|-|-|
| 대상 DB | `bbakggum` (MariaDB, 46 테이블 / 3 VIEW / 2 PROCEDURE) — **삭제·변경하지 않고 보존** |
| 신규 DB | `bbakggum_v2` **신규 구축** (DB명은 V2 유지, 문서 버전과 무관) |
| 실행 DDL | [`db/bbakggum_v2_DDL_V3.sql`](../db/bbakggum_v2_DDL_V3.sql) — **DDL의 단일 원본** |
| 기존 소스 | `D:\Programming\ProductManager` (WinForms, 업무 규칙 확인 근거) |
| 검증 | MariaDB 11.6.2에서 DDL 전체 실행 + 업무 시나리오/제약조건 테스트 완료 (§13) |

## 개정 이력

| 버전 | 일자 | 내용 |
|-|-|-|
| V3 | 2026-09-30 | V2 검토 반영: 실행 오류 제거, 명명 통일, 수량/상태 원칙, 권한·감사, 동시성, 마이그레이션 추적 |
| V3.1 | 2026-09-30 | 공정 3단계, 작업 LOT = 단위공정 1회 수행, 수주 스캔 투입, 주공정 LOT 추적, 혼적 표준 선택, 공정별 부적합 차감 |
| V3.2 | 2026-09-30 | 소스 확인: 이전 LOT 스캔·선입선출 없음(trace 제거), `originelotno`/`templotno`/`convertlotno`/`isfixed` 의미 확정 |
| V3.3 | 2026-09-30 | 확인사항 19건 반영: 조건 = **항목 × 단계 행렬**(단계 템플릿/조건 항목), 작업표준 = 품목·단위공정·설비·공정별, 투입 = 수주번호 또는 주 LOT 입력, 후공정 = 주 LOT 투입목록 선택, 검사 = 주 LOT 투입내역 중 대상 선택 + 품목·업체별 기준, 성적서 양식 = 품목·업체별 연결, 출하 = 전표, 마감 = 출하 중 선택, LOT번호 규칙, LOT 진행 상태 표시, 미사용 테이블 폐기 |
| V3.4 | 2026-09-30 | LOT 상태 = 단위공정별 **배정 → 투입 → 완료** (출하·검사는 상태 아님), 출력 양식 통합 + **용도 구분**(`print_type`), 검사 = **검사 1회 : 대상 N** (`inspection_target`, 성적서는 대상별 1장), 마감 = **업체별·전표별**, 미선택 전표 이월, 재작업 LOT도 동일 번호 규칙 |
| V3.5 | 2026-09-30 | **모든 출력물 = 사용자 엑셀 양식 등록 방식**, 출력 용도 **사용자 확장**(`print_purpose`), 양식 파일 **DB 버전 보관**, 치환자 사전(`print_field`), 출력 이력(`print_log`), **구현 시 주의사항**(§15: 스케줄·진행현황, 엑셀 양식 출력 — 기존 소스 분석), 기존 DB 보존 + 신규 구축 원칙 명시 |
| V3.6 | 2026-09-30 | 양식 등록 방식 **2가지**: EXCEL(사용자 수정 양식) + **FIXED**(코드 고정 레이아웃 — 거래명세표 등 구 PrintDoc 7종, 레이아웃 옵션은 관리자 조정), **하드코딩 → 관리자 설정**(§15.4, `system_setting` 확장·초기값, 로직 참조 공통코드, 단말별 프린터 `workstation_print_setting`, 도장 이미지 DB 보관) |
| V3.8 | 2026-09-30 | **1단계 기존 폼 분석 반영** (`docs/legacy_forms/`): 입고번호 = 스캔 수주번호(`order_item_no`), 수주 행 요구사항 Snapshot·우선순위·별도관리·고객 작업지시번호, 품목 단가 적용 구분(EA/KG/CHARGE), 설비당 투입 중 작업 1건, 한 LOT에 같은 수주 1회, 관리항목 템플릿(`step_template_condition`), 검사구분(입고/공정/출하)·재검사·**검사 결과 공통 적용**, 부적합 처리구분(재처리/출하/선별/보류/폐기/반송), 출하 시험편·거래처 Snapshot·전표 단위 마감 상태(미마감/마감/이월), 공통 첨부(`attachment`), 설정·공통코드 추가. 배정 병합 시 최대 작업시간, 지연 시 뒤 배정 계획시각 자동 이동 |
| V3.7 | 2026-09-30 | 고정 양식 6종 전체 **레이아웃 옵션 스키마·초기값** 확정(§15.3.1), 우선순위 표시명·색상 하드코딩 → 공통코드 `PRIORITY`, `sales_order.priority` 기본값 1(일반)로 수정 (기존 코드 기준 3 = 긴급) |

> SQL은 `.sql` 파일 하나로만 관리한다 (문서와 DDL 불일치 방지).

---

# 0. 용어

| 용어 | 의미 | 테이블 |
|-|-|-|
| 수주 = 입고 | 고객 소재 입고 등록 (임가공) | `sales_order`, `sales_order_item` |
| 출하 = 납품 | 출하 전표 등록 | `shipment`(전표), `shipment_item` |
| 공정 | 공정명 (침탄, 질화 …) | `heat_process` |
| 단위공정 | 세척, 침탄, 템퍼링, 쇼트 … | `unit_process` |
| 작업단계 | 단위공정 안의 단계 (승온, 균열, 침탄 …) | `step_template_item` |
| 작업 LOT | 단위공정 1회 수행 | `production_work` |
| 주 LOT | 주공정(예: 침탄) 작업 LOT — 모든 추적의 기준 | `production_work.is_main_process = 1` |
| 작업표준 | 품목별 조건 (항목 × 단계) | `standard*` |
| 제출 LOT | 고객 성적서/출하에 쓰는 LOT번호 (구 `convertlotno`) | `production_work.submit_lot_no` |

---

# 1. 설계 원칙

## 1.1 명명 규칙

| 대상 | 규칙 | 예 |
|-|-|-|
| 테이블 | `snake_case`, 단수형 | `production_work` |
| PK | `{table}_id` | `sales_order_item_id` |
| 순수 N:M 매핑 | 복합 PK 허용 | `app_user_role` |
| FK | 참조 PK와 동일 | `customer_id` |
| 역할 FK | `{역할}_{참조PK}` | `main_work_id`, `origin_work_id`, `worker_employee_id` |
| 감사 컬럼 | `created_by` 등 → `app_user_id` 값 (FK 제약 없음) | |
| 코드/명칭/번호 | `_code` / `_name` / `_no` | |
| 일시/일자 | `_at` / `_date` | |
| 수량/중량/금액 | `_qty` / `_weight` / `_amount` | |
| 여부 | `is_` | `is_standard_fixed` |
| 설정/실적 값 | `set_` / `actual_` | |
| 당시 값 | `_snapshot` | |
| 비고 | `remark` | |

사용 금지: `t_` 접두, 붙여쓴 이름, `useyn`, `seq`, `step1~N`, `item1~N`, `v1~N`, `p1~N`, 예약어.

## 1.2 수량 원칙

| 수량 | 원본 |
|-|-|
| 주문(입고) | `sales_order_item.order_qty` |
| 공정별 투입 | `production_work_input.input_qty` (작업 LOT × 수주) |
| 공정별 부적합 | `defect_occurrence.defect_qty` (발견 공정 투입 행) |
| 출하 | `shipment_item.shipment_qty` |

- **공정별 양품 = 투입 − 그 행의 부적합** (기존 `OutputQt = InputQt − DefectCount`와 동일). 저장하지 않고 VIEW로 계산한다.
- 투입 잔량(구 `t_inputwaiting.remainqt` 저장형)은 `allocated_qty − 해당 수주·단위공정 투입 합계`로 계산한다.

## 1.3 상태 / Version / Snapshot / 감사

- 상태는 대상마다 `status` 하나와 `CHECK` 제약으로 관리하고, 판정은 `decision`으로 분리한다.
- 공정 경로, 작업표준, 검사기준은 **Version**으로 관리한다. 거래에서 참조한 Version은 수정할 수 없다.
- 품목, 업체, 설비, 작업자명은 거래 행에 **Snapshot**으로 남긴다. 확정 조건은 LOT에 복사해 보존한다.
- 취소는 `status = 'CANCELLED'`, 오입력 제거는 `is_deleted = 1`로 처리한다.
- 변경은 `audit_log`에 남기고, 동시 수정은 `row_version`으로 막는다.

---

# 2. 공정 / 작업표준 / 조건

## 2.1 구조

```text
[공정 경로]      heat_process (침탄) ─ heat_process_version
                   └ heat_process_operation  세척 → 침탄(주공정) → 템퍼링 → 쇼트

[단계 템플릿]    step_template (단위공정 × 설비유형/설비)          ← t_standardtemplate
                   └ step_template_item      승온, 균열, 침탄, 확산, 강온, 소입유지, 소입

[조건 항목]      condition_item              온도(℃), 시간(min), CP(%), RX, NH3 …

[작업표준]       standard (품목 × 단위공정 × 설비유형/설비 × 공정 [× 업체])  ← t_standard
                   └ standard_version        charge 수량, 표준 작업시간, 적용 단계 템플릿
                       └ standard_condition  항목 × 단계 = 값                      ← t_standarddetail

[확정 조건]      production_work (작업 LOT) — standard_version_id, is_standard_fixed
                   └ production_work_condition  항목 × 단계 = 설정값/실적값  ← t_conditiontemplate + t_workconditiondetail
```

- 조건은 고정 컬럼이 아니라 **항목 × 단계 행렬**로 저장한다. 설비 유형마다 항목이 달라도(가스로 CP, 진공로 압력 등) 테이블을 바꿀 필요가 없다.
- 단계와 무관한 LOT 공통 조건(예: 장입량)은 단계를 NULL로 둔다.

## 2.2 투입 시 표준 확정 (기존 Gas 폼 `FixStandard`)

```text
1. 투입 품목들의 작업표준(단위공정·설비 일치, is_current Version)을 조회해 표시
2. 여러 품목이 들어가면 그중 하나를 선택 → production_work_input.is_standard_basis = 1 (LOT당 1행)
3. 확정 → production_work.standard_version_id, is_standard_fixed = 1, standard_fixed_at
        → standard_condition을 production_work_condition.set_value로 복사 (작업자 수정 가능)
4. 확정 후 표준이 바뀌어도 LOT의 조건은 그대로 유지 (Version + 복사)
```

---

# 3. LOT / 투입 / 추적

## 3.1 LOT번호

- 형식은 **`YYMMDD-설비이니셜-작업순번`** 입니다 (예: `260930-B01-001`).
  - `production_work.lot_seq`에 순번을 두고, `UNIQUE(equipment_id, work_date, lot_seq)`로 중복을 막습니다.
- 스케줄 임시 LOT은 `production_schedule.planned_lot_no`에 둡니다 (구 `templotno`, 예: `P260215-B01-001`). RELEASE 시점에 작업 LOT번호를 부여합니다.
- **입고는 LOT으로 관리하지 않습니다.** LOT 생성 전에는 수주번호로 관리합니다.

## 3.2 투입 등록 (단위공정 공통 1개 화면)

| 시점 | 입력 | 동작 |
|-|-|-|
| 주 LOT 생성 전 (세척, 주공정) | **수주번호** | 해당 수주 × 단위공정 투입 잔량 확인 → 투입 행 생성. 주공정이면 `main_work_id = 자신` |
| 주 LOT 생성 후 (템퍼링, 쇼트 …) | **주 LOT번호** | 주 LOT의 투입 제품 목록 표시 → 선택 → 투입 행 생성 (`main_work_id`, `main_input_id`) |
| 주 LOT 생성 후, 수주번호 입력 | 수주번호 | 해당 수주가 **여러 주 LOT으로 나뉘어 있으면 주 LOT 선택창** 표시 → 선택 |
| 재작업 | 부적합 건 선택 | 새 작업 LOT(`is_rework`) + `origin_work_id` = 원(최초) 작업 LOT, `defect_occurrence.rework_input_id` 연결 |

## 3.3 연결 컬럼

| 투입 행 | `main_work_id` | `main_input_id` | `origin_work_id` |
|-|-|-|-|
| 전공정 (세척) | NULL — 수주번호로 연결 | NULL | NULL |
| 주공정 (침탄) | 자신 | NULL | NULL |
| 후공정 (템퍼링, 쇼트) | 주 LOT (병합이면 행마다 다름) | 선택한 주 LOT 투입 행 | NULL |
| 재작업 | 재작업 LOT이 주공정이면 자신 | NULL | 원 작업 LOT (재작업의 재작업도 최초 LOT) |

## 3.4 추적 조회 (주 LOT 기준)

| 방향 | 방법 | 정밀도 |
|-|-|-|
| 주 LOT → 후공정 LOT | `main_work_id` | 정확 |
| 주 LOT → 재작업 LOT | `origin_work_id` | 정확 |
| 주 LOT → 전공정 LOT | 주 LOT의 수주번호 → 같은 수주의 전공정 투입 | 수주 단위 (업무 확인 결과 수용) |
| 부적합 → 공정 / 주 LOT | `production_work_input_id` (발견 공정 귀속) + `main_work_id` 병기 | 정확 |
| 출하 → LOT | `shipment_item.main_work_id` | 정확 |
| 제출 LOT → 작업 LOT | `production_work.submit_lot_no` (작업 LOT당 1개) | 정확 |

## 3.5 작업 LOT 진행 상태 (단위공정마다)

기존 `progressstep`과 같이 **스케줄 배정과 작업 기록을 하나의 상태로** 표시합니다. 출하를 완료로 보지 않습니다.

| `production_work.status` | 표시 | 시점 |
|-|-|-|
| ALLOCATED | 배정 | 스케줄 배정(RELEASE)으로 작업 LOT 생성 |
| INPUT | 투입 | 투입 등록 |
| COMPLETED | 완료 | 해당 단위공정 작업 완료 |
| CANCELLED | 취소 | |

- `vw_work_lot_status`는 상태 외에 검사 대상 수, 미결 부적합 수, 출하 수량을 **참고 정보 컬럼**으로 함께 제공합니다.
- 가동 중 정지·재개 등 세부 이력이 필요하면 `production_work_event`(ALLOCATE / INPUT / START / PAUSE / RESUME / COMPLETE / CANCEL)에 남깁니다. 상태는 늘리지 않습니다.

---

# 4. 계획 / 작업

| 테이블 | 담당 |
|-|-|
| `production_schedule` | 계획 블록 = 설비 × 단위공정 1회 (순서, 계획시간, 임시 LOT) |
| `production_schedule_item` | 블록의 수주품목·계획수량 |
| `production_work` | 작업 LOT (실제시각, 표준 확정, 제출 LOT) |
| `production_work_event` | 시작/정지/재개/완료 이력 |
| `schedule_board_*` | 구 `t_schedulebox` — **UI 표시용** (WinForms 병행기간 한정) |

- 계획 블록과 작업 LOT은 **1:1**입니다 (`UNIQUE(production_schedule_id)`).
  - 한 블록을 나눠 수행해야 하면 계획을 먼저 나눕니다.
  - 병합은 한 LOT에 투입 행이 여러 개 생기는 것이므로 1:1 규칙과 충돌하지 않습니다.
- 작업 상태는 ALLOCATED(배정) / INPUT(투입) / COMPLETED(완료) / CANCELLED입니다 (§3.5).
- 계획 상태는 PLANNED / CONFIRMED / RELEASED / CANCELLED입니다. RELEASED가 되면 작업 LOT이 "배정" 상태로 생성됩니다.
- 재작업 LOT도 같은 LOT번호 규칙(`YYMMDD-설비이니셜-순번`)을 적용합니다.

---

# 5. 검사 / 성적서

## 5.1 흐름

```text
1. 주 LOT번호 입력 → 투입 내역(production_work_input) 표시
2. 검사 대상 선택 (여러 개) → inspection 1건 + 선택한 대상마다 inspection_target 1행
     예) 투입 대상 4개 중 3개 선택 → 검사 I-01 (대상 3), 나머지 1개 → 검사 I-02 (대상 1)
3. 대상별 검사기준 조회: 품목(+업체) inspection_standard → is_current version → inspection_criteria (+ 측정 위치)
4. 대상별 측정값 입력: inspection_item (항목) → inspection_measurement (시료별 값)
5. 판정: 측정값 vs 하한/상한 → 항목 decision → 대상 decision → 검사 decision (대상 중 하나라도 FAIL이면 FAIL)
6. FAIL → defect_occurrence (해당 투입 행 귀속, inspection_target 연결, 주 LOT 병기)
7. 성적서 발행: 대상(품목)별 1장 — part_print_template(품목+업체, 용도 INSPECTION_REPORT 기본 양식)의 현재 버전
               → print_log 기록 + report_issued_at / report_issue_count, 제출 LOT은 submit_lot_no_snapshot ({{ConvertLot}})
```

## 5.2 테이블

| 테이블 | 역할 | 원본 |
|-|-|-|
| `inspection_standard` / `_version` | 품목(+업체)별 검사기준 / Version | 신규 (구 기준은 품목·업체별 직접 등록) |
| `inspection_criteria` | 항목별 요구사항 (항목유형, 위치, 스펙, 측정기, 스케일, 하한/상한, 시료수) | t_inspectioncriteria |
| `inspection_criteria_point` | 측정 위치 | t_inspectioncriteria.p1~p10 |
| `inspection` | 검사 1회 (검사번호, 주 LOT, 검사자, 종합판정) | t_inspection (inspectionno) |
| `inspection_target` | 검사 대상 품목 (투입 행, 수주, 업체, 기준 Version, 판정, 성적서 양식·발행) | t_inspection (subno 행) |
| `inspection_item` / `inspection_measurement` | 대상별 항목 판정 / 시료별 측정값 | t_inspectiondetail (항목 + v1~v10) |

## 5.3 출력 양식 — 등록 방식 2가지

| 방식 | `template_kind` | 대상 | 양식 내용 | 수정 |
|-|-|-|-|-|
| **엑셀 양식** | `EXCEL` | 업체·품목마다 양식이 다르거나 사용 중 수정이 필요한 출력물 (검사성적서, 사용자 추가 용도) | 사용자가 만든 엑셀 파일 (`{{치환자}}`) | 사용자가 파일 재업로드 → 새 버전 |
| **고정 양식** | `FIXED` | 양식이 고정이고 사용 중 수정이 필요 없는 출력물 (거래명세표, 공정이동표, 제품라벨, 작업일보, 진행현황표, 작업표준서) | 코드 렌더러(`renderer_key`) + **레이아웃 옵션 JSON** (페이지당 행 수, 폰트, 도장 위치, 보관용 부수 등) | 관리자가 옵션 조정 → 새 버전. 레이아웃 자체 변경은 개발 |

- 두 방식 모두 같은 `print_purpose` → `print_template` → `print_template_version` 구조를 씁니다. 품목·업체별 기본 양식 연결(`part_print_template`)과 발행 이력(`print_log`)도 동일합니다. 호출하는 쪽은 방식을 구분하지 않습니다.
- 한 용도에 두 방식을 같이 둘 수 있습니다. 예) 출하전표는 기본으로 FIXED 거래명세표를 쓰고, 특정 업체는 EXCEL 양식을 씁니다.
- 고정 양식을 새로 추가하려면 렌더러 코드를 개발해야 합니다. 이후 변경 가능한 값은 모두 옵션으로 빼고 코드 상수로 두지 않습니다 (§15.4).

### 5.3.1 엑셀 양식 (EXCEL)

검사성적서에 적용했던 **"사용자가 엑셀로 양식 작성 → 등록 → 발행 시 호출"** 방식입니다. 수정이 필요한 출력물과 사용자가 추가하는 용도에 적용합니다.

```text
print_data_source (시스템 정의)          값을 채우는 코드 단위: 검사대상 / 작업LOT / 작업표준 / 출하전표 / 마감
   ├ print_field                         치환자 사전 {{키}} (+ 한글 별칭, 유형 TEXT/NUMBER/DATE/IMAGE/LIST)
   └ print_purpose (사용자 확장)         용도: 검사성적서, 작업지시서, … + 사용자가 추가 (예: 열처리 기록지)
        └ print_template                 양식 (이름, 발행 형식 PDF/XLSX)
             └ print_template_version    엑셀 파일 자체를 DB 보관, 버전별 (현재 버전 1개)
part_print_template                      품목(+업체) × 용도 → 기본 양식 1개
print_log                                발행 이력 (양식 버전, 대상, 치환값 Snapshot, 발행본)
```

| 규칙 | 내용 |
|-|-|
| 용도 확장 | 사용자가 `print_purpose`를 추가합니다. 용도마다 **데이터 공급원 1개를 선택**합니다. 새 데이터 공급원(값을 채우는 코드)은 개발이 필요합니다 |
| 양식 등록 (EXCEL) | 엑셀 파일을 업로드하면 치환자를 추출해 사전과 대조하고, 사전에 없는 치환자는 경고합니다. 파일은 DB(`file_content`)에 저장하고 해시로 중복을 확인합니다 |
| 양식 수정 | 파일을 다시 올리면 **새 버전**이 됩니다. 현재 버전만 바뀌고, 과거 발행물은 발행 당시 버전으로 재출력할 수 있습니다 |
| 양식 선택 | 품목(+업체) × 용도의 기본 양식을 먼저 찾고, 없으면 품목 공통, 그다음 용도 기본 양식 순서로 찾습니다. 사용자가 발행 화면에서 바꿀 수 있습니다 |
| 치환자 문법 (EXCEL) | **`{{키}}` 한 가지로 통일**합니다 (한글 별칭 `{{검사번호}}` 허용). 반복행은 LIST 영역, 이미지는 병합 영역에 맞춰 삽입합니다 (§15.2) |
| 발행 | 서버(API)에서 EXCEL은 채우기 → PDF 변환, FIXED는 렌더러 + 옵션으로 PDF를 생성합니다. 클라이언트에는 Office·LibreOffice를 설치하지 않습니다 |
| 프린터 | 단말(PC·태블릿) × 용도별 프린터·부수·자동인쇄를 `workstation_print_setting`에서 관리자가 지정합니다 (구 PrinterInfo 로컬 파일 대체) |
| 도장 | 거래명세표 도장 이미지는 `company.stamp_image`에 저장합니다 (구 PC 경로 `stamp_path`) |
| 이력 | 발행할 때마다 `print_log`에 양식 버전, 대상, 치환값을 남기고, 성적서처럼 보존이 필요한 용도는 발행본 파일도 저장합니다 |

---

# 6. 출하 / 마감

- **출하(=납품)는 전표로 등록합니다.** `shipment`는 구 `t_outcomesum`(전표 합계), `shipment_item`은 구 `t_outcome`입니다. 행마다 **출하 LOT(주 LOT)**을 기록합니다.
- **마감은 업체별로, 전표 단위로 진행합니다.** 업체마다 마감일이 다릅니다(`customer.closing_day`).
  - `shipment_closing` 1건 = 한 업체의 마감 (마감 기준일 `closing_date`, 귀속 연·월)
  - 마감에 포함할 전표를 선택하면 `shipment.shipment_closing_id`가 채워집니다.
- **발생한 전표도 다음 달로 이월할 수 있습니다.** 선택하지 않은 전표는 `shipment_closing_id = NULL`(미마감)로 남고, 다음 마감에서 선택합니다.
- 마감이 확정되면(`CLOSED`) 연결된 전표는 수정할 수 없습니다. 재오픈(`REOPENED`)은 감사 로그를 남깁니다.
- 전표와 마감의 업체 일치는 API에서 검증합니다.

---

# 7. 스케줄 계산 (Scheduling Service)

기존 `sp_RecalculateWorkPlanSequence`, `sp_RecalculateWorkPlanTimes` 로직은 **API Scheduling Service** 한 곳으로 옮깁니다. 기존 SP는 결과 비교용으로만 남깁니다.

| 규칙 | 기준 |
|-|-|
| 단위 | 설비 × 작업일 내 `sequence_no` 순 |
| 시작시각 | 이전 블록 종료. 실적이 있으면 `actual_end_at` |
| 소요시간 | 블록에 담긴 품목별 작업시간 중 **최대값** (병합 포함, 수량 비례 아님 — 2026-09-30 확정). 품목별 작업시간 결정 순서: ① 작업표준 `running_time_min` → ② 같은 품목·설비유형의 직전 작업 시간 → ③ `process_default_time` → ④ 사용자 입력 후 기준시간 등록 → ⑤ 설정 `schedule.default_running_time_min` |
| 작업일 경계 | 첫 교대 시작시각 (구 코드 `startTime.Hour < 8`이면 전일 → `work_shift`로 설정화) |
| 제외 | `work_calendar` 휴일, `equipment_downtime.is_planned` |
| 고정 | `is_time_locked` (신규 기능) |
| 대상 | PLANNED, CONFIRMED만 (RELEASED 이후 불변) |
| 지연 반영 | 작업이 계획보다 늦게 끝나거나 늦게 진행 중이면 **같은 설비의 뒤 배정 계획시각을 실제로 뒤로 이동해 저장**하고 화면을 갱신 (고정 블록 제외) — 2026-09-30 확정 (구: 화면 표시만 이동) |
| 동시성 | 설비·일자 `SELECT … FOR UPDATE` + `row_version` |

---

# 8. 테이블 카탈로그 (77 테이블 + 5 VIEW)

| 영역 | 테이블 |
|-|-|
| 시스템/공통 (14) | company, common_code_group, common_code, system_setting, department, job_position, employee, app_user, role, app_user_role, menu, role_menu, audit_log, migration_id_map |
| 기준정보 (11) | customer, equipment_type, equipment, equipment_history, instrument, part, part_customer, part_history, defect_reason, work_shift, work_calendar |
| 공정/표준/검사기준 (18) | unit_process, process_default_time, heat_process, heat_process_version, heat_process_operation, step_template, step_template_item, condition_item, standard, standard_version, standard_condition, inspection_standard, inspection_standard_version, inspection_criteria, inspection_criteria_point, unit_inspection_item, part_heat_process, step_template_condition |
| 출력 양식 (9) | print_data_source, print_field, print_purpose, print_template, print_template_version, part_print_template, print_log, workstation, workstation_print_setting |
| 수주/계획/생산 (12) | sales_order, sales_order_item, production_input_queue, worker_assignment, production_schedule, production_schedule_item, schedule_board_layout, schedule_board_item, production_work, production_work_event, production_work_input, production_work_condition |
| 검사/부적합/출하/설비 (13) | inspection, inspection_target, inspection_item, inspection_measurement, line_inspection, line_inspection_measurement, defect_occurrence, shipment_closing, shipment, shipment_item, equipment_downtime, maintenance, attachment |
| VIEW (5) | vw_production_work_input_qty, vw_production_work_summary, vw_sales_order_item_process_progress, vw_sales_order_item_progress, vw_work_lot_status |

---

# 9. 기존 46개 테이블 → V3.5 매핑

| No | 기존 | V3.5 | 처리 | 소스 확인 |
|-:|-|-|-|-|
| 1 | t_combolist | common_code_group, common_code (+ defect_reason, condition_item 등 추출) | 재설계 | |
| 2 | t_company | company | 변경 | |
| 3 | t_condition_con | — | **폐기 후보** | 전 프로젝트 소스 미사용 (연속로 조건용, 미사용). 운영 DB 데이터 건수 확인 후 폐기 |
| 4 | t_conditiontemplate | production_work_condition (단계 목록) | 통합 | LOT별 확정 단계 헤더 |
| 5 | t_customer | customer | 변경 | |
| 6 | t_defect | defect_occurrence (+ defect_reason) | 재설계 | |
| 7 | t_department | — | **폐기 후보** | 소스 미사용 |
| 8 | t_dept | department (+ 부서별 role) | 변경 | 로그인 시 메뉴 접근 기준 |
| 9 | t_dept_menu | role_menu | 재설계 | 부서 → role 1:1 생성 후 이관 |
| 10 | t_downtime | equipment_downtime | 변경 | |
| 11 | t_employee | employee | 변경 | |
| 12 | t_equipment | equipment_type, equipment | 분해 | |
| 13 | t_heatprocess | heat_process (+ version, operation 신규) | 변경 | 공정명 |
| 14 | t_income | sales_order, sales_order_item | 분해 | 수주 = 입고 |
| 15 | t_inputsub | production_work_input (장입/추출 시각) | 통합 | |
| 16 | t_inputwaiting | production_input_queue (잔량은 계산) | 재설계 | 저장형 remainqt 제거 |
| 17 | t_inspection | inspection (inspectionno), inspection_target (subno 행) | 분해 | 검사 1회 : 대상 N, 성적서 대상별 |
| 18 | t_inspectioncriteria | inspection_standard(+version), inspection_criteria, inspection_criteria_point | 분해 | 품목·업체별 |
| 19 | t_inspectiondetail | inspection_item, inspection_measurement | 분해 | 항목 + v1~v10 |
| 20 | t_inspectionsub | — | **폐기 후보** | 소스 미사용 |
| 21 | t_inspectiontemplate | print_template + print_template_version (용도 INSPECTION_REPORT) | 통합 | 엑셀 양식. 파일은 PC 경로 → DB 보관 |
| 22 | t_instruments | instrument | 변경 | |
| 23 | t_lineinspection | line_inspection, line_inspection_measurement | 분해 | hx/cx/dx/tx → Row |
| 24 | t_maintenance | maintenance | 변경 | |
| 25 | t_menu_catalog | menu | 변경 | |
| 26 | t_outcome | shipment_item | 변경 | 출하 상세 |
| 27 | t_outcomesum | shipment (+ shipment_closing 신규) | 변경 | **출하 전표**. 마감은 업체별·전표 선택, 이월 가능 |
| 28 | t_part | part, part_customer (+ part_heat_process) | 분해 | |
| 29 | t_part_template | part_print_template | 변경 | 용도(print_purpose)별 기본 양식 |
| 30 | t_printsheet | — | **폐기 후보** | 소스 미사용 |
| 31 | t_printtemplate | print_template (좌표형 layout_json 방식은 폐기 → 엑셀 양식 재작성) | 통합 | 구 방식(QuestPDF 좌표) 미이관 |
| 32 | t_process_default_time | process_default_time | 변경 | |
| 33 | t_schedulebox | schedule_board_layout, schedule_board_item | 한시 | UI 표시용 |
| 34 | t_standard | standard, standard_version | 분해 | 품목·단위공정·설비(·업체) |
| 35 | t_standard_gas | standard_condition | 통합 | 가스 항목 → condition_item |
| 36 | t_standarddetail | standard_condition | 재설계 | item × step1~15 |
| 37 | t_standardtemplate | step_template, step_template_item | 분해 | 설비·단위공정별 단계 |
| 38 | t_system_settings | system_setting | 변경 | |
| 39 | t_templatefieldname | print_field (+ print_data_source) | 재설계 | 치환자 사전, 데이터 공급원별 |
| 40 | t_unitinspectionitem | unit_inspection_item | 변경 | |
| 41 | t_unitprocess | unit_process | 변경 | |
| 42 | t_work | production_work (+ event) | 재설계 | |
| 43 | t_workconditiondetail | production_work_condition | 재설계 | LOT별 item × step |
| 44 | t_workerassignment | worker_assignment | 변경 | |
| 45 | t_worksub | production_work_input | 재설계 | |
| 46 | workplan | production_schedule, production_schedule_item | 재설계 | |

**주요 컬럼 이동**

| 기존 | V3.5 |
|-|-|
| t_work.lotno | `production_work.lot_no` (+ `lot_seq`) |
| t_work.convertlotno / t_inspection.convertlot | `production_work.submit_lot_no` / `inspection.submit_lot_no_snapshot` |
| t_work.isfixed, fixedpartid, standardid | `is_standard_fixed`, `production_work_input.is_standard_basis`, `standard_version_id` |
| t_worksub.originelotno | `production_work_input.origin_work_id` (현재 값 설정 로직 없음 → 신규 구현) |
| workplan.templotno | `production_schedule.planned_lot_no` |
| t_work.progressstep (배정/투입/완료) / isdone / isinspectiondone | `status` (ALLOCATED/INPUT/COMPLETED) / VIEW `is_done`, `is_inspection_done` |
| t_inspection.printsheetid | `inspection_target.print_template_id` |
| t_income.planremainqty, t_inputwaiting.remainqt | VIEW 계산 |
| t_workerassignment.workername | 삭제 (`worker_name_snapshot`) |
| t_employee.part | `employee.team_name` |

---

# 10. 마이그레이션 / 병행운영

```text
① 백업 → ② bbakggum_v2 생성(DDL 실행)
③ 기준정보 → ④ 공정/단계 템플릿/조건 항목/작업표준 (item 문자열 → condition_item 정규화)
⑤ 검사기준 / 양식 → ⑥ 수주 → ⑦ 계획
⑧ 작업: t_work → production_work, t_worksub + t_inputsub → production_work_input,
        t_conditiontemplate + t_workconditiondetail → production_work_condition
⑨ 검사/부적합 → ⑩ 출하 전표(t_outcomesum/t_outcome) → ⑪ 검증
```

- 모든 이관 Row는 `migration_id_map`에 기록합니다. 스크립트는 재실행이 가능해야 합니다.
- 구 데이터에는 주 LOT 병기가 없으므로 후공정 행의 `main_work_id`는 NULL로 두고, 전환 이후 데이터부터 기록합니다.
- 폐기 후보 4개(t_condition_con, t_department, t_inspectionsub, t_printsheet)는 운영 DB 건수를 확인합니다. 데이터가 있으면 백업 테이블로 보존한 뒤 제외합니다.
- **병행운영:** 모듈 단위로 쓰기 주체를 하나로 정하고, 방향별 동기화 Job을 둡니다 (JOIN VIEW는 쓰기가 불가능하므로 사용하지 않음).
- **전환 순서:** 기준정보 → 수주 → 계획 → 작업 → 검사/부적합 → 출하/마감.

---

# 11. 웹 (React + Calendar)

- **구성:** React + TypeScript → ASP.NET Core API (Dapper) → MariaDB. 계산·검증·권한·감사는 API가 맡습니다.
- **Calendar/Gantt:**
  - Resource는 `equipment`, Event는 `production_schedule`입니다.
  - Drag & Drop은 `row_version`을 확인하고 설비·일자를 잠근 뒤 재계산합니다.
- **투입 화면 (단위공정 공통):**
  - 수주번호 또는 주 LOT을 입력합니다. 주 LOT이 여러 개면 선택창을 띄웁니다.
  - 주 LOT 투입 목록에서 선택하고, 표준 확정 → 조건 복사·수정을 거칩니다.
  - 주공정이면 제출 LOT을 입력합니다.
- **검사 화면:** 주 LOT 입력 → 대상 선택(여러 개) → 대상별 기준 표시 → 측정값 입력 → 자동 판정 → 대상(품목)별 성적서 발행 순서입니다.
- **LOT 현황:** 단위공정별 배정/투입/완료(`vw_work_lot_status.display_status`)로 색상을 구분하고, 검사·부적합·출하는 참고 정보로 표시합니다.
- **출력:** 품목(+업체)과 용도(`print_purpose`)로 기본 양식을 찾아 서버에서 엑셀 → PDF로 발행합니다. 양식 관리 화면에서 용도 추가, 양식 업로드(치환자 검증), 버전 이력, 미리보기를 제공합니다.
- **마감 화면:** 업체 선택 → 미마감 전표 목록 → 마감할 전표 선택 → 마감 확정. 선택하지 않은 전표는 이월됩니다.
- **추적 조회:** 주 LOT, 수주번호, 제출 LOT 중 하나로 조회합니다.

---

# 12. 확인 필요 사항

**V3.4에서 확정된 항목**

| 항목 | 결정 |
|-|-|
| LOT 상태 | 단위공정별 배정 → 투입 → 완료. 출하는 완료 기준이 아님 |
| 출력 양식 | 모든 출력물 엑셀 양식 등록 방식, 용도는 사용자 확장 (`print_purpose`) |
| 검사 단위 | 검사 1회에 대상 여러 개, 성적서는 대상(품목)별 발행 |
| 마감 | 업체별·전표별, 미선택 전표 이월 |
| 재작업 LOT번호 | 동일 규칙 |

**남은 확인 사항** (운영 DB 조회 필요)

| # | 항목 | 방법 |
|-|-|-|
| 1 | 구 `t_standarddetail.item`, `t_workconditiondetail.item` 문자열 종류 (condition_item 정규화 대상) | `SELECT item, COUNT(*) … GROUP BY item` |
| 2 | 폐기 후보 4개 테이블의 운영 데이터 유무 | 테이블별 `COUNT(*)` |
| 3 | 구 `t_inspection`에서 같은 inspectionno에 subno가 여러 개인 실제 데이터 형태 | inspectionno별 subno 분포 |
| 4 | 고정 양식 옵션 편집 범위 (관리자에게 열어줄 키 / 개발자만 수정할 키 구분) | 관리자 화면 설계 시 |
| 5 | 고정 양식 중 업체별로 엑셀 양식이 필요한 출력물이 있는지 (예: 특정 업체 전용 거래명세표) | 업무 확인 |

---

# 13. 검증 결과 (MariaDB 11.6.2 임시 인스턴스)

| 항목 | 결과 |
|-|-|
| DDL | 오류 없음 (FK 체크 활성 상태) — 테이블 77 / VIEW 5 (초기 데이터 포함, `db/dev/dev-db.ps1 smoke` 통과) |
| 시나리오 | 수주 → 세척(수주번호) → 침탄 주 LOT 2개(표준 확정·조건 수정·제출 LOT) → 템퍼링(주 LOT 2개 병합, 목록 선택) → 검사 2회(대상 4개 중 3 + 1) → 부적합(침탄·템퍼링 각각 귀속) → 재작업 LOT(원 LOT 2개) → 출하 전표 2건 → 1건 마감 / 1건 이월 |

**LOT 진행 상태 (`vw_work_lot_status`)** — 상태는 배정/투입/완료만, 나머지는 참고 정보

| LOT | 단위공정 | 상태 | 양품 | 검사 대상 | 미결 부적합 | 출하 |
|-|-|-|-:|-:|-:|-:|
| 260930-W01-001 | 세척 | 완료 | 2,000 | | | |
| 260930-B01-001 | 침탄(주) | 완료 | 1,370 | 4 | 1 | 695 |
| 260930-B02-001 | 침탄(주) | 완료 | 790 | | 1 | |
| 260930-T01-001 | 템퍼링 | 완료 | 1,955 | | 1 | |
| 261001-B01-001 | 침탄(재작업) | 투입 | 40 | | | |

**검사 / 성적서** — 주 LOT 260930-B01-001, 투입 대상 4개

| 검사번호 | 순번 | 품목 | 판정 | 성적서 |
|-|-:|-|-|-|
| I-0930-01 | 1 | 기어A | FAIL | 미발행 |
| I-0930-01 | 2 | 샤프트B | PASS | 발행 |
| I-0930-01 | 3 | 기어A | PASS | 발행 |
| I-0930-02 | 1 | 샤프트B | PASS | 발행 |

**마감**: SH-0924-01 → CL-C1-2609 (2026-09) 마감, SH-0930-01 → 미마감(이월)

**출력 양식**: 시스템 용도 6종 + 사용자 추가 용도(열처리 기록지) 등록, 양식 v1 → v2 교체 시 현재 버전 1개 유지, 발행 이력에 v2 기록. 제약: 현재 버전 중복, 용도 코드 중복, 미정의 치환자 유형 거부

**주 LOT 260930-B01-001 추적**: 후공정 260930-T01-001 · 재작업 261001-B01-001 · 전공정 260930-W01-001(수주번호)

**확정 조건 (항목 × 단계)**: 침탄 920℃ / 180min / CP 1.10, 확산 920℃ / 60min / CP 0.80 (표준 0.85에서 수정)

**검사 판정 (58~62 HRC)**: 59.5 PASS / 57.2 FAIL / 60.1 PASS

**제약 동작 (모두 거부)**: LOT 순번 중복, 같은 항목 × 단계 조건 중복, 품목·업체 검사기준 중복, 정의되지 않은 작업 상태(RUNNING), 같은 검사에 같은 대상 중복, 품목·업체·용도별 기본 양식 2개

---

# 14. 최종 원칙

- 공정은 경로(heat_process_operation), 단계(step_template), 조건(standard_condition, 항목 × 단계)으로 관리한다.
- 작업 LOT = 단위공정 1회 수행이며, LOT번호는 `YYMMDD-설비이니셜-순번`이다.
- 투입은 주 LOT 전에는 수주번호, 이후에는 주 LOT으로 입력한다. 모든 추적은 주 LOT 기준이다.
- 혼적 LOT은 투입 품목 중 하나의 표준을 선택·확정하고, 조건을 LOT에 복사해 보존한다.
- 매 공정 양품 = 투입 − 부적합, 부적합은 발견 공정에 귀속하고 주 LOT을 병기한다.
- 작업 LOT 상태는 단위공정별 배정 → 투입 → 완료이며, 검사·출하는 상태가 아니라 별도 기록이다.
- 검사는 주 LOT 투입내역에서 대상 여러 개를 선택해 1회로 진행하고, 품목·업체별 기준으로 판정하며, 성적서는 대상(품목)별로 발행한다.
- 출력 양식은 EXCEL(사용자 수정)과 FIXED(코드 고정 + 관리자 옵션) 두 방식으로 등록하고, 용도는 사용자가 추가하며, 양식은 DB에 버전으로 보관하고 발행 이력을 남긴다.
- 운영 중 바뀔 수 있는 값(시각, 주기, 세율, 경로, 번호 형식, 레이아웃 수치, 표시명)은 코드에 두지 않고 관리자 설정·공통코드·양식 옵션으로 관리한다.
- 출하는 전표로 등록하고 출하 LOT을 기록하며, 마감은 업체별로 전표를 선택해 진행하고 미선택 전표는 이월한다.
- 확인되지 않은 업무 규칙을 근거로 구 테이블을 삭제하지 않는다.

---

# 15. 구현 시 주의사항 (기존 프로젝트에서 가장 어려웠던 부분)

기존 소스(`D:\Programming\ProductManager`)를 분석한 결과입니다. 같은 문제가 반복되지 않도록 신규 구현 전에 확인합니다.

## 15.1 스케줄 작업 / 진행현황 표시

| # | 기존 구현에서 확인된 문제 | 위치 | 신규 구현 원칙 |
|-|-|-|-|
| S1 | **재계산 로직이 두 벌** — DB SP(`sp_RecalculateWorkPlanSequence/Times`)와 C# `RecalculateEquipmentCore`가 공존 | `WorkPlanRepository.cs` 263·275·2343행 | 계산은 **API Scheduling Service 한 곳**. SP는 전환 전 결과 비교용으로만 남김 |
| S2 | **작업일 경계 08:00 하드코딩**이 여러 화면에 분산 (`Hour < 8`, `AddHours(8)`) | `F_WorkPlanForm`, `F_GraphicWindow`, `WorkItemPanel`, `F_DowntimeInput` | `work_shift` 첫 교대 시작시각을 **서비스 한 곳**에서 계산해 모든 화면이 사용. 화면에 시각 상수 금지 |
| S3 | **자정/작업일을 넘는 작업의 표시 분할** (전날 화면 박스 + 오늘 화면 박스) | `WorkItemPanel.cs` 주석 | 저장은 `planned_start_at` ~ `planned_end_at` 연속 구간 하나. 분할은 **화면 렌더링 단계에서만** (Gantt가 자동 처리) |
| S4 | **순번 재정렬 시 임시 고순번 부여 후 재계산** | `F_WorkPlanForm.cs` 1527·1908행 | 이동·재정렬은 설비·일자 잠금(`SELECT … FOR UPDATE`) 안의 **한 트랜잭션**에서 순번 재부여. 순번 UNIQUE 제약을 두지 않은 이유 |
| S5 | **다른 PC 반영이 60초 타이머 폴링** + 같은 프로세스 안에서만 동작하는 정적 이벤트(`WorkPlanEventManager`) | `F_WorkPlanForm.InitTimer`, `Workplaneventmanager.cs` | 웹은 **SignalR 푸시**(설비·일자 단위 그룹). 병행기간 WinForms는 기존 폴링 유지 |
| S6 | **동시 수정 충돌 감지 없음** (두 사람이 같은 설비를 이동) | — | `row_version` 낙관적 잠금 → 충돌 시 409 + 최신 상태 다시 표시 |
| S7 | **계획과 실적 상태가 한 화면 로직에 섞임** (배정/투입/완료 판단이 화면 코드에 분산) | `WorkItemPanel`, `PlanItemPanel` | 상태는 `production_work.status` 하나(배정/투입/완료). 화면은 `vw_work_lot_status`만 읽음 |
| S8 | 스케줄 저장소 한 파일이 2,500행 이상 (`WorkPlanRepository.cs`) | — | 계획 CRUD / 재계산 / 조회(Gantt) / 진행현황을 서비스 단위로 분리 |
| S9 | 조회 범위 | — | Gantt·현황은 **기간 + 설비** 조건 필수. 전체 이력을 한 번에 불러오지 않음 |

**검증 방법:** 전환 전 같은 날짜·설비로 구 SP 결과와 신규 서비스 결과를 1:1 비교하는 자동 테스트를 둡니다(§7). 자정을 넘는 작업, 휴일, 고정 블록, 재작업 끼워넣기 케이스를 반드시 포함합니다.

## 15.2 엑셀 양식 등록 / 출력

| # | 기존 구현에서 확인된 문제 | 위치 | 신규 구현 원칙 |
|-|-|-|-|
| P1 | **출력 엔진이 네 갈래** — ① 좌표형(배경 이미지 + `layout_json`, QuestPDF) ② ClosedXML 치환 ③ EPPlus 치환(화면 코드 안) ④ 코드 고정 레이아웃(PrintDoc 7종, QuestPDF) | `InspectionReportRenderer`, `InspectionPrintService`, `F_InspectionAddForm` 2800행대, `PrintDoc/*.cs` | **EXCEL / FIXED 두 방식**만 남기고 서버 `PrintService` 1개가 분기. 좌표형(①)은 폐기, ②③은 EXCEL 엔진 하나로 통합, ④는 FIXED 렌더러로 이관 |
| P2 | **치환자 문법 불일치** — `{{키}}`(ClosedXML)와 `{키}`·`{한글}`(EPPlus) 혼용 | 동일 | **`{{키}}` 하나로 통일**, 한글 별칭은 `print_field.field_alias`로 사전 관리 |
| P3 | **치환자 목록이 코드에 하드코딩** (Dictionary 수작업) | `BuildPlaceholders`, `F_InspectionAddForm` | 치환자는 `print_field` 사전에서 관리하고, 데이터 공급원 코드는 사전 키만 채움. 양식 업로드 시 **미정의 치환자 경고** |
| P4 | **반복 데이터를 고정 좌표 키로 표현** (`{{T1_3_P2}}` = 탭1 3행 P2) → 행 수가 양식에 고정, 초과분 누락 | `InspectionPrintService` | LIST 치환자 영역을 행 단위로 복제·확장하는 방식 지원. 기존 좌표 키는 **호환용으로 유지** (구 양식 재사용) |
| P5 | **양식 파일을 PC 로컬 폴더에 복사하고 절대경로를 DB에 저장** → 다른 PC에서 파일 없음, 같은 이름이면 덮어써서 이력 소실 | `F_InspectionTemplate.cs` 525~560행 | 파일 자체를 DB(`print_template_version.file_content`)에 보관, **해시로 중복 확인**, 덮어쓰기 대신 **새 버전** |
| P6 | **PDF 변환이 클라이언트 설치 의존** (과거 Office Interop → 현재 LibreOffice 설치 필요) | `ConvertXlsxToPdfViaLibreOffice` | **서버에서만 변환**. 서버에 LibreOffice(또는 동등 엔진) 1곳 설치, 변환 타임아웃·동시 실행 큐 관리 |
| P7 | 이미지(경화층 차트, 조직사진)를 치환자 셀의 병합 영역에 삽입 — 동작은 좋으나 **첫 시트·첫 셀만** 처리 | `InsertHardnessChartImage` | IMAGE 치환자는 **모든 시트·모든 위치**를 처리하고, 병합 영역 크기에 맞춤 (기존 방식 유지) |
| P8 | 발행 이력이 없어 **어떤 양식으로 무엇을 발행했는지 추적 불가** | — | `print_log`에 양식 버전, 치환값 Snapshot, (성적서) 발행본 저장 → 동일 재발행 보장 |
| P9 | 양식 선택 규칙이 코드마다 다름 (`GetDefaultByPartIdAndType(partId, "inspection")`) | `InspectionPrintService.GetTemplateForPart` | 품목+업체 → 품목 공통 → 용도 기본 순서의 **단일 조회 함수** |
| P10 | 사용자 양식 작성 가이드 부재 | — | 양식 관리 화면에 **용도별 사용 가능 치환자 목록**(사전)과 샘플 양식 다운로드 제공 |

**엑셀 양식 작성 규칙 (사용자 안내용 초안)**

```text
- 치환자:  셀에 {{키}} 또는 {{한글별칭}}  예) {{InspectionNo}}  {{검사번호}}  {{ConvertLot}}
- 반복행:  {{#목록명}} … {{/목록명}} 으로 감싼 행 범위를 데이터 수만큼 복제   예) {{#Measurements}}
- 이미지:  이미지가 들어갈 영역을 셀 병합 후 {{이미지키}} 기입   예) {{HardnessChart}}
- 호환:    기존 좌표형 키 {{T1_3_P2}}, {{C1_2_Spec}} 도 그대로 사용 가능
- 서식:    셀 서식(글꼴, 테두리, 숫자형식)은 양식 그대로 유지됨. 값만 치환
```

## 15.3 고정 양식(FIXED) — 영업 전표 방식

기존 영업 전표(거래명세표)는 검사성적서와 달리 **QuestPDF 코드로 레이아웃을 고정**해서 출력합니다. `PrintDoc` 폴더의 다른 출력물도 모두 같은 방식입니다.

| 기존 클래스 | 출력물 | 호출 화면 | V3.6 `renderer_key` / 용도 |
|-|-|-|-|
| `OutputSheet` (+ `OutputComSheet`, `MultiOutputSheet`) | 거래명세표 (공급자/거래처 보관용 2부, 절취선, 도장) | `F_OutForm`, `F_MonthlyClosing` | `SALES_SLIP` / 출하전표 |
| `ProcessSheet` | 공정이동표 | `F_IncomeAddForm` | `PROCESS_SHEET` / 공정이동표 |
| `ProductLabel` | 제품표시 라벨 | `F_IncomeAddForm` | `PRODUCT_LABEL` / 제품표시 라벨 |
| `WorkDailySheet` | 작업일보 | `F_GasForm` | `WORK_DAILY` / 작업일보 |
| `ProgressSheet` | 작업 진행 현황표 | 스케줄 화면 | `PROGRESS_SHEET` / 작업 진행 현황표 |
| `WorkStandardSheet` | 작업표준서 | 작업표준 화면 | `WORK_STANDARD` / 작업표준서 |

**기존 방식에서 확인된 문제와 신규 원칙**

| # | 문제 | 위치 | 신규 원칙 |
|-|-|-|-|
| F1 | 레이아웃 수치가 전부 `const` (거래명세표만 32개: 페이지당 6행, 폰트 크기, 행 높이, 선 두께, 도장 크기·좌표 등) | `OutputSheet.cs` 14~71행 | 수치는 `print_template_version.layout_options_json`으로 빼서 관리자가 조정. 렌더러는 옵션이 없으면 코드 기본값 사용 |
| F2 | **업무 규칙이 출력 코드 안에** — 부가세율 `TAX_RATE = 0.1m` | `OutputSheet.cs` 63행 | 금액 계산은 출하 서비스가 하고, 결과(공급가·세액)를 전표에 저장. 세율은 설정 `sales.vat_rate`. 출력은 저장된 값만 표시 |
| F3 | 보관용 문구·색상 분기가 문자열 비교 (`storageType == "(거래처 보관용)"`) | `OutputSheet.cs` 650행대 | 부수·문구·색상을 옵션 배열(`copies`, `copy_border_color`)로 |
| F4 | 도장 이미지를 회사 ID로 **PC 경로**에서 읽음 (`GetCompanyStampPath`) | `OutputSheet.cs` 192행 | `company.stamp_image` (DB) |
| F5 | 같은 전표에 클래스가 3개 (`OutputSheet`, `OutputComSheet`, `MultiOutputSheet`) | `PrintDoc` | 렌더러 1개 + 옵션(도장 유무, 여러 전표 한 PDF 병합) |
| F6 | 폰트 `GulimChe`, `맑은 고딕` 고정 — 서버에 폰트 없으면 깨짐 | 여러 곳 | 옵션 `font_family` + **서버에 폰트 설치를 배포 체크리스트에 포함** |

### 15.3.1 고정 양식 레이아웃 옵션 (`layout_options_json`, 초기값 = 기존 코드 상수)

**공통 스키마** — 모든 렌더러가 같은 키 구조를 사용합니다.

| 키 | 내용 |
|-|-|
| `page` | `size`(A4) + `orientation`(PORTRAIT/LANDSCAPE) + `margin`, 또는 라벨처럼 `width_mm`/`height_mm`/`margin_mm` |
| `font` | `family` + 영역별 크기 (`title`, `header`, `content` …) |
| `title` | 문서 제목 문자열 |
| `sections` | `[{key, title, visible}]` — 구역 순서·제목·표시 여부 |
| `columns` / `*_columns` | `[{key, title, width \| relative, visible}]` — 표 컬럼 제목·너비 |
| `barcode` | `{format, content, width, height, scale}` |
| `empty_text` | 데이터 없을 때 문구 |
| `*_code_group` | 표시명·색상을 가져올 공통코드 그룹 |

**렌더러별 옵션**

| 렌더러 | 기존 클래스 | 용지 | 조정 가능 옵션 (초기값) |
|-|-|-|-|
| `SALES_SLIP` 거래명세표 | `OutputSheet` (+Com, Multi) | A4 세로, 여백 0 | 폰트 GulimChe·영역별 12종, 페이지당 품목 6행, **보관용 2부** (문구·테두리색), 여백·절취선 간격 5종, 행 높이 4종, 선 두께, 배경색, **도장**(표시·크기 50·X -250·Y 65/480), 여러 전표 한 PDF 병합 |
| `PROCESS_SHEET` 공정이동표 | `ProcessSheet` | A4 세로, 여백 20 | 폰트 6종 (제목 40, 품명 30, 보안품 32), 보안품 표시, **좌측 라벨 16개**, 공정 기록 행 16, 컬럼 9개(제목·너비), **바코드** CODE_128 200×70 (수주번호), 우선순위 → 공통코드 `PRIORITY` |
| `PRODUCT_LABEL` 제품표시 라벨 | `ProductLabel` | **65×80mm**, 여백 5mm | 폰트 4종, 항목명 열 22mm, **표시 항목 14개**(키·라벨), 바코드 CODE_128 300×60 |
| `WORK_DAILY` 작업일보 | `WorkDailySheet` | A4 가로, 여백 8 | 폰트 5종, **구역 8개**(로트·투입·분할·작업표준·작업조건·검사·불량·특기, 순서·표시), 투입표 컬럼 8개, 분할표 컬럼 4개, 조건 항목열 60, 라인검사 측정점 5, 빈 데이터 문구 |
| `PROGRESS_SHEET` 작업 진행 현황표 | `ProgressSheet` | A4 세로, 여백 20 | 폰트 3종, 컬럼 7개, 상태 → 공통코드 `WORK_STATUS` (구 "대기/진행중" 하드코딩) |
| `WORK_STANDARD` 작업표준서 | `WorkStandardSheet` | A4 가로, 여백 8 | 폰트 5종, **구역 5개**(품목정보·요구사항·설비/사이클·작업공정·작업표준), 라벨 폭 42, 관리항목 열 48 |

**렌더러 구현 규칙**

- 옵션이 없거나 잘못된 키는 렌더러 코드의 기본값으로 동작합니다. 옵션 스키마는 버전별로 검증합니다(JSON Schema).
- 관리자 화면에는 옵션 편집기와 **샘플 데이터 미리보기**가 필요합니다. 저장하면 새 버전이 만들어집니다.
- 옵션으로 바꿀 수 없는 것(구역 추가, 표 구조 변경)은 렌더러 개발 대상입니다. 이런 요구가 잦으면 해당 출력물을 EXCEL 방식으로 전환합니다.

고정 양식도 발행 이력(`print_log`)을 남기므로 재발행하면 같은 옵션 버전으로 출력됩니다. 수정이 필요 없는 다른 양식도 이후 FIXED 방식으로 추가할 수 있습니다. 렌더러 개발 + `print_template` 1행 등록이면 됩니다.

## 15.4 하드코딩 → 관리자 설정

기존 코드에서 운영 중 바뀔 수 있는 값이 코드에 고정된 곳을 찾았습니다. 신규 구현에서는 **코드 상수 금지**를 원칙으로 하고, 아래 네 곳 중 하나로 관리합니다.

| 관리 위치 | 대상 | 관리자 화면 |
|-|-|-|
| `system_setting` | 시각, 주기, 기본값, 세율, 경로, 번호 형식 등 단일 값 | 설정 화면 (분류별, 타입·범위 검증, 기본값 복원, 변경 이력 `audit_log`) |
| `common_code` (`is_system = 1`) | 로직이 참조하는 코드 목록과 **표시명** (판정, 상태, 부적합 처리, 검사항목 유형) | 공통코드 화면 — 코드는 고정, 표시명·순서·속성만 수정 |
| `print_template_version.layout_options_json` | 고정 양식 레이아웃 수치 | 양식 관리 화면 |
| `workstation_print_setting` | 단말별 프린터·부수 | 단말 관리 화면 |

**기존 하드코딩 → 신규 관리 위치 (초기값은 DDL에 등록됨)**

| # | 기존 하드코딩 | 위치 | 신규 |
|-|-|-|-|
| H1 | 작업일 시작 08:00 (`Hour < 8`, `AddHours(8)`, 화면 범위 08:00~익일 08:00) | `F_WorkPlanForm` 81·121·126·407·425행, `F_GraphicWindow` 79·106·111·157·177·508행, `WorkItemPanel` | `work_shift` 첫 교대 → 없으면 `schedule.day_start_time` |
| H2 | 현황 새로고침 60초 | `F_WorkPlanForm` 148행, `F_GraphicWindow` 133행 | `schedule.refresh_interval_sec` (웹은 SignalR + 보조 폴링) |
| H3 | 표준 없을 때 작업시간 8시간 | `F_WorkPlanForm` 2024행, `F_Option` 654행 | `schedule.default_running_time_min` |
| H4 | 임시 LOT 접두어 `P`, 형식 `P{yyMMdd}-{설비}-{D3}` | `WorkPlanFormHelper` 26행 | `schedule.temp_lot_prefix`, `lot.number_format` |
| H5 | 비가동 입력 기본 08:00~09:00 | `F_DowntimeInput` 253·254행 | `downtime.default_duration_min` + 교대 시작 |
| H6 | 부가세율 10% | `OutputSheet` 63행 | `sales.vat_rate` (+ `sales.amount_rounding`) |
| H7 | LibreOffice 경로 2곳 고정 탐색, 변환 제한 60초 | `InspectionPrintService` 239~297행 | `print.pdf_converter_path`, `print.pdf_convert_timeout_sec` (서버) |
| H8 | 파일 폴더 `BaseDirectory\Files\Inspection\HardnessChart`, `ExcelTemplate`, `Structure`, `CertForm` 등 (PC별) | `InspectionPrintService` 188행 등 | `file.storage_root` (서버) — 양식·도장은 DB |
| H9 | 판정 문자열 비교 `== "합격"` 5곳, `== "불합격"` 9곳 | 검사 화면 등 | 코드 `PASS`/`FAIL` 비교, 표시명은 공통코드 `DECISION` |
| H10 | 부적합 처리 항목 `"출하","재처리","선별","보류"` (콤보 고정), `reworkStep == "재처리"` 분기 | `F_DefectAdd.Designer` 232행, `DefectRepository` 246행 | 공통코드 `DEFECT_ACTION` |
| H11 | 검사항목 유형 ↔ 양식 prefix 매핑 (`"1"→T1`, `"0"→C1` 등) | `InspectionPrintService` 16~33행, `InspectionItemHelper.Map` | 공통코드 `INSPECTION_ITEM_TYPE.attr_json` |
| H12 | 작업 상태 표시명 배정/투입/완료 | 화면 코드 | 공통코드 `WORK_STATUS` (VIEW `vw_work_lot_status`가 참조) |
| H13 | 프린터 설정을 PC 로컬 텍스트 파일에 저장 | `PrinterInfo` 91·104·150행 | `workstation_print_setting` |
| H14 | 도장 이미지 PC 경로 | `F_CompanyForm` 13·83행, `OutputSheet` | `company.stamp_image` |
| H15 | 거래명세표 레이아웃 const 32개 | `OutputSheet` 14~71행 | FIXED 양식 옵션 (§15.3) |
| H16 | 로그 보관 7일 | `AppLogger` | `log.retention_days` |
| H17 | 우선순위 표시명·색상 (0 여유 회색, 1 일반, 2 우선 주황, 3 긴급 빨강) | `ProcessSheet` 174~189행 | 공통코드 `PRIORITY.attr_json.color` |
| H18 | 진행 상태 문자열 "대기", "진행중" | `ProgressSheet` 161~166행 | 공통코드 `WORK_STATUS` |
| H19 | 고정 양식 5종 레이아웃 const (폰트·행수·컬럼 폭·라벨·바코드 크기) | `PrintDoc/*.cs` | FIXED 양식 옵션 (§15.3.1) |

**구현 규칙**

- 설정은 앱 시작 시 한 번에 읽어 캐시합니다. 변경하면 서버가 캐시를 갱신하고 SignalR로 알립니다. `requires_restart = 1`인 항목만 재시작이 필요합니다.
- 설정값은 `value_type`과 `min_value`/`max_value`로 검증한 뒤 저장합니다. 잘못된 값이 들어오면 `default_value`로 동작하고 경고를 남깁니다.
- `is_system = 1`인 공통코드의 `code`는 수정·삭제할 수 없습니다. 표시명만 바꿀 수 있어서 로직은 깨지지 않습니다.
- **DB 접속 정보는 예외입니다.** 서버 설정 파일(환경변수·비밀 저장소)로 관리합니다. 구 `F_Option`의 클라이언트별 접속 문자열 저장 방식은 웹 전환 후 없어집니다.
- 코드 리뷰 체크리스트에 "시각·수치·경로·표시문자열 리터럴 금지"를 넣습니다.

---

# 16. 신규 구축 진행 방식

| 원칙 | 내용 |
|-|-|
| 기존 DB 보존 | `bbakggum`은 **삭제·ALTER하지 않습니다.** 신규 `bbakggum_v2`를 별도로 생성하고, 기존 DB는 읽기(이관·비교) 대상으로만 사용합니다 |
| 기존 프로젝트 참조 | 업무 규칙이 불분명하면 `D:\Programming\ProductManager` 소스를 열어 확인합니다. 확인 결과는 이 문서 §9(매핑)·§12(확인사항)·§15(주의사항)에 근거와 위치를 남깁니다 |
| 이관 | 구 → 신 단방향 스크립트, `migration_id_map`으로 추적, 재실행 가능. 검증(§10)을 통과한 뒤 전환 |
| 병행 운영 | 전환 전까지 WinForms + `bbakggum`이 운영 기준. 모듈 단위로 전환 |
| 우선 구현 | 난이도가 높은 **스케줄/진행현황**과 **엑셀 양식 출력 엔진**을 초기에 프로토타입으로 검증 (구 SP 결과 비교, 기존 성적서 양식으로 발행 비교) |

---

# 17. 1단계 기존 폼 분석 반영 (V3.8)

분석 문서: `docs/legacy_forms/README.md` (폼 50개). 이번 버전에서 확정한 규칙입니다.

| 영역 | 확정 규칙 | 근거 |
|-|-|-|
| 수주(입고) | 입고번호(`I{yyMMdd}-{NNN}`)가 **품목 행 번호**이며 현장에서 스캔하는 "수주번호" → `sales_order_item.order_item_no` UNIQUE. 요구사항(요구경도·심부경도·경화층·조직) Snapshot, 행 단위 우선순위, 고객 작업지시번호, 별도관리 | F_IncomeAddForm |
| 단가 | 품목 **단가 적용 구분** EA / KG / CHARGE (구 `ea`/`kg`/`ch`). 출하 금액 = EA: 수량×단가, KG: 중량×단가, CHARGE: charge수×단가 (구 코드는 항상 수량×단가 — 오류) | F_PartDetailForm, B4 |
| 투입 | **주 LOT 생성 전**(전공정·주공정): 수주번호 기준, LOT당 같은 수주 1회. **주 LOT 생성 후**(후공정): 주 LOT번호 기준, LOT당 **같은 주 LOT의 같은 수주** 1회 — 주 LOT이 다르면 같은 수주를 한 후공정 LOT에 함께 투입 가능 (예: 수주 ①의 B01분 + B02분을 한 템퍼링 LOT에). DB: `UNIQUE(production_work_id, sales_order_item_id, IFNULL(main_work_id,0))`. **설비당 투입(진행) 중 작업 1건** | F_GasForm, B6 (2026-09-30 보완) |
| 불량 수량 | 작업 화면에서 투입 행별 불량수량 입력(배출 = 투입 − 불량). 검사 불합격 확정 시에는 대상 투입수량 전체를 부적합으로 등록 — 별도 입력 없음 | F_GasForm, F_InspectionAddForm, B7 |
| 작업표준 | 관리항목(행)도 설비·단위공정 템플릿에 속함 → `step_template_condition`. 단위공정은 **기준정보에서만 등록** | F_WorkStandardAddForm, B5 |
| 검사 | 검사구분 입고/공정/출하 (`TI`/`TP`/`TO`). LOT 입력 → 투입 품목 중 대상 선택 → **측정·판정은 검사 1회 공통**(`inspection_item` → `inspection`), **성적서는 대상 품목별** 발행. 후공정을 여러 주 LOT과 합쳐 진행해도 **검사는 주 LOT 단위로 분리** 관리. 확정 후 수정 = 재검사(`reinspection_of_id`). 구 `isinspectiondone` = 라인검사 완료 | F_InspectionAddForm, F_GasForm, B1 |
| 부적합 처리 | 처리구분 재처리 / 출하 / 선별 / 보류 (+폐기, 반송) | F_DefectAdd |
| 출하 | 시험편 수량(잔량 차감, 금액 제외), 거래처 사업자정보 Snapshot, 마감일, 품목 합산 출력 | F_OutAddForm |
| 마감 | 전표 단위 상태 **미마감 / 마감완료 / 이월** + 귀속 마감월, 업체별 진행, 자동 이월 | F_MonthlyClosing |
| 스케줄 | 병합 시 최대 작업시간, 지연 시 뒤 배정 계획시각 자동 이동, 작업시간 결정 5단계 | F_WorkPlanForm, B2·B3 |
| 권한 | 로그인 사용자 × **부서(역할) 복수 선택** — 공용 PC에서 생산+영업처럼 여러 부서를 가진 사용자는 메뉴 합집합. 권한 없으면 차단(fail-closed) | F_Main, B8 |
| 첨부 | 품목 도면·이미지, 조직사진, 경화층 차트 → `attachment` (PC 로컬 폴더 폐지) | F_PartDetailForm, F_InspectionAddForm |
