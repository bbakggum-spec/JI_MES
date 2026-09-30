# 출하(납품) / 월마감 — F_OutAddForm, F_OutForm, F_MonthlyClosing

| 항목 | F_OutAddForm | F_OutForm | F_MonthlyClosing |
|-|-|-|-|
| 파일 | `F_OutAddForm.cs` (807행) + `Services/OutcomeAddService.cs`, `OutcomeBufferService`, `Models/OutcomeModels.cs` | `F_OutForm.cs` (1,175행) | `F_MonthlyClosing.cs` (763행) + `Services/ClosingService.cs`, `Repository/OutcomeSumRepository.cs` |
| 역할 | 출하 전표 등록·수정, 저장 후 거래명세표 출력 | 출하 목록 조회·삭제, 체크 합계, 엑셀, 거래명세표(개별·병합) 출력 | 전표별 마감·이월·마감취소·자동이월 |
| 신규 대응 | 출하 전표 등록 / `shipment`, `shipment_item` | 출하 목록 | 마감 / `shipment_closing` + 전표 마감 상태 |

## F_OutAddForm — 출하 전표 등록

1. 거래처 선택 → **재고 목록** = 해당 거래처 수주 중 출고잔량(`t_income.outremainqt`) > 0
2. 재고 행 더블클릭/담기 → **장바구니(버퍼)**. 기본 출고수량 = 출고잔량 전체
3. 행별 입력: **출고수량**(1 이상), **시험편 수량**(0 이상) — `출고 + 시험편 ≤ 출고가능` 검증
4. 계산: 중량 = 출고수량 × 단중, **금액 = 출고수량 × 단가** (단위 `Unit`과 무관)
5. 저장 (`OutcomeAddService.Save`)
   - 전표번호 **`O{yyMMdd}-{NNN}`** (출고일 기준 `MAX(subno)+1`)
   - 행마다 `t_outcome` INSERT → 수주 `outqt += 출고`, `outremainqt -= 출고 + 시험편`, `testspecimen += 시험편`
   - `t_outcomesum` (전표 합계) INSERT — **거래처 사업자정보 Snapshot** (사업자번호·대표자·업태·종목·주소)
   - 전표에 **마감일(closingDate)**, 비고, **품목 합산 출력(sumAsPart)** 여부 저장
   - **트랜잭션 없음** (개별 INSERT/UPDATE 연속)
6. 저장 / 저장+출력 / 저장+출력+계속 등록 → `PrintDoc/OutputSheet` 거래명세표 → 설정된 프린터로 인쇄
7. 수정 (`SaveUpdate`): 기존 전표 행을 다시 계산

## F_OutForm — 출하 목록
- 기간·거래처·검색 필드로 조회, 체크 행 합계
- 삭제 (`btnDelete_Click`) — 수주 출고수량 복원
- 확장 보기(품목 상세), 엑셀 내보내기
- 거래명세표: 선택 전표 개별 출력 / **여러 전표 PDF 병합** (`MergePdfFiles`) / 합산 출력(`OutSheetCom`)

## F_MonthlyClosing — 월마감
- 조회: 기간·거래처(선택)·상태 → 전표 목록 + 전표 펼침 시 상세 행, 합계 행, 상태별 색상
- 마감 상태는 **전표(`t_outcomesum`)의 컬럼** — `closingstatus` (0 미마감 / 1 마감완료 / 2 이월), `closingmonth`
  - **마감**: 선택 전표 → `closingmonth = 선택 월 1일`, 상태 1
  - **이월**: 선택 전표 → `closingmonth = 다음 달 1일`, 상태 2
  - **자동 이월**: 기간 내 미마감 전표 전부 → 다음 달, 상태 2
  - **마감 취소**: `closingmonth = NULL`, 상태 0
- 월 통계: 거래 건수, 마감 건수, 마감 금액
- 엑셀 내보내기
- 마감 헤더(누가·언제 마감했는지) 기록 **없음**

## 읽기·쓰기 테이블

| 테이블 | 용도 |
|-|-|
| `t_income` | 재고(출고잔량), `outqt`, `outremainqt`, `testspecimen` 갱신 |
| `t_outcome` | 전표 상세 (incomeno 기준 — **LOT 정보 없음**) |
| `t_outcomesum` | 전표 합계 + 거래처 Snapshot + 마감 상태·월 |
| `t_customer`, `t_company` | 거래처·자사 정보, 도장 |

## 하드코딩

| 값 | 신규 |
|-|-|
| 전표번호 `O{yyMMdd}-{NNN}` | 설정 `shipment.number_format` |
| 마감 상태 enum 한글명 (미마감·마감완료·이월) | 코드 + 공통코드 `CLOSING_STATUS` |
| 부가세 10% (출력 코드) | `sales.vat_rate` (설계 §15.4 H6) |

## 신규 설계에 반영할 사항

| # | 발견 | 조치 |
|-|-|-|
| S1 | 기존 출하는 **수주 단위** (LOT 없음). 재고 = 수주별 출고잔량 | 신규는 `shipment_item.main_work_id`(출하 LOT) 기록 — 재고 목록을 **수주 × 주 LOT 양품 잔량**으로 표시. 구 데이터 이관 시 LOT은 NULL |
| S2 | **시험편 수량** (출고와 별도로 잔량 차감, 금액 미포함) | `shipment_item.test_specimen_qty` 추가, 출하 잔량 계산에 포함 |
| S3 | 금액 = 수량 × 단가 (단위 EA/KG 무시) | **단가 단위별 계산 규칙 사용자 확인** (KG 단가면 중량 × 단가) → `sales_order_item.unit_code` 기준 계산 |
| S4 | 전표에 거래처 사업자정보 Snapshot | `shipment`에 `customer_business_no/ceo/address…_snapshot` 추가 |
| S5 | 전표별 마감일·품목합산 출력 여부 | `shipment.closing_due_date`, `shipment.print_sum_by_part` 추가 |
| S6 | 마감은 **전표 컬럼**(상태 0/1/2 + 마감월), 이월은 명시적 상태 | `shipment.closing_status`(UNCLOSED/CLOSED/CARRIED_OVER) + `closing_year/month` 추가, `shipment_closing`은 **마감 실행 기록**(누가·언제·합계)으로 유지 |
| S7 | 자동 이월 (기간 내 미마감 전표 일괄) | 마감 서비스 기능 |
| S8 | 저장이 트랜잭션 없음 → 수주 잔량 불일치 위험 | 한 트랜잭션, 잔량은 계산 (VIEW) |
| S9 | 여러 전표 PDF 병합 출력 | FIXED `SALES_SLIP` 옵션 `merge_multiple_slips` (반영됨) |
| S10 | 삭제 시 수주 출고수량 복원 | 전표 취소 = `status CANCELLED` + 잔량은 VIEW가 자동 반영 |
