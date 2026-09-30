# F_WorkPlanForm (스케줄 배정) / F_GraphicWindow (작업 현황판)

| 항목 | F_WorkPlanForm | F_GraphicWindow |
|-|-|-|
| 파일 | `F_WorkPlanForm.cs` (2,181행) + `Common/PlanItemPanel.cs`, `WorkItemPanel.cs`, `WorkPlanFormHelper.cs`, `Workplaneventmanager.cs`, `Repository/WorkPlanRepository.cs` (2,558행), `Services/WorkPlanService.cs` | `F_GraphicWindow.cs` (1,057행) |
| 역할 | 수주를 설비 Gantt에 끌어다 놓아 **배정(계획)** 생성·이동·병합, 재처리 배정 | 설비유형별 **현장 현황판**. 설비 더블클릭으로 작업 등록(F_GasForm), 배정 더블클릭으로 작업 전환 |
| 진입 | `F_Main` 메뉴 → `new F_WorkPlanForm("가스로")` (**설비유형 하드코딩**) | `F_Main` 생산 메뉴 → 메뉴 항목 텍스트 = 설비유형 |
| 신규 대응 | 생산계획 Calendar/Gantt, Scheduling Service, `production_schedule(_item)` | LOT 현황(`vw_work_lot_status`), 투입 화면 진입 |

두 폼은 **Gantt 그리기 코드가 거의 중복**됩니다 (시간 헤더, 설비 영역, 작업 패널, 배정 패널, 새로고침 타이머).

## F_WorkPlanForm 흐름

### 배정 대상 목록
- 배정 대기 수주(`dgvReadyIncome`): 업체·품목·공정 필터, 다중 선택, 드래그 시작
- 체크박스로 **재처리 대기 목록**(불량 `reworkstep = '재처리'`, `plan_complete = 0`)으로 전환

### 수주 → 설비 드롭 (`EquipPanel_DragDrop`, `HandleIncomeDrop`)
- `CreatePlanFromIncome`
  - 작업시간 `ResolveRunningTime` 우선순위: ① 표준(설비유형 × 품목) → ② **같은 품목·설비유형의 이전 작업 시간** → ③ 설비유형 기준시간(`t_process_default_time`) → ④ **사용자 입력 창(`F_DummyTime`) 후 기준시간으로 자동 등록** → ⑤ 8시간
  - 투입수량 = min(수주 배정잔량, 표준 charge 수량)
  - 임시 LOT = 마지막 배정 임시 LOT 또는 마지막 작업 LOT 다음 번호 (`P{yyMMdd}-{설비}-{NNN}`)
- 기존 배정 패널에 드롭하면 위치에 따라
  - **앞에 삽입 / 뒤에 삽입**: 새 배정 + 배정잔량 차감 + 전체 재계산
  - **병합(Merge)**: 기존 배정의 수량에 더함 (`min(배정잔량, 수량)`), 작업시간 = 표준시간 × 수량 / charge 수량 (**비례 계산**) — 배정은 수주번호를 **1개만** 가지므로 **병합된 다른 수주의 연결은 사라지고 수량만 남음**
- 배정 간 드래그: 순서 이동·다른 설비로 이동 (`HandlePlanDrop`, `MovePlanToEquipment`, 임시 고순번 → 재정렬)

