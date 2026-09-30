# 기존 WinForms 폼 분석 (1단계)

- 대상: `D:\Programming\ProductManager\ProductManager\F_*.cs` — 폼 50개, 약 29,000행 (Designer 제외)
- 분석일: 2026-09-30
- 방법: 폼별 메서드 구조·이벤트 흐름·서비스/SQL 호출을 추출하고 업무 규칙이 있는 구간만 정독

## 문서 목록

| 문서 | 대상 폼 |
|-|-|
| [F_GasForm.md](F_GasForm.md) | 작업 등록 (단위공정 공통 작업 화면) |
| [F_InspectionAddForm.md](F_InspectionAddForm.md) | 검사 등록·확정·재검사·성적서 |
| [F_WorkPlanForm_F_GraphicWindow.md](F_WorkPlanForm_F_GraphicWindow.md) | 스케줄 배정 / 작업 현황판 |
| [F_OutForm_F_OutAddForm_F_MonthlyClosing.md](F_OutForm_F_OutAddForm_F_MonthlyClosing.md) | 출하 등록·목록 / 월마감 |
| [F_IncomeForm_F_IncomeAddForm.md](F_IncomeForm_F_IncomeAddForm.md) | 입고(수주) 등록·목록 |
| [F_WorkStandard_forms.md](F_WorkStandard_forms.md) | 작업표준 등록·목록·템플릿·복사 |
| [기타_폼_요약.md](기타_폼_요약.md) | 나머지 37개 (품질·생산보조·영업재고·기준정보·시스템) |

## 전 영역 공통 문제

| 문제 | 발견 위치 | 신규 원칙 |
|-|-|-|
| 저장이 **트랜잭션 없이 여러 번의 개별 호출** (잔량·플래그 갱신) | 작업 저장, 출하 저장, 입고 저장 | 업무 단위 한 트랜잭션 |
| 잔량을 **저장형 컬럼**으로 가산·차감 (투입·출고·배정 잔량, isinput) | `t_income`, `t_inputwaiting` | 거래 행 합계로 계산 (VIEW) |
| 번호 채번 `MAX+1` (잠금·UNIQUE 없음) | LOT, 검사, 입고, 출하 번호 | UNIQUE + 재시도 또는 채번 테이블 |
| 표시 문자열로 로직 분기 ("합격", "재처리", "투입" …) | 여러 곳 | 코드값 + 공통코드 |
| 파일을 PC 로컬 폴더·절대경로로 관리 | 양식, 도장, 도면, 조직사진, 차트 | 서버 저장소/DB + 첨부 테이블 |

## 설계 반영 사항 종합

### A. 사용자 확인 없이 반영 가능 (분석으로 확정)

| # | 내용 | 근거 | 대상 |
|-|-|-|-|
| A1 | 설비당 진행 중(투입 상태) 작업 1건 제약 | W1 | `production_work` UNIQUE |
| A2 | 조건 항목(관리항목)도 설비·단위공정 템플릿에 속함 → `step_template_condition` | T1 | 신규 테이블 |
| A3 | 입고번호(=스캔하는 수주번호)는 품목 행 번호 → `sales_order_item.order_item_no` UNIQUE | N1 | 컬럼 |
| A4 | 수주 행 요구사항 Snapshot(경도·심부경도·경화층·조직), 고객 작업지시번호, 행 단위 우선순위, 별도관리 | N2~N5 | 컬럼 |
| A5 | 검사구분 입고/공정/출하 (`TI`/`TP`/`TO`), 재검사 연결 | I1, I3 | `inspection.inspection_type`, `reinspection_of_id` |
| A6 | 구 `isinspectiondone` = **라인검사 완료** → 호환 VIEW 정정 | G6 | VIEW |
| A7 | 출하 시험편 수량 | S2 | `shipment_item.test_specimen_qty` |
| A8 | 출하 전표 거래처 사업자정보 Snapshot, 마감일, 품목합산 출력 | S4, S5 | `shipment` 컬럼 |
| A9 | 마감 상태는 전표 단위 (미마감/마감/이월) + 마감월, 자동이월 | S6, S7 | `shipment.closing_status`, `closing_year/month` |
| A10 | 부적합 처리구분 선별·보류 추가, 판정자·완료자 사원 FK | D1~D3 | `defect_occurrence` CHECK |
| A11 | 공통 첨부 테이블 (품목 도면·이미지, 조직사진, 경화층 차트, 검사 첨부) | I6, 품목 | `attachment` |
| A12 | 설정 추가: 완료시각 반올림 분, 검사·출하·입고 번호 형식 | 하드코딩 | `system_setting` |
| A13 | 공통코드 추가: `INSPECTION_TYPE`, `CLOSING_STATUS` | 하드코딩 | `common_code` |
| A14 | 이관 규칙: LOT번호 원문 보존(두 형식), 측정상세 중복 제거, 작업시간 시간→분, 설비 All 복사본 통합, 표준 여러 행 → Version | G9, I7, T2, T3, T6 | 설계 §10 |

### B. 사용자 확인 결과 (2026-09-30)

| # | 질문 | 답변 → 반영 |
|-|-|-|
| B1 | 검사 측정값 공통 / 품목별 | LOT 입력 → 투입 품목 중 대상 선택 → **검사 결과 공통 적용, 성적서는 품목별 출력** → `inspection_item`을 `inspection`에 연결, 대상 판정 컬럼 제거 |
| B2 | 배정 작업시간 | **병합 시 작업시간이 긴 쪽 기준** → 설계 §7 |
| B3 | 지연 시 뒤 배정 | **이후 배정 LOT 모두 계획시간 자체가 뒤로 밀리고 화면 갱신** → 설계 §7 |
| B4 | kg 단가 | **품목에 단가 적용 구분 있음** (구 `ea`/`kg`/`ch`) → `part.price_basis`, 출하 금액 단위별 계산 |
| B5 | 단위공정 자동 등록 | **기준정보에서 등록** |
| B6 | 같은 수주 제한 | **주 LOT 생성 전후로 분리 적용**: 전(전공정·주공정)은 LOT당 같은 수주 1회, 후(후공정)는 주 LOT번호 기준 — 다른 주 LOT이면 같은 수주도 한 LOT에 함께 투입 가능, 검사는 주 LOT 단위 분리 → `UNIQUE(production_work_id, sales_order_item_id, IFNULL(main_work_id,0))` |
| B7 | 불량수량 입력 | **별도 입력 안 함** — 구조 확인: 작업 화면 투입 행 불량수량 입력 + 검사 불합격은 대상 전체 수량 |
| B8 | PC별 부서 | **사용자가 부서를 복수 선택** (공용 PC: 생산+영업) → `app_user_role` N:M, 메뉴 합집합 |

A1~A14는 설계 V3.8·DDL에 반영 완료 (`db/dev/dev-db.ps1 smoke` 통과).
