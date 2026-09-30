# 작업표준 — F_WorkStandardAddForm, F_WorkStandardForm, F_StandardTemplateAdd, F_StandardCopy

| 폼 | 행 | 역할 |
|-|-|-|
| `F_WorkStandardAddForm` | 1,139 | 작업표준 등록·수정 (항목 × 단계 조건 그리드), 템플릿 저장, 표준 복사 호출 |
| `F_WorkStandardForm` | 309 | 작업표준 목록 조회, 작업표준서 출력 (`PrintDoc/WorkStandardSheet`) |
| `F_StandardTemplateAdd` | 292 | 설비·단위공정별 **템플릿** 등록 (관리항목 row / 단계 column) |
| `F_StandardCopy` | 158 | 기존 작업표준 선택 → 조건 복사 |

관련: `Repository/WorkStandardRepository`, `WorkStandardDetailRepository`, `StandardColumnRepository`(t_standardtemplate), `UnitProcessRepository`

신규 대응: 기준정보 > 단계 템플릿 / 작업표준 화면, 테이블 `step_template(_item)`, `condition_item`, `standard`, `standard_version`, `standard_condition`

## 템플릿 (`t_standardtemplate`) — 설비유형 × 설비명 × 단위공정

| `templatetype` | 내용 | Item1~15 |
|-|-|-|
| `row` | **관리항목** 목록 (조건 항목: 온도, 시간, CP …) | 항목명 |
| `column` | **단계** 목록 (승온, 균열, 침탄 …) | 단계명 |

⇒ 조건 항목(행)도 **설비·단위공정마다 템플릿으로 정해져 있음**

## F_WorkStandardAddForm 흐름
1. 선택: 업체 → 품목, 설비유형 → 설비명(**"All" = 해당 유형 전체 설비**), 단위공정(**새 이름 입력 가능 → 저장 시 단위공정 자동 등록**)
2. 조건이 모두 선택되면 **최신 작업표준 자동 로드** (`GetLatestByEquipmentAndPartAndProcess`) + 최신 템플릿과 병합해 그리드 표시 (All이면 자동 로드 안 함)
3. 투입 기준: charge 수량, 작업시간(시간), charge 단위
4. 조건 그리드: 행 = 관리항목, 열 = Step1~15 (행 삽입·삭제·이동)
5. 저장 (트랜잭션)
   - 선택 설비마다 **작업표준 새 행 INSERT** + 상세 INSERT ⇒ **수정해도 기존 행 보존 = 사실상 버전 관리** ("새 버전, 기존 자료 보존")
   - "All"이면 **유형 내 설비 수만큼 동일 표준 복사**
   - 템플릿이 없으면 설비별로 row/column 템플릿 자동 저장 (트랜잭션 밖)
6. 표준 복사 (`F_StandardCopy`) — 다른 품목의 표준을 불러와 수정

## 읽기·쓰기 테이블

| 테이블 | 용도 |
|-|-|
| `t_standard` | 작업표준 헤더 (설비유형·설비명·이니셜·단위공정·업체·품목·charge·작업시간) — 저장마다 새 행 |
| `t_standarddetail` | 항목(`item`) × Step1~15 값 |
| `t_standardtemplate` | row/column 템플릿 |
| `t_unitprocess` | 신규 단위공정 자동 등록 |
| `t_customer`, `t_part`, `t_equipment` | 선택 목록 |

## 신규 설계에 반영할 사항

| # | 발견 | 조치 |
|-|-|-|
| T1 | 관리항목(행)도 설비·단위공정 템플릿에 속함 | **`step_template_condition` 추가** (단계 템플릿 × `condition_item` × 순서) — 표준·투입 화면의 행 목록 원천 |
| T2 | 저장 = 새 행 (기존 보존) | `standard_version` 새 Version (반영됨). 이관 시 같은 키(설비·품목·단위공정)의 여러 행 → **Version 1..N으로 변환** (최신이 current) |
| T3 | 설비 "All" → 설비마다 복사본 | 신규는 `standard.equipment_type_id`만 지정하고 `equipment_id` NULL = 유형 공통 (복사 불필요). 이관 시 **동일 내용 복사본은 유형 공통 1건으로 통합** 검토 |
| T4 | 새 단위공정 이름 입력 시 자동 등록 | 신규는 기준정보에서만 등록 (오타로 단위공정이 늘어나는 것 방지) — 사용자 확인 |
| T5 | 표준 조회 키에 업체(`CustomerId`) 포함 여부 불명확 | `standard.customer_id` NULL 허용 (반영됨) — 이관 데이터 분포 확인 |
| T6 | 작업시간 단위 = 시간 | 신규 `running_time_min` (×60 이관) |
| T7 | 표준 복사 기능 | 작업표준 화면 "다른 품목에서 복사" |
