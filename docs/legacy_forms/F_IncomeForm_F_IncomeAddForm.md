# 입고(수주) — F_IncomeAddForm, F_IncomeForm

| 항목 | F_IncomeAddForm | F_IncomeForm |
|-|-|-|
| 파일 | `F_IncomeAddForm.cs` (958행) + `Services/IncomeAddService.cs`, `OrderBufferService`, `Models/IncomeModels.cs` | `F_IncomeForm.cs` (870행) + `Services/IncomeService.cs` |
| 역할 | 입고 등록·수정, 저장 후 공정이동표·제품라벨 출력 | 입고 목록 조회·삭제, 엑셀, 공정이동표·라벨 출력(미리보기 포함) |
| 신규 대응 | 수주(입고) 등록 / `sales_order`, `sales_order_item` | 수주 목록 |

## F_IncomeAddForm — 입고 등록

1. 거래처 선택 → 거래처 품목 목록(기종 키워드 검색) → 더블클릭/담기로 **장바구니**
2. 행별 입력: 수량(필수), 고객로트, 코일번호, 작업지시번호, 우선순위, 비고 등. 품목 정보(품번·규격·기종·재질·단중·단가·단위·공정·요구경도·심부경도·경화층·조직)는 품목에서 복사
3. **별도관리** 체크 → `Grade` 컬럼에 문자열 `"별도관리"` 저장 (컬럼 용도 전용)
4. 저장 (`IncomeAddService.Save(inDate, isReturn)`)
   - **장바구니 행마다 입고번호 1개** `I{yyMMdd}-{NNN}` (입고일 기준 순번) — 헤더 없음, **1 입고번호 = 1 품목 행**
   - 중량 = 수량 × 단중, **금액 = 수량 × 단가**
   - 잔량 3종을 **저장형**으로 초기화: `inputremainqt`(투입), `outremainqt`(출고), `planremainqt`(배정) = 수량
   - 반입(`isReturn`)은 저장 단위 전체에 적용
   - 트랜잭션 없음 (행별 INSERT)
5. 저장+공정이동표 출력 / 저장+라벨 출력 (`PrintDoc/ProcessSheet`, `ProductLabel`) → 설정 프린터
6. 수정 모드 (`F_IncomeAddForm(inno)`): 한 입고번호 수정, 별도관리 변경 불가

## F_IncomeForm — 입고 목록
- 기간·거래처·검색필드 조회, 체크 합계, 엑셀 내보내기
- 삭제 (체크 행)
- **공정이동표**(진행 체크 시트) 출력·미리보기, **제품라벨** 출력·미리보기 — 여러 건 일괄

## 읽기·쓰기 테이블

| 테이블 | 용도 |
|-|-|
| `t_income` | 입고 = 수주 품목 행 (잔량 3종·isinput·우선순위·스펙 복사 포함) |
| `t_customer`, `t_part`, `t_heatprocess` | 거래처·품목·공정 조회 |

## 하드코딩

| 값 | 신규 |
|-|-|
| 입고번호 `I{yyMMdd}-{NNN}` | 설정 `sales_order.item_number_format` |
| `"별도관리"` 문자열을 `Grade`에 저장 | 전용 컬럼 `is_separately_managed` |

## 신규 설계에 반영할 사항

| # | 발견 | 조치 |
|-|-|-|
| N1 | **입고번호 = 품목 행 번호** (헤더 없음). 현장에서 스캔하는 "수주번호"가 이것 | `sales_order_item`에 **`order_item_no` (UNIQUE, 예 `I260930-001`)** 추가 — 스캔·투입·출하·검사 모두 이 번호로 조회. `sales_order`는 같은 날 한 번에 등록한 묶음(선택) |
| N2 | 품목 스펙(재질·요구경도·심부경도·경화층·조직·등급)을 입고 행에 복사 | `sales_order_item`에 요구사항 Snapshot 컬럼 추가 (`hardness_snapshot`, `core_hardness_snapshot`, `case_depth_snapshot`, `texture_snapshot`) |
| N3 | 작업지시번호(`WorkOrderNo`) — 고객 측 번호 | `sales_order_item.customer_work_order_no` 추가 |
| N4 | 우선순위 (0~3) — 행 단위 | `priority`를 `sales_order_item`으로 이동 (현재 헤더에 있음) |
| N5 | **별도관리** 플래그 (공정이동표 특기사항에 `[Grade] 별도관리` 표시) | `sales_order_item.is_separately_managed` — 공정이동표 옵션에서 표시 |
| N6 | 반입(재입고) 여부 | `is_return` (반영됨) |
| N7 | 잔량 3종 저장형 (투입·출고·배정) | VIEW 계산 (설계 원칙 §1.2) — 배정잔량은 `production_schedule_item` 합계로 계산 |
| N8 | 저장 후 공정이동표·라벨 즉시 출력 | FIXED `PROCESS_SHEET`, `PRODUCT_LABEL` + 단말 자동인쇄 설정 |
