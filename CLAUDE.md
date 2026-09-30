# JI_MES — 열처리 MES 신규 구축

기존 WinForms 시스템(`bbakggum` DB)을 **삭제·변경하지 않고** 신규 웹 시스템(`bbakggum_v2`)을 따로 구축한다.

## 파일 위치

| 경로 | 내용 |
|-|-|
| `docs/bbakggum_신규 설계안_V3.md` | **설계 기준 문서** (V3.13). 업무 규칙·테이블·주의사항의 근거 |
| `db/bbakggum_v2_DDL_V3.sql` | **DDL 단일 원본** (77 테이블 + 5 VIEW + 초기 데이터). 스키마 변경은 여기만 수정 |
| `db/test/smoke_scenario.sql` | 업무 흐름 스모크 시나리오 (DDL 변경 후 반드시 실행) |
| `db/dev/dev-db.ps1` | 개발 DB 관리 (3307 포트, 로컬 전용). `seed_dev.sql` = 화면 확인용 개발 데이터 |
| `db/test/legacy/` | 구 SP 비교용 (원문 복사, 테스트가 `jimes_legacy_ref_test`로 실행) |
| `docs/reference/` | V2 설계안, 참고 이미지 |
| `api/` | ASP.NET Core Minimal API (.NET 10 LTS, Dapper, MySqlConnector). 구조·규칙은 설계 §18 |
| `web/` | React 19 + TypeScript + Vite + **Ant Design 6** + TanStack Query. 구조·규칙은 설계 §19 |
| `D:\Programming\ProductManager` | **기존 WinForms 소스 — 읽기 전용.** 업무 규칙 확인용. 수정 금지 |

설계 문서는 크므로 **필요한 절만 읽는다** (예: 스케줄 작업 → §4·§7·§15.1·§20, 출력 → §5.3·§15.2~15.3·§21, 기준정보 → §22, 설정 → §15.4).

## 명령

```powershell
powershell -ExecutionPolicy Bypass -File db\dev\dev-db.ps1 status   # 상태
powershell -ExecutionPolicy Bypass -File db\dev\dev-db.ps1 start    # 시작
powershell -ExecutionPolicy Bypass -File db\dev\dev-db.ps1 apply    # bbakggum_v2 재생성 (데이터 삭제)
powershell -ExecutionPolicy Bypass -File db\dev\dev-db.ps1 smoke    # 재생성 + 스모크 시나리오
powershell -ExecutionPolicy Bypass -File db\dev\dev-db.ps1 seed     # 재생성 + 화면 확인용 개발 데이터 (생산계획 등)
powershell -ExecutionPolicy Bypass -File db\dev\dev-db.ps1 stop

dotnet test api                                          # API 테스트 (개발 DB 인스턴스에 bbakggum_v2_test 를 새로 만듦)
dotnet run --project api\src\JiMes.Api --launch-profile http   # http://localhost:5080 (개발 DB bbakggum_v2)

npm run dev --prefix web        # http://localhost:5173 (API 5080 프록시 — API 먼저 실행)
npm test --prefix web           # 웹 단위 테스트 (vitest)
npm run build --prefix web      # 타입 검사 + 빌드 → api/src/JiMes.Api/wwwroot (API 단독으로 웹 제공)
npm run lint --prefix web
```

- API 접속 정보·최초 관리자 비밀번호(개발용)는 `api/src/JiMes.Api/Properties/launchSettings.json` 환경변수. 운영은 환경변수 `ConnectionStrings__Main`, `Bootstrap__AdminPassword`
- 새 API 엔드포인트는 `RequirePermission(MenuKeys.x, …)` / `RequireLogin()` / `AllowAnonymous()` 중 하나를 반드시 선언 (누락 시 테스트 실패). 새 메뉴는 DDL §9.8, 새 설정은 DDL §9.6 + `SettingKeys`. 새 화면은 메뉴(DDL §9.8) → `web/src/pages/registry.ts` 등록, 버튼은 `useCan`. 단순 기준정보는 `MasterCatalog.cs` 정의 1개 + 메뉴만 (화면·API 자동, 정의 ↔ DDL 테스트)

- 출력: PDF 변환은 서버 LibreOffice(`print.pdf_converter_path`), 고정 양식 글꼴은 설치 이름("굴림체", "맑은 고딕") — 개발 PC에 둘 다 설치되어 있어야 출력 테스트가 돈다 (LibreOffice 없으면 해당 테스트 건너뜀)
- Dapper 조회 행은 **속성 클래스**로 받는다 (BIGINT UNSIGNED id 를 레코드·튜플 생성자로는 매핑 못 함)
- 개발 DB: `127.0.0.1:3307`, root 비밀번호 없음, DB `bbakggum_v2`. **운영 MariaDB(3306, `bbakggum`)에는 접속·실행하지 않는다.**
- PowerShell 스크립트(.ps1)에 한글이 있으면 **UTF-8 BOM**으로 저장한다 (Windows PowerShell 5.1).

## 확정된 핵심 규칙 (상세는 설계 문서)

