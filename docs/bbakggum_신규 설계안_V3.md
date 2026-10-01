# bbakggum DB 구조개편 설계안 V3.30

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
| V3.30 | 2026-10-01 | **10단계 ① 발행 공통·수주 출력** (§29.1): 업무 화면 발행 API(화면 읽기 권한·여러 건 한 파일·양식 선택), 공용 출력 버튼, 공정이동표·제품표시 라벨 렌더러(구 ProcessSheet·ProductLabel), 바코드 SVG, 데이터 공급원 SALES_ORDER(입고 행). DDL: SALES_ORDER 치환자 사전 |
| V3.29 | 2026-10-01 | **9단계 ③ 작업자 주·야 배치 보드** (§28.5): 설비 × 교대 칸에 작업자 끌어다 놓기(태블릿 누르기), 이동·주/보조·해제, 전날 배치 복사, SignalR 실시간 반영. 메뉴 `equipment.worker_assignment`. **9단계 완료** |
| V3.28 | 2026-10-01 | **9단계 ② 설비 보전·측정기구 교정** (§28.3~28.4, 구 F_MaintenanceForm): 보전 목록·점검 예정(지남·임박)·사진 첨부, 측정기구별 교정 상태·이력·성적서 파일(교정 저장 시 최근·다음 교정일 반영). 공통 첨부 `AttachmentStore`·`AttachmentList` 로 정리. DDL: `maintenance` 부위·업체·비용·다음 점검일·row_version, `instrument_calibration`, 공통코드 `MAINTENANCE_TYPE`·`MAINTENANCE_STATUS`·첨부 2종, 설정 `maintenance.due_soon_days`·`instrument.calibration_due_soon_days`, 메뉴 `equipment.maintenance`·`quality.calibration`. 이관: 구 측정기구 기록 → 교정 이력 |
| V3.27 | 2026-10-01 | **9단계 ① 비가동·공정검사 항목** (§28.1~28.2, 구 F_DowntimeInput·F_DowntimeStatus): 입력·현황 한 화면(설비별·사유별 합계, 진행 중 [종료]), 계획 비가동 저장 시 스케줄 재계산, 공정검사 항목 기준정보. DDL: 메뉴 `equipment.downtime`·`master.unit_inspection_item` |
| V3.26 | 2026-10-01 | **구매관리 설계** (§27: 구매처·구매 품목·구매(발주·입고)·현황 출력, 13단계), "준비 중" 화면 정리·단계 조정 (§26.4~26.5, 운영 전환 = 14단계). §12 ⑤ 거래명세표 양식 선택 콤보 구현, ⑩ 일괄 전환·⑪ 주공정 추정 안 함·⑫ 거래처 없는 품목 이관 안 함·⑬ 비매출처 = 매입처 확정 (이관 스크립트 반영) |
| V3.25 | 2026-10-01 | **미구현 기능 정리·추가 계획** (§26): 구 폼 대비 미구현 목록, 구현 개념 결정 A~I (비가동 한 화면, 보전·측정기구 분리, 작업자 끌어다 놓기 배치, 라인검사, 경화깊이 그래프, SPC, 공정 확인 서명, 구 폼별 출력물, 모니터링 로테이션), 9~13단계. §12 ④ Community 유지·⑤ 업체 전용 양식 없음 |
| V3.24 | 2026-10-01 | **8단계 구 DB 이관·검증·병행운영** (§25): 구 덤프를 `bbakggum_legacy` 로 복원(운영 접속 없음), 이관 스크립트 `db/migration` (설정 → 기준정보 → 공정·표준 → 수주 → 작업 → 품질 → 출하 → 검증, `migration_id_map` 재실행 가능), 구 문자열 → 공통코드 변환, 건수·합계 대조 검증 + 문제 목록, 시험 데이터 `db/test/migration_fixture.sql`, 병행운영(구 → 신규 단방향)·일괄 전환 절차. §12 #1~3·⑧⑨ 데이터로 확인. DDL: 공통코드 그룹 `DOWNTIME_REASON`. **8단계 완료** |
| V3.23 | 2026-10-01 | **7단계 ③ 대시보드 KPI** (§24.3, 구 F_DashForm): 영업(오늘·이번 달 입고·출하 금액, 미투입, 미마감), 설비 가동(진행 LOT 진행률·지연, 다음 배정, 비가동), 품질(미처리 부적합 상태별, 오늘 검사), 일별 추이(입고·출하 금액 / 부적합 건수 — 단위별 별도 차트, 표 보기). 패널·계열은 업무 메뉴 읽기 권한대로. 설정 `dashboard.trend_days`. **7단계 완료** |
| V3.22 | 2026-10-01 | **7단계 ② 수주 진행·재고** (§24.2, 구 F_OrderStatus·F_InventoryForm·F_OutcomeStatus): 입고 행별 경로 순 단위공정 투입/양품, 미투입·미처리 부적합·재고(출하 가능)·출하·출하 잔량·금액, 보기(미출하·재고 있음·미투입·전체). 메뉴 `report.order` |
| V3.21 | 2026-10-01 | **7단계 ① LOT 현황·추적** (§24.1, 구 F_WorkHistoryForm·F_ProductionStatus): LOT 현황(vw_work_lot_status + 수주·품목·거래처 요약), 추적 = 어떤 번호(LOT·제출 LOT·입고번호)든 주 LOT 으로 바꿔 전공정·후공정·재작업·검사·부적합·출하를 모음. 메뉴 `report.lot` |
| V3.20 | 2026-10-01 | **6단계 ⑥ 출하·마감** (§23.7, 구 F_OutAddForm·F_OutForm·F_MonthlyClosing): 출하 재고 = 수주 × 주 LOT 출하 가능(부적합·특채·기출하·시험편 반영), 전표 등록·수정·취소, 단가 구분별 금액(EA/KG/CHARGE)·세액, 거래처 Snapshot, 거래명세표 발행(출하 화면 권한), 업체별 마감(기준일 = 마감일 말일 보정)·이월·마감 취소. 설정 `shipment_closing.number_format`, 공통코드 `CLOSING_STATUS` 색·`CLOSING_RUN_STATUS`, 메뉴 `sales.shipment`·`sales.closing`. **6단계 완료** |
| V3.19 | 2026-10-01 | **6단계 ⑤ 부적합·재작업** (§23.6, 구 F_Defect·F_DefectAdd): 작업 화면 투입 행 불량 등록(양품 한도), 부적합 목록·판정(처리구분 `DEFECT_ACTION`)·완료·취소, 재처리 → 재작업 LOT(원 LOT 연결, 주공정이면 주 LOT = 자신) → 재작업 LOT 완료 시 부적합 자동 완료. DDL: `defect_occurrence.remark`, 공통코드 `DEFECT_STATUS`, 메뉴 `quality.defect` |
| V3.18 | 2026-10-01 | **6단계 ④ 검사·성적서** (§23.5, 구 F_InspectionAddForm): LOT 입력 → 투입 행 중 대상 선택, 측정·판정 검사 공통(기준 범위 자동 판정 + 수동), 저장(미확정)·확정(불합격 → 대상마다 부적합 = 대상 수량)·재검사(새 번호)·취소, 대상별 성적서 발행(검사 화면 권한 — §12 ⑦ 해결). 공통코드 `INSPECTION_STATUS`, `DECISION` 색상·`NA`, 메뉴 `quality.inspection` |
| V3.17 | 2026-10-01 | **6단계 ③ 투입·작업** (§23.4, 구 F_GasForm): 작업 화면(설비별 배정·진행 LOT), 즉시 작업, 입고번호/주 LOT 스캔 투입(주공정 전·주공정·주공정 후 단계, 잔량 = 수주 − 기투입 / 주 LOT 양품 − 기투입, 초과 차단), 투입(시작)·완료(설정 단위 내림), 표준 확정 → 조건 복사·수정. DDL: `production_work_condition.item_sequence_no`, 메뉴 `production.work` |
| V3.16 | 2026-10-01 | **6단계 ② 계획 확정·작업지시** (§23.3): 확정/해제, 작업지시(RELEASE) = 작업 LOT 배정 생성(LOT번호 `lot.number_format`, 앞 계획까지 일괄), 작업지시 취소(투입 전만, LOT 번호 재사용 안 함), 보드에 작업 LOT 표시 |
| V3.15 | 2026-10-01 | **6단계 ① 수주(입고)** (§23): 묶음 1건 + 행마다 입고번호, 품목 스펙 Snapshot, 거래처 품목 후보, 계획·투입·출하 수량 이하로 수량 축소·공정 변경·취소 금지. 번호 부여 공용 `DocumentNumbers`(이름 잠금). 설정 `sales_order.number_format`·`sales_order.list_default_days`, 공통코드 `ORDER_STATUS`, 메뉴 `sales.order` |
| V3.14 | 2026-10-01 | **작업표준 입력표 가변식** (§2.1·§22.5, 구 `F_WorkStandardAddForm` 비교): 스텝(열)·관리항목(행)을 작업표준 Version 마다 직접 보관(`standard_version_step`·`standard_version_item`), 조건 = (스텝 순서, 항목). 단계 템플릿은 입력표 **초기값**(불러오기·"이 구성을 템플릿으로 저장"). `production_work_condition` 단계 키 = 스텝 순서(템플릿 id 참조 제거). 입력표에서 조건 항목 즉석 등록. 추가·수정 입력은 가운데 별도 창, 좌측 메뉴 단독 스크롤 |
| V3.13 | 2026-09-30 | **5단계 기준정보** (§22, §12 확인 ⑧⑨): 단순 기준정보 14종 = 정의 기반 범용 API·화면(정의 ↔ DDL 대조 테스트), 사용자·역할 권한 관리, 품목(거래처 품번·공정·도면/이미지 첨부·성적서 양식 연결·이력), 공정 경로·단계 템플릿·작업표준(행렬·버전·복사), 검사기준(버전·항목·측정 위치). DDL: 메뉴 master.* 19개, 공통코드 `CUSTOMER_TYPE`·`DAY_TYPE`·`CONDITION_VALUE_TYPE`·`ATTACHMENT_KIND`·`RANGE_TYPE`, 설정 `file.max_attachment_mb`·`standard.code_format` |
| V3.12 | 2026-09-30 | **4단계 ② 출력 엔진** (§21, §12 확인 4건): EXCEL(치환·반복행·이미지·구 좌표 키 호환 → 서버 LibreOffice PDF) / FIXED(거래명세표 렌더러 + 옵션), 양식 선택 단일 함수, 발행 이력·재발행, 양식 관리 화면. DDL: `print_template.is_default`(용도 기본 양식), `shipment.supply_amount`·`vat_amount`·`total_amount`(F2), 치환자 사전 초기 데이터(검사 대상·출하 전표), 메뉴 `system.print`, 고정 양식 글꼴 = 설치 이름 목록("굴림체", "맑은 고딕"), 거래명세표 여백 20 |
| V3.11 | 2026-09-30 | **4단계 ① 스케줄 서비스 + Gantt** (§20, §7 규칙 구체화, §12 확인 3건): 계산 엔진·5단계 작업시간·설비 잠금·지연 반영 재계산·구 SP 실제 실행 비교. DDL: `production_schedule.duration_source`, 공통코드 `SCHEDULE_STATUS`·`RUNNING_TIME_SOURCE`, 설정 `schedule.board_days`, 메뉴 `production.schedule` |
| V3.10 | 2026-09-30 | **3단계 웹 골격** (§19): React + Vite + Ant Design, 쿠키 세션·권한 메뉴(DB 메뉴 트리)·SignalR 캐시 무효화·대시보드 틀·시스템 화면 3종(관리자 설정·공통코드·변경 이력). API: 클라이언트 설정 조회 `GET /api/client-settings`, 운영 시 웹 정적 파일 제공(같은 출처) |
| V3.9 | 2026-09-30 | **2단계 API 골격** (§18): 쿠키 인증·역할 합집합 권한(fail-closed)·감사·설정/공통코드 캐시·row_version·SignalR. DDL: 권한 초기 데이터(ADMIN 역할, 시스템 메뉴 트리 — §9.8), 설정 `auth.permission_cache_sec`·`auth.login_max_attempts_per_min`·`auth.password_min_length` 추가 |
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
                   └ standard_version        charge 수량, 표준 작업시간, 불러온 템플릿(참고)
                       ├ standard_version_step  스텝(열) 이름·순서 — Version 마다 자유   ← t_standarddetail "스텝" 행
                       ├ standard_version_item  관리항목(행)·순서                         ← t_standarddetail.item
                       └ standard_condition     항목 × [공통 + 스텝 순서] = 값            ← t_standarddetail step1~15

[확정 조건]      production_work (작업 LOT) — standard_version_id, is_standard_fixed
                   └ production_work_condition  항목 × 단계 = 설정값/실적값  ← t_conditiontemplate + t_workconditiondetail
