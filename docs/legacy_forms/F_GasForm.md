# F_GasForm — 작업 등록/조회 (단위공정 공통 작업 화면)

| 항목 | 내용 |
|-|-|
| 파일 | `ProductManager/F_GasForm.cs` (3,188행) + `Common/GasFormHelper.cs`, `Services/WorkService.cs`, `Services/GasWorkService.cs` |
| 역할 | 작업 LOT 1건(단위공정 1회 수행)의 등록·수정·완료. 투입, 표준 확정, 작업조건, 분할투입, 라인검사, 불량, 특기사항, 작업일보 출력 |
| 이름 주의 | "가스로" 폼이지만 **모든 단위공정이 이 화면 하나를 사용** (사용자 설명의 "한 개의 등록 화면") |
| 신규 대응 | 화면: 작업 투입 / API: `production-works`, `production-works/{id}/inputs` / 테이블: `production_work`, `production_work_input`, `production_work_condition`, `production_work_event`, `line_inspection*` |

## 진입 모드 (생성자 3종)

| 모드 | 호출 | 동작 |
|-|-|-|
| `NEW` | `F_GasForm(equipment, parent, suggestedStartTime)` | 신규 작업. 시작시각 제안값(설비 마지막 작업 종료 기준 `CalculateSuggestedStartTime`) |
| `LOAD` | `F_GasForm(work, equipment, parent)` | 기존 작업 조회·수정. 완료면 잠금, 라인검사 완료면 검사탭 잠금 |
| `PLAN_VIEW` | `F_GasForm(plan, equipment, parent)` | 스케줄 배정(`workplan`)을 열어 작업으로 전환 |

## 주요 흐름

### 투입 입력 (`txtIncomeNo` Enter → `ProcessInput`)
1. 단위공정 미선택이면 **해당 설비의 최근 작업 단위공정으로 자동 설정** (`AutoSetUnitProcessIfEmpty`)
2. 입력값이 LOT 형식(`GasFormHelper.IsLotNoFormat`)이면 **LOT 입력**, 아니면 **수주번호 입력**
3. **LOT 입력** (`ProcessLotNo`): 그 LOT의 투입내역(`t_worksub`) 조회 → 2건 이상이면 `F_InputSelectionForm`으로 선택 → 선택 행을 복사해 투입
   - 투입수량 = **원 LOT의 투입수량 그대로** (원 LOT 불량 차감 없음), 잔량 검증 없음
   - 주 LOT번호는 **어디에도 기록하지 않음** (수주·품목 정보만 복사)
4. **수주번호 입력** (`AddIncomeToWorkSub`): `t_inputwaiting`(수주 × 단위공정 × 설비유형) 조회, 없으면 수주수량으로 생성 → 잔량 0이면 거부 → **잔량 전체를 투입수량 기본값**으로 추가
5. 같은 LOT에 같은 수주번호는 1회만 (중복 거부)
6. 추가·삭제 시 `UpdateRunningTime`: 투입 품목들의 표준 작업시간(설비유형 × 품목 × 단위공정) 중 **최대값** → 종료시각 = 시작 + 작업시간(시간 단위)

### 저장 (`TrySaveWork`)
1. 검증: LOT번호·단위공정 필수, 시작 < 종료, 투입수량 > 0
2. `NEW`/`PLAN_VIEW`: `SaveNewWork` (t_work + t_worksub, 트랜잭션)
   - `PLAN_VIEW`면 **배정(`workplan`) 행 삭제** 후 배정잔량(`planremainqt`) 보정 — 배정에서 빠진 수주 복구, 새로 추가된 수주 차감(음수 허용)
   - `NEW`면 투입 수주마다 배정잔량 차감
3. 저장 후 **별도 호출로** 분할투입(`t_inputsub`), 투입대기 잔량 delta 차감/복원(`t_inputwaiting`), 수주 `isinput` 표시/해제, 라인검사 저장
4. 작업조건 그리드가 있으면 조건 저장

### 완료 (`btn_Complete_Click`)
- 완료시각: 현재시각(**5분 단위 내림**, `RoundToNearest5Minutes`는 이름과 달리 floor) 또는 입력값
- 소요시간 = 종료 − 시작 (**시간 단위, 소수 1자리**)
- `t_work.isdone = 1, progressstep = '완료'` → 이후 작업정보 수정 불가

### 표준 확정 (`btn_FixStandard_Click` → `WorkService.FixStandard(lotNo, standardId, fixedPartId)`)
- 투입 품목 중 선택 행(없으면 첫 행)의 품목으로 표준 확정 → `isfixed = 1`, `fixedpartid`, `standardid`
- `CopyStandardToCondition`: 표준(항목 × Step1~15)을 작업조건 그리드로 복사 → 수정 후 저장 (`t_conditiontemplate` 헤더 + `t_workconditiondetail` 값)
- 조건 셀 더블클릭 시 **서명 입력**(`F_SignatureDialog`)

### 라인검사 (공정검사 탭)
- 경도 H X1~X5 × N1~N3, 조직 C, 경화깊이 D, 기타 T 입력 칸 (고정 좌표형 컨트롤) → `t_lineinspection`
- **라인검사 완료** (`btnLineInspectionComplete_Click`): 작업 완료 후에만 가능 → `t_work.isinspectiondone = 1` → 검사탭 잠금
- ⇒ 구 **`isinspectiondone`은 최종검사(성적서)가 아니라 라인검사 완료**