- **작업 LOT = 단위공정 1회 수행.** LOT번호 `YYMMDD-설비이니셜-순번` (재작업도 동일). 입고는 LOT 관리 안 함
- 투입: 주 LOT 생성 전 **수주번호**, 이후 **주 LOT** 입력. 수주가 여러 주 LOT이면 선택창. 모든 추적은 **주 LOT 기준**
- LOT 상태는 단위공정별 **배정 → 투입 → 완료** 만 (검사·출하는 상태 아님)
- 공정 = 경로(`heat_process_operation`) / 단계 템플릿(`step_template`) / 조건 **항목 × 단계** (`standard_condition`)
- 혼적 LOT: 투입 품목 중 1개로 **표준 확정** → 조건을 LOT에 복사 (수정 가능)
- 양품 = 투입 − 부적합 (매 공정, 저장 안 하고 VIEW 계산). 부적합은 발견 공정 귀속 + 주 LOT 병기
- 검사 = 1회 : 대상 N (`inspection_target`), **측정·판정은 검사 공통**, 성적서는 **대상(품목)별 1장**. 검사구분 입고/공정/출하
- 스캔하는 "수주번호" = 입고 품목 행 번호 `sales_order_item.order_item_no`. 주 LOT 전에는 LOT당 같은 수주 1회, 주 LOT 후에는 같은 주 LOT의 같은 수주 1회(다른 주 LOT이면 함께 투입 가능, 검사는 주 LOT 단위). 설비당 투입 중 작업 1건
- 출하 금액은 품목 단가 구분(EA/KG/CHARGE)별 계산, 시험편 수량 별도
- 출하 = 전표, 마감 = 업체별·전표 단위 상태(미마감/마감/이월)
- 출력 양식 2방식: **EXCEL**(사용자 엑셀 양식, `{{키}}`) / **FIXED**(코드 렌더러 + 관리자 옵션 JSON). 용도는 사용자 확장
- 스케줄 계산은 **API Scheduling Service 한 곳**. 계획 블록 : 작업 LOT = 1:1. 병합 시 최대 작업시간, 지연 시 뒤 배정 계획시각 자동 이동

## 개발 규칙

- **하드코딩 금지:** 시각·주기·세율·경로·번호형식·레이아웃 수치·표시문자열은 `system_setting` / 공통코드(`is_system`) / 양식 옵션으로 (설계 §15.4)
- 상태·판정은 코드값(`PASS`, `ALLOCATED` …)으로 비교하고 표시명은 공통코드에서 읽는다
- 명명: `snake_case` 테이블, PK `{table}_id`, 역할 FK `{역할}_{참조PK}`, 여부 `is_`, 당시값 `_snapshot` (설계 §1.1)
- 동시 수정 대상은 `row_version` 낙관적 잠금, 변경 이력은 `audit_log`
- 스키마를 바꾸면 DDL 수정 → `dev-db.ps1 smoke` 통과 → 설계 문서 해당 절 갱신
- 기존 업무 규칙이 불분명하면 `D:\Programming\ProductManager`를 열어 확인하고, 근거(파일·행)를 설계 문서에 남긴다
- 사용자 응답·작업 요약은 **한국어**

## 진행 단계

| 단계 | 내용 | 상태 |
|-|-|-|
| 0 | 폴더·Git·CLAUDE.md·개발 DB | 완료 (2026-09-30) |
| 1 | 기존 폼 50개 구동 방식 분석 → `docs/legacy_forms/` 폼별 정리 + 설계 보완 | **분석 완료 (2026-09-30)** — 설계 V3.8·DDL 반영 완료 |
| 2 | API 골격 (인증·권한·감사·설정 캐시·row_version·SignalR) | **완료 (2026-09-30)** — 설계 V3.9 §18, 테스트 49건. 사용자·역할 관리 API는 5단계 |
| 3 | 웹 골격 (Ant Design 레이아웃·로그인·권한 메뉴·대시보드 틀) | **완료 (2026-09-30)** — 설계 V3.10 §19. 시스템 화면 3종(설정·공통코드·변경 이력) 포함 |
| 4 | 고난도 프로토타입: ① 스케줄 서비스 + Gantt (구 SP 결과 비교) ② 출력 엔진 (EXCEL 성적서 + FIXED 거래명세표) | **완료 (2026-09-30)** — ① 설계 §20 ② 설계 §21, §12 확인 7건(①~⑦). API 테스트 100건 |
| 5 | 기준정보 화면 | **완료 (2026-09-30)** — 설계 V3.13 §22 (범용 14종 + 사용자·역할 + 품목 + 공정·템플릿·작업표준 + 검사기준), §12 확인 ⑧⑨. API 테스트 121건 |
| 6 | 업무: 수주 → 계획 → 투입 → 검사·성적서 → 부적합·재작업 → 출하·마감 | 대기 |
| 7 | 조회·대시보드 (LOT 현황, 추적, KPI) | 대기 |
| 8 | 구 DB 이관 스크립트·검증·병행운영 | 대기 |

### 1단계 진행 방법

- 순서: `F_GasForm`, `F_InspectionAddForm`, `F_WorkPlanForm`, `F_OutForm`·`F_OutAddForm`·`F_MonthlyClosing`, `F_IncomeForm`·`F_IncomeAddForm`, `F_WorkStandardAddForm` → 나머지 (`ls F_*.cs`로 목록 확인, 총 50개 약 29,000행)
- 폼별 정리 항목: 역할 / 진입 경로 / 주요 이벤트 흐름 / 읽기·쓰기 테이블 / 숨은 업무 규칙 / 하드코딩 / 신규 설계 대응(테이블·API·화면)
- 설계와 다른 규칙을 발견하면 설계 문서 §12(확인 필요)에 올리고 사용자에게 확인
- 한 세션에 폼 여러 개를 다 읽지 말고 영역 단위로 나눈다 (사용량 관리)