### 시간 계산
- 다음 시작시각 = max(설비 마지막 배정 종료, 마지막 작업 종료, 현재시각)
- 재계산 `RecalculateAllEquipmentPlans` → Repository `RecalculateEquipmentCore` (C#) — 별도로 SP `sp_RecalculateWorkPlanSequence/Times`도 존재 (설계 §15.1 S1)
- 작업일 = 시작시각이 08시 전이면 전날 (하드코딩)

### 기타
- **밀린 배정 오늘로 이동** (`btnMoveOverduePlans`) — 과거 날짜 배정을 오늘로
- **임시 LOT 재번호** (`btnRefreshLotNumbers`)
- 배정 회수 (`RecallPlan`) — 배정 삭제 후 수주 목록으로 복귀, 배정잔량 복원
- 재처리 배정 (`RegisterPlanFromDefect`) — 불량 건으로 `IsRework = true` 배정, 불량 `plan_complete = 1`
- 60초 타이머 `RefreshProgressAsync`: **작업·배정 건수만 비교**해 달라졌을 때만 다시 그림, 아니면 진행바만 갱신 → 건수가 같으면 다른 PC의 수정(시간·순서 변경)이 반영되지 않음
- 작업 패널 종료 위치에 맞춰 뒤 배정 패널을 **화면에서만** 밀어냄 (`PushPlanPanels`) — DB 시간은 그대로

## F_GraphicWindow 흐름
- 설비 영역 더블클릭 → **해당 설비에 미완료 작업이 있으면 신규 등록 차단** ("진행 중인 작업을 먼저 완료", 기존 작업 열기 제안) → 없으면 `F_GasForm(NEW)`
- 작업 패널 더블클릭 → `F_GasForm(LOAD)`
- 배정 패널 더블클릭 → `F_GasForm(PLAN_VIEW)` → 저장 시 배정 삭제·작업 생성 (`PlanConverted` 이벤트)
- 설비별로 열린 작업 폼을 사전으로 관리 (같은 설비 중복 창 방지)

## 읽기·쓰기 테이블

| 테이블 | 용도 |
|-|-|
| `workplan` | 배정 (planno, templotno, workdate, equipmentid, orderno **1개**, runningtime(시간), inputqty, start/end, sequence, isrework) |
| `t_income` | 배정 대기, `planremainqt` 차감/복원 |
| `t_work` | 작업 패널, 마지막 종료시각, 마지막 LOT |
| `t_standard` | 작업시간·charge 수량 |
| `t_process_default_time` | 설비유형 기준시간 (배정 중 자동 등록) |
| `t_defect` | 재처리 대기, `plan_complete` |
| `t_equipment` | 설비 목록 (설비유형별) |

## 하드코딩

| 값 | 위치 | 신규 |
|-|-|-|
| 배정 화면 설비유형 `"가스로"` | `F_Main` 283행 | 설비유형 선택/전체 |
| 작업시간 최종 기본값 8시간 | `ResolveRunningTime` | `schedule.default_running_time_min` |
| 작업일 경계 08시, 화면 범위 08~익일 08 | 여러 곳 | `work_shift` / `schedule.day_start_time` |
| 새로고침 60초 | `InitTimer` | `schedule.refresh_interval_sec` |
| 세로 눈금 2시간 간격 | `F_GraphicWindow` 221행 | 화면 옵션 |

## 신규 설계에 반영할 사항

| # | 발견 | 조치 |
|-|-|-|
| W1 | **설비당 진행 중(미완료) 작업 1건** 규칙 | `production_work`에 `UNIQUE(equipment_id, (status = 'INPUT'))` 형태의 영속 생성컬럼 제약 추가 + API 검증 |
| W2 | 작업시간 결정 5단계 (표준 → 이전 작업 → 기준시간 → 사용자 입력·자동 등록 → 기본값) | Scheduling Service 규칙으로 명시 (설계 §7 보강) |
| W3 | 병합 시 수주 연결 소실 | `production_schedule_item`(블록 × 수주 N)으로 해결 (설계 반영됨) |
| W4 | 병합 시 작업시간 = 표준 × 수량/charge (비례), 일반 배정은 표준 그대로 | **확정: 병합 시 담긴 품목 표준시간 중 최대값** (투입 화면의 최대값 규칙과 동일). 구 비례 계산 폐기 |
| W5 | 밀린 배정 오늘로 이동, 임시 LOT 재번호, 배정 회수 | Scheduling Service 기능으로 포함 |
| W6 | 새로고침이 건수 비교 → 변경 누락 | SignalR 이벤트(블록 id + row_version) 기반 갱신 |
| W7 | 배정 → 작업 전환 = 배정 삭제 | 블록 RELEASED + 작업 LOT 1:1 (설계 반영됨) |
| W8 | 작업 종료 지연 시 뒤 배정을 화면에서만 밀어냄 | 재계산 시 실적 종료시각 기준으로 **실제 계획 시각을 조정**할지, 표시만 할지 **사용자 확인** |
| W9 | 두 폼의 Gantt 코드 중복 | 웹은 Gantt 컴포넌트 1개 (계획 모드 / 현황 모드) |
| W10 | 재처리 배정은 불량 1건 → 배정 1건 (수량 = min(불량수량, charge)) | 재작업 계획 = 부적합 여러 건을 한 블록에 담을 수 있게 (`production_schedule_item` + 부적합 연결) |