### 기타
- 분할투입(트레이·수량·투입/출고시각) 그리드 → `t_inputsub`
- 특기사항, 마킹, 작업자, **변환LOT(제출 LOT)** → `t_work`
- 작업일보 출력 (`btnWorkSheet_Click` → `PrintDoc/WorkDailySheet`)

## LOT번호 생성 (`WorkService.GenerateLotNo` + `GasFormHelper.ReformatLotNo`)
- 순번 = `MAX(subno)+1` (작업일 × 설비이니셜) — **잠금·UNIQUE 없음 → 동시 저장 시 중복 가능**
- 생성은 `B01-260930-001` → 재배열해 `260930-B01-001`
- LOT 판별은 **두 형식 모두 허용** (`YYMMDD-XXX-NNN`, `XXX-YYMMDD-NNN`) ⇒ 과거 데이터에 두 형식이 섞여 있을 수 있음

## 읽기·쓰기 테이블

| 테이블 | 용도 |
|-|-|
| `t_work` | 작업 LOT (lotno, subno, progressstep, 시각, 합계, worker, convertlotno, marking, standardid, isfixed, fixedpartid, isdone, isinspectiondone, remarks) |
| `t_worksub` | 투입 행 (lotno × incomeno) |
| `t_inputwaiting` | 수주 × 단위공정 투입 잔량 (저장형) |
| `t_income` | 수주 조회, `planremainqt` 차감/복원, `isinput` 표시 |
| `workplan` | PLAN_VIEW 전환 시 삭제 |
| `t_standard`, `t_standarddetail`, `t_standardtemplate` | 표준·단계 조회 |
| `t_conditiontemplate`, `t_workconditiondetail` | 확정 조건 저장 |
| `t_inputsub` | 분할투입 |
| `t_lineinspection` | 라인검사 |

## 하드코딩

| 값 | 위치 | 신규 |
|-|-|-|
| 완료시각 5분 내림 | `GasFormHelper.RoundToNearest5Minutes` | 설정 `work.complete_time_round_min` (신규 추가 필요) |
| progressstep 문자열 "투입", "완료" | `CreateWorkFromForm`, `CompleteWorkWithActualTime` | 코드값 + 공통코드 `WORK_STATUS` |
| LOT 형식 2종 판별 규칙 | `IsLotNoFormat` | 설정 `lot.number_format` 기반 판별 + 구 형식 호환 |
| 작업시간 단위 = 시간 | `CalculateEndTime`, `CalculateTakenTime` | 신규는 분 단위 (`*_min`) — 이관 시 ×60 |

## 신규 설계에 반영할 사항

| # | 발견 | 신규 설계 조치 |
|-|-|-|
| G1 | LOT 입력 시 원 LOT 투입수량을 그대로 복사 (불량 차감 없음, 잔량 검증 없음) | 기본값 = 주 LOT 해당 행 **양품 − 이 단위공정에 이미 투입한 수량**, 초과 투입 차단 |
| G2 | 주 LOT 미기록 | `main_work_id`, `main_input_id` 기록 (설계 §3.3) |
| G3 | 배정 전환 시 `workplan` 삭제 → 계획 대비 실적 불가 | 계획 블록 유지 + RELEASED, 1:1 (설계 §4) |
| G4 | 저장이 여러 번의 개별 호출 (배정잔량·투입잔량·isinput·분할·라인검사) — 부분 실패 시 불일치 | **한 트랜잭션**, 잔량은 저장하지 않고 계산 (설계 §1.2) |
| G5 | LOT 순번 `MAX+1` 경쟁 조건 | `UNIQUE(equipment_id, work_date, lot_seq)` + 충돌 시 재시도 |
| G6 | `isinspectiondone` = **라인검사 완료** | 신규 `line_inspection.status = COMPLETED`로 대응. 호환 VIEW의 `is_inspection_done` 의미를 라인검사 기준으로 정정 필요 |
| G7 | 같은 LOT에 같은 수주 1회 | `UNIQUE(production_work_id, sales_order_item_id)` 추가 검토 (재작업 투입과 충돌 여부 확인) |
| G8 | `HeatProcessName`에 설비유형 저장 | 이관 시 이 컬럼으로 공정명을 추정하지 않음 |
| G9 | LOT 형식 2종 혼재 | 이관 시 원래 LOT번호 문자열 **그대로 보존** (라벨·성적서에 인쇄된 번호와 일치해야 함), 조회는 두 형식 모두 지원 |
| G10 | 단위공정 미선택 시 설비의 최근 작업 단위공정 자동 선택 | 신규 투입 화면 기본값으로 유지 |
| G11 | 작업조건 셀 서명 입력 | `production_work_condition`에 서명 정보 컬럼 필요 여부 확인 (서명 이미지/서명자·시각) |
| G12 | 완료 후 작업정보 잠금, 라인검사 완료 후 검사탭 잠금 | 상태 기반 편집 제한 (API 검증) |