```

- 조건은 고정 컬럼이 아니라 **항목 × 단계 행렬**로 저장한다. 설비 유형마다 항목이 달라도(가스로 CP, 진공로 압력 등) 테이블을 바꿀 필요가 없다.
- 단계와 무관한 LOT 공통 조건(예: 장입량)은 단계를 NULL로 둔다.
- **입력표는 가변식** (구 `F_WorkStandardAddForm`): 스텝·관리항목을 작업표준 화면에서 바로 추가·이름 변경·삭제·이동한다. 단계 템플릿은 표를 처음 채우는 초기값일 뿐이라 템플릿을 고쳐도 기존 표준·LOT 조건은 바뀌지 않는다 (§22.5).

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
| 단위 | 설비별 체인 (작업일, `sequence_no`) 순. 재계산 후 작업일마다 시작 순으로 순번 재부여 (작업지시 블록 포함) |
| 시작시각 | 이전 블록 종료. 체인 시작점 = max(현재, 진행 중 작업의 `actual_start_at` + 예상시간, 작업지시됐지만 투입 전인 블록의 계획 종료). 빈 시간은 당겨 붙인다 (구 C# 재계산과 동일 — §12 4단계 확인 ②) |
| 자정 | **연속 배치.** 구 SP의 "자정 넘으면 다음날 08:00으로 점프"는 폐기 (쉬는 시간은 휴일·비가동으로 표현) — 4단계 비교 테스트로 차이 확인 |
| 소요시간 | 블록에 담긴 품목별 작업시간 중 **최대값** (병합 포함, 수량 비례 아님 — 2026-09-30 확정). 품목별 작업시간 결정 순서: ① 작업표준 `running_time_min` → ② 같은 품목·설비유형의 직전 작업 시간 → ③ `process_default_time` → ④ 사용자 입력 후 기준시간 등록 → ⑤ 설정 `schedule.default_running_time_min` |
| 작업일 경계 | 첫 교대 시작시각 (구 코드 `startTime.Hour < 8`이면 전일 → `work_shift`로 설정화) |
| 제외 | `work_calendar` 휴일 작업일에는 **시작하지 않음** (시작한 블록은 휴일로 넘어가도 끊지 않음 — §12 4단계 확인 ①), `equipment_downtime.is_planned` 구간과는 **겹치지 않음** (비가동 종료 뒤로) |
| 고정 | `is_time_locked` (신규 기능) — 시각을 바꾸지 않고, 다른 블록이 겹치지 않게 비켜 가는 장애물. 고정 블록은 이동 불가 |
| 대상 | PLANNED, CONFIRMED만 (RELEASED 이후 불변) |
| 지연 반영 | 작업이 계획보다 늦게 끝나거나 늦게 진행 중이면 **같은 설비의 뒤 배정 계획시각을 실제로 뒤로 이동해 저장**하고 화면을 갱신 (고정 블록 제외) — 2026-09-30 확정 (구: 화면 표시만 이동) |
| 동시성 | 설비 행 `SELECT … FOR UPDATE` (여러 설비는 id 순으로 잠금) + `row_version`. 재계산이 바꾸는 시각·순번·임시 LOT은 파생값이라 `row_version`을 올리지 않음 (지연 반영이 사용자 편집을 409로 만들지 않게) |

**구 로직 비교 결과 (4단계, 구 SP를 비교 전용 DB에서 실제 실행)** — 테스트 `api/tests/…/Scheduling/LegacyScheduleComparisonTests.cs`

| 경우 | 결과 |
|-|-|
| 같은 작업일 안 연속 배치, 실적 종료 뒤 시작 | 신규 = 구 SP = 구 C# 재계산 |
| 자정을 넘는 블록 | 신규 = 구 C#(연속). 구 SP는 다음날 08:00으로 점프 → **폐기** |
| 소수 시간 (1.5h) | 신규 90분 정확. 구 SP는 `INTERVAL 1.5 HOUR`가 반올림되어 **2h로 계산되는 결함** |
| 임시 LOT번호 | 설정 초기값(`P` + `{yyMMdd}-{EQUIP}-{SEQ:000}`)으로 구 C#과 동일 |

---

# 8. 테이블 카탈로그 (79 테이블 + 5 VIEW)

| 영역 | 테이블 |
|-|-|
| 시스템/공통 (14) | company, common_code_group, common_code, system_setting, department, job_position, employee, app_user, role, app_user_role, menu, role_menu, audit_log, migration_id_map |
| 기준정보 (11) | customer, equipment_type, equipment, equipment_history, instrument, part, part_customer, part_history, defect_reason, work_shift, work_calendar |
| 공정/표준/검사기준 (20) | unit_process, process_default_time, heat_process, heat_process_version, heat_process_operation, step_template, step_template_item, condition_item, standard, standard_version, standard_version_step, standard_version_item, standard_condition, inspection_standard, inspection_standard_version, inspection_criteria, inspection_criteria_point, unit_inspection_item, part_heat_process, step_template_condition |
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
| 36 | t_standarddetail | standard_version_step, standard_version_item, standard_condition | 재설계 | "스텝" 행 → step, item 행 → item, step1~15 값 → condition (step_no = 열 번호) |
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
- **구현 (8단계, §25):** 스크립트 `db/migration`, 매핑 규칙 §25.3, 검증 §25.4, 병행운영·전환 절차 §25.7 (구 → 신규 단방향 — 일괄 전환 권장, §12 ⑩).

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

**남은 확인 사항** (운영 DB 조회 필요 — #1~3 은 V3.24 에서 2026-04-09 덤프로 확인, 결과 §25.2. 최신 덤프로 이관할 때 검증이 다시 확인)

| # | 항목 | 방법 |
|-|-|-|
| 1 | 구 `t_standarddetail.item`, `t_workconditiondetail.item` 문자열 종류 (condition_item 정규화 대상) | `SELECT item, COUNT(*) … GROUP BY item` |
| 2 | 폐기 후보 4개 테이블의 운영 데이터 유무 | 테이블별 `COUNT(*)` |
| 3 | 구 `t_inspection`에서 같은 inspectionno에 subno가 여러 개인 실제 데이터 형태 | inspectionno별 subno 분포 |
| 4 | 고정 양식 옵션 편집 범위 (관리자에게 열어줄 키 / 개발자만 수정할 키 구분) | 관리자 화면 설계 시 |
| 5 | 고정 양식 중 업체별로 엑셀 양식이 필요한 출력물이 있는지 (예: 특정 업체 전용 거래명세표) | **V3.25 해결: 없음** (§12 ⑤) |

**4단계 스케줄 — 구현 기준으로 정했고 확인이 필요한 규칙** (§7)

| # | 항목 | 현재 구현 | 대안 |
|-|-|-|-|
| ① | 휴일로 넘어가는 블록 | 휴일 작업일에는 시작 안 함. 전날 시작한 사이클은 휴일로 넘어가도 계속 | 휴일 전에 끝나지 않으면 휴일 뒤로 미룸 |
| ② | 미래 계획의 빈 시간 | 체인을 당겨 붙임 (다음 주 계획도 오늘로) — 특정 시각은 "시각 고정"으로 | 계획 시작시각을 하한으로 존중 (당기지 않음) |
| ③ | 다른 설비유형으로 이동 | 작업시간 유지 | 이동한 설비유형 기준으로 5단계 재결정 |

**4단계 출력 — 확인이 필요한 사항** (§21)

| # | 항목 | 현재 구현 | 확인 |
|-|-|-|-|
| ④ | QuestPDF 라이선스 | 구 WinForms와 같은 Community (설정 `Print:QuestPdfLicense`) | **V3.25 결정: 구 프로젝트와 같은 Community 유지** (구 `F_IncomeAddForm.cs` 829·905행 `LicenseType.Community`). 조건(연 매출 100만 달러 미만)은 구·신규 동일하게 적용 — 바뀌면 설정만 Professional 로 |
| ⑤ | 업체 전용 거래명세표(EXCEL) | `part_print_template`는 품목 기준이라 전표(여러 품목)에는 연결 불가 → 용도 기본 양식 또는 발행 시 양식 지정 | **V3.25 결정: 업체 전용 양식 없음** — 당사 거래명세표 1종(FIXED). 다른 양식이 필요하면 엑셀 양식 등록 → 용도 기본 양식·발행 시 선택으로 사용 (테이블 추가 안 함, §12 #5 해결). **V3.26: 출하 화면 거래명세표 버튼 옆 양식 선택 콤보** — 기본 양식이 먼저 선택되고, 업체 전용 양식으로 바꿔 출력 (`GET /api/shipments/slip-templates`, `POST /api/shipments/{id}/slip?printTemplateId=`) |
| ⑥ | 거래명세표 행별 세액 | 금액 × 세율 반올림(표시용), 합계 세액은 전표 저장값 — 행 세액 합과 1원 단위로 다를 수 있음 (구 동일) | 행 세액 표시를 유지할지 |
| ⑦ | 발행 권한 | **V3.18: 성적서는 검사 화면 권한(`quality.inspection` R) `POST /api/inspections/targets/{id}/report`** — `system.print` 는 양식 관리용 | V3.20: 거래명세표도 출하 화면 권한(`sales.shipment` R) `POST /api/shipments/{id}/slip` — 해결 |

**5단계 기준정보 — 확인이 필요한 사항** (§22)

| # | 항목 | 현재 | 확인 |
|-|-|-|-|
| ⑧ | 구 품목의 **공정 16단계별 단가**(`SubPrice1~16`)·단계명(`Subp1~16`) | 구 코드는 저장·표시만 하고 금액 계산에 쓰지 않음 (`F_PartDetailForm` 320~343행, `PartRepository`). 신규 DDL에 없음 | 실제로 쓰는 값인지. 쓰면 `part_process_price`(품목 × 단위공정 × 단가) 추가 후 이관 — **V3.24: 덤프는 전부 0·빈 값 → 추가 안 함** (이관 검증이 다시 확인) |
| ⑨ | 구 품목 **양산 상태**(`MassStatus` — 양산/개발 등) | 구 코드는 표시만. 신규 DDL에 없음 | 필요하면 공통코드 + `part.mass_status` 추가 — **V3.24: 2026-04-09 덤프는 전부 "양산" → 추가 안 함** (이관 검증이 다시 확인) |

**8단계 이관 — 확인이 필요한 사항** (§25)

| # | 항목 | 현재 구현 | 확인 |
|-|-|-|-|
| ⑩ | 전환 방식 | **V3.26 결정: 일괄 전환.** 구 시스템은 지금 사용하지 않음 → 병행운영 없이 최종 덤프 → `migrate -Fresh` → 신규로 시작 (§25.7) | 해결 |
| ⑪ | 주공정 판정 (구 데이터) | **V3.26 결정: 주공정은 신규에서 도입한 개념 — 구 데이터에서 추정하지 않음.** 설정 `main_unit_processes` 기본 빈 값 = 이관 LOT 은 모두 주공정 아님 (주 LOT 추적은 전환 후 공정 경로 등록부터) | 해결 |
| ⑫ | 거래처 없는 품목 3,132건 (2026-04-09 덤프) | **V3.26 결정: 이관 안 함** (삭제된 거래처의 품목 — 필요하면 신규에서 등록). 이름으로 연결하지 않음. 문제 목록 `SKIPPED` | 해결 |
| ⑬ | 비매출처 11건 | **V3.26 확인: 매입처 맞음** → PURCHASE. 구매관리(§27)의 구매처로 사용 | 해결 |


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
| `SALES_SLIP` 거래명세표 | `OutputSheet` (+Com, Multi) | A4 세로, 여백 20 (구 `page.Margin(20)`) | 폰트 "굴림체"(설치 이름, 대체 목록)·영역별 12종, 페이지당 품목 6행, **보관용 2부** (문구·테두리색), 여백·절취선 간격 5종, 행 높이 4종, 선 두께, 배경색, **도장**(표시·크기 50·X -250·Y 65/480), 여러 전표 한 PDF 병합 |
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


---

# 18. API 골격 (2단계, V3.9)

위치: `api/` — `JiMes.slnx`, `src/JiMes.Api` (ASP.NET Core 10 Minimal API + Dapper + MySqlConnector), `tests/JiMes.Api.Tests` (xUnit, 테스트 DB `bbakggum_v2_test`를 DDL에서 매번 새로 생성).

## 18.1 구조

| 폴더 | 내용 |
|-|-|
| `Infrastructure/Data` | 연결 팩토리(`ConnectionStrings:Main`), Dapper snake_case 매핑, `RowVersion.ExecuteVersionedUpdateAsync` |
| `Infrastructure/Security` | 쿠키 인증, `PermissionService`(역할 합집합·캐시), `RequirePermission`/`RequireLogin`, 비밀번호(PBKDF2), 최초 관리자 생성 |
| `Infrastructure/Audit` | `AuditWriter` — 변경과 **같은 트랜잭션**에서 `audit_log` 기록, before/after는 snake_case JSON |
| `Infrastructure/Settings`·`Codes` | `system_setting`·`common_code` 메모리 캐시 (시작 시 적재, 변경 API가 갱신) |
| `Infrastructure/Realtime` | SignalR `/hubs/events` (서버 → 웹 알림 전용) |
| `Infrastructure/Errors` | 업무 예외 → ProblemDetails(`code` 포함). 404 `NOT_FOUND`, 409 `CONCURRENCY_CONFLICT`, 422 업무 규칙, 400 `VALIDATION` |
| `Features/*` | 기능별 엔드포인트 (Auth, Settings, CommonCodes, AuditLogs, Health) |

## 18.2 인증

- 쿠키 인증 (`jimes.auth`, HttpOnly, **SameSite=Strict** — CSRF 방지). 웹은 Vite 프록시/같은 출처로 `/api`, `/hubs`를 호출한다.
- 세션 쿠키(브라우저 종료 시 삭제) + 서버 측 만료 `auth.session_timeout_min`, 요청이 있으면 연장.
- 비활성·삭제 계정은 기존 쿠키도 거부 (권한 캐시 주기 `auth.permission_cache_sec` 안에 반영, API로 바꾸면 즉시).
- 로그인 실패는 계정 없음·비밀번호 오류·비활성을 구분하지 않는다 (`INVALID_CREDENTIALS`). IP당 분당 시도 제한 `auth.login_max_attempts_per_min`.
- **최초 관리자:** `app_user`가 비어 있을 때만 설정 `Bootstrap:AdminLoginId/AdminUserName/AdminRoleCode` + 환경변수 `Bootstrap__AdminPassword`로 생성 (DDL에 계정·비밀번호를 넣지 않음). 개발용 값은 `launchSettings.json`.

## 18.3 권한 (fail-closed)

- API 권한 키 = `menu.menu_key`. 유효 권한 = 사용자의 **활성 역할들의 `role_menu` 합집합** (B8 공용 PC 복수 부서).
- 모든 `/api`·`/hubs` 엔드포인트는 `RequirePermission(메뉴키, Read|Create|Update|Delete)` / `RequireLogin()` / `AllowAnonymous()` 중 하나를 **반드시 선언**. 선언이 없어도 기본 정책(로그인 필수)으로 막히고, 누락은 테스트가 잡는다.
- 관리자 = `ADMIN` 역할이 DDL에서 **모든 메뉴 전체 권한**을 받는다 (역할 코드 우회 로직 없음). 메뉴는 화면 단계마다 DDL §9.8에 추가하고, 코드 상수 `MenuKeys`와 DDL 불일치는 테스트가 잡는다.
- `GET /api/auth/me`: 사용자·역할 + **읽기 권한 있는 메뉴와 그 상위만** 담은 트리 + 메뉴별 권한 (웹 사이드바·버튼 표시용).

## 18.4 설정·공통코드 캐시 (§15.4 구현)

- 시작 시 전체 적재. 저장값이 형식·범위를 벗어나면 `default_value`로 동작하고 경고 로그 (`isFallback` 표시).
- 변경: 행 잠금(`FOR UPDATE`) → 검증·정규화 → 저장 → `audit_log` → 커밋 → 캐시 갱신 → SignalR `settingChanged {key}` / `commonCodeChanged {groupCode, code}`.
- `is_system` 공통코드는 표시명·순서·속성·비고만 수정 (사용 여부 변경 시 422 `SYSTEM_CODE_LOCKED`). 조회는 로그인 사용자 전체.
- 코드에서 읽는 설정 키는 `SettingKeys` 상수로만 — DDL에 없으면 테스트 실패.

## 18.5 row_version

- 읽기 응답에 `rowVersion`을 담고, 수정 요청이 그대로 돌려보낸다.
- UPDATE는 `SET …, row_version = row_version + 1 WHERE {table}_id = @Id AND row_version = @RowVersion` 형식으로 `ExecuteVersionedUpdateAsync`에 넘긴다 (형식이 아니면 거부). 0행이면 대상이 없으면 404, 있으면 409.

## 18.6 API 목록 (2단계)

| 메서드·경로 | 권한 |
|-|-|
| `GET /api/health` | 익명 |
| `POST /api/auth/login` · `POST /api/auth/logout` · `GET /api/auth/me` · `POST /api/auth/change-password` | 익명(로그인) / 로그인 |
| `GET /api/settings` · `PUT /api/settings/{key}` · `POST /api/settings/{key}/reset` | `system.setting` R / U / U |
| `GET /api/common-codes` · `PUT /api/common-codes/{id}` | 로그인 / `system.code` U |
| `GET /api/audit-logs?tableName&recordId&appUserId&from&to&page&pageSize` | `system.audit` R |
| `/hubs/events` (SignalR) | 로그인 |
| `GET /api/client-settings` | 로그인 (`SettingKeys.ClientVisible` 키만 — 경로·세율 등 관리 정보 제외) |
| `/openapi/v1.json` | 개발 환경만 |

## 18.7 남은 일 (다음 단계에서)

- 사용자·역할·메뉴 권한 관리 API (`system.user`, `system.role`) — 변경 시 `PermissionService.InvalidateAll()` 호출.
- 운영 배포: Data Protection 키 저장 위치 지정(서버 재시작·다중 인스턴스 시 쿠키 유지), HTTPS, 파일 로그(`log.retention_days`).

---

# 19. 웹 골격 (3단계, V3.10)

위치: `web/` — React 19 + TypeScript + Vite + **Ant Design 6**, react-router 8, TanStack Query, `@microsoft/signalr`, dayjs.

## 19.1 실행 구성

| 환경 | 방식 |
|-|-|
| 개발 | `npm run dev` (5173). Vite가 `/api`, `/hubs`(WebSocket)를 API(5080)로 프록시 → 같은 출처라 쿠키(SameSite=Strict) 그대로 동작 |
| 운영 | `npm run build` → `api/src/JiMes.Api/wwwroot` (Git 제외). API가 정적 파일 + SPA 경로(`/api`·`/hubs` 제외 → `index.html`)를 제공. 별도 웹 서버·CORS 없음 |

## 19.2 구조

| 폴더 | 내용 |
|-|-|
| `api/client.ts` | `fetch` 공통. ProblemDetails → `ApiError(status, code, message, errors)`. 401이면 세션 만료 처리(로그인 요청 제외). `fieldErrors<폼>()`로 서버 검증 오류를 폼 필드에 표시 |
| `auth/` | `AuthProvider` = `GET /api/auth/me` 결과가 로그인 상태. `RequireAuth`(비로그인 → `/login`, 돌아올 경로 보존), `useCan(메뉴키, 동작)` 버튼 표시용 |
| `realtime/` | 로그인 중에만 `/hubs/events` 연결. `settingChanged` → 설정 캐시, `commonCodeChanged` → 공통코드 캐시 무효화, 재연결 시 전체 재조회. 끊겨도 화면은 주기 조회로 동작 |
| `hooks/` | `useCommonCodes()` — 코드값 → 표시명(`name`), 선택 목록(`options`, 사용 중만), 속성(`attr`). `useClientSettings()` — 작업일 시작 시각·새로고침 주기 |
| `layout/` | 사이드바 = 서버가 준 메뉴 트리(읽기 권한 메뉴만), 헤더 = 경로·실시간 상태·사용자 메뉴(비밀번호 변경·로그아웃) |
| `pages/registry.tsx` | `menu_key` → 화면. **경로는 DB `menu.route`** — 권한 없는 화면은 라우트가 생기지 않아 404. 등록 안 된 메뉴는 "준비 중" |
| `utils/workDate.ts` | 작업일 = 시작 시각(설정) 이전이면 전날 (§15.4 H1) |

## 19.3 규칙

- 새 화면: DDL §9.8 메뉴 추가 → `pages/registry.tsx` 등록 → 버튼은 `useCan`으로 표시(최종 차단은 서버 403).
- 상태·판정은 코드값으로 비교하고 표시는 `useCommonCodes().name(그룹, 코드)`. 시각·주기는 `useClientSettings()` — 웹에도 하드코딩 금지.
- 서버 데이터는 TanStack Query로만 읽고, 변경 후 해당 키를 무효화한다 (`queryKeys.ts` — 실시간 알림과 같은 키 공유).
- 표는 좁은 화면(현장 PC·태블릿)을 고려해 `scroll.x`와 열 너비를 지정한다.

## 19.4 대시보드

작업일(설정 기준 범위), 시스템 상태(API·DB, 실시간 연결, `schedule.refresh_interval_sec` 주기 확인), 내 계정. LOT 진행·설비 가동·검사·부적합·출하·마감 패널 → 7단계 §24.3 에서 채움.

---

# 20. 스케줄 서비스 + 생산계획 Gantt (4단계 ①, V3.11)

## 20.1 구조

| 위치 | 내용 |
|-|-|
| `Features/Scheduling/ScheduleCalculator.cs` | **계산 규칙 전부** (§7). DB·시계 의존 없는 순수 함수 — 체인·앵커·달력(작업일 시작, 휴일, 비가동)을 받아 시각·작업일 배정 |
| `RunningTimeResolver.cs` | 작업시간 ①표준(설비 > 설비유형 > 공통, 현재 버전) ②같은 품목·단위공정·설비유형 최근 완료 작업 ③설비 기준시간(설비 > 유형, 공정 지정 > 공통). ④⑤는 요청이 결정 |
| `SchedulingService.cs` | 설비 잠금 → 사용자 의도 저장(row_version) → 체인 재계산(시각·작업일·순번·임시 LOT) → 감사 → 커밋 → SignalR `scheduleChanged {equipmentIds}` |
| `ScheduleDelayMonitor.cs` | 지연 반영: `schedule.refresh_interval_sec`마다 계획이 있는 설비 재계산. 설정 `Scheduling:AutoRecalculate=false`로 끔 (테스트) |
| `Infrastructure/Numbering/NumberFormat.cs` | 번호 형식 설정 해석 (`{yyMMdd}`, `{SEQ:000}`, `{EQUIP}` …) — LOT·입고·출하·검사번호 공용 |
| `web/src/pages/production/` | `SchedulePage`(배정 대기·도구막대·상세·입력창), `ScheduleGantt`(설비 행 × 시간), `ganttMath`(좌표·삽입 위치 — 단위 테스트) |

## 20.2 API

| 메서드·경로 | 권한 (`production.schedule`) | 내용 |
|-|-|-|
| `GET /api/schedule/board?from&days&equipmentTypeId` | R | 설비·블록(+수주)·실적·휴일·비가동. 범위 = 작업일 시작부터 `days`일 (기본 `schedule.board_days`, 상한 31) |
| `GET /api/schedule/backlog?equipmentTypeId&search` | R | 수주품목 × 공정 경로 단위공정 중 계획 잔량 > 0 (설비유형 지정 시 그 유형의 단계 템플릿 공정만) |
| `POST /api/schedule/blocks` | C | 배정. `beforeBlockId` 앞에 삽입(없으면 끝). 수량 = min(잔량, 표준 charge) |
| `PUT /api/schedule/blocks/{id}/move` | U | 이동·순서 변경(다른 설비 포함, 두 설비 재계산) |
| `POST /api/schedule/blocks/{id}/items` | U | 병합 — 작업시간 = 담긴 품목 최대값 |
| `PUT /api/schedule/blocks/{id}/lock` | U | 시각 고정/해제 |
| `POST /api/schedule/blocks/{id}/cancel` | D | 취소 (구 배정 회수) — 잔량 복귀 |
| `POST /api/schedule/equipment/{id}/recalculate` | U | 수동 재계산 |

- 수정 요청은 모두 `rowVersion` 필수 → 불일치 409. 작업지시·취소된 블록 422 `BLOCK_NOT_EDITABLE`, 고정 블록 이동 422 `BLOCK_LOCKED`.
- 작업시간을 ①~③에서 못 찾으면 422 `DURATION_REQUIRED` + `defaultMin`. 화면이 입력창을 띄워 `durationMin`(+`saveAsDefault` → 설비유형 기준시간 등록) 또는 `useDefaultDuration`으로 다시 요청.

## 20.3 화면

- 배정 대기 행을 설비 행에 끌어 놓으면 놓은 시각 기준 삽입 위치(중간점이 뒤인 첫 블록 앞), 블록 위에 놓으면 병합/앞/뒤 선택. 블록 끌기 = 이동.
- 표시: 상태 색(공통코드 `SCHEDULE_STATUS` 속성), 우선순위 왼쪽 띠(`PRIORITY`), 고정 점선, 위쪽 가는 막대 = 실적, 빗금 = 비가동, 붉은 선 = 현재, 휴일 음영. 자정·작업일을 넘는 블록은 한 막대로 연속 표시 (§15.1 S3).
- 갱신: SignalR `scheduleChanged` → 보드·배정 대기 재조회, 보조로 `schedule.refresh_interval_sec` 주기 조회.
- 개발 데이터: `dev-db.ps1 seed` (설비 6대, 공정 경로, 표준, 수주 6건, 진행 중 작업, 계획 비가동).

## 20.4 남은 일

- ~~계획 상태 전환~~ → 6-② 완료 (§23.3).
- 재작업 계획 → 6-⑤ 재작업 LOT (즉시 작업과 같은 작업지시 블록, §23.6). 밀린 계획 오늘로, 작업자 배정은 남음.
- SignalR 그룹을 설비·일자 단위로 나누기 (현재 전체 전송) — 동시 사용자가 늘면.

---

# 21. 출력 엔진 (4단계 ②, V3.12)

## 21.1 구조 — 발행 경로 1개 (§15.2 P1)

```text
POST /api/print/issue {purposeCode, sourceId, printTemplateId?}
  → 용도(print_purpose) → 데이터 공급원 코드(IPrintDataProvider) → PrintData(값 · 목록 · 이미지)
  → 양식 선택: 지정 양식 > 품목+업체 기본 > 품목 공통 기본 > 용도 기본(print_template.is_default)   (P9 단일 함수)
  → EXCEL: ExcelTemplateRenderer(ClosedXML) → PdfConverter(서버 LibreOffice)   /  FIXED: IFixedRenderer(QuestPDF) + layout_options_json
  → print_log(양식 버전 · 치환값 Snapshot · 해시 · 보관 용도면 발행본) + 공급원 반영(성적서 발행 일시·횟수)
```

| 위치 (`api/src/JiMes.Api/Features/Printing/`) | 내용 |
|-|-|
| `Placeholders.cs` | 치환자 문법·추출·사전 대조 (미정의 = 경고, 반복행 짝 불일치·비목록 = 오류) |
| `ExcelTemplateRenderer.cs` | 모든 시트 치환, 셀 전체가 숫자 치환자면 숫자 셀(양식 서식 유지), 반복행 복제(서식·행 높이 포함, 0건이면 행 삭제), 이미지 = 병합 영역 전체에 맞춤(모든 시트·위치) |
| `PdfConverter.cs` | `print.pdf_converter_path`, `print.pdf_convert_timeout_sec`. 서버 1곳, **직렬 대기열** (LibreOffice 프로필 충돌 방지), 작업별 임시 폴더 |
| `Fixed/SalesSlipRenderer.cs` | 거래명세표 (구 OutputSheet 3종 → 1개). 옵션 없으면 코드 기본값 = 구 상수 |
| `Fixed/PdfFonts.cs` | 글꼴 = 옵션 `font.family` (이름 또는 대체 목록) 중 **설치된 것만** 사용, 없으면 422 `FONT_NOT_INSTALLED` |
| `Providers/InspectionTargetProvider.cs` | 검사성적서 — 대상별 1장. 구 키 12개 + `T{탭}_{행}_…`(측정) · `C{탭}_{행}_…`(기준) 호환, 목록 `Measurements`, 이미지 `HardnessChart`·`StructurePhoto`(첨부) |
| `Providers/ShipmentProvider.cs` | 출하 전표 — 금액은 전표 저장값(F2, 없으면 세율 설정으로 계산), 품목 합산 출력(구 MergeByPart), 도장 `company.stamp_image` |
| `TemplateAdminService.cs` | 양식 추가(EXCEL), 파일 등록 = 새 버전(크기 `print.max_template_file_mb`, 해시 중복 거부, 치환자 검증), FIXED 옵션 = 새 버전, 용도 기본 지정, **샘플 양식**(P10) |

## 21.2 치환자 (사용자 안내)

```text
{{키}} / {{한글별칭}}                 모든 시트·셀. 셀 전체가 숫자 치환자 하나면 숫자로 들어가 양식의 숫자 서식 적용
{{#목록}} … {{/목록}}                  시작 표시가 있는 행 ~ 끝 표시가 있는 행을 데이터 수만큼 복제. 안에서는 {{항목}} 또는 {{목록.항목}}
{{이미지키}}                           셀 병합 후 기입 → 병합 영역 전체에 맞춰 삽입
{{T1_3_P2}}, {{C1_2_Spec}}            (검사성적서) 구 좌표형 키. 데이터 행보다 양식 행이 많으면 빈칸
사전에 없는 키                          등록 시 경고, 출력물에는 {{키}} 그대로 남음 (오류가 보이게)
```

## 21.3 구 코드 대비 바뀐 점 (소스 확인)

| 구 | 신규 |
|-|-|
| 거래명세표 일자 = 인쇄 시각 (`DateTime.Now`, OutputSheet 287행) | 출하일 |
| 세율 `TAX_RATE = 0.1m` 출력 코드 안 (63행) | 전표 저장값 표시, 계산은 `sales.vat_rate`·`sales.amount_rounding` |
| 도장 PC 경로 (`Stamps\Company\stamp_{id}.png`) | `company.stamp_image` |
| 경화층 차트: 첫 시트·첫 셀, `MoveTo(첫 셀, 마지막 셀)` → 병합 영역 마지막 행·열이 빠짐 | 모든 시트·위치, 병합 영역 전체 |
| 클라이언트 PC 두 경로에서 soffice 탐색, 60초 고정 | 서버 설정 1곳, 제한시간 설정, 직렬 처리 |
| 글꼴 이름 `GulimChe`/`Malgun Gothic` 코드 고정 | 옵션 목록 (QuestPDF는 파일에 기록된 이름 "굴림체", "맑은 고딕"으로만 찾음 — 설정 `Print:UseSystemFonts`) |

## 21.4 API · 화면

| 메서드·경로 | 권한 (`system.print`) |
|-|-|
| `GET /api/print/purposes` (+ 치환자 사전), `GET …/purposes/{code}/sample-template` | R |
| `GET /api/print/templates?purposeCode`, `POST /api/print/templates` | R / C |
| `POST /api/print/templates/{id}/versions` (multipart 파일), `…/options` (FIXED JSON), `PUT …/default` | U |
| `GET /api/print/templates/{id}/versions`, `GET /api/print/versions/{id}/file` | R |
| `POST /api/print/issue`, `POST /api/print/logs/{id}/reprint` (당시 버전 + 당시 값), `GET /api/print/logs`, `GET /api/print/logs/{id}/file` (보관본) | R (6단계에서 업무 화면 권한으로) |

- 발행 응답 헤더 `X-Print-Log-Id` = 이력 번호. 파일명은 `{용도명}_{검사번호-순번 | 전표번호}.pdf` (RFC 5987 한글 파일명).
- 화면 `시스템 > 출력 양식`: 용도별 양식 목록, 엑셀 양식 추가·파일 등록(미정의 치환자 경고 표시), 버전 이력·파일 받기, FIXED 옵션 JSON 편집(새 버전), 용도 기본 지정, 샘플 양식·치환자 목록, 발행 확인(대상 ID 입력).
- 배포 체크리스트: 서버에 LibreOffice, 한글 글꼴(굴림체·맑은 고딕) 설치.

## 21.5 남은 일

- 나머지 고정 양식 렌더러 5종(공정이동표·제품라벨·작업일보·진행현황표·작업표준서)과 데이터 공급원(작업 LOT·작업표준·마감·수주·일자별 계획) — 해당 업무 단계(5·6단계)에서.
- 옵션 편집기의 샘플 데이터 미리보기, 옵션 JSON Schema 검증 (§15.3.1).
- 단말별 프린터·자동 인쇄(`workstation_print_setting`) — 브라우저 인쇄 방식 결정 후.

---

# 22. 기준정보 (5단계)

## 22.1 나눔

| 순서 | 대상 | 방식 |
|-|-|-|
| 5-① | 자사 정보(도장), 거래처, 설비 유형, 설비, 단위공정, 조건 항목, 설비 기준시간, 불량 사유, 측정기구, 교대, 공장 달력, 부서, 직위, 사원 + **사용자·역할 권한** | 단순 기준정보 = **정의 기반 범용 API·화면** (§22.2) / 사용자·역할은 전용 |
| 5-② | 품목 (거래처별 품번, 도면·이미지 첨부, 성적서 양식 연결, 변경 이력) | 전용 화면 |
| 5-③ | 공정 경로(버전·단위공정 순서), 단계 템플릿(단계·관리항목), 작업표준(항목 × 단계 행렬, 버전, 복사) | 전용 화면 |
| 5-④ | 검사기준 (품목+업체, 버전, 항목, 측정 위치) | 전용 화면 |

## 22.2 단순 기준정보 — 정의 기반 (5-①)

- 정의: `api/…/Features/Master/MasterCatalog.cs` — 테이블마다 필드(라벨·형식·필수·길이·중복 금지·범위·공통코드·참조·기본값·검색·목록 표시·도움말). **API 검증·SQL·화면 구성의 단일 원천.**
- API: 정의마다 `/api/master/{key}` 경로를 따로 등록 → 메뉴 권한 `master.{key}`가 엔드포인트에 고정 (fail-closed 검사 대상).

| 메서드·경로 | 권한 |
|-|-|
| `GET /api/master/{key}/meta` · `GET /api/master/{key}?search&includeInactive&page` · `GET /api/master/{key}/{id}` | R |
| `POST /api/master/{key}` / `PUT /api/master/{key}/{id}` (`__reason` = 변경 사유) | C / U |
| `DELETE /api/master/{key}/{id}` — 공장 달력·설비 기준시간만 (참조 없는 설정성 자료) | D |
| `GET·PUT·DELETE /api/master/{key}/{id}/image/{name}` — 자사 도장 (PNG·JPG, DB 보관) | R / U |
| `GET /api/master/{key}/options` — 다른 화면 드롭다운용 id·표시명 | 로그인 |

- 규칙: `is_active`가 있는 테이블은 **삭제 없이 사용 중지** (과거 거래 참조 보존). 수정은 보내온 필드만. 오류는 필드별로 모아 400. 참조 중 삭제 422 `IN_USE`. 모든 변경 `audit_log` (사용 여부 변경 = `STATUS_CHANGE`).
- CHECK 값의 표시명은 공통코드 `CUSTOMER_TYPE`, `DAY_TYPE`, `CONDITION_VALUE_TYPE` (코드 고정).
- 정의 ↔ DDL 대조 테스트(`MasterCatalogTests`): 컬럼 존재·형식·길이, NOT NULL ↔ 필수/기본값, 중복 금지 ↔ UNIQUE 인덱스, 참조·공통코드·메뉴 존재. **DDL을 바꾸면 정의도 같이 고쳐야 테스트 통과.**
- 화면: `web/src/pages/master/MasterPage.tsx` — `master.*` 메뉴는 전용 화면 등록이 없으면 이 화면. 목록(검색·사용 중지 포함·페이지), 행 클릭 → 편집 창(형식별 입력, 공통코드·참조 선택, 변경 사유, 이미지).
- 새 단순 기준정보 = DDL 테이블 + 메뉴(§9.8) + 정의 1개. 화면·API 코드 추가 없음.

## 22.3 사용자·역할 (5-①)

- `시스템 > 사용자 관리`: 계정 추가(초기 비밀번호 — `auth.password_min_length`), 이름·사원 연결·역할(복수)·사용 여부, 비밀번호 초기화.
- `시스템 > 역할·권한`: 역할 추가(코드 고정), 메뉴 × 조회/등록/수정/삭제 행렬. 쓰기 권한을 주면 조회도 함께.
- 변경 즉시 권한 캐시 무효화 → 다음 요청부터 적용, 사용 중지 계정은 기존 세션도 끊김.
- 잠금 방지: 자기 계정 사용 중지·자기 역할 변경 불가 (`SELF_LOCKOUT`), 관리자 역할(`Bootstrap:AdminRoleCode`)의 권한·사용 여부 변경 불가 (`ADMIN_ROLE_LOCKED`).

## 22.4 품목 (5-②)

- API `/api/parts` (권한 `master.part`), 화면 `기준정보 > 품목` (`web/src/pages/master/PartsPage.tsx`).
- 저장 1회 = 품목 + 거래처별 품번(`part_customer`: 고객 품번, 고객 LOT 필수, 주 거래처 1개) + 적용 공정(`part_heat_process`: 기본 1개). 목록에서 빠진 거래처·공정은 **사용 중지**(수주·검사 참조 보존).
- 품번 중복: 구 화면은 무조건 거부 → 신규는 422 `DUPLICATE_PART_NUMBER` 후 **확인하면 저장** (`allowDuplicatePartNumber`). 품목 코드는 UNIQUE.
- 변경마다 `part_history`(전·후 스냅샷, 이력 탭에서 바뀐 항목만 비교) + `audit_log`.
- 도면·이미지: 공통 첨부 `attachment` (owner `part`, 종류 = 공통코드 `ATTACHMENT_KIND` 중 속성 owner=part), DB 보관, 크기 `file.max_attachment_mb`, SHA-256 해시. 구 PC 폴더(`PartDrawingFolder`, `PartImageFolder`) 폐지.
- 성적서 양식 연결(`part_print_template`): 품목 + 거래처(또는 모든 거래처) × 양식, 거래처·용도당 기본 1개 → 출력 양식 선택 1·2순위 (§21.1).
- 공정(열처리) **즉석 등록 없음** — 구 화면은 이름 입력 시 공정·단위공정 자동 등록 (오타로 늘어나는 문제, 구 T4). 공정은 5-③ 화면에서만.

## 22.5 공정 경로 · 단계 템플릿 · 작업표준 (5-③)

| 화면 (권한) | API | 규칙 |
|-|-|-|
| 공정 경로 (`master.heat_process`) | `/api/heat-processes` (`PUT …/route` = 경로 저장) | 경로 = 단위공정 순서 + 주공정(최대 1) + 필수 여부. **현재 Version을 수주 품목·작업 LOT이 쓰면 새 Version**, 아니면 현재 Version 수정. 목록에 경로 요약(`세척 > 침탄* > 템퍼링`) |
| 단계 템플릿 (`master.step_template`) | `/api/step-templates` | 단위공정 × 설비유형(또는 설비)별 **입력표 초기값** = 스텝(열) + 관리항목(행). 편집은 작업표준과 같은 입력표 편집기. 참조하는 곳이 없으므로 저장 = 통째로 다시 씀 (스텝 삭제·단위공정 변경 자유) |
| 작업표준 (`master.standard`) | `/api/standards` (`POST …/versions` = 새 Version) | 키 = 품목 × 단위공정 × 설비유형/설비 × 거래처 × 공정, 같은 키는 1개(`DUPLICATE_STANDARD` + 기존 id). 코드 = 설정 `standard.code_format`. **저장할 때마다 새 Version** (구 "새 행 INSERT" 방식 유지). **입력표 = Version 이 가진 스텝·관리항목** (가변식, 아래). 숫자 항목 검증, 빈 칸은 저장 안 함. 템플릿 불러오기·템플릿으로 저장, 다른 품목 표준에서 복사, 과거 Version 보기 |

- **Version 시작 시각:** DB DATETIME은 소수초를 버리므로 같은 초에 연달아 저장하면 이전 종료 = 이전 시작이 되어 CHECK(`effective_to > effective_from`)에 걸린다 → 시작 = max(현재 초, 이전 시작 + 1초) (`VersionClock`, 테스트에서 발견).
- 검증을 먼저, 이전 Version 종료는 나중 (잘못된 입력이 이전 Version을 닫지 않게).
- **가변식 입력표 (V3.14, 구 `F_WorkStandardAddForm` 비교)** — 구 폼은 첫 행 "스텝"에 스텝 이름을 적고 `Item` 칸에 관리항목을 자유 입력했으며(행 삽입·삭제·이동), 템플릿(`t_standardtemplate`)은 조건 선택 시 표를 채우는 초기값이었다 (`F_WorkStandardAddForm.cs` 114~199·548~649행, 템플릿이 없으면 저장 시 자동 저장 692~703행). 신규도 같은 방식:
  - 스텝(열): 표 머리에서 이름 입력, [+ 스텝], ←→ 이동, 삭제 (값이 있으면 확인). 개수 제한 없음 (구 15개).
  - 관리항목(행): 조건 항목에서 검색해 추가, 없으면 그 자리에서 새로 등록(`master.condition_item` 생성 권한). ↑↓ 이동, 삭제. 문자열 자유 입력 대신 항목 id — 오타로 같은 항목이 둘로 갈리지 않게.
  - 저장: `steps[]`(이름, 순서 = 열) · `items[]`(항목 id, 순서 = 행) · `conditions[]`(`stepNo` = 열 번호 1..N, null = 공통). 행에 없는 항목·없는 스텝 번호의 값은 거부.
  - 템플릿 불러오기: 템플릿의 스텝·항목으로 표를 바꾸고, 이미 적은 값은 (스텝 이름, 항목)이 같으면 유지 (구 `LoadDetailsWithLatestTemplate`). 자동 템플릿 저장 대신 **"이 구성을 템플릿으로 저장"** 버튼 (선택).
  - LOT 조건(`production_work_condition`)도 단계 키 = 스텝 순서 + 이름 Snapshot (6단계 투입 시 표준에서 복사).
- 편집 중 폼은 서버 자료 **내용이 바뀔 때만** 새로 만든다 (`useDataVersion`) — 실시간 재접속 시 전체 재조회로 입력 중 값이 사라지던 문제 (V3.14 에서 발견, 품목·검사기준 편집 창도 같이 수정).

## 22.6 검사기준 (5-④)

- 화면 `기준정보 > 검사기준` (`master.inspection_standard`), API `/api/inspection-standards`.
- 헤더 = 품목 + 거래처(비우면 공통), 같은 조합 1개(`DUPLICATE_INSPECTION_STANDARD`). Version 규칙은 공정 경로와 같음 (검사가 쓴 Version → 새 Version).
- 항목: 유형(공통코드 `INSPECTION_ITEM_TYPE` → 성적서 T/C 좌표 키 접두어), 항목·위치·요구사항(성적서 표기)·측정기·시험값·스케일·단위, **판정 방식**(공통코드 `RANGE_TYPE`: 범위/하한 이상/상한 이하/기록만 — 속성으로 하한·상한 필요 여부), 하한·상한, 시료수·시험수, 측정 위치 목록(구 P1~P10 → 개수 제한 없음 행).
- 검증은 행별 오류를 모아 400 (하한·상한 필요, 하한 > 상한, 시료수 ≥ 1 …).
- **이관 주의 (8단계):** 구 `t_inspectioncriteria.itemtype`은 `"0"~"5"` 또는 이름 — `INSPECTION_ITEM_TYPE` 코드로 변환해야 한다. 변환 안 된 값은 화면에서 붉게 "'경도' → 다시 선택"으로 표시되고 저장 시 거부된다.

---

# 23. 업무 (6단계)

## 23.1 나눔

| 순서 | 범위 | 상태 |
|-|-|-|
| 6-① | 수주(입고) 등록·목록·수정·취소 | V3.15 완료 |
| 6-② | 계획 확정·작업지시(RELEASE → 작업 LOT 배정) | V3.16 완료 (재작업 계획은 6-⑤) |
| 6-③ | 투입·작업 (수주번호/주 LOT 스캔, 표준 확정·조건 복사, 시작·완료) | V3.17 완료 (불량 수량은 6-⑤) |
| 6-④ | 검사·성적서 (검사 1회 : 대상 N, 판정, 대상별 성적서 발행) | V3.18 완료 |
| 6-⑤ | 부적합·재작업 | V3.19 완료 |
| 6-⑥ | 출하·마감 (전표, 금액 계산, 업체별 마감·이월) | V3.20 완료 |

## 23.2 수주(입고) — 6-①

| 화면 (권한 `sales.order`) | API | 규칙 |
|-|-|-|
| 영업 > 수주(입고) 목록 | `GET /api/sales-orders/items?from&to&customerId&search&openOnly&includeCancelled` | 입고 **행** 단위 (입고번호 = 스캔하는 수주번호). 기본 기간 = 오늘 − `sales_order.list_default_days`. 투입(주공정)·출하·출하 잔량은 VIEW 계산 |
| 수주 등록 창 | `GET …/part-candidates?customerId&search&all`, `POST /api/sales-orders` | 거래처 품목(`part_customer`)을 담고 행마다 수량·단가·고객LOT·코일·작업지시번호·우선순위·별도관리. 같은 품목을 LOT별로 여러 행. 저장 = 묶음(`sales_order_no`, 설정 `sales_order.number_format`) 1건 + **행마다 입고번호**(`sales_order.item_number_format`, 입고일 기준 순번) |
| 입고 행 창 (보기·수정·취소) | `GET/PUT …/items/{id}`, `POST …/items/{id}/cancel` | 수정·취소는 묶음 `row_version` (같은 묶음의 행을 동시에 고치면 409) |

- **Snapshot:** 품명·품번·규격·기종·재질·요구경도·심부경도·경화층·조직·단위중량·단가 구분을 행에 복사 (구 IncomeAddService와 같음). 중량 = 수량 × 단중. 단가는 품목 값 기본, 행에서 수정 가능.
- **공정:** 선택한 공정 경로의 **현재 Version**을 참조 (`heat_process_version_id`). 선택 없으면 품목 기본 공정(`part_heat_process.is_default`). 공정이 없으면 "미지정"으로 붉게 표시 — 생산계획 배정 대기에 나오지 않는다.
- **거래처 품목 필수 LOT:** `part_customer.is_customer_lot_required` 품목은 고객LOT 없이 등록 불가 (화면 검사).
- **사용 중 보호:** 단위공정별 계획·투입 수량 중 최대, 출하+시험편 합 = 사용 수량. 수량은 사용 수량 미만으로 줄일 수 없고(`QTY_BELOW_USED`), 계획·투입이 있으면 공정 변경(`ROUTE_IN_USE`)·취소(`ORDER_ITEM_IN_USE`) 불가. 구 목록 "삭제"는 **취소**(`status = CANCELLED`, 사유 감사 기록)로 바꿈 — 행이 모두 취소되면 묶음도 취소.
- **번호 부여 (`Infrastructure/Numbering/DocumentNumbers.cs`):** 형식 설정의 `{SEQ}` 자리만 다른 같은 접두 번호 수 + 1부터 빈 번호. 같은 번호 체계는 `GET_LOCK`으로 직렬화하고 잠금 읽기로 최신 커밋을 본다 — 동시 등록 테스트(6건 동시)로 확인. 출하·검사번호도 이것을 쓴다.
- 구 "별도관리는 수정 모드에서 변경 불가"는 이유가 없어(라벨 재출력으로 해결) 수정 가능으로 둠.

**남은 일 (6-①):** 저장 후 **공정이동표·제품라벨** 즉시 출력 — FIXED 렌더러 `PROCESS_SHEET`·`PRODUCT_LABEL`과 단말 자동 인쇄는 출력 방식(브라우저 인쇄) 결정과 함께 (§21.5). 엑셀 내보내기.

## 23.3 계획 확정·작업지시 — 6-②

| API (권한 `production.schedule` U) | 동작 |
|-|-|
| `PUT /api/schedule/blocks/{id}/confirm {rowVersion, confirmed}` | PLANNED ↔ CONFIRMED. 확정도 재계산 대상이고 수정 가능 — 현장에 "확정된 계획"을 알리는 표시 |
| `POST …/blocks/{id}/release {rowVersion, includePrevious}` | **작업지시** = 작업 LOT 생성(`production_work`, 상태 ALLOCATED "배정") + 이벤트 ALLOCATE. `includePrevious` = 같은 설비 체인에서 이 계획까지 모두. 응답 = 만든 LOT 목록 |
| `POST …/blocks/{id}/unrelease {rowVersion}` | 작업지시 취소 — 투입 전(ALLOCATED)만(`WORK_STARTED`). LOT 은 CANCELLED + 삭제 표시 + 계획 연결 해제로 남기고(감사·번호 보존), 계획은 CONFIRMED로 돌아가 재계산 |

- **LOT번호:** 설정 `lot.number_format` (`{yyMMdd}` = 계획 작업일, `{EQUIP}` = 설비 이니셜, `{SEQ}` = 설비 × 작업일 순번). 순번 = 그 설비·작업일의 마지막 LOT 순번 + 1 — **취소된 LOT 순번도 다시 쓰지 않는다** (현장에 붙은 번호와 혼동 방지). 임시 LOT(`P…`) 미리보기도 같은 기준.
- **LOT 정보:** 주공정 여부 = 담긴 수주의 공정 경로에서 이 단위공정이 주공정인지. 공정 경로 Version 은 담긴 수주가 모두 같을 때만 기록(혼적이면 NULL). 예상 작업시간 = 계획 작업시간. 단위공정·설비·공정명 Snapshot.
- **스케줄:** 작업지시된 블록은 재계산에서 빠지고, 투입 전이면 체인 시작점(앵커)이 그 계획 종료 뒤가 된다 (§20). 보드에 작업 LOT번호·상태를 표시.
- **화면:** 생산계획 블록 상세 아래 [계획 확정/확정 해제] [작업지시] [앞 계획까지 작업지시], 작업지시된 블록은 [작업지시 취소]. Gantt 막대 이름은 작업 LOT번호.
- 작업자 배정(`worker_assignment`)·밀린 계획 오늘로 옮기기는 투입 화면(6-③)과 함께 검토.

## 23.4 투입·작업 — 6-③ (구 F_GasForm)

화면 **생산 > 작업(투입)** (권한 `production.work`): 왼쪽 = 설비별 LOT (진행 중 → 배정 → 이 작업일 완료), 오른쪽 = 선택 LOT.

| API (`/api/works`) | 권한 | 동작 |
|-|-|-|
| `GET /board?equipmentTypeId`, `GET /{id}`, `GET /{id}/standards` | R | 보드 / LOT(머리·투입·조건·이력) / 표준 확정 후보 |
| `POST /{id}/scan {code}` | R | 입고번호 또는 주 LOT번호 → 투입 후보(기준·기투입·잔량) — 저장 안 함 |
| `POST /` {equipmentId, unitProcessId} | C | **즉시 작업** (구 NEW 모드) — 계획 1:1 규칙을 지키려고 지금 시각의 작업지시(RELEASED) 계획 블록을 함께 만든다. 작업시간 = 설정 기본값, 표준 확정 때 표준 작업시간 |
| `POST /{id}/inputs`, `PUT/DELETE /{id}/inputs/{inputId}` | U | 투입 추가·수량 수정·삭제 |
| `POST /{id}/start`, `POST /{id}/complete` | U | 투입(시작) 배정 → 투입, 완료 투입 → 완료 |
| `POST /{id}/fix-standard`, `PUT /{id}/conditions` | U | 표준 확정(조건 복사), 조건 수정 |
| `PUT /{id}` | U | 제출 LOT·마킹·특기사항 |

**투입 단계** — 수주의 공정 경로에서 이 LOT 단위공정의 위치로 정한다 (`WorkService.PhaseAsync`).

| 단계 | 스캔 | 기록 | 투입 가능(잔량) |
|-|-|-|-|
| 주공정 전 (PRE) | 입고번호 | `main_work_id = NULL` | 수주수량 − 이 단위공정 기투입 |
| 주공정 (MAIN) | 입고번호 | `main_work_id = 자신` (LOT `is_main_process = 1`) | 수주수량 − 이 단위공정 기투입 |
| 주공정 후 (POST) | 입고번호 → 그 수주가 든 주 LOT 후보(여러 개면 선택) / 주 LOT번호 → 그 LOT 투입 행 | `main_work_id` = 주 LOT, `main_input_id` = 주 LOT 투입 행 | 주 LOT 행 **양품**(투입 − 부적합) − 이 단위공정에서 그 행으로 기투입 |

- 초과 투입 차단 (`INPUT_EXCEEDS_REMAINING`, 구: 검증 없음 G1). 같은 LOT 에 같은 수주(주 LOT 기준) 1회 (`DUPLICATE_INPUT`). 한 LOT 의 투입은 같은 단계끼리 (`PHASE_MISMATCH`). 공정 경로 없는 수주·경로에 없는 단위공정은 거부.
- 잔량은 매번 계산하고 수주 행을 `FOR UPDATE` 로 잠가 동시 투입을 직렬화 (구: 저장형 잔량 + 여러 번 개별 저장 G4).
- 투입 행 수정은 "후공정이 가져간 수량 + 부적합" 아래로 못 줄임, 삭제는 후공정·검사·부적합이 참조하면 불가. 삭제는 참조가 없는 오입력 정정이라 행을 지우고 감사 기록.
- **시작:** 배정 → 투입(INPUT), 실적 시작 = 입력값 또는 지금(분 단위). 설비당 진행 중 LOT 1건 (`EQUIPMENT_BUSY`, DB UNIQUE 와 같은 규칙). 시작·완료 후 그 설비 계획을 재계산(지연 반영·당김).
- **완료:** 완료시각 기본값 = 지금을 설정 `work.complete_time_round_min` 단위로 **내림** (구 RoundToNearest5Minutes 실제 동작) — 내린 값이 시작보다 앞이면(시작 직후 완료) 시작시각, 작업시간(분) 저장. 완료·취소 LOT 은 투입·조건 변경 불가 (G12). 제출 LOT·마킹·특기사항은 완료 뒤에도 수정 가능.
- **표준 확정:** 후보 = 투입 품목마다 품목 × 단위공정 × (설비 > 설비유형 > 공통) × (거래처·공정 지정이면 일치) 의 현재 Version, 구체적인 것 먼저. 확정 = 기준 투입 행 표시(LOT 당 1행) + LOT 에 표준 Version·단계 템플릿·예상 작업시간 기록 + **입력표 전체(관리항목 × [공통 + 스텝], 값 없는 칸 포함)를 LOT 조건으로 복사** — 행 순서는 `item_sequence_no`(DDL 추가). 다시 확정하면 조건을 새로 복사. 조건은 작업표준과 같은 입력표 편집기로 수정.
- 즉시 작업 LOT 의 공정 경로는 첫 투입 수주의 경로로 채운다 (표시용).

**남은 일 (6-③):** 분할투입(트레이·장입/추출 시각), 일시정지·재개 이벤트, 작업자 배정, 라인검사 탭(`line_inspection`), 조건 실적값(`actual_value`)·서명(G11), 작업일보 출력 — 필요 순서대로. 불량 수량 입력은 6-⑤ 부적합과 함께.

## 23.5 검사·성적서 — 6-④ (구 F_InspectionAddForm)

화면 **품질 > 검사** (권한 `quality.inspection`): 목록(기간·구분·검색) + 검사 창(등록·수정·확정·재검사·성적서).

| API (`/api/inspections`) | 권한 | 동작 |
|-|-|-|
| `GET /?from&to&type&search`, `GET /{id}`, `GET /criteria/{versionId}` | R | 목록 / 검사(대상·항목·측정값·기준) / 검사기준 항목 |
| `GET /lot?lotNo` | R | **LOT 입력** — 어떤 LOT 이든(보통 주 LOT, I5) 그 투입 행 = 대상 후보 (기검사 수, 품목(+거래처) 검사기준 현재 Version — 거래처 전용 우선) |
| `POST /` | C | 등록 = 저장(미확정 IN_PROGRESS). 검사번호 = 설정 `inspection.number_format`, `{TYPE}` = 공통코드 `INSPECTION_TYPE` attr prefix (TI/TP/TO) |
| `PUT /{id}` | U | 다시 저장 (미확정만) — 대상은 바뀐 것만 넣고 빼 기존 대상(성적서 이력)을 유지, 항목·측정값은 다시 씀 |
| `POST /{id}/complete` | U | **확정** — 판정 필수. 불합격이면 대상마다 `defect_occurrence` (수량 = 대상 수량 전체 I4, 발견 공정 = 대상 투입 행, 주 LOT 병기, 상태 OPEN) |
| `POST /{id}/reinspect` | C | 확정 후 수정 = **재검사** — 새 번호로 대상·항목·측정값 복사(`reinspection_of_id`), 원 검사·부적합은 그대로 (처리는 6-⑤) |
| `POST /{id}/cancel` | D | 미확정 검사 취소 |
| `POST /targets/{targetId}/report?printTemplateId` | R | **성적서 = 대상(품목)별 1장** (출력 엔진 §21, 용도 `INSPECTION_REPORT`, 양식 품목+업체 > 품목 > 용도 기본) |

- **측정·판정은 검사 공통** (I2) — 대상이 여러 품목이어도 측정값은 한 벌. 판정 기준 = 검사에 지정한 검사기준 Version 하나 (대상 품목 중 하나의 기준을 고른다, 구 B4 는 "첫 품목"으로 고정이었음).
- **판정** (`InspectionJudge`, 순수 함수 + 단위 테스트): 기준 판정 방식 BETWEEN/MIN/MAX 이고 숫자 측정값이 있으면 자동(범위 밖 시료 하나라도 있으면 불합격, 시료별 OK/NG). NONE·기준 외 항목·문자 측정값은 사용자가 고른 판정(합격/불합격/해당없음). 종합 = 하나라도 불합격이면 불합격, 합격이 있으면 합격. 결과 요약 = 최소~최대. 화면도 같은 규칙으로 입력 중 미리 판정(붉은 칸).
- 측정 시료 수 = 기준 `sample_count` (시료 추가 가능, 50개까지), 측정 위치 이름은 칸 도움말. 기준 외 항목 추가 가능.
- 검사자 필수 (구 동일). 대상은 그 LOT 의 투입 행만. 제출 LOT Snapshot = 주 LOT 의 제출 LOT (없으면 그 LOT).
- 구 결함 정리: 측정 상세 대상 수만큼 중복 저장(B1) → 검사당 1벌, sub_no = 검사 내 1..N(B2), 번호 경쟁(B3) → `DocumentNumbers`.

**남은 일 (6-④):** 경화층 곡선 차트·조직 사진 첨부(`attachment` INSPECTION — 성적서 이미지 키는 이미 지원), 관리도(X-bar), 라인검사(공정 중 검사 탭).

## 23.6 부적합·재작업 — 6-⑤ (구 F_Defect · F_DefectAdd)

| 등록 경로 | 수량 | 발견 공정 |
|-|-|-|
| 검사 확정(불합격) §23.5 | 대상 수량 전체 | 대상 투입 행 |
| **작업 화면 투입 행 [불량 등록]** `POST /api/works/{id}/inputs/{inputId}/defects` (`production.work` U) | 입력 (≤ 투입 − 기존 부적합 − 후공정이 가져간 수량 `DEFECT_EXCEEDS_GOOD`) | 그 투입 행 (투입·완료 LOT 만) |

화면 **품질 > 부적합** (`quality.defect`): 목록(기본 = 미처리: 미결정·결정·재작업 중 / 기간·상태·처리구분·검색) + 부적합 창.

| API (`/api/defects`) | 권한 | 동작 |
|-|-|-|
| `GET /`, `GET /{id}` | R | 목록·상세 (발생 LOT·공정, 주 LOT, 출처 검사번호, 재작업 LOT·상태) |
| `PUT /{id}/decide` | U | **판정** — 처리구분(공통코드 `DEFECT_ACTION`: 재처리·출하·선별·보류·폐기·반송)·판정자·메모 → 결정(DECIDED). 재작업 전까지 다시 판정 가능 |
| `POST /{id}/rework` {equipmentId, unitProcessId} | U | **재작업 LOT** — 재처리로 결정된 건만. 즉시 작업과 같은 작업지시 블록(`is_rework`)+작업 LOT 을 만들고 부적합 수량을 투입(`origin_work_id` = 최초 LOT — 재작업의 재작업도 최초, 재작업 단위공정이 수주 경로의 주공정이면 주 LOT = 자신, 아니면 원 주 LOT 유지 §3.3), 부적합 → 재작업 중(REWORKING, `rework_input_id`). 한 트랜잭션 |
| `POST /{id}/complete` | U | 완료자·처리 내용 → 완료 (결정·재작업 중) |
| `POST /{id}/cancel` | D | 오등록 취소 (미결정·결정만) — 양품 복귀 |

- **재작업 LOT 완료 → 그 부적합 자동 완료** (구: 완료자 수동 입력). 수동 완료도 가능.
- 재작업 투입은 정상 투입 잔량·수주 진행에 들어가지 않는다 (`is_rework` 제외, §1.2 VIEW 와 같은 기준).
- 상태 표시명·색은 공통코드 `DEFECT_STATUS` (구 `check_complete`·`plan_complete` 2개 플래그 → 상태 1개, D2). 판정자·완료자 = 사원 id (D3).

**남은 일 (6-⑤):** 여러 부적합을 한 재작업 LOT 에 모으기(현재 1건 = 1 LOT, 같은 설비 LOT 에 추가 투입은 작업 화면 스캔으로는 불가), 특채(조건부 출하) 수량의 출하 연계(6-⑥).

## 23.7 출하·마감 — 6-⑥ (구 F_OutAddForm · F_OutForm · F_MonthlyClosing)

화면 **영업 > 출하** (`sales.shipment`), **영업 > 마감** (`sales.closing`).

| API | 권한 | 동작 |
|-|-|-|
| `GET /api/shipments?from&to&customerId&search&includeCancelled`, `GET /{id}` | 출하 R | 전표 목록(수량·공급가액·세액·합계·마감 상태·귀속월)·상세 |
| `GET /api/shipments/stock?customerId&excludeShipmentId` | 출하 R | **출하 재고** (S1) — 수주 × 출하 LOT(주 LOT). 가능 = 주 LOT 투입 − 부적합(주 LOT·후공정, **처리구분 '출하'(특채)는 제외 = 출하 가능**) − 기출하 − 시험편. 주 LOT 이 없는 수주(경로에 주공정 없음·이관)는 수주 단위. 수주 전체 출하 잔량도 상한 |
| `POST /api/shipments`, `PUT /{id}` | 출하 C / U | 전표 저장 (한 트랜잭션 S8, 수주 행 잠금으로 동시 출하 직렬화). 수정은 이 전표 수량을 빼고 다시 검증 |
| `POST /{id}/cancel` | 출하 D | 전표 취소 — 재고는 계산값이라 자동 복귀 (S10) |
| `POST /{id}/slip` | 출하 R | 거래명세표 (용도 `SHIPMENT_SLIP`, 기본 FIXED `SALES_SLIP`) |
| `GET /api/closings/customers?year&month`, `GET /candidates`, `GET /`, `GET /{id}` | 마감 R | 업체별 요약(마감 기준일·미마감·마감) / 후보 전표 / 마감 기록 |
| `POST /api/closings` | 마감 C | **마감** — 선택 전표 CLOSED(귀속 연·월, `shipment_closing_id`) + 마감 기록(번호 `shipment_closing.number_format`, 마감자·시각·합계 Snapshot). `carryOverOthers` = 나머지 미마감을 다음 달로 이월(구 자동 이월 S7). 전표·마감 업체 일치 검증 (§6) |
| `POST /api/closings/carry-over` | 마감 U | 선택 전표 이월 (CARRIED_OVER, 지정 월) |
| `POST /api/closings/{id}/reopen` | 마감 D | 마감 취소 — 전표 미마감으로, 마감 기록은 REOPENED 로 보존 + 감사 |

- **금액** (`ShipmentMath`, 단위 테스트): EA 수량 × 단가 / KG 중량(수량 × 단중) × 단가 / CHARGE charge 수 × 단가 — 구 코드는 항상 수량 × 단가(B4). 시험편은 금액 제외(S2). 행 금액을 설정 `sales.amount_rounding` 으로 반올림 → 공급가액 = 합, 세액 = 공급가액 × `sales.vat_rate` 반올림, 저장(출력은 저장값 §15.3 F2). KG 단가인데 단중 없음·CHARGE 인데 charge 수 없음은 거부.
- **Snapshot:** 전표에 거래처 상호·사업자번호·대표자·업태·종목·주소(S4), 행에 단가 구분·단가·제출 LOT(주 LOT 의 제출 LOT)·고객 LOT·품목.
- **마감 기준일** = 업체 `closing_day` (31 = 말일, 그 달 날짜 수로 보정, 없으면 말일). 마감 창 기본 선택 = 기준일까지 출하한 미마감·이월 전표.
- 마감된 전표는 수정·취소 불가(`SHIPMENT_CLOSED`) — 마감 취소 후 가능. 마감 상태 표시는 공통코드 `CLOSING_STATUS`, 마감 기록 상태는 `CLOSING_RUN_STATUS`.

**남은 일 (6-⑥):** 여러 전표 거래명세표 병합 출력(S9 옵션은 있음), 엑셀 내보내기, 반입(재입고) 처리 흐름.

---

# 24. 조회·대시보드 (7단계)

| 순서 | 범위 | 상태 |
|-|-|-|
| 7-① | LOT 현황·추적 | V3.21 완료 |
| 7-② | 수주 진행·재고 현황 | V3.22 완료 |
| 7-③ | 대시보드 KPI | V3.23 완료 |

## 24.1 LOT 현황·추적 — 7-① (구 F_WorkHistoryForm · F_ProductionStatus)

화면 **조회 > LOT 현황·추적** (`report.lot`): 탭 2개. 현황의 행을 누르면 그 LOT 으로 추적 탭이 열린다.

| API (`/api/reports`) | 동작 |
|-|-|
| `GET /lots?from&to&equipmentId&unitProcessId&status&mainOnly&search` | LOT 현황 = `vw_work_lot_status` (상태는 배정·투입·완료만 §3.5, 후공정 완료/전체·검사 확정/전체·미처리 부적합·출하는 참고 컬럼) + 투입·입고번호·품명·거래처 요약. 검색 = LOT·제출 LOT·입고번호·품명·거래처·고객 LOT |
| `GET /trace/resolve?code` | 번호 → 주 LOT 후보. 작업 LOT: 주공정이면 그 LOT, 후공정이면 투입 행의 주 LOT, 재작업이면 원 LOT 의 주 LOT, 전공정이면 그 수주들의 주 LOT. 제출 LOT → 그 LOT. 입고번호 → 그 수주가 든 주 LOT 들(여러 개면 화면에서 선택) |
| `GET /trace/{mainWorkId}` | 주 LOT 추적 — 투입 수주(투입·부적합·양품), **전공정**(같은 수주가 주 LOT 없이 들어간 LOT, 수주 단위 연결), **후공정**(`main_work_id`), **재작업**(`origin_work_id`), 검사(대상이 주 LOT·후공정·재작업 투입 행), 부적합(`main_work_id`·발생 LOT), 출하(`shipment_item.main_work_id`) |

- 정밀도는 §3.4 그대로: 전공정만 수주 단위(같은 수주가 여러 주 LOT 으로 나뉘면 전공정 LOT 이 양쪽에 보임), 나머지는 정확.
- 현황 목록은 `schedule.refresh_interval_sec` 주기로 다시 읽는다.

## 24.2 수주 진행·재고 — 7-② (구 F_OrderStatus · F_InventoryForm · F_OutcomeStatus)

화면 **조회 > 수주 진행·재고** (`report.order`), API `GET /api/reports/orders?from&to&customerId&search&view` (`view` = OPEN 미출하 / STOCK 재고 있음 / NOT_INPUT 미투입 / 없음 = 전체).

| 컬럼 | 계산 (저장형 잔량 없음 §1.2) |
|-|-|
| 공정 진행 | 수주 공정 경로 순 단위공정마다 투입·양품·LOT 수 = `vw_sales_order_item_process_progress` (재작업 제외). *= 주공정 |
| 미투입 | 수주 − 주공정 투입 |
| 미처리 부적합 | 미결정·결정·재작업 중 부적합 수량 |
| 재고 | 주 LOT 투입 − 부적합(특채 '출하' 제외, 후공정 포함) − 출하 − 시험편 = **출하 화면 재고와 같은 기준** (구 F_InventoryForm 의 입고 − 출하 − 시험편은 작업 전 수량도 재고로 셌음) |
| 출하 / 출하 잔량 / 출하 금액 | 취소 안 된 전표 합 / 수주 − 출하 − 시험편 / 행 금액 합 |

## 24.3 대시보드 KPI — 7-③ (구 F_DashForm)

| 패널 | API (`/api/dashboard`) | 권한 | 내용 |
|-|-|-|-|
| 영업 | `GET /sales` | 로그인 — 입고는 `sales.order` R, 출하는 `sales.shipment` R 일 때만 채움 (둘 다 없으면 403) | 오늘·이번 달 입고 건수·금액(수량 × 단가), 미투입 입고 행, 오늘·이번 달 출하 건수·금액(전표 합계), 미마감 전표 건수·금액 |
| 설비 가동 | `GET /equipment` | `production.work` R | 설비마다 진행 중 LOT(시작·경과/예상 작업시간 진행률, 넘으면 "지연"), 다음 배정 LOT·계획 시작, 배정 수, 이 작업일 완료 수, 비가동 중(`equipment_downtime`) |
| 품질 | `GET /quality` | `quality.defect` R | 미처리 부적합 상태별 건수, 오늘 검사(확정·불합격)·미확정 전체, 최근 미처리 5건 |
| 일별 추이 | `GET /trend?days` | 로그인 — 계열마다 위 권한 (없는 계열은 null) | 기간 = 설정 `dashboard.trend_days`(상한 92일). 입고 금액·건수, 출하 금액·건수, 부적합 건수·수량 |

- 패널은 해당 업무 메뉴 읽기 권한이 있는 사용자에게만 보인다 (금액 노출 범위). 영업·설비·품질은 `schedule.refresh_interval_sec` 주기로 다시 읽는다.
- **그래프** (`web/src/components/TrendBars.tsx`, dataviz 기준): 입고·출하 금액은 같은 단위(원) 한 축 막대, 부적합 건수는 단위가 달라 **별도 차트**(이중 축 금지). 색 = 기준 팔레트 고정 순서(1 파랑 입고, 2 주황 출하, 3 청록 부적합), 라이트·다크 각 단계를 검증 스크립트로 확인(색각 ΔE 9.2 / 9.4). 라이트 테마에서 청록이 대비 3:1 미만 → **"표로 보기"** 전환을 함께 둔다. 날짜 칸 마우스 올림 = 값 풍선, 2개 계열은 범례.

---

# 25. 구 DB 이관·검증·병행운영 (8단계, V3.24)

| 순서 | 범위 | 상태 |
|-|-|-|
| 8-① | 구 덤프 복원 + 데이터 현황 + 이관 스크립트(기준정보 → 출하) + 검증 + 병행운영 절차 | V3.24 완료 |

## 25.1 원칙

- **운영 `bbakggum` 에는 접속하지 않는다.**
  - 원본은 운영 백업 덤프(HeidiSQL)를 이름만 `bbakggum_legacy` 로 바꿔 복원한 것이다.
  - 복원 명령은 `dev-db.ps1 legacy -Dump <파일>`이다. 이름을 바꾼 뒤에도 `bbakggum` 이름이 남아 있으면 복원하지 않는다.
- **스크립트:** `db/migration/*.sql`을 이름 순서로 실행한다 (`migrate.ps1`).
  - 같은 서버의 `bbakggum_legacy` → `bbakggum_v2` 를 `INSERT … SELECT` 로 옮긴다.
  - 작업 스키마 `bbakggum_mig`에는 설정·문제 목록·검증 결과를 둔다. 신규 DB에는 `migration_id_map`만 남는다.
- **재실행 가능:**
  - 모든 이관 행을 `migration_id_map(legacy_table, legacy_key, new_table, new_id)` 에 기록한다.
  - 다시 돌리면 매핑 없는 행만 추가한다.
  - 구 PK가 없는 표는 구 키를 정해 둔다.

    | 표 | 구 키 |
    |-|-|
    | 설비유형, 조건 항목 | 이름 |
    | 단계 템플릿 | 설비명\|단위공정명 |
    | 수주 묶음 | 거래처\|입고일 |
    | 검사 | 검사번호 |
    | 검사기준 | 품목\|거래처 |
- **자연키가 없는 신규 표** (기준 작업시간·부적합·비가동·보전): 비고에 임시 표식 `#mig:구id` 을 넣어 매핑한 뒤 원래 값으로 되돌린다.
- **구 문자열·번호 → 코드값은 공통코드에서 읽는다** (§15.4).

  | 맞추는 기준 | 공통코드 |
  |-|-|
  | 표시명 일치 | `WORK_STATUS`(배정/투입/완료), `DECISION`(합격/불합격), `DEFECT_ACTION`(재처리…), `CUSTOMER_TYPE` |
  | 정렬순서 | `INSPECTION_ITEM_TYPE` = 구 번호, `CLOSING_STATUS` = 구 값+1 |
  | attr | `PRICE_BASIS.legacy`(ea/kg/ch), `INSPECTION_TYPE.prefix`(TI/TP/TO) |

  맞지 않으면 기본값을 넣고 문제 목록에 `CODE_UNMATCHED`로 남긴다.
- 이관 규칙 값(코드 접두, 주공정 단위공정, 구 표 행 표식 등)은 `00_config.sql` 한 곳에 둔다. 이관 시점 매개변수이며 운영 설정이 아니다.

## 25.2 데이터 현황 (2026-04-09 덤프, `backup_260409_3.sql`)

| 구분 | 건수 |
|-|-|
| 기준정보 (실데이터) | 거래처 951, 품목 24,079, 설비 36(유형 10), 단위공정 16, 공정 117, 자사 1, 공정검사 항목 6, 단계 템플릿 8행(설비·단위공정 3쌍) |
| 거래 (시험 입력 수준) | 입고 12, 작업 7, 투입 7, 작업 조건 60행, 검사 2(상세 13), 작업표준 4, 검사기준 7 |
| 0건 | 출하·마감·부적합·비가동·보전·사원·측정기구·계획·작업자 배정·양식 |

- **품목 거래처 연결:** 품목 3,132건은 구 `customerid`가 거래처에 없다.
  - **이관하지 않는다** (V3.26, §12 ⑫ — 삭제된 거래처의 품목). 이관 품목 = 20,947건.
  - 필요한 품목은 신규 시스템에서 다시 등록한다.
- **품목 공정:** 품목 98건은 공정 id가 없다.
- **같은 품목 묶음:** 업체·품명·품번·규격·모델이 같은 품목이 4,092묶음이다. 구 DB는 업체별 행이라 합치지 않았다.
- **거래 데이터 제외:** 거래처·품목 id 1(샘플) 행은 원본 기준정보가 없어 제외했다.
- **0건 표:** 해당 경로는 `db/test/migration_fixture.sql` 로 시험했다 (§25.5).

§12 확인 결과:

| # | 결과 |
|-|-|
| 1 | 조건 항목 문자열 13종 + 단계 이름 행 `스텝`. 표기 흔들림이 없어 이름 그대로 `condition_item`으로 옮긴다 (확인자 = TEXT) |
| 2 | 폐기 후보 4개 (`t_condition_con`·`t_department`·`t_inspectionsub`·`t_printsheet`) 모두 0건 → 폐기. 검증이 건수를 다시 확인한다(WARN) |
| 3 | 검사번호마다 subno 1개. subno는 검사번호 안 순번이 아니라 이어지는 번호다. 신규 `inspection_target.sub_no`는 검사 안에서 1부터 다시 매긴다 |
| ⑧ | 품목 `subprice1~16` 전부 0, `subp` 전부 빈 값 → 컬럼 추가 안 함 (검증이 다시 확인) |
| ⑨ | 품목 `massstatus` 전부 "양산" → 컬럼 추가 안 함 (검증이 다시 확인) |

- 1번의 조건 항목 13종: 온도·시간·CP·RX·NH3·N2·교반·격자전류·전압·압력·출력·냉각시간·확인자.
- 3번 예: TO260327-001 = subno 1, TO260327-002 = subno 2.

## 25.3 매핑 규칙

### 기준정보 (`10_master.sql`)

| 구 → 신규 | 규칙 |
|-|-|
| t_company → company | 첫 행만. 도장 경로(PC)는 이관하지 않으므로 화면에서 다시 등록한다 |
| t_combolist → common_code | 그룹 = `combo_group_{이름}` 설정 (비가동사유 → `DOWNTIME_REASON`, DDL에 그룹 추가). 코드 = 항목 문자열 |
| t_customer → customer | 코드 `C`+id 6자리. 구분: 매출처 → SALES, 비매출처 → 설정 `customer_type_unmatched`(PURCHASE). 마감일 숫자·"말일" → 1~31 |
| t_equipment → equipment_type + equipment | 아래 참조 |
| t_unitprocess / t_heatprocess → unit_process / heat_process | 코드 `UP`·`HP`+id. 공정 경로는 아래 참조 |
| t_part → part (+ part_customer, part_heat_process) | 아래 참조 |
| t_employee → employee (+ department, job_position) | 코드 `E`+id. 부서·직위는 이름으로 만든다. **주민번호(rpn)·숙소·휴가는 이관하지 않는다** |
| t_instruments / t_process_default_time / t_unitinspectionitem | 코드 `INS`/`LI`+id. 기준시간은 시간 → 분 |

- **설비 (t_equipment):**
  - 설비유형 = 설비·작업표준·템플릿·기준시간에 쓰인 이름 전부 (코드 = 이름).
  - 설비 코드 = 이니셜. 이니셜이 중복이거나 비어 있으면 `EQ`+id.
  - 이미지 경로는 이관하지 않는다.
- **공정 경로 (t_heatprocess `subp1~16`):**
  - 단위공정 이름과 맞는 단계만 경로(Version 1)로 옮긴다.
  - 주공정 = 경로 안의 첫 주공정 단위공정.
- **품목 (t_part):**
  - 코드 `P`+id.
  - 단가 구분 = `PRICE_BASIS.legacy`, `outcomeunit` → `unit_code`, `customercode` → 거래처 품번.
  - 거래처 id 가 없으면 품목을 이관하지 않는다 (`SKIPPED`). 공정 id 가 없으면 이름이 하나뿐인 공정으로 연결하고 (`REF_BY_NAME`), 그것도 없으면 연결하지 않는다 (`REF_MISSING`).
  - 이관하지 않는 컬럼:
    - massstatus·subp·subprice (§25.2)
    - bundleqt
    - 도면·이미지·성적서 경로 (PC 경로 — 첨부로 다시 등록)
    - printsheetid

### 공정·표준 (`20_process.sql`)

| 구 → 신규 | 규칙 |
|-|-|
| item 문자열 → condition_item | 값에 숫자 아닌 것이 있으면 TEXT |
| t_standardtemplate → step_template (+ item, condition) | 설비명·단위공정별로 column 행 = 단계, row 행 = 관리항목 (중복 행은 첫 행) |
| t_standard + t_standarddetail → standard + Version 1 (+ step·item·condition) | 아래 참조 |
| t_inspectioncriteria → inspection_standard + Version 1 + criteria(+point) | 아래 참조 |

- **작업표준 (t_standard + t_standarddetail):**
  - `스텝` 행 = 스텝 이름. 없으면 템플릿의 같은 순번 이름, 그것도 없으면 `#열번호`.
  - 다른 행 = 관리항목. 값 없는 행도 유지하고, 같은 항목이 두 번이면 첫 행만 쓴다.
  - 값 있는 칸 = 조건 (step_no = 열 번호).
  - 작업시간은 시간 → 분.
- **검사기준 (t_inspectioncriteria):**
  - 품목 × 업체 = 기준 1개.
  - 항목 유형 번호(0부터) → `INSPECTION_ITEM_TYPE` 정렬순서(1부터).
  - 판정 방식 Range/Min/Max → BETWEEN/MIN/MAX. 빈 값이면 상·하한 유무로 정한다.
  - 하한 > 상한이면 둘 다 비운다.

### 수주 (`30_order.sql`)

t_income → sales_order + sales_order_item.

- 같은 거래처·입고일 = 수주 1건 (`MIG`yyMMdd-거래처id).
- 입고번호 = `order_item_no` 그대로. 중복이면 `MIGI`+id.
- 별도관리 = grade "별도관리".
- 잔량·금액은 저장하지 않는다.
- 상태는 OPEN (진행은 VIEW).

### 작업 (`40_work.sql`)

- **t_work → production_work:**
  - 진행 단계 → `WORK_STATUS`, 시간 → 분.
  - **주공정 = 설정 `main_unit_processes`** (기본 빈 값 = 모두 주공정 아님 — 주공정은 신규 개념, §12 ⑪).
  - 계획이 없으므로 `production_schedule_id`는 NULL.
  - 한 설비에 투입 중 LOT이 여러 개면 가장 최근 것만 투입, 나머지는 완료로 바꾼다 (`VALUE_CHANGED`).
  - 같은 설비·작업일·순번이 중복이면 순번을 비운다.
- **t_worksub (+ t_inputsub) → production_work_input:**
  - 같은 LOT × 입고번호 = 1행. 수량은 합산하고, 구 행 모두를 같은 신규 행으로 매핑한다.
  - **주공정 LOT 투입만 `main_work_id` = 자신. 후공정·전공정은 NULL** (§10).
  - 재작업 원 LOT = `originelotno`.
  - 표준 확정 품목 = `fixedpartid`의 첫 행.
  - 장입·추출 = LOT의 첫 장입·마지막 추출. 트레이는 투입이 1행인 LOT에만 붙인다.
- **t_workconditiondetail (+ t_conditiontemplate) → production_work_condition:** 작업표준과 같은 방식이다. 표 모양을 유지하려고 값 없는 칸도 모두 넣는다.

### 품질 (`50_quality.sql`)

- **t_inspection → inspection + inspection_target:**
  - 검사번호 = 검사 1건.
  - 구분 = 번호 앞 2자리 (`INSPECTION_TYPE.prefix`).
  - 판정 = 대상 중 하나라도 불합격이면 불합격.
  - 기준 = 첫 대상 품목(+업체)의 현재 검사기준.
- **t_inspectiondetail → inspection_item + inspection_measurement:**
  - 검사 × 유형 × 항목 × 위치 = 항목 1개.
  - 값은 (시료번호, v1~v10) 순으로 펼친다.
  - **시료번호 0 행(경화층 측정 깊이)은 측정값이 아니므로 제외한다.**
- **t_defect → defect_occurrence:**
  - 처리 문자열 → `DEFECT_ACTION`.
  - 상태: 완료 표시 → COMPLETED, 처리 있음 → DECIDED, 그 외 → OPEN.
  - 재처리면 그 LOT을 원 LOT으로 한 재작업 투입 행을 연결한다.
  - 판정자·완료자 이름은 비고에 넣는다.

### 출하 (`60_shipment.sql`)

- **t_outcomesum → shipment, t_outcome → shipment_item:**
  - 전표번호는 그대로 쓴다.
  - 세액 = 공급가액 × `sales.vat_rate` (`sales.amount_rounding`).
  - 구 `closingstatus` 0/1/2 → 미마감/마감/이월.
  - **마감완료 전표는 업체 × 마감월로 `shipment_closing` 1건을 만든다** (`MIGCL`yyyyMM-거래처id, 합계 = 연결 전표).
  - 마감월이 없는 마감·이월 전표는 출하월로 넣는다.
  - 구 출하에는 LOT 정보가 없어 `main_work_id`는 NULL.
  - 구 스키마에 마감 컬럼이 없으면(2026-04 이전) 미마감으로 넣는다.
- **t_downtime / t_maintenance:**
  - 설비는 이름으로 찾는다.
  - 자정을 넘는 비가동은 다음날 종료로 넣는다.
  - 측정기구 점검 기록(instrumentid)은 측정기구 교정 이력(`instrument_calibration`)으로 옮긴다 (V3.28, §28.4). 측정기구의 최근·다음 교정일도 갱신.

### 이관하지 않는 표

검증이 데이터 유무를 다시 보고, 있으면 WARN으로 표시한다.

| 표 | 이유 |
|-|-|
| workplan, t_schedulebox | 계획은 신규 시스템에서 다시 수립 |
| t_inputwaiting | 잔량은 계산 |
| t_workerassignment | 교대 등록 후 다시 입력 |
| t_lineinspection | 열 의미 확인 필요 |
| t_standard_gas | 작업표준 조건으로 다시 입력 |
| t_printtemplate, t_part_template | 엑셀 양식으로 다시 등록 |
| t_templatefieldname | 신규 치환자 사전 사용 |
| t_system_settings | PC 경로 |
| 폐기 후보 4개 | §25.2 |

## 25.4 검증 (`90_verify.sql` → `bbakggum_mig.verify_result`, FAIL 이면 오류)

1. **건수:**
   - 표마다 구 키 = 이관(매핑) + 제외(문제 목록 SKIPPED/DUPLICATE) 이어야 한다.
   - 설명 없는 행이 하나라도 있으면 FAIL.
   - 신규 쪽에 매핑 없는 행(병행운영 중 신규 입력)이 있으면 WARN.
2. **합계 (이관된 행끼리 비교):**
   - 입고 수량·중량
   - 투입 수량·LOT 수
   - 부적합 수량
   - 전표 공급가액, 출하 수량·금액, 전표 상세 합 = 전표 공급가액
   - 마감완료 전표 수
   - 작업표준·작업 조건값 칸 수
   - 검사 측정값 개수, 검사기준 항목 수
   - 거래처 미연결 품목 수
3. **이관 안 하는 표·컬럼:** 데이터가 있으면 WARN (§25.3 이관하지 않는 표).

**문제 목록** `bbakggum_mig.issue`에는 최근 실행분만 남는다. 전환 전에 사람이 확인할 목록이다.

| 코드 | 뜻 |
|-|-|
| SKIPPED | 제외 |
| DUPLICATE | 중복 |
| REF_BY_NAME | 이름으로 연결 |
| REF_MISSING | 참조 대상 없음 |
| CODE_UNMATCHED | 코드 변환 실패 |
| VALUE_CHANGED | 값 변경 |
| VALUE_DROPPED | 값 버림 |
| MERGED | 행 합침 |
| FILE_PATH | 파일 경로 미이관 |

**2026-04-09 덤프 결과:**

- 전 항목 PASS.
- 제외: 샘플 거래처·품목(id 1)에 딸린 입고 5·투입 2·검사 대상 2·작업표준 2·검사기준 1.
- 이관 시간 약 70초 (품목 2.4만).

## 25.5 시험

- **`db/test/migration_fixture.sql`:** 덤프에 없는 경로를 개발 복원본에 넣는다.
  - 부적합 3종
  - 전표 3종 (마감/미마감/이월)
  - 상세 (시험편·kg·없는 전표)
  - 자정 넘는 비가동, 보전
  - 구 스키마 마감 컬럼

  복원 → 이 파일 → `migrate -Fresh` 순서로 실행하고 검증을 통과해야 한다.
- **같은 파일의 "재실행 시험" 블록:** 실행 후 `migrate`가 검증을 통과해야 한다. 확인 내용:
  - 기존 수주 묶음에 행 추가 (행 번호가 이어짐)
  - 재작업 LOT의 원 LOT 연결
  - 전표 상세 추가, 공급가액 갱신
  - 미마감 → 마감 전환 (같은 마감에 연결, 합계 재계산)
  - 거래처명·LOT 진행 상태 갱신
- **같은 덤프로 두 번 실행:** 매핑·행 수가 그대로여야 한다 (2026-10-01 확인: 매핑 25,270행).

## 25.6 재실행 시 갱신 범위 (병행운영 동기화)

| 영역 | 재실행 동작 |
|-|-|
| 기준정보 | 추가 + 매핑된 행을 구 값으로 **덮어씀** |
| 작업표준·검사기준 | 처음 한 번만 (Version 확정 후 수정 금지 — 이후 변경은 신규 화면의 새 Version) |
| 수주·투입·작업 조건·검사·부적합·출하 상세·비가동·보전 | 추가만 |
| 작업 LOT | 추가 + 진행 상태·실적 시각·제출 LOT·표준 확정 갱신 |
| 출하 전표 | 추가 + 금액·마감 상태 갱신 (마감 생성·합계 재계산 포함) |

기준정보 = 거래처·설비·단위공정·공정·품목(+거래처·기본 공정)·사원·측정기구·기준시간·공정검사 항목·단계 템플릿.

## 25.7 병행운영·전환 절차

1. **준비:**
   - 운영 서버에서 HeidiSQL로 `bbakggum`을 백업한다.
   - 신규 서버(또는 같은 서버)에 `bbakggum_legacy`로 복원한다. `dev-db.ps1 legacy`와 같은 방식으로 이름을 바꾼다.
   - 운영 이름 그대로 복원하면 안 된다.
2. **신규 DB:**
   - DDL을 적용한다.
   - `db\migration\migrate.ps1 -Server … -Port … -User … -AskPassword`를 실행한다. 비밀번호는 실행 중에만 환경변수로 클라이언트에 전달된다.
   - 검증 결과·문제 목록을 확인한다.
3. **병행운영 (쓰기 주체 = 구 시스템) — V3.26: 구 시스템을 쓰지 않으므로 생략.** 필요해지면 아래 방식:
   - 구 시스템으로 계속 일한다. 신규 시스템은 조회·교육·검증용이다.
   - 주기적으로 새 덤프를 받아 1·2를 반복한다 (재실행, §25.6).
   - 이 기간의 신규 시스템 입력은 다음 `-Fresh` 에서 사라진다. 검증의 "매핑 없는 신규 행" WARN으로 보인다.
4. **전환 (일괄):**
   1. 구 시스템 입력을 중지한다.
   2. 마지막 덤프를 받는다.
   3. `bbakggum_v2`를 지우고 DDL을 다시 적용한다.
   4. `migrate`를 실행한다 (= `-Fresh`).
   5. 검증 PASS를 확인한다.
   6. 신규 시스템으로 쓰기 시작하고, 구 시스템은 읽기 전용으로 둔다.
5. **전환 직후 할 일:**
   - 공정 경로(heat_process_operation) 등록. 구 공정에는 경로가 없다. 주공정 판정·후공정 투입이 경로 기준으로 동작하려면 필요하다.
   - 교대(work_shift)·작업자 배정
   - 양식(엑셀) 등록, 품목 연결
   - 도장·도면 첨부
   - 진행 중 계획 다시 수립
   - 사용자·역할

**전환은 일괄로 한다 (V3.26, §12 ⑩).** 모듈 단위 전환(§10 순서)에는 신규 → 구 역방향 동기화가 필요하나 만들지 않는다.

---

# 26. 미구현 기능 정리와 추가 계획 (V3.25)

## 26.1 구 폼 대비 미구현·부분 구현 (2026-10-01 대조)

구 폼 50개(`docs/legacy_forms/`) 중 업무 흐름(수주 → 계획 → 작업 → 검사 → 부적합 → 출하·마감 → 조회)은 6·7단계에서 구현을 마쳤다. 남은 것은 아래와 같다.

| 구 폼·기능 | 현재 | 단계 |
|-|-|-|
| F_DowntimeInput · F_DowntimeStatus (비가동) | 테이블만 있음. 스케줄·대시보드가 읽기만 함 | 9 |
| F_MaintenanceForm (설비 보전) | 테이블만 있음. 설비 메뉴 그룹이 비어 있음 | 9 |
| 작업자 배정 (구 t_workerassignment) | 테이블만 있음 | 9 |
| 공정검사 항목 기준정보 (unit_inspection_item) | 메뉴 없음 | 9 |
| 고정 양식 출력물 8종 (공정이동표·제품라벨·작업지시서·LOT 라벨·작업일보·작업표준서·마감내역서·진행현황표) | 용도만 등록됨. 데이터 공급원은 성적서·출하 2개뿐 | 10 |
| 목록 엑셀·CSV 내보내기 (구 F_CustomerForm 등) | 없음 | 10 |
| 라인검사 (구 t_lineinspection) | 테이블만 있음 | 11 |
| F_DepthVHardness (경화깊이-경도 그래프) | 없음 | 11 |
| F_xBar (SPC X̄-R 관리도) | 없음 | 11 |
| F_InspectionStatus (검사 현황 집계) | 검사 목록 필터로 대체 (△) | 11 |
| F_SignatureDialog (공정 확인 서명) | 없음 | 12 |
| F_GraphicWindow (작업 현황판) | 생산계획·작업 화면으로 나눠 대체 (△) | 12 |
| F_DashForm 월별 추이 | 일별만 있음 (△) | 12 |
| 단말별 프린터·자동 인쇄 (`workstation_print_setting`) | 없음 | 12 |
| 운영 배포·전환 | 이관 스크립트만 있음 (§25) | 13 |

대상 아님: F_TemplateDesigner(좌표형 디자이너 — 폐기 결정), F_WorkDetailForm·F_PlanRegisterForm·F_PlanDetailForm(빈 폼).

## 26.2 구현 개념 결정 (사용자 답변 2026-10-01)

| # | 항목 | 결정 |
|-|-|-|
| A | 비가동 | **한 화면에서** 입력·현황을 함께 다룬다. 계획 비가동(스케줄 제외 구간)과 고장 비가동을 같은 화면에서 구분한다 |
| B | 설비 보전 | **설비 보전과 측정기구 관리는 별도.** 측정기구는 교정 이력을 따로 관리한다 (구 `recordtype` 혼용 정리) |
| C | 작업자 배정 | **주간·야간 수동 배치.** 화면에 설비와 인원을 표시하고, 설비별 주간·야간 칸에 인원을 **끌어다 놓아** 배치한다 |
| D | 라인검사 | **품질·생산 메뉴 둘 다에서 접근.** 수주 또는 LOT 선택 → 품목 선택 → 검사 항목 입력·등록. **검사 수량(시료 수) 제한 없음** |
| E | 경화깊이 | 경화층 측정값으로 **경화깊이-경도 그래프 표시 + 유효경화깊이 계산**. 그래프는 **성적서에도 넣는다** |
| F | SPC | 선택 조건: **품목, 설비, 위치, 시료 개수, 군 구분(LOT / 일자 / 월)**. 관리도는 구와 같은 X̄-R |
| G | 공정 확인 서명 | **직접 서명 날인이 필요**하다. 입력 시각을 표시하고, **실제로 그 시점에 실행했는지가 핵심**이다 (사후 일괄 서명 방지 — 서명 시각·서명자를 기록하고 고칠 수 없게) |
| H | 출력물 | **구 폼마다 정해진 출력물을 그대로 둔다** (어느 화면에서 무엇을 출력하는지는 구 폼 기준) |
| I | 모니터링 | **모니터링 화면을 만들고, 전 공정을 섹터별로 돌아가며(로테이션) 표시**한다 (현장 TV) |
| ④ | QuestPDF | 구 프로젝트와 같은 Community 유지 (§12 ④) |
| ⑤ | 업체 전용 거래명세표 | 없음. 필요하면 엑셀 양식으로 등록해 사용 (§12 ⑤) |

## 26.3 추가 단계

| 단계 | 범위 | 근거 |
|-|-|-|
| 9 | 설비·인력: 비가동(한 화면), 설비 보전, 측정기구 교정 이력, 교대·작업자 배치 보드(끌어다 놓기), 공정검사 항목 메뉴 | A·B·C |
| 10 | 출력물 완성: 구 폼별 출력물을 고정 양식 렌더러 + 데이터 공급원으로, 각 화면에 출력 버튼, 목록 엑셀 내보내기 | H |
| 11 | 품질 분석: 라인검사(품질·생산 접근), 경화깊이 그래프·유효경화깊이(성적서 포함), SPC X̄-R, 검사 현황 | D·E·F |
| 12 | 현장: 공정 확인 서명(시각 기록·변경 불가), 모니터링 로테이션 화면, 대시보드 월별, 단말 프린터 | G·I |
| 13 | 운영 준비·전환: 서버 배포·백업, 최신 덤프 이관 리허설, 공정 경로·양식 등록, 병행운영 후 전환 | §25.7, §12 ⑩~⑬ |

## 26.4 화면에 "준비 중"으로 보이는 항목 (2026-10-01 확인)

| 위치 | 현재 표시 | 원인 | 해결 단계 |
|-|-|-|-|
| 사이드바 **설비** 메뉴 그룹 | ~~회색(선택 불가)~~ → V3.27~V3.29 해결 | 하위 화면이 하나도 없음 (`menuItems.tsx` — route 없는 그룹은 비활성) | 9: 비가동, 설비 보전, 작업자 배치 |
| 시스템 > 출력 양식 — 용도 목록 | 공정이동표·제품표시 라벨·작업지시서·LOT 라벨·작업일보·작업 진행 현황표·작업표준서·마감내역서에 **"준비 중"** 태그 | 데이터 공급원이 성적서·출하 2개만 구현됨 → 나머지 용도는 발행 불가 | 10 |
| (메뉴 추가 예정) 구매 | 없음 | §27 신규 영역 | 13 |

메뉴가 있는데 화면이 없는 경우(`PlaceholderPage`)는 현재 없다. 등록된 메뉴는 모두 화면이 있다.

## 26.5 단계 조정 (V3.26)

| 단계 | 범위 |
|-|-|
| 9 | 설비·인력 — 설비 메뉴 그룹 채움 (비가동·설비 보전·작업자 배치) + 측정기구 교정 + 공정검사 항목 |
| 10 | 출력물 완성 — 출력 양식 "준비 중" 8종 해소 + 목록 엑셀 내보내기 |
| 11 | 품질 분석 |
| 12 | 현장 (서명·모니터링) |
| 13 | **구매관리** (§27) — 구매처·구매 품목·구매(발주·입고)·현황 출력 |
| 14 | 운영 준비·전환 — 일괄 전환 (§12 ⑩). 구매관리는 독립 영역이라 전환 뒤에 해도 된다 |

---

# 27. 구매관리 (13단계 설계, V3.26)

구 시스템에 없던 영역이다. 구 거래처의 "비매출처"(= 매입처, §12 ⑬)를 구매처로 쓴다. 기본 구성은 **구매처 등록 → 구매 품목 등록 → 구매 등록(발주 → 입고) → 현황 조회·출력**이다. 열처리 공장의 구매 대상은 가스(RX·NH3·N2·LNG)·소입유·소모품·부품·외주 가공 등이다.

## 27.1 범위

| 포함 (13단계) | 나중 (필요하면) |
|-|-|
| 구매처 = 거래처(구분 매입처·매출+매입) | 구매 요청·결재 |
| 구매 품목(자재) 기준정보 | 자재 재고(출고·재고 실사), 안전재고 알림 |
| 구매 등록: 발주 전표 → 입고(부분 입고 가능) | 매입 마감·세금계산서 대조 |
| 현황: 기간·구매처·품목별 구매, 미입고, 월별 매입 집계 + 출력 | 설비 보전(§9단계)·외주 가공과 연결 |
| 발주서 출력 (FIXED 기본 + 엑셀 양식 선택) | |

## 27.2 테이블 (13단계에서 DDL 추가)

| 테이블 | 내용 | 주요 컬럼 |
|-|-|-|
| `customer` (기존) | 구매처 = `customer_type` PURCHASE·BOTH | 그대로 사용. 구매처 화면은 거래처 화면을 구분으로 거른 것 |
| `purchase_item` | 구매 품목(자재) | `purchase_item_code`, `purchase_item_name`, `specification`, `unit_code`, `item_category`(공통코드 `PURCHASE_ITEM_CATEGORY`: 가스·유류·소모품·부품·외주·기타), `default_supplier_customer_id`, `unit_price`, `is_active` |
| `purchase_order` | 구매(발주) 전표 | `purchase_order_no`(설정 `purchase_order.number_format`), `order_date`, `supplier_customer_id`, `due_date`, `status`(공통코드 `PURCHASE_STATUS`: 발주 ORDERED → 부분입고 PARTIAL → 입고완료 RECEIVED / 취소 CANCELLED), `supply_amount`·`vat_amount`·`total_amount`, 구매처 Snapshot, `remark`, `row_version`, `is_deleted` |
| `purchase_order_item` | 발주 품목 행 | `purchase_order_id`, `line_no`, `purchase_item_id`, `order_qty`, `unit_price`, `amount`, 품명·규격 Snapshot, `remark` |
| `purchase_receipt` | 입고 기록 (부분 입고 여러 번) | `purchase_order_item_id`, `receipt_date`, `receipt_qty`, `receiver_employee_id`, `remark` |

- 입고 잔량·전표 상태 진행은 저장형 잔량 없이 입고 합계로 계산한다 (§1.2 원칙). 상태는 입고 등록·취소 때 서비스가 다시 계산해 저장한다 (목록 필터용).
- 금액은 출하와 같은 계산 (`sales.vat_rate`, `sales.amount_rounding` — 이름은 영업이지만 세율 공통. 필요하면 `purchase.vat_rate` 분리).
- 감사(`audit_log`)·낙관적 잠금(`row_version`)은 다른 전표와 같다.

## 27.3 화면·메뉴

| 메뉴 키 | 화면 | 내용 |
|-|-|-|
| `purchase.supplier` | 구매처 | 거래처 중 매입처·매출+매입만. 등록 시 구분 = 매입처 기본 (거래처 기준정보와 같은 데이터) |
| `purchase.item` | 구매 품목 | 단순 기준정보 (`MasterCatalog` 정의 — 화면·API 자동) |
| `purchase.order` | 구매 등록 | 목록(기간·구매처·상태·검색) + 전표 창: 구매처 선택 → 품목 행(품목 선택 시 기본 단가) → 저장. 전표 창에서 **입고 등록**(행별 입고 수량·일자, 부분 입고), 입고 취소, 전표 취소(입고 전만), **발주서 출력**(양식 선택 콤보 — §12 ⑤와 같은 방식) |
| `purchase.status` | 구매 현황 | 보기: 기간별 구매(전표·품목) / 미입고(발주 − 입고) / 월별 매입 집계(구매처 × 월). **현황 출력**(PDF·엑셀), 목록 엑셀 내보내기 |

- 출력 용도 추가: `PURCHASE_ORDER`(발주서, 데이터 공급원 `PURCHASE_ORDER`), `PURCHASE_STATUS`(구매 현황표, 데이터 공급원 `PURCHASE_PERIOD`).
- 대시보드: 구매 권한이 있으면 이번 달 구매 금액·미입고 건수 패널 (13단계 끝에).

## 27.4 확인할 것 (13단계 시작 전)

| # | 질문 |
|-|-|
| P1 | 구매 대상 분류 (가스·소입유·소모품·부품·외주 가공 …) 와 품목 수 규모 |
| P2 | 발주 없이 바로 입고(현장 구매)도 하는지 — 하면 "발주 = 입고 완료" 한 번에 등록 |
| P3 | 매입 마감(구매처별 월 마감)이 필요한지 — 출하 마감(§23.7)과 같은 구조로 확장 가능 |
| P4 | 자재 재고(사용량 출고)를 관리할지 — 가스·소입유 사용량을 작업 LOT 과 연결할지 |
| P5 | 발주서 양식 (당사 양식 있는지 — 있으면 엑셀 양식으로 등록) |

---

# 28. 설비·인력 (9단계)

| 순서 | 범위 | 상태 |
|-|-|-|
| 9-① | 비가동 (입력·현황 한 화면) + 공정검사 항목 기준정보 | V3.27 완료 |
| 9-② | 설비 보전 + 측정기구 교정 이력 | V3.28 완료 |
| 9-③ | 작업자 주·야 배치 보드 (끌어다 놓기) | V3.29 완료 |

## 28.1 비가동 — 9-① (구 F_DowntimeInput · F_DowntimeStatus, 결정 A)

화면 **설비 > 비가동** (`equipment.downtime`) 한 화면에 입력과 현황을 함께 둔다.

| 영역 | 내용 |
|-|-|
| 조건 | 기간(기본 이번 달 1일~오늘), 설비, 구분(전체·고장·계획) |
| 현황 | 건수·비가동 시간·진행 중 건수, 설비별(고장·계획 시간), 사유별 합계. 진행 중은 지금까지 시간 |
| 목록 | 일자·설비·구분·시작·종료·시간·사유·보고자·비고. 진행 중은 [종료] (지금 시각) |
| 등록·수정 창 | 설비, 구분(고장/계획), 시작(기본 지금), 종료, 시간(분 — 종료를 모를 때만), 사유(공통코드 `DOWNTIME_REASON`), 보고자(사원), 비고 |

| API (`/api/downtimes`) | 규칙 |
|-|-|
| `GET ?from&to&equipmentId&planned` | 기간에 걸친 행 + 기간과 관계없이 진행 중(종료·시간 없음)인 행 |
| `POST` / `PUT /{id}` | 시작 필수. 종료 ≥ 시작. **계획 비가동은 종료 필수** (스케줄 제외 구간). 시간(분) = 종료 − 시작 (분 단위, 초 버림), 종료가 없으면 입력값. 일자 = 시작일. 사유는 사용 중인 공통코드만 |
| `POST /{id}/end` | 진행 중 종료 (기본 지금) |
| `DELETE /{id}` | 삭제 (감사 기록) |

- **계획 비가동을 등록·수정·삭제하면 그 설비의 계획을 다시 계산**한다 (`SchedulingService.RecalculateAsync`, §7 제외 구간). 설비를 바꾸면 전·후 설비 모두.
- 구 기본값 08~09시 하드코딩 제거 — 화면이 지금 시각을 기본으로 넣는다.
- 대시보드 "비가동 중"(§24.3)은 같은 테이블을 읽는다.
- 비가동 사유는 공통코드 `DOWNTIME_REASON` (사용자 관리, 이관 시 구 t_combolist 에서 채움).

## 28.2 공정검사 항목 — 9-①

기준정보 **공정검사 항목** (`master.unit_inspection_item`, 단순 기준정보 정의 — 화면·API 자동): 코드·이름·단위공정(비우면 공통)·순서·사용. 라인검사(11단계)의 측정 항목. 구 t_unitinspectionitem 6건은 이관됨.

## 28.3 설비 보전 — 9-② (구 F_MaintenanceForm)

화면 **설비 > 설비 보전** (`equipment.maintenance`).

| 영역 | 내용 |
|-|-|
| 조건 | 기간(기본 두 달 전 1일~오늘), 설비, 상태, 검색(내용·부위·업체) |
| 점검 예정 | 설비별 가장 최근 보전의 "다음 점검 예정일"이 지났거나(`지남`) 설정 `maintenance.due_soon_days`(기본 14일) 안이면(`임박`) 표시. 누르면 그 설비로 거름 |
| 목록 | 보전일·설비·구분·상태·내용·부위·업체·작업자·비용·다음 점검·첨부 수, 아래 줄에 건수·비용 합계(취소 제외) |
| 창 | 내용 탭: 설비, 구분(공통코드 `MAINTENANCE_TYPE`), 보전일, 상태(`MAINTENANCE_STATUS` 접수·진행·완료·취소), 시작·완료, 작업자, 수리·교체 부위, 외부 업체, 비용, 내용, 결과, 다음 점검 예정일. 사진·자료 탭: 공통 첨부(보전 사진 `MAINTENANCE_PHOTO`·기타) |

| API (`/api/maintenances`) | 규칙 |
|-|-|
| `GET ?from&to&equipmentId&status&search`, `GET /due`, `GET /{id}` | 상세 = 보전 + 첨부 |
| `POST` / `PUT /{id}` (row_version) / `DELETE /{id}?rowVersion` | 구분은 사용 중인 공통코드 (이관한 구 문자열은 바꾸지 않으면 그대로 저장 가능). 완료 ≥ 시작, 다음 점검일 ≥ 보전일, 비용 ≥ 0. **완료로 저장하는데 완료 시각이 없으면** 오늘 보전 = 지금, 지난 날짜 = 보전일(시작 시각이 있으면 시작 시각). 삭제는 첨부까지 |
| `POST·GET·DELETE /{id}/attachments…` | 공통 첨부 (품목 첨부와 같은 규칙 — `AttachmentStore`) |

- DDL: `maintenance` 에 `repair_part`·`vendor_name`·`cost`·`next_due_date`·`row_version` 추가 (구 repairpart·repaircost·nextrepairdate). 외부 업체는 구매관리(§27) 전까지 문자열.
- 공통코드: `MAINTENANCE_TYPE`(사용자 관리 — 기본 수리·점검·예방정비·부품 교체), `MAINTENANCE_STATUS`(시스템), 첨부 종류 `MAINTENANCE_PHOTO`·`CALIBRATION_CERT`.
- 공통 첨부 저장·조회·삭제를 `Infrastructure/Files/AttachmentStore` 로 모음 (품목·보전·교정이 같이 씀). 웹 `components/AttachmentList`.

## 28.4 측정기구 교정 — 9-② (결정 B: 설비 보전과 별도)

화면 **품질 > 측정기구 교정** (`quality.calibration`). 측정기구 자체(코드·이름·종류·교정 주기)는 기준정보 > 측정기구.

| 영역 | 내용 |
|-|-|
| 왼쪽 | 측정기구별 상태(지남·임박·미정·정상 — 지남·임박 먼저), 주기, 최근·다음 교정일, 이력 수. 임박 기준 = 설정 `instrument.calibration_due_soon_days`(기본 30일) |
| 오른쪽 | 선택한 기구의 교정 이력 + [교정 등록] |
| 창 | 교정일, 판정(공통코드 `DECISION` — 합격·불합격·조건부, 항목 전용 '해당없음' 제외), 교정 기관, 성적서 번호, 다음 교정일, 비용, 비고. 성적서 파일 탭: 공통 첨부(`CALIBRATION_CERT`) |

| API (`/api/calibrations`) | 규칙 |
|-|-|
| `GET /instruments?includeInactive`, `GET ?instrumentId`, `GET /{id}` | |
| `POST` / `PUT /{id}` / `DELETE /{id}` | 저장·삭제마다 **측정기구 최근 교정일 = 마지막 교정일, 다음 교정일 = 그 교정의 다음 교정일(비우면 교정일 + 교정 주기)**. 더 오래된 교정을 넣어도 최근 값은 그대로, 최근 교정을 지우면 이전 교정으로 되돌림 |
| `…/{id}/attachments` | 교정 성적서 파일 |

- DDL: 테이블 `instrument_calibration` 신규.
- 이관: 구 t_maintenance 중 측정기구 기록(instrumentid)은 `instrument_calibration` 으로 (설비 기록은 `maintenance`) — §25.3 갱신.

## 28.5 작업자 주·야 배치 — 9-③ (결정 C, 구 t_workerassignment)

화면 **설비 > 작업자 배치** (`equipment.worker_assignment`). 자동 배정 없이 사람이 직접 배치한다.

| 영역 | 내용 |
|-|-|
| 위 | 작업일(전날·다음날·오늘), [전날 배치 복사] — 없는 칸만 더함 (이미 있는 배치 유지) |
| 왼쪽 작업자 | 사원 기준정보의 "작업자 배정 대상"·재직 사원 (그 날 배치된 사람은 대상이 아니어도 보임), 조별 묶음, 검색. 교대별 배치 수 표시 (주1·야0) |
| 오른쪽 보드 | 행 = 사용 중 설비(표시 순서), 열 = 사용 중 교대(기준정보 > 교대, 순서). 설비 옆에 진행 중 LOT. 칸 = 배치 칩 (실선 = 주, 점선 = 보조) |

| 조작 | 동작 |
|-|-|
| 작업자를 칸으로 끌어다 놓기 | 배치 (등록 권한) |
| (태블릿) 작업자를 누르고 칸을 누르기 | 배치 — 안내는 제목 옆에 (표가 밀리지 않게) |
| 칩을 다른 칸으로 끌기 | 이동 (수정 권한) |
| 칩 두 번 누르기 | 주 ↔ 보조 (수정 권한) |
| 칩을 왼쪽 목록으로 끌기 / × | 해제 (삭제 권한) |

| API (`/api/worker-assignments`) | 규칙 |
|-|-|
| `GET /board?workDate` | 교대·설비·작업자·배치를 한 번에 |
| `POST` / `PUT /{id}` / `DELETE /{id}` | 같은 칸(작업일·교대·설비)에 같은 사람은 한 번 (`ASSIGNMENT_DUPLICATE`, DB UNIQUE). 같은 사람을 같은 교대 여러 설비에 둘 수 있음 (여러 대 담당). 사용 중인 교대·설비만 |
| `POST /copy {fromDate, toDate}` | 사용 중인 교대·설비·재직 사원만, 없는 칸만 |

- 바뀌면 SignalR `workerAssignmentChanged {workDate}` → 보고 있는 다른 화면이 바로 다시 읽음.
- 교대가 없으면 "기준정보 > 교대 에서 주간·야간을 등록" 안내. (교대의 첫 시작 시각은 스케줄 작업일 시작이기도 하다 — §7)
- 끌어 놓기는 브라우저 기본 Drag & Drop (생산계획 Gantt 와 같은 방식), 터치 기기는 누르기 방식으로.

---

# 29. 출력물 완성 (10단계)

구 폼별 출력물 (결정 H — 구 폼마다 정해진 것을 그대로):

| 구 화면 → 신규 화면 | 출력물 | 상태 |
|-|-|-|
| F_IncomeAddForm·F_IncomeForm → 수주(입고) | 공정이동표, 제품표시 라벨 | 10-① |
| F_GasForm → 작업(투입) | 작업일보 | 10-② |
| F_WorkStandardForm → 작업표준 | 작업표준서 | 10-② |
| F_OutForm → 출하 | 거래명세표 | 6단계 (발행 버튼 공용화 10-①) |
| F_InspectionAddForm → 검사 | 성적서 | 6단계 |
| F_CustomerForm·F_PartForm·F_InspectionForm·F_MonthlyClosing | 목록 엑셀·CSV·PDF 내보내기 (구 ExportHelper) | 10-③ |

- 구 `ProgressSheet`(작업 진행 현황표)는 클래스만 있고 어느 화면에서도 쓰지 않는다 (F_IncomeForm 의 "진행 현황표" 버튼은 실제로 공정이동표를 출력, `F_IncomeForm.cs` 610~630행). 처리는 10-③.

| 순서 | 범위 | 상태 |
|-|-|-|
| 10-① | 발행 공통(화면 권한·여러 건 한 파일·양식 선택 버튼) + 수주 공정이동표·제품표시 라벨 | V3.30 완료 |
| 10-② | 작업일보·작업표준서 (+ 작업 LOT·작업표준 데이터 공급원) | 대기 |
| 10-③ | 마감 출력·진행현황표 정리·목록 내보내기 | 대기 |

## 29.1 발행 공통 + 수주 출력 — 10-①

**발행 공통 (업무 화면)**

| API (`/api/print`) | 규칙 |
|-|-|
| `GET /choices?purposeCode` | 용도의 사용 중·현재 버전 있는 양식 (기본 먼저). 권한 = 데이터 공급원 화면 읽기 |
| `POST /documents {purposeCode, sourceIds[], printTemplateId?}` | 발행. 권한 = **데이터 공급원의 화면 읽기** (공급원마다 `MenuKey`: 검사 대상 = 검사, 출하 = 출하, 수주 = 수주 …, §12 ⑦ 일반화). 한 번에 200건까지 |

- **여러 건 = 한 파일**: FIXED 는 렌더러가 같은 문서에 페이지를 이어 붙인다 (`IFixedRenderer.Compose`). EXCEL 은 건마다 채운 통합문서의 시트를 한 통합문서로 모아(`_2`, `_3` …) PDF 로 바꾼다. 발행 이력(`print_log`)은 건마다 1행 (같은 파일 해시), 발행본 보관은 1건 발행일 때만.
- 양식: 지정하지 않으면 건마다 품목(+업체) → 품목 → 용도 기본으로 고르고, 서로 다르면 "양식을 골라 주세요" (`TEMPLATE_DIFFERS`).
- 웹 `components/PrintButton`: 양식이 둘 이상이면 양식 선택 콤보(기본 먼저)를 붙인다. PDF 는 새 탭(팝업이 막히면 내려받기), 엑셀은 내려받기. 출하 화면 거래명세표도 이 버튼으로 바꿈 (§12 ⑤ 콤보 그대로).
- 렌더러 공통: `LayoutOptions`(옵션 읽기, 컬럼 목록), `LayoutValues`(옵션 키 snake_case ↔ 값 키 PascalCase), `Barcodes`(ZXing CODE_128 → **SVG** — 구는 Windows 비트맵, 서버 OS 무관하게).

**수주 출력** (화면 수주(입고): 목록에서 행을 골라 [공정이동표]·[제품표시 라벨], 입고 행 창에도 버튼)

| 출력물 | 렌더러 | 내용 (구와 같음) |
|-|-|-|
| 공정이동표 | `PROCESS_SHEET` (A4) | 제목 + 바코드(수주번호) / 보안품 · 기종 · **우선순위(공통코드 `PRIORITY` 표시명·색)** / 좌측 정보 16행(거래처·품명·규격·기종·재질·수량 ea·중량 kg·단중 kg·요구경도·심부경도·경화층·조직·공정·고객로트·코일번호) × 공정 기록 16행(순서·공정명 = **수주 공정 경로 단계**, 작업로트·T.NO·수량·작업자·비고는 현장 수기) / 특기사항([별도관리]·[재작업]·[반입], [비고]) + 바코드 |
| 제품표시 라벨 | `PRODUCT_LABEL` (65×80mm) | 제목 / 14항목 (값 한 줄, 넘치면 …) / 바코드 |

- 데이터 공급원 `SALES_ORDER` = **입고 행(sales_order_item) 1건**. 수주번호(`SalesOrderNo`) = 스캔하는 입고번호 `order_item_no` (구 IncomeNo), 묶음 번호는 `OrderBundleNo`. 치환자 사전 30개(엑셀 양식용) DDL 추가.
- 구와 다른 점: 공정 순서는 구 품목·공정의 `subp1~16` 문자열 대신 수주 행의 공정 경로(`heat_process_operation`). 특기사항의 구 "[Grade] 별도관리" 는 별도관리·재작업·반입 표시로.
