# F_InspectionAddForm — 검사 등록/수정/확정/성적서

| 항목 | 내용 |
|-|-|
| 파일 | `ProductManager/F_InspectionAddForm.cs` (3,084행) + `Repository/InspectionAddRepository.cs`, `Services/InspectionPrintService.cs`, `Services/InspectionReportRenderer.cs` |
| 역할 | LOT의 투입내역 중 검사 대상 선택 → 검사기준 표시 → 측정값 입력 → 판정 → 저장/확정 → 재검사 → 성적서 출력 |
| 신규 대응 | 화면: 검사 / API: `inspections` / 테이블: `inspection`, `inspection_target`, `inspection_item`, `inspection_measurement`, `defect_occurrence`, `print_*` |

## 진입

| 생성자 | 동작 |
|-|-|
| `(inspectionType)` | 신규. 검사구분 **`TI` 입고검사 / `TP` 공정검사 / `TO` 출하검사** (그 외 값은 TO로 강제) |
| `(inspectionType, inspectionNo, isViewMode)` | 기존 검사 조회·수정 |

호출: `F_InspectionForm` (목록) — `TI`는 입고 화면 쪽에서 호출(256행)

## 검사번호
- 형식 **`{구분}{yyMMdd}-{NNN}`** (예: `TO260930-001`), 순번 = 같은 날 같은 구분의 `MAX(subno)+1`
- 저장 시 대상마다 `subno`를 다시 계산해서 증가 → **subno가 검사 내 1..N이 아니라 날짜·구분 전체 순번**

## 흐름

1. **LOT번호 입력** → `GetWorkSubsByLotNo` → 투입내역 그리드(체크박스), 고객로트 표시(`ApplyCustomerLotToGrid`)
2. 품목 기본 성적서 양식 자동 선택 (`LoadDefaultTemplateForPart` → `t_part_template` type `inspection`)
3. **검사기준 = 첫 번째 체크된 품목**의 `t_inspectioncriteria` (`LoadFirstCheckedCriteria`) → 탭별 그리드
   - 탭: 외관·경도·치수(Value) / 경화층(Depth, **경화층 곡선 차트** ScottPlot 스플라인) / 조직(Structure, **이미지 드래그앤드롭**) / 라인(Line) / 추가사항(Etc) / 탭6 경화층 차트 확장
4. 측정값 입력 → 행별 판정 → **종합 판정** (`DetermineOverallDecision`: 하나라도 "불합격"이면 불합격)
5. **저장** (`btnSave_Click`): 검사자 필수, 조직 이미지 파일 처리, `SaveInspection` (트랜잭션)
   - 체크된 대상마다 `t_inspection` 1행 (LOT × 수주, 판정·메모·양식·변환LOT 공통)
   - 측정 상세 `t_inspectiondetail`은 **검사번호 단위** (대상별 아님)
   - `is_complete = 0` (미확정) → 이후 **수정저장은 UPDATE** (`BtnUpdateSave_Click`: 상세 전체 삭제 후 재삽입)
6. **확정** (`BtnDecision_Click`): `is_complete = 1` + 불합격이면 대상마다 `t_defect` 생성 (**불량수량 = 대상 투입수량 전체**)
7. **확정 후 수정** = **재검사** (`BtnReInspectionSave_Click`): 새 검사번호로 저장
8. 경화층 차트 PNG 저장 (`Files/Inspection/HardnessChart/{검사번호}.png`), 조직 이미지 `Files/.../Structure`
9. 출력: ① `btnPrintSheet_Click` 좌표형 PDF(QuestPDF, `t_printtemplate`) ② `btnExportExcel_Click` 엑셀 양식 치환(EPPlus, `{한글}`/`{English}` 치환자, 이미지 삽입) ③ `InspectionPrintService` (ClosedXML `{{키}}`, LibreOffice PDF)
10. 관리도 (`btnControlChart_Click` → `F_xBar`)

## 읽기·쓰기 테이블

| 테이블 | 용도 |
|-|-|
| `t_worksub` | LOT 투입내역 (검사 대상 후보) |
| `t_income` | 고객로트 |
| `t_inspectioncriteria` | 품목(+업체) 검사기준 |
| `t_inspection` | 검사 대상 행 (inspectionno + subno) |
| `t_inspectiondetail` | 측정값 (itemtype, item, location, number, v1~v10, result, decision) — inspectionno 단위 |
| `t_defect` | 확정 시 불합격 대상 |
| `t_part_template`, `t_inspectiontemplate`, `t_printtemplate` | 양식 |

## 버그·위험

| # | 내용 | 위치 |
|-|-|-|
| B1 | `SaveInspectionDetails`가 **대상 반복문 안**에서 호출 → 대상 N개면 측정 상세가 **N번 중복 저장** | `InspectionAddRepository.SaveInspection` 159행대 |
| B2 | subno가 검사 내 순번이 아님 (날짜·구분 전체 MAX+1) | 같은 곳 |
| B3 | 검사번호 순번 `MAX+1` 경쟁 조건 | `GetNextSubNo` |
| B4 | 기준을 첫 번째 품목에서만 가져옴 → 서로 다른 품목을 한 검사에 넣으면 다른 품목도 첫 품목 기준으로 판정 | `LoadFirstCheckedCriteria` |
| B5 | 출력 경로 3가지 공존 (좌표형 / EPPlus / ClosedXML) | 설계 §15.2 P1·P2 |

## 하드코딩

| 값 | 신규 |
|-|-|
| 검사구분 `TI`/`TP`/`TO` + 표시명 입고·공정·출하검사 | 공통코드 `INSPECTION_TYPE` (attr: 번호 접두어) |
| 검사번호 형식 `{구분}{yyMMdd}-{NNN}` | 설정 `inspection.number_format` |
| 판정 "합격"/"불합격" 문자열 | 코드 `PASS`/`FAIL` + 공통코드 `DECISION` |
| 파일 폴더 `Files/Inspection/HardnessChart`, `Structure` | `file.storage_root` 또는 DB |
| itemtype `"0"~"5"` ↔ 탭 매핑 | 공통코드 `INSPECTION_ITEM_TYPE` |

## 신규 설계에 반영할 사항

| # | 발견 | 조치 |
|-|-|-|
| I1 | 검사구분 3종 (입고/공정/출하) | `inspection.inspection_type` 추가 + 공통코드 `INSPECTION_TYPE` |
| I2 | 측정값은 **검사 1회 공통** (대상별 아님), 판정도 공통 | `inspection_item`을 `inspection`에 연결하고 `inspection_target_id`는 선택(NULL = 공통) — **품목별 개별 측정이 필요한지 사용자 확인** |
| I3 | 저장(미확정) → 확정 → 확정 후 수정은 새 번호 재검사 | `inspection.status` WAITING/IN_PROGRESS(저장) → COMPLETED(확정), `reinspection_of_id` 추가 |
| I4 | 불합격 확정 시 대상 투입수량 전체를 불량으로 등록 | `defect_occurrence.defect_qty` 기본값 = 대상 수량 (수정 가능 여부 확인) |
| I5 | 검사 대상은 **어떤 LOT이든** 입력 가능 (주 LOT 한정 아님) | 설계 §5.1 "주 LOT 입력"을 "LOT 입력(주로 주 LOT)"으로 완화 |
| I6 | 경화층 차트 이미지·조직 사진 | 첨부 저장소(`file.storage_root` 또는 DB BLOB) + `inspection_attachment` 테이블 필요 |
| I7 | B1 중복 저장 | 이관 시 `t_inspectiondetail` **중복 제거** (inspectionno + itemtype + item + location + number 기준) |
