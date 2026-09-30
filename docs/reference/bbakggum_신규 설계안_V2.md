# bbakggum DB 구조개편 설계안

* 대상 DB: `bbakggum`
* 기준 원본: 현재 제공된 MariaDB 구조 Export
* 원본 확인 테이블: 46개
* VIEW: 3개
* PROCEDURE: 2개
* 목적: 테이블/필드 명명 규칙 표준화, 업무영역 정리, 스케줄/진행상태 구조 정리, Snapshot/Version 정책 적용, 기존 테이블 누락 없는 전환계획 수립
* 작성 원칙: 기존 업무 의미를 최대한 보존하되, 현재 구조의 중복·수평형 컬럼·불명확한 상태값을 단계적으로 정규화한다.

\---

# 1\. 핵심 설계 원칙

## 1.1 명명 규칙

|대상|기준|예|
|-|-|-|
|테이블|`snake\_case`, 단수형|`customer`, `production\_work`|
|PK|`{table}\_id`|`customer\_id`|
|FK|참조 PK와 동일|`customer\_id`|
|업무코드|`\_code`|`part\_code`|
|명칭|`\_name`|`part\_name`|
|설명|`\_description`|`equipment\_description`|
|날짜+시간|`\_at`|`created\_at`, `started\_at`|
|날짜|`\_date`|`work\_date`|
|시간|`\_time`|`start\_time`|
|수량|`\_qty`|`input\_qty`|
|금액|`\_amount`|`total\_amount`|
|여부|`is\_`|`is\_active`, `is\_deleted`|
|상태|`status`|`production\_status`|
|순번|`sequence\_no`|`sequence\_no`|
|버전|`version\_no`|`version\_no`|
|당시 값|`\_snapshot`|`part\_name\_snapshot`|

### 사용하지 않을 명칭

```text
t\_customer
customerid
custname
useyn
isuse
seq
no
step1
step2
v1
v2
```

\---

# 2\. Master / Transaction / Detail / History / Snapshot 구분

## Master

현재 기준정보.

```text
company
customer
part
equipment
employee
department
instrument
defect
```

## Transaction

실제로 발생한 업무.

```text
sales\_order
production\_work
inspection
shipment
maintenance
downtime
```

## Detail

상위 업무의 상세.

```text
sales\_order\_item
production\_work\_item
inspection\_item
inspection\_measurement
shipment\_item
```

## History

Master의 변경 이력.

```text
part\_history
equipment\_history
...
```

필요한 Master에만 적용한다.

## Snapshot

과거 업무 당시의 값을 보존한다.

예:

```text
production\_work
    part\_id
    part\_name\_snapshot
    material\_snapshot
    specification\_snapshot
    process\_version\_id
```

\---

# 3\. Snapshot / Version 적용 원칙

## 3.1 Snapshot이 필요한 데이터

다음 데이터는 생산/검사/출하 시점의 값이 변경되면 과거 기록 재현에 영향을 주므로 보존한다.

|데이터|적용|
|-|-|
|고객명|필요 시 Snapshot|
|품목명|적용|
|품번|적용|
|규격|적용|
|재질|적용|
|공정명|Version + 필요 시 Snapshot|
|열처리 조건|Version 필수|
|검사기준|Version 필수|
|설비|당시 설비 정보 재현 필요 시 Snapshot|
|생산수량|Transaction 자체에 보존|
|검사결과|Transaction 자체에 보존|
|출하정보|Transaction 자체에 보존|

## 3.2 Version 관리가 필요한 기준정보

```text
process
process\_version
process\_step

heat\_process
heat\_process\_version
heat\_process\_step

inspection\_template
inspection\_template\_version
inspection\_criteria
```

핵심은 생산기록이 "현재 공정"을 바라보는 것이 아니라 **생산 당시 확정된 Version**을 바라보도록 하는 것이다.

\---

# 4\. 스케줄과 진행상태 구조 — 현재 DB에서 반드시 분리해서 볼 부분

현재 DB에는 스케줄/진행상태와 관련된 기능이 여러 테이블에 흩어져 있다.

## 4.1 현재 스케줄 관련 핵심 테이블

### `workplan`

현재 실제 스케줄의 핵심 테이블이다.

주요 필드:

```text
planno
templotno
workdate
equipmentid
equipmentno
equipmenttype
customername
partname
orderno
runningtime
inputqty
starttime
endtime
sequence
isrework
```

또한 다음 Procedure가 `workplan`의 순서와 시간을 직접 재계산한다.

```text
sp\_RecalculateWorkPlanSequence
sp\_RecalculateWorkPlanTimes
```

따라서 `workplan`은 단순 표시용 테이블이 아니라 **실제 생산계획/설비배정 데이터**로 판단한다.

### 권장 신규 명칭

```text
production\_schedule
```

또는 설비별 작업배정의 의미를 강조하면:

```text
equipment\_schedule
```

이번 설계에서는 `production\_schedule`을 기본 명칭으로 사용한다.

\---

# 5\. `t\_schedulebox`의 역할

현재 `t\_schedulebox`는 다음 구조를 갖는다.

```text
scheduleboxid
part
title
quantity
x
y
width
height
index
position
tag
hold
writedate
startdate
starttime
enddate
endtime
```

이 중:

```text
x
y
width
height
position
tag
title
```

등은 업무 데이터라기보다 **스케줄 화면의 Box/UI 배치 정보**에 가깝다.

따라서 `t\_schedulebox`를 `production\_schedule`과 합치지 않는다.

## 권장 구조

```text
production\_schedule
        │
        └── schedule\_board\_item
```

그리고 화면 배치 정보가 실제로 필요한 경우:

```text
schedule\_board\_layout
```

을 별도로 둔다.

즉,

```text
생산 스케줄 데이터
≠
스케줄 화면 표시 위치
```

로 분리한다.

\---

# 6\. 현재 진행상태 구조

현재 `t\_work`에는 다음 상태 필드가 있다.

```text
progressstep
isdone
isfixed
isinspectiondone
```

특히 `progressstep`의 COMMENT가:

```text
배정, 투입, 완료
```

로 되어 있어 현재 생산진행 상태를 담당하고 있다.

또한:

```text
starttime
endtime
takentime
expectedtime
```

으로 실제/예정 시간을 관리한다.

## 권장 신규 구조

```text
production\_work
----------------
production\_work\_id
schedule\_id
lot\_id
equipment\_id
employee\_id
work\_date

status
planned\_start\_at
planned\_end\_at
actual\_start\_at
actual\_end\_at

expected\_duration
actual\_duration

is\_fixed
is\_inspection\_completed
```

`progressstep`를 단순 문자열로 계속 사용하는 대신:

```text
status
```

를 중심으로 관리한다.

예:

```text
PLANNED
ALLOCATED
READY
RUNNING
PAUSED
COMPLETED
INSPECTION\_WAIT
INSPECTION\_COMPLETED
CANCELLED
```

실제 적용 상태명은 프로그램 업무 흐름을 확인하여 확정한다.

\---

# 7\. 스케줄과 진행상태의 관계

권장 관계:

```text
production\_schedule
        │
        │ 1
        ▼
production\_work
        │
        ├── production\_input
        ├── inspection
        ├── defect
        └── production\_output
```

의미:

```text
스케줄
  ↓
작업 배정
  ↓
실제 작업 시작
  ↓
실제 작업 종료
  ↓
검사
  ↓
생산 결과
```

따라서 **스케줄과 실제 진행상태를 하나의 테이블에서 관리하지 않는다.**

\---

# 8\. 스케줄의 시간 계산

현재 `sp\_RecalculateWorkPlanTimes`는 다음을 수행한다.

* 설비별 작업 순서 조회
* `sequence` 기준으로 순차 계산
* `runningtime`을 이용해 종료시간 계산
* 전 작업 종료시간을 다음 작업 시작시간으로 사용
* 날짜가 넘어가면 다음날 08:00부터 재계산
* 실제 `t\_work`의 종료시간도 스케줄 계산의 기준으로 사용

따라서 이 기능은 단순 UI 기능이 아니다.

신규 구조에서는:

```text
production\_schedule
    sequence\_no
    planned\_start\_at
    planned\_end\_at
    planned\_duration
```

을 유지하고,

실제 작업은:

```text
production\_work
    actual\_start\_at
    actual\_end\_at
    actual\_duration
```

으로 분리한다.

\---

# 9\. 작업자 배정

현재 `t\_workerassignment`:

```text
workerassignmentid
employeeid
workername
equipmentid
shift
workdate
writedate
```

이 테이블은 사용자 요구사항인:

* 주/야 2교대
* 설비별 작업자 배정
* 작업일자별 배정

과 직접 연결된다.

## 권장

```text
work\_shift
work\_pattern
worker\_assignment
```

그리고:

```text
worker\_assignment
    employee\_id
    equipment\_id
    shift\_id
    work\_date
```

로 정리한다.

`workername`은 FK로 조회 가능하므로 원칙적으로 제거한다.

단, 생산실적 문서에 당시 작업자명을 보존해야 한다면:

```text
worker\_name\_snapshot
```

을 생산실적에 저장한다.

\---

# 10\. 스케줄/진행상태 관련 테이블 종합

|기존 테이블|현재 역할|신규 역할|주요 상태/시간|처리|
|-|-|-|-|-|
|`workplan`|설비별 작업배정/계획|`production\_schedule`|workdate/starttime/endtime/sequence|**재설계**|
|`t\_schedulebox`|스케줄 화면 Box|`schedule\_board\_item` 또는 `schedule\_board\_layout`|start/end + x/y/width/height|**분리/재설계**|
|`t\_work`|실제 작업 + 진행상태|`production\_work`|progressstep/isdone/start/end|**재설계**|
|`t\_workerassignment`|설비별 작업자/교대 배정|`worker\_assignment`|shift/workdate|**재설계**|
|`t\_income`|수주/투입 가능량|`sales\_order` / `sales\_order\_item`|priority/planremainqty/isinput|**분리**|
|`t\_inputwaiting`|공정 투입 대기|`production\_queue` 또는 `production\_input\_queue`|allocated\_qty/remain\_qty|**재설계**|
|`t\_inputsub`|LOT 분할 투입 시간|`production\_input`|in/out time|**재설계**|
|`t\_defect`|불량/재작업 처리|`defect\_occurrence`|decision/check\_complete/plan\_complete|**재설계**|
|`t\_outcomesum`|출하 월마감 집계|`shipment\_closing` 또는 Snapshot|closingstatus|**유지/재설계**|

\---

# 11\. 기존 46개 테이블 전체 전환 목록

아래 목록은 원본 DB에서 확인된 **46개 CREATE TABLE을 빠짐없이 포함**한다.

처리구분:

* **유지**: 기능과 구조를 크게 유지
* **변경**: 명명/PK/FK/필드 정리
* **통합**: 다른 테이블과 합침
* **분해**: 하나의 테이블을 여러 테이블로 분리
* **재설계**: 업무 모델 자체를 재정의
* **폐기**: 신규 구조에서 별도 테이블로 유지하지 않음
* **VIEW 전환**: 저장 테이블 대신 View/Query로 대체 가능

\---

## 11.1 기준정보

|No|기존 테이블|주요 역할|신규 테이블|처리|Snapshot/Version|
|-:|-|-|-|-|-|
|1|`t\_company`|회사정보|`company`|변경|-|
|2|`t\_customer`|고객정보|`customer`|변경|필요 시|
|3|`t\_part`|품목정보|`part`|**분해/재설계**|**Version**|
|4|`t\_department`|구형 부서정보|`department`|**통합**|-|
|5|`t\_dept`|부서정보|`department`|**통합**|-|
|6|`t\_employee`|사원정보|`employee`|변경|필요 시|
|7|`t\_equipment`|설비정보|`equipment`|변경|**이력**|
|8|`t\_instruments`|측정기구|`instrument`|변경|이력 고려|
|9|`t\_defect`|불량/처리|`defect\_occurrence`|**재설계**|Snapshot|
|10|`t\_combolist`|콤보 공통값|`common\_code` / `common\_code\_item`|**재설계**|-|
|11|`t\_system\_settings`|시스템 설정|`system\_setting`|변경|-|

\---

## 11.2 권한/메뉴

|No|기존 테이블|주요 역할|신규 테이블|처리|
|-:|-|-|-|-|
|12|`t\_menu\_catalog`|메뉴 정의|`menu`|변경|
|13|`t\_dept\_menu`|부서-메뉴 권한|`department\_menu`|변경|
|14|`t\_templatefieldname`|템플릿 필드명|`template\_field`|변경|

\---

## 11.3 공정/표준

|No|기존 테이블|주요 역할|신규 테이블|처리|
|-:|-|-|-|-|
|15|`t\_heatprocess`|열처리 공정|`heat\_process` + `heat\_process\_version` + `heat\_process\_step`|**분해**|
|16|`t\_unitprocess`|단위공정|`unit\_process`|변경|
|17|`t\_process\_default\_time`|공정 기본시간|`process\_default\_time`|변경|
|18|`t\_standard`|작업/투입 표준|`standard` + `standard\_version`|**분해**|
|19|`t\_standarddetail`|표준 상세/Step|`standard\_step`|**分解**|
|20|`t\_standard\_gas`|표준 가스조건|`standard\_gas`|변경|
|21|`t\_standardtemplate`|표준 템플릿|`standard\_template`|변경|
|22|`t\_conditiontemplate`|조건 템플릿|`condition\_template` + `condition\_template\_item`|**분해**|
|23|`t\_condition\_con`|조건 연결/내용|`condition` / `condition\_item`|**재설계**|
|24|`t\_workconditiondetail`|작업실행조건|`work\_condition` + `work\_condition\_step`|**분해**|

`step1 \~ step15` 구조는 신규 구조에서 제거한다.

\---

## 11.4 수주/입고/투입

|No|기존 테이블|주요 역할|신규 테이블|처리|
|-:|-|-|-|-|
|25|`t\_income`|수주/입고 관리|`sales\_order` + `sales\_order\_item`|**분해/재설계**|
|26|`t\_inputwaiting`|투입대기|`production\_input\_queue`|재설계|
|27|`t\_inputsub`|분할 투입|`production\_input`|재설계|
|28|`t\_worksub`|작업 투입 상세|`production\_work\_item` / `production\_input`|**분해**|

`t\_income`에 들어 있는 고객/품목 정보는 Master와 분리하고, 과거 문서 재현에 필요한 값은 Snapshot으로 보존한다.

\---

# 12\. LOT 구조 추가

현재 여러 테이블에서:

```text
lotno
originelotno
convertlotno
templotno
```

가 각각 사용되고 있다.

신규 구조에서는 LOT을 독립 Entity로 만든다.

```text
lot
----------------
lot\_id
lot\_no
parent\_lot\_id
part\_id
order\_item\_id
original\_qty
current\_qty
status
created\_at
```

이를 통해:

```text
원 LOT
  ↓
분할 LOT
  ↓
재작업 LOT
  ↓
변환 LOT
```

의 계보를 관리할 수 있다.

\---

# 13\. 생산

|No|기존 테이블|주요 역할|신규 테이블|처리|
|-:|-|-|-|-|
|29|`workplan`|생산/설비 작업계획|`production\_schedule`|**재설계**|
|30|`t\_work`|실제 생산작업|`production\_work`|**재설계**|
|31|`t\_schedulebox`|스케줄 화면표시|`schedule\_board\_item` / `schedule\_board\_layout`|**분리**|
|32|`t\_workerassignment`|작업자 배정|`worker\_assignment`|변경|

\---

# 14\. 검사

|No|기존 테이블|주요 역할|신규 테이블|처리|
|-:|-|-|-|-|
|33|`t\_inspection`|검사 Header|`inspection`|재설계|
|34|`t\_inspectionsub`|검사 상세/부속|`inspection\_item`|재설계|
|35|`t\_inspectiondetail`|검사 측정값|`inspection\_measurement`|**재설계**|
|36|`t\_inspectioncriteria`|검사기준|`inspection\_criteria`|**Version 관리**|
|37|`t\_inspectiontemplate`|검사 템플릿|`inspection\_template` + `inspection\_template\_version`|**분해**|
|38|`t\_unitinspectionitem`|단위공정 검사항목|`unit\_inspection\_item`|변경|
|39|`t\_lineinspection`|공정검사|`line\_inspection` + `line\_inspection\_measurement`|**분해**|

`v1 \~ v10`, `hn1x1` 등 반복 컬럼은 신규 구조에서 Row 기반으로 변경한다.

\---

# 15\. 생산결과/출하

|No|기존 테이블|주요 역할|신규 테이블|처리|
|-:|-|-|-|-|
|40|`t\_outcome`|생산/출고 결과|`production\_output` / `shipment`|**업무 분리**|
|41|`t\_outcomesum`|출하 집계/월마감|`shipment\_closing`|**재설계**|

`t\_outcomesum`의 `closingstatus`는 단순 진행상태가 아니라 **월마감이라는 회계/업무 Snapshot 성격**이 있으므로 유지할 가치가 높다.

\---

# 16\. 설비/보전

|No|기존 테이블|주요 역할|신규 테이블|처리|
|-:|-|-|-|-|
|42|`t\_downtime`|설비 비가동|`equipment\_downtime`|변경|
|43|`t\_maintenance`|설비 보전/수리|`maintenance`|변경|

\---

# 17\. 인쇄/화면 템플릿

|No|기존 테이블|주요 역할|신규 테이블|처리|
|-:|-|-|-|-|
|44|`t\_part\_template`|품목-템플릿 연결|`part\_template`|변경|
|45|`t\_printsheet`|출력 Sheet|`print\_sheet`|변경|
|46|`t\_printtemplate`|출력 템플릿|`print\_template`|변경|

\---

# 18\. 기존 46개 테이블 누락 확인

```text
01 t\_combolist             → common\_code / common\_code\_item
02 t\_company               → company
03 t\_condition\_con         → condition / condition\_item
04 t\_conditiontemplate     → condition\_template / condition\_template\_item
05 t\_customer              → customer
06 t\_defect                → defect\_occurrence
07 t\_department            → department
08 t\_dept                  → department
09 t\_dept\_menu             → department\_menu
10 t\_downtime              → equipment\_downtime
11 t\_employee              → employee
12 t\_equipment             → equipment
13 t\_heatprocess            → heat\_process / version / step
14 t\_income                → sales\_order / sales\_order\_item
15 t\_inputsub              → production\_input
16 t\_inputwaiting           → production\_input\_queue
17 t\_inspection             → inspection
18 t\_inspectioncriteria     → inspection\_criteria
19 t\_inspectiondetail       → inspection\_measurement
20 t\_inspectionsub          → inspection\_item
21 t\_inspectiontemplate     → inspection\_template / version
22 t\_instruments            → instrument
23 t\_lineinspection         → line\_inspection / measurement
24 t\_maintenance            → maintenance
25 t\_menu\_catalog            → menu
26 t\_outcome                 → production\_output / shipment
27 t\_outcomesum              → shipment\_closing
28 t\_part                    → part
29 t\_part\_template           → part\_template
30 t\_printsheet              → print\_sheet
31 t\_printtemplate           → print\_template
32 t\_process\_default\_time    → process\_default\_time
33 t\_schedulebox             → schedule\_board\_item / layout
34 t\_standard                → standard / version
35 t\_standard\_gas            → standard\_gas
36 t\_standarddetail          → standard\_step
37 t\_standardtemplate        → standard\_template
38 t\_system\_settings         → system\_setting
39 t\_templatefieldname       → template\_field
40 t\_unitinspectionitem      → unit\_inspection\_item
41 t\_unitprocess             → unit\_process
42 t\_work                    → production\_work
43 t\_workconditiondetail     → work\_condition / work\_condition\_step
44 t\_workerassignment        → worker\_assignment
45 t\_worksub                 → production\_work\_item / production\_input
46 workplan                  → production\_schedule
```

**기존 46개 테이블을 모두 매핑하였다.**

\---

# 19\. 기존 VIEW / PROCEDURE도 별도 관리

현재 확인된 VIEW:

```text
v\_inspection\_report
v\_part\_customer
v\_work\_with\_all\_parts
```

신규 구조에서는 이 VIEW를 그대로 이전하지 않고 신규 테이블 관계에 맞게 재작성한다.

권장:

```text
vw\_inspection\_report
vw\_part\_customer
vw\_production\_work
```

현재 확인된 Procedure:

```text
sp\_RecalculateWorkPlanSequence
sp\_RecalculateWorkPlanTimes
```

이 두 Procedure는 실제 스케줄 기능에 중요한 로직이다.

신규 구조에서는:

```text
sp\_recalculate\_schedule\_sequence
sp\_recalculate\_schedule\_times
```

로 변경한다.

\---

# 20\. 최종 핵심 업무 ER 구조

```text
company
 │
 ├── customer
 │      │
 │      └── sales\_order
 │             │
 │             └── sales\_order\_item
 │                    │
 │                    └── lot
 │
 ├── part
 │      │
 │      └── process
 │             │
 │             └── process\_version
 │
 ├── equipment
 │
 ├── department
 │      │
 │      └── employee
 │
 └── instrument


sales\_order\_item
        │
        ▼
       lot
        │
        ▼
production\_schedule
        │
        ├──────── equipment
        ├──────── worker\_assignment
        │
        ▼
production\_work
        │
        ├── production\_input
        ├── work\_condition
        ├── inspection
        │      ├── inspection\_item
        │      └── inspection\_measurement
        │
        ├── defect\_occurrence
        │
        └── production\_output
                  │
                  ▼
               shipment
                  │
                  ▼
             shipment\_closing
```

\---

# 21\. 스케줄 화면 구조

스케줄 화면에서 필요한 데이터는 다음처럼 분리한다.

```text
\[업무 데이터]

production\_schedule
-------------------
schedule\_id
work\_date
equipment\_id
order\_item\_id
lot\_id
sequence\_no
planned\_start\_at
planned\_end\_at
planned\_duration
status


\[실제 진행]

production\_work
---------------
production\_work\_id
schedule\_id
actual\_start\_at
actual\_end\_at
actual\_duration
status


\[화면 표시]

schedule\_board\_layout
---------------------
layout\_id
equipment\_id
display\_order
x
y
width
height
title
```

이렇게 하면 화면의 Box 위치를 변경해도 실제 생산 스케줄 데이터가 변경되지 않는다.

\---

# 22\. 진행상태 설계

현재 `progressstep`, `isdone`, `isinspectiondone`을 여러 Boolean으로 관리하는 방식은 장기적으로 상태 충돌 가능성이 있다.

예:

```text
isdone = 1
isinspectiondone = 0
```

이면 "완료"인지 "검사대기"인지 별도의 해석이 필요하다.

신규 구조에서는 핵심 상태를 하나의 `status`로 관리한다.

예:

```text
PLANNED
ALLOCATED
READY
RUNNING
PAUSED
COMPLETED
INSPECTION\_WAIT
INSPECTION\_COMPLETED
CANCELLED
```

단, 실제 상태 목록은 현재 프로그램의 화면/업무 흐름을 확인한 후 확정한다.

\---

# 23\. 상태와 집계값은 분리

다음과 같은 필드는 상태와 수량을 구분한다.

```text
status
input\_qty
output\_qty
defect\_qty
remaining\_qty
```

예를 들어:

```text
status = COMPLETED
output\_qty = 950
defect\_qty = 50
```

처럼 관리한다.

`isdone`, `isinspectiondone` 등을 무조건 모두 없애는 것이 아니라, 기존 프로그램에서 Boolean을 직접 참조하는 부분을 조사한 뒤 호환 VIEW 또는 계산 컬럼으로 단계적으로 제거한다.

\---

# 24\. 삭제 처리

현재 일부 테이블에는:

```text
isdeleted
deleted\_by
deleted\_at
```

가 존재한다.

신규 구조에서는 Transaction 데이터의 물리 삭제를 최소화한다.

```text
is\_deleted
deleted\_by
deleted\_at
```

를 사용할 수 있으며, 특히:

* 수주
* 생산작업
* 검사
* 불량
* 출하

등은 실제 물리 삭제보다 취소/무효화 상태를 우선한다.

\---

# 25\. 마이그레이션 전략

기존 DB를 바로 변경하지 않는다.

## Phase 1

```text
bbakggum
    ↓
현재 구조 분석
```

## Phase 2

```text
bbakggum\_v2
```

신규 구조 생성.

## Phase 3

기존 데이터 Migration.

```text
t\_customer → customer
t\_part → part
t\_income → sales\_order/sales\_order\_item
t\_work → production\_work
workplan → production\_schedule
...
```

## Phase 4

기존 프로그램과 호환되는 VIEW 생성.

## Phase 5

프로그램을 모듈별로 신규 테이블로 전환.

## Phase 6

구 테이블을 Read Only로 전환.

## Phase 7

데이터 검증 후 구 테이블 폐기.

\---

# 26\. 이번 설계에서 반드시 추가 확인할 사항

현재 SQL 구조만으로는 다음 업무 규칙의 정확한 의미까지 확정할 수 없다.

1. `t\_income`이 정확히 "수주"인지 "입고"인지
2. `t\_outcome`이 출하인지 생산완료인지
3. `t\_schedulebox`가 단순 UI 저장인지 실제 스케줄 데이터인지
4. `progressstep`의 실제 전체 상태 목록
5. `t\_standard`와 `t\_heatprocess`의 실제 업무 관계
6. `t\_conditiontemplate`과 `t\_standardtemplate`의 실제 차이
7. `t\_inspection` / `t\_inspectionsub` / `t\_inspectiondetail`의 화면별 사용관계
8. `t\_outcomesum`이 단순 집계인지 월마감 Snapshot인지
9. `t\_department`와 `t\_dept` 중 실제 프로그램에서 사용 중인 테이블
10. 기존 VIEW 3개의 실제 프로그램 사용 위치

따라서 위 항목은 **현재 SQL만 보고 임의로 삭제하지 않는다.**

\---

# 27\. 설계 확정 후 생성할 신규 핵심 테이블

최종적으로는 다음 그룹을 중심으로 CREATE TABLE을 작성한다.

## 기준정보

```text
company
customer
department
employee
equipment
equipment\_type
instrument
part
part\_customer
defect
```

## 공정/표준

```text
process
process\_version
process\_step
heat\_process
heat\_process\_version
heat\_process\_step
heat\_process\_gas
unit\_process
standard
standard\_version
standard\_step
```

## 수주/LOT

```text
sales\_order
sales\_order\_item
lot
```

## 생산계획/작업

```text
production\_schedule
production\_work
production\_work\_item
production\_input
production\_input\_queue
worker\_assignment
work\_shift
work\_pattern
```

## 검사

```text
inspection
inspection\_item
inspection\_measurement
inspection\_criteria
inspection\_template
inspection\_template\_version
unit\_inspection\_item
line\_inspection
line\_inspection\_measurement
```

## 생산결과/출하

```text
production\_output
shipment
shipment\_item
shipment\_closing
defect\_occurrence
```

## 설비

```text
equipment\_downtime
maintenance
```

## 시스템

```text
common\_code
common\_code\_item
menu
department\_menu
system\_setting
```

## 템플릿/출력

```text
template\_field
part\_template
print\_sheet
print\_template
schedule\_board\_layout
```

\---

# 28\. 결론

이번 구조개편의 핵심은 단순히 테이블 이름을 바꾸는 것이 아니다.

현재:

```text
수주
 ├─ 품목정보 복사
 ├─ 고객정보 복사
 ├─ 공정정보 복사
 └─ 상태정보

workplan
 ├─ 고객명
 ├─ 품목명
 ├─ 설비
 ├─ 시작시간
 └─ 종료시간

t\_work
 ├─ 고객/품목
 ├─ 설비
 ├─ 작업시간
 ├─ 진행상태
 └─ 검사상태
```

처럼 서로 다른 업무 단계가 중복되어 있는 것을,

```text
Master
   ↓
Order
   ↓
LOT
   ↓
Schedule
   ↓
Production Work
   ↓
Inspection
   ↓
Defect / Output
   ↓
Shipment / Closing
```

으로 명확하게 분리한다.

그리고 **과거 생산기록을 현재 Master 변경으로부터 보호하기 위해 Version + Snapshot을 적용**한다.

특히 스케줄은:

```text
production\_schedule = 계획
production\_work     = 실제 진행
schedule\_board      = 화면 표시
worker\_assignment   = 작업자 배정
```

으로 분리하는 것을 기본 설계로 한다.



\---

# 29\. 신규 DB 전체 CREATE TABLE 스키마

> \*\*중요\*\*
>
> 아래 DDL은 현재 `bbakggum`의 46개 테이블을 기준으로 재설계한 \*\*V2 논리/물리 설계 초안\*\*이다.
>
> - 기존 테이블을 그대로 ALTER하는 용도가 아니다.
> - `bbakggum\_v2` 같은 신규 DB에서 생성하는 것을 전제로 한다.
> - 기존 데이터의 모든 컬럼을 무조건 1:1 복사하지 않고, 업무 의미에 따라 정규화하였다.
> - 특히 `step1\~step15`, `item1\~item15`, `v1\~v10` 형태의 반복 컬럼은 Row 기반 Detail 테이블로 변경한다.
> - Snapshot/Version은 과거 생산·검사 기록 재현에 필요한 부분에 적용한다.
> - 실제 운영 적용 전에는 기존 프로그램의 INSERT/UPDATE/SELECT 사용처와 데이터 분포를 검증해야 한다.

## 29.1 DB 생성

```sql
CREATE DATABASE IF NOT EXISTS `bbakggum\_v2`
  DEFAULT CHARACTER SET utf8mb4
  COLLATE utf8mb4\_uca1400\_ai\_ci;

USE `bbakggum\_v2`;

SET FOREIGN\_KEY\_CHECKS = 0;
```

\---

# 30\. 공통 기준정보

## 30.1 company

```sql
CREATE TABLE company (
    company\_id       BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    company\_name     VARCHAR(100) NOT NULL,
    ceo\_name         VARCHAR(50) NOT NULL DEFAULT '',
    business\_no      VARCHAR(20) NOT NULL DEFAULT '',
    business\_type    VARCHAR(50) NOT NULL DEFAULT '',
    business\_item    VARCHAR(100) NOT NULL DEFAULT '',
    phone            VARCHAR(50) NOT NULL DEFAULT '',
    email            VARCHAR(100) NOT NULL DEFAULT '',
    fax              VARCHAR(50) NOT NULL DEFAULT '',
    address          VARCHAR(200) NOT NULL DEFAULT '',
    address\_detail   VARCHAR(200) NOT NULL DEFAULT '',
    stamp\_path       VARCHAR(500) NULL,
    is\_active        TINYINT(1) NOT NULL DEFAULT 1,
    created\_at       DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    updated\_at       DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP ON UPDATE CURRENT\_TIMESTAMP,
    PRIMARY KEY (company\_id),
    UNIQUE KEY uk\_company\_business\_no (business\_no)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 30.2 customer

```sql
CREATE TABLE customer (
    customer\_id      BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    customer\_code    VARCHAR(50) NOT NULL,
    customer\_type    VARCHAR(20) NOT NULL DEFAULT 'SALES',
    customer\_name    VARCHAR(100) NOT NULL,
    business\_no      VARCHAR(20) NULL,
    ceo\_name         VARCHAR(50) NULL,
    business\_type    VARCHAR(50) NULL,
    business\_item    VARCHAR(50) NULL,
    phone            VARCHAR(30) NULL,
    email            VARCHAR(100) NULL,
    fax              VARCHAR(30) NULL,
    address          VARCHAR(255) NULL,
    address\_detail   VARCHAR(255) NULL,
    closing\_day      VARCHAR(10) NULL,
    remark           TEXT NULL,
    is\_active        TINYINT(1) NOT NULL DEFAULT 1,
    created\_at       DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    updated\_at       DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP ON UPDATE CURRENT\_TIMESTAMP,
    PRIMARY KEY (customer\_id),
    UNIQUE KEY uk\_customer\_code (customer\_code),
    KEY ix\_customer\_name (customer\_name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 30.3 department

`t\_department`와 `t\_dept`는 하나의 부서 Master로 통합한다.

```sql
CREATE TABLE department (
    department\_id    BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    department\_code  VARCHAR(30) NOT NULL,
    department\_name  VARCHAR(50) NOT NULL,
    is\_active        TINYINT(1) NOT NULL DEFAULT 1,
    created\_at       DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    updated\_at       DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP ON UPDATE CURRENT\_TIMESTAMP,
    PRIMARY KEY (department\_id),
    UNIQUE KEY uk\_department\_code (department\_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 30.4 position

```sql
CREATE TABLE position (
    position\_id      BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    position\_code    VARCHAR(30) NOT NULL,
    position\_name    VARCHAR(50) NOT NULL,
    is\_active        TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (position\_id),
    UNIQUE KEY uk\_position\_code (position\_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 30.5 employee

기존 `t\_employee.id`에 PK가 없던 문제를 보완한다.

```sql
CREATE TABLE employee (
    employee\_id      BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    employee\_code    VARCHAR(50) NOT NULL,
    employee\_name    VARCHAR(50) NOT NULL,
    nationality      VARCHAR(50) NULL,
    address          VARCHAR(100) NULL,
    phone            VARCHAR(50) NULL,
    mobile\_phone     VARCHAR(50) NULL,
    department\_id    BIGINT UNSIGNED NULL,
    position\_id      BIGINT UNSIGNED NULL,
    part\_name        VARCHAR(50) NULL,
    employment\_date  DATE NULL,
    resignation\_date DATE NULL,
    is\_working       TINYINT(1) NOT NULL DEFAULT 1,
    is\_assignment\_target TINYINT(1) NOT NULL DEFAULT 0,
    is\_active        TINYINT(1) NOT NULL DEFAULT 1,
    remark           TEXT NULL,
    created\_at       DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    updated\_at       DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP ON UPDATE CURRENT\_TIMESTAMP,
    PRIMARY KEY (employee\_id),
    UNIQUE KEY uk\_employee\_code (employee\_code),
    KEY ix\_employee\_department (department\_id),
    CONSTRAINT fk\_employee\_department
        FOREIGN KEY (department\_id) REFERENCES department(department\_id),
    CONSTRAINT fk\_employee\_position
        FOREIGN KEY (position\_id) REFERENCES position(position\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 30.6 equipment\_type

```sql
CREATE TABLE equipment\_type (
    equipment\_type\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    equipment\_type\_code VARCHAR(30) NOT NULL,
    equipment\_type\_name VARCHAR(50) NOT NULL,
    description       VARCHAR(255) NULL,
    is\_active         TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (equipment\_type\_id),
    UNIQUE KEY uk\_equipment\_type\_code (equipment\_type\_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 30.7 equipment

```sql
CREATE TABLE equipment (
    equipment\_id      BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    equipment\_type\_id BIGINT UNSIGNED NULL,
    equipment\_code    VARCHAR(50) NOT NULL,
    equipment\_no      INT NULL,
    equipment\_initial VARCHAR(50) NULL,
    equipment\_name    VARCHAR(50) NOT NULL,
    install\_date      DATE NULL,
    image\_path        VARCHAR(500) NULL,
    is\_active         TINYINT(1) NOT NULL DEFAULT 1,
    created\_at        DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    updated\_at        DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP ON UPDATE CURRENT\_TIMESTAMP,
    PRIMARY KEY (equipment\_id),
    UNIQUE KEY uk\_equipment\_code (equipment\_code),
    KEY ix\_equipment\_type (equipment\_type\_id),
    CONSTRAINT fk\_equipment\_type
        FOREIGN KEY (equipment\_type\_id) REFERENCES equipment\_type(equipment\_type\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 30.8 instrument

```sql
CREATE TABLE instrument (
    instrument\_id     BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    instrument\_code   VARCHAR(50) NOT NULL,
    instrument\_name   VARCHAR(100) NOT NULL,
    instrument\_type   VARCHAR(50) NULL,
    serial\_no         VARCHAR(100) NULL,
    calibration\_cycle INT NULL,
    last\_calibrated\_at DATETIME NULL,
    next\_calibration\_at DATETIME NULL,
    is\_active         TINYINT(1) NOT NULL DEFAULT 1,
    remark            TEXT NULL,
    created\_at        DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    updated\_at        DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP ON UPDATE CURRENT\_TIMESTAMP,
    PRIMARY KEY (instrument\_id),
    UNIQUE KEY uk\_instrument\_code (instrument\_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

\---

# 31\. 공통 코드/메뉴/시스템

## 31.1 common\_code

```sql
CREATE TABLE common\_code (
    common\_code\_group\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    group\_code           VARCHAR(50) NOT NULL,
    group\_name           VARCHAR(100) NOT NULL,
    is\_active             TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (common\_code\_group\_id),
    UNIQUE KEY uk\_common\_code\_group (group\_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 31.2 common\_code\_item

```sql
CREATE TABLE common\_code\_item (
    common\_code\_item\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    common\_code\_group\_id BIGINT UNSIGNED NOT NULL,
    code                 VARCHAR(50) NOT NULL,
    code\_name            VARCHAR(100) NOT NULL,
    sort\_order           INT NOT NULL DEFAULT 0,
    is\_active            TINYINT(1) NOT NULL DEFAULT 1,
    remark               VARCHAR(255) NULL,
    PRIMARY KEY (common\_code\_item\_id),
    UNIQUE KEY uk\_common\_code\_item (common\_code\_group\_id, code),
    CONSTRAINT fk\_common\_code\_item\_group
        FOREIGN KEY (common\_code\_group\_id)
        REFERENCES common\_code(common\_code\_group\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 31.3 menu

```sql
CREATE TABLE menu (
    menu\_id        BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    menu\_key       VARCHAR(50) NOT NULL,
    menu\_name      VARCHAR(100) NOT NULL,
    parent\_menu\_id BIGINT UNSIGNED NULL,
    route          VARCHAR(255) NULL,
    sort\_order     INT NOT NULL DEFAULT 0,
    is\_active      TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (menu\_id),
    UNIQUE KEY uk\_menu\_key (menu\_key),
    CONSTRAINT fk\_menu\_parent
        FOREIGN KEY (parent\_menu\_id) REFERENCES menu(menu\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 31.4 department\_menu

```sql
CREATE TABLE department\_menu (
    department\_id BIGINT UNSIGNED NOT NULL,
    menu\_id       BIGINT UNSIGNED NOT NULL,
    can\_read      TINYINT(1) NOT NULL DEFAULT 1,
    can\_create    TINYINT(1) NOT NULL DEFAULT 0,
    can\_update    TINYINT(1) NOT NULL DEFAULT 0,
    can\_delete    TINYINT(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (department\_id, menu\_id),
    CONSTRAINT fk\_department\_menu\_department
        FOREIGN KEY (department\_id) REFERENCES department(department\_id),
    CONSTRAINT fk\_department\_menu\_menu
        FOREIGN KEY (menu\_id) REFERENCES menu(menu\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 31.5 system\_setting

```sql
CREATE TABLE system\_setting (
    setting\_key VARCHAR(100) NOT NULL,
    setting\_value TEXT NOT NULL,
    updated\_at DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP ON UPDATE CURRENT\_TIMESTAMP,
    PRIMARY KEY (setting\_key)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

\---

# 32\. 품목 / 공정 / LOT

## 32.1 part

```sql
CREATE TABLE part (
    part\_id          BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    part\_code        VARCHAR(50) NOT NULL,
    part\_name        VARCHAR(100) NOT NULL,
    part\_number      VARCHAR(100) NULL,
    specification    VARCHAR(100) NULL,
    model            VARCHAR(100) NULL,
    material         VARCHAR(100) NULL,
    unit\_weight      DECIMAL(12,4) NULL,
    unit\_code        VARCHAR(20) NULL,
    hardness         VARCHAR(100) NULL,
    core\_hardness    VARCHAR(100) NULL,
    effective\_hardening\_depth VARCHAR(100) NULL,
    grade            VARCHAR(50) NULL,
    texture          VARCHAR(100) NULL,
    is\_active        TINYINT(1) NOT NULL DEFAULT 1,
    created\_at       DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    updated\_at       DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP ON UPDATE CURRENT\_TIMESTAMP,
    PRIMARY KEY (part\_id),
    UNIQUE KEY uk\_part\_code (part\_code),
    KEY ix\_part\_number (part\_number)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 32.2 part\_customer

```sql
CREATE TABLE part\_customer (
    part\_customer\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    part\_id          BIGINT UNSIGNED NOT NULL,
    customer\_id      BIGINT UNSIGNED NOT NULL,
    customer\_part\_code VARCHAR(100) NULL,
    customer\_lot\_required TINYINT(1) NOT NULL DEFAULT 0,
    is\_primary       TINYINT(1) NOT NULL DEFAULT 0,
    is\_active        TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (part\_customer\_id),
    UNIQUE KEY uk\_part\_customer (part\_id, customer\_id),
    CONSTRAINT fk\_part\_customer\_part
        FOREIGN KEY (part\_id) REFERENCES part(part\_id),
    CONSTRAINT fk\_part\_customer\_customer
        FOREIGN KEY (customer\_id) REFERENCES customer(customer\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 32.3 process

```sql
CREATE TABLE process (
    process\_id       BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    process\_code     VARCHAR(50) NOT NULL,
    process\_name     VARCHAR(100) NOT NULL,
    process\_type     VARCHAR(50) NULL,
    is\_active        TINYINT(1) NOT NULL DEFAULT 1,
    created\_at       DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    updated\_at       DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP ON UPDATE CURRENT\_TIMESTAMP,
    PRIMARY KEY (process\_id),
    UNIQUE KEY uk\_process\_code (process\_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 32.4 process\_version

```sql
CREATE TABLE process\_version (
    process\_version\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    process\_id         BIGINT UNSIGNED NOT NULL,
    version\_no         INT NOT NULL,
    effective\_from     DATETIME NOT NULL,
    effective\_to       DATETIME NULL,
    is\_current         TINYINT(1) NOT NULL DEFAULT 1,
    remark             VARCHAR(255) NULL,
    created\_at         DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    PRIMARY KEY (process\_version\_id),
    UNIQUE KEY uk\_process\_version (process\_id, version\_no),
    CONSTRAINT fk\_process\_version\_process
        FOREIGN KEY (process\_id) REFERENCES process(process\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 32.5 process\_step

```sql
CREATE TABLE process\_step (
    process\_step\_id    BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    process\_version\_id BIGINT UNSIGNED NOT NULL,
    sequence\_no        INT NOT NULL,
    unit\_process\_id    BIGINT UNSIGNED NULL,
    step\_name          VARCHAR(100) NOT NULL,
    planned\_duration\_min DECIMAL(10,2) NULL,
    is\_active          TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (process\_step\_id),
    UNIQUE KEY uk\_process\_step (process\_version\_id, sequence\_no),
    CONSTRAINT fk\_process\_step\_version
        FOREIGN KEY (process\_version\_id) REFERENCES process\_version(process\_version\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 32.6 unit\_process

```sql
CREATE TABLE unit\_process (
    unit\_process\_id   BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    unit\_process\_code VARCHAR(50) NOT NULL,
    unit\_process\_name VARCHAR(100) NOT NULL,
    sort\_order        INT NOT NULL DEFAULT 0,
    is\_active         TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (unit\_process\_id),
    UNIQUE KEY uk\_unit\_process\_code (unit\_process\_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 32.7 process\_default\_time

```sql
CREATE TABLE process\_default\_time (
    process\_default\_time\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    equipment\_type\_id       BIGINT UNSIGNED NULL,
    equipment\_id            BIGINT UNSIGNED NULL,
    unit\_process\_id         BIGINT UNSIGNED NULL,
    running\_time\_min        DECIMAL(10,2) NOT NULL DEFAULT 0,
    remark                  VARCHAR(200) NOT NULL DEFAULT '',
    created\_at              DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    updated\_at              DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP ON UPDATE CURRENT\_TIMESTAMP,
    PRIMARY KEY (process\_default\_time\_id),
    KEY ix\_process\_default\_equipment\_type (equipment\_type\_id),
    KEY ix\_process\_default\_equipment (equipment\_id),
    CONSTRAINT fk\_process\_default\_equipment\_type
        FOREIGN KEY (equipment\_type\_id) REFERENCES equipment\_type(equipment\_type\_id),
    CONSTRAINT fk\_process\_default\_equipment
        FOREIGN KEY (equipment\_id) REFERENCES equipment(equipment\_id),
    CONSTRAINT fk\_process\_default\_unit\_process
        FOREIGN KEY (unit\_process\_id) REFERENCES unit\_process(unit\_process\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 32.8 lot

```sql
CREATE TABLE lot (
    lot\_id             BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    lot\_no             VARCHAR(100) NOT NULL,
    parent\_lot\_id      BIGINT UNSIGNED NULL,
    part\_id            BIGINT UNSIGNED NOT NULL,
    order\_item\_id      BIGINT UNSIGNED NULL,
    original\_qty       DECIMAL(14,3) NOT NULL DEFAULT 0,
    current\_qty        DECIMAL(14,3) NOT NULL DEFAULT 0,
    status             VARCHAR(30) NOT NULL DEFAULT 'OPEN',
    created\_at         DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    updated\_at         DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP ON UPDATE CURRENT\_TIMESTAMP,
    PRIMARY KEY (lot\_id),
    UNIQUE KEY uk\_lot\_no (lot\_no),
    KEY ix\_lot\_parent (parent\_lot\_id),
    KEY ix\_lot\_part (part\_id),
    CONSTRAINT fk\_lot\_parent
        FOREIGN KEY (parent\_lot\_id) REFERENCES lot(lot\_id),
    CONSTRAINT fk\_lot\_part
        FOREIGN KEY (part\_id) REFERENCES part(part\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

> `lot.order\_item\_id`는 `sales\_order\_item` 생성 이후 FK를 추가하는 것이 안전하다.

\---

# 33\. 열처리 공정 / 표준

## 33.1 heat\_process

```sql
CREATE TABLE heat\_process (
    heat\_process\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    process\_id      BIGINT UNSIGNED NULL,
    heat\_process\_code VARCHAR(50) NOT NULL,
    heat\_process\_name VARCHAR(100) NOT NULL,
    is\_active       TINYINT(1) NOT NULL DEFAULT 1,
    created\_at      DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    updated\_at      DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP ON UPDATE CURRENT\_TIMESTAMP,
    PRIMARY KEY (heat\_process\_id),
    UNIQUE KEY uk\_heat\_process\_code (heat\_process\_code),
    CONSTRAINT fk\_heat\_process\_process
        FOREIGN KEY (process\_id) REFERENCES process(process\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 33.2 heat\_process\_version

```sql
CREATE TABLE heat\_process\_version (
    heat\_process\_version\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    heat\_process\_id         BIGINT UNSIGNED NOT NULL,
    version\_no              INT NOT NULL,
    effective\_from          DATETIME NOT NULL,
    effective\_to            DATETIME NULL,
    is\_current              TINYINT(1) NOT NULL DEFAULT 1,
    remark                  VARCHAR(255) NULL,
    created\_at              DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    PRIMARY KEY (heat\_process\_version\_id),
    UNIQUE KEY uk\_heat\_process\_version (heat\_process\_id, version\_no),
    CONSTRAINT fk\_heat\_process\_version\_process
        FOREIGN KEY (heat\_process\_id) REFERENCES heat\_process(heat\_process\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 33.3 heat\_process\_step

```sql
CREATE TABLE heat\_process\_step (
    heat\_process\_step\_id    BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    heat\_process\_version\_id BIGINT UNSIGNED NOT NULL,
    sequence\_no             INT NOT NULL,
    step\_name               VARCHAR(100) NOT NULL,
    sub\_process\_code        VARCHAR(50) NULL,
    temperature             DECIMAL(8,2) NULL,
    holding\_time\_min        DECIMAL(10,2) NULL,
    zone\_no                 VARCHAR(50) NULL,
    wash\_mode               VARCHAR(50) NULL,
    cp                      VARCHAR(50) NULL,
    rx                      VARCHAR(50) NULL,
    nh3                     VARCHAR(50) NULL,
    lng                     VARCHAR(50) NULL,
    n2                      VARCHAR(50) NULL,
    agitator                TINYINT(1) NULL,
    condition\_value         TEXT NULL,
    PRIMARY KEY (heat\_process\_step\_id),
    UNIQUE KEY uk\_heat\_process\_step (heat\_process\_version\_id, sequence\_no),
    CONSTRAINT fk\_heat\_process\_step\_version
        FOREIGN KEY (heat\_process\_version\_id)
        REFERENCES heat\_process\_version(heat\_process\_version\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 33.4 standard

```sql
CREATE TABLE standard (
    standard\_id       BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    standard\_code     VARCHAR(50) NOT NULL,
    standard\_name     VARCHAR(100) NOT NULL,
    part\_id           BIGINT UNSIGNED NOT NULL,
    equipment\_type\_id BIGINT UNSIGNED NULL,
    equipment\_id      BIGINT UNSIGNED NULL,
    unit\_process\_id   BIGINT UNSIGNED NULL,
    charge\_qty        DECIMAL(14,3) NOT NULL DEFAULT 0,
    charge\_unit       VARCHAR(50) NOT NULL DEFAULT 'charge',
    is\_active         TINYINT(1) NOT NULL DEFAULT 1,
    created\_at        DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    updated\_at        DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP ON UPDATE CURRENT\_TIMESTAMP,
    PRIMARY KEY (standard\_id),
    UNIQUE KEY uk\_standard\_code (standard\_code),
    CONSTRAINT fk\_standard\_part
        FOREIGN KEY (part\_id) REFERENCES part(part\_id),
    CONSTRAINT fk\_standard\_equipment\_type
        FOREIGN KEY (equipment\_type\_id) REFERENCES equipment\_type(equipment\_type\_id),
    CONSTRAINT fk\_standard\_equipment
        FOREIGN KEY (equipment\_id) REFERENCES equipment(equipment\_id),
    CONSTRAINT fk\_standard\_unit\_process
        FOREIGN KEY (unit\_process\_id) REFERENCES unit\_process(unit\_process\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 33.5 standard\_version

```sql
CREATE TABLE standard\_version (
    standard\_version\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    standard\_id         BIGINT UNSIGNED NOT NULL,
    version\_no          INT NOT NULL,
    effective\_from      DATETIME NOT NULL,
    effective\_to        DATETIME NULL,
    is\_current          TINYINT(1) NOT NULL DEFAULT 1,
    remark              VARCHAR(255) NULL,
    PRIMARY KEY (standard\_version\_id),
    UNIQUE KEY uk\_standard\_version (standard\_id, version\_no),
    CONSTRAINT fk\_standard\_version\_standard
        FOREIGN KEY (standard\_id) REFERENCES standard(standard\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 33.6 standard\_step

```sql
CREATE TABLE standard\_step (
    standard\_step\_id    BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    standard\_version\_id BIGINT UNSIGNED NOT NULL,
    sequence\_no         INT NOT NULL,
    item\_code           VARCHAR(50) NULL,
    item\_name           VARCHAR(100) NULL,
    step\_value          VARCHAR(255) NULL,
    condition\_type      VARCHAR(50) NULL,
    condition\_value     VARCHAR(255) NULL,
    PRIMARY KEY (standard\_step\_id),
    UNIQUE KEY uk\_standard\_step (standard\_version\_id, sequence\_no),
    CONSTRAINT fk\_standard\_step\_version
        FOREIGN KEY (standard\_version\_id) REFERENCES standard\_version(standard\_version\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 33.7 standard\_gas

```sql
CREATE TABLE standard\_gas (
    standard\_gas\_id     BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    standard\_version\_id BIGINT UNSIGNED NOT NULL,
    sequence\_no         INT NOT NULL,
    step\_name           VARCHAR(50) NOT NULL,
    zone\_no             VARCHAR(50) NULL,
    equipment\_no        VARCHAR(50) NULL,
    wash\_mode           VARCHAR(50) NULL,
    temperature         VARCHAR(50) NULL,
    time\_value          VARCHAR(50) NULL,
    cp                  VARCHAR(50) NULL,
    rx                  VARCHAR(50) NULL,
    nh3                 VARCHAR(50) NULL,
    lng                 VARCHAR(50) NULL,
    n2                  VARCHAR(50) NULL,
    agitator            TINYINT(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (standard\_gas\_id),
    UNIQUE KEY uk\_standard\_gas\_step (standard\_version\_id, sequence\_no),
    CONSTRAINT fk\_standard\_gas\_version
        FOREIGN KEY (standard\_version\_id) REFERENCES standard\_version(standard\_version\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 33.8 condition\_template / condition\_template\_item

```sql
CREATE TABLE condition\_template (
    condition\_template\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    template\_code         VARCHAR(50) NOT NULL,
    template\_name         VARCHAR(100) NOT NULL,
    template\_type         VARCHAR(50) NULL,
    is\_active              TINYINT(1) NOT NULL DEFAULT 1,
    created\_at             DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    PRIMARY KEY (condition\_template\_id),
    UNIQUE KEY uk\_condition\_template\_code (template\_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;

CREATE TABLE condition\_template\_item (
    condition\_template\_item\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    condition\_template\_id      BIGINT UNSIGNED NOT NULL,
    sequence\_no                INT NOT NULL,
    item\_name                  VARCHAR(100) NOT NULL,
    field\_type                 VARCHAR(50) NULL,
    PRIMARY KEY (condition\_template\_item\_id),
    UNIQUE KEY uk\_condition\_template\_item (condition\_template\_id, sequence\_no),
    CONSTRAINT fk\_condition\_template\_item\_template
        FOREIGN KEY (condition\_template\_id)
        REFERENCES condition\_template(condition\_template\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 33.9 work\_condition / work\_condition\_step

```sql
CREATE TABLE work\_condition (
    work\_condition\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    production\_work\_id BIGINT UNSIGNED NOT NULL,
    running\_time\_min DECIMAL(10,2) NULL,
    template\_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (work\_condition\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;

CREATE TABLE work\_condition\_step (
    work\_condition\_step\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    work\_condition\_id     BIGINT UNSIGNED NOT NULL,
    sequence\_no           INT NOT NULL,
    item\_name             VARCHAR(100) NULL,
    value                 VARCHAR(255) NULL,
    PRIMARY KEY (work\_condition\_step\_id),
    UNIQUE KEY uk\_work\_condition\_step (work\_condition\_id, sequence\_no),
    CONSTRAINT fk\_work\_condition\_step\_condition
        FOREIGN KEY (work\_condition\_id) REFERENCES work\_condition(work\_condition\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

\---

# 34\. 수주 / 주문 / LOT

## 34.1 sales\_order

```sql
CREATE TABLE sales\_order (
    sales\_order\_id       BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    order\_no             VARCHAR(50) NOT NULL,
    order\_date           DATE NOT NULL,
    customer\_id          BIGINT UNSIGNED NOT NULL,
    priority             INT NOT NULL DEFAULT 3,
    status               VARCHAR(30) NOT NULL DEFAULT 'OPEN',
    remark               VARCHAR(255) NULL,
    is\_deleted           TINYINT(1) NOT NULL DEFAULT 0,
    deleted\_by           VARCHAR(100) NULL,
    deleted\_at           DATETIME NULL,
    created\_at           DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    updated\_at           DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP ON UPDATE CURRENT\_TIMESTAMP,
    PRIMARY KEY (sales\_order\_id),
    UNIQUE KEY uk\_sales\_order\_no (order\_no),
    KEY ix\_sales\_order\_customer (customer\_id),
    CONSTRAINT fk\_sales\_order\_customer
        FOREIGN KEY (customer\_id) REFERENCES customer(customer\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 34.2 sales\_order\_item

```sql
CREATE TABLE sales\_order\_item (
    order\_item\_id       BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    sales\_order\_id      BIGINT UNSIGNED NOT NULL,
    line\_no             INT NOT NULL,
    part\_id             BIGINT UNSIGNED NOT NULL,
    heat\_process\_version\_id BIGINT UNSIGNED NULL,
    customer\_lot        VARCHAR(100) NULL,
    coil\_no             VARCHAR(100) NULL,
    unit\_weight         DECIMAL(12,4) NULL,
    unit\_price          DECIMAL(15,2) NULL,
    unit\_code           VARCHAR(20) NULL,
    order\_qty           DECIMAL(14,3) NOT NULL DEFAULT 0,
    input\_qty           DECIMAL(14,3) NOT NULL DEFAULT 0,
    output\_qty          DECIMAL(14,3) NOT NULL DEFAULT 0,
    shipment\_qty        DECIMAL(14,3) NOT NULL DEFAULT 0,
    remaining\_input\_qty  DECIMAL(14,3) NOT NULL DEFAULT 0,
    remaining\_shipment\_qty DECIMAL(14,3) NOT NULL DEFAULT 0,
    material\_snapshot   VARCHAR(100) NULL,
    part\_name\_snapshot  VARCHAR(100) NULL,
    part\_number\_snapshot VARCHAR(100) NULL,
    specification\_snapshot VARCHAR(100) NULL,
    model\_snapshot      VARCHAR(100) NULL,
    heat\_process\_name\_snapshot VARCHAR(100) NULL,
    is\_input             TINYINT(1) NOT NULL DEFAULT 0,
    is\_return            TINYINT(1) NOT NULL DEFAULT 0,
    is\_rework            TINYINT(1) NOT NULL DEFAULT 0,
    status               VARCHAR(30) NOT NULL DEFAULT 'OPEN',
    PRIMARY KEY (order\_item\_id),
    UNIQUE KEY uk\_sales\_order\_item (sales\_order\_id, line\_no),
    CONSTRAINT fk\_sales\_order\_item\_order
        FOREIGN KEY (sales\_order\_id) REFERENCES sales\_order(sales\_order\_id),
    CONSTRAINT fk\_sales\_order\_item\_part
        FOREIGN KEY (part\_id) REFERENCES part(part\_id),
    CONSTRAINT fk\_sales\_order\_item\_heat\_process\_version
        FOREIGN KEY (heat\_process\_version\_id)
        REFERENCES heat\_process\_version(heat\_process\_version\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

`lot.order\_item\_id` FK:

```sql
ALTER TABLE lot
    ADD CONSTRAINT fk\_lot\_order\_item
    FOREIGN KEY (order\_item\_id) REFERENCES sales\_order\_item(order\_item\_id);
```

\---

# 35\. 생산 투입 대기 / 투입

## 35.1 production\_input\_queue

```sql
CREATE TABLE production\_input\_queue (
    production\_input\_queue\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    order\_item\_id             BIGINT UNSIGNED NOT NULL,
    unit\_process\_id           BIGINT UNSIGNED NULL,
    equipment\_type\_id         BIGINT UNSIGNED NULL,
    allocated\_qty              DECIMAL(14,3) NOT NULL DEFAULT 0,
    remaining\_qty              DECIMAL(14,3) NOT NULL DEFAULT 0,
    status                     VARCHAR(30) NOT NULL DEFAULT 'WAITING',
    created\_at                 DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    updated\_at                 DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP ON UPDATE CURRENT\_TIMESTAMP,
    PRIMARY KEY (production\_input\_queue\_id),
    CONSTRAINT fk\_input\_queue\_order\_item
        FOREIGN KEY (order\_item\_id) REFERENCES sales\_order\_item(order\_item\_id),
    CONSTRAINT fk\_input\_queue\_unit\_process
        FOREIGN KEY (unit\_process\_id) REFERENCES unit\_process(unit\_process\_id),
    CONSTRAINT fk\_input\_queue\_equipment\_type
        FOREIGN KEY (equipment\_type\_id) REFERENCES equipment\_type(equipment\_type\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 35.2 production\_input

```sql
CREATE TABLE production\_input (
    production\_input\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    production\_work\_id  BIGINT UNSIGNED NULL,
    lot\_id              BIGINT UNSIGNED NOT NULL,
    tray\_mark            VARCHAR(50) NULL,
    input\_qty            DECIMAL(14,3) NOT NULL DEFAULT 0,
    input\_weight         DECIMAL(14,4) NULL,
    input\_at             DATETIME NULL,
    output\_at            DATETIME NULL,
    status               VARCHAR(30) NOT NULL DEFAULT 'WAITING',
    PRIMARY KEY (production\_input\_id),
    CONSTRAINT fk\_production\_input\_work
        FOREIGN KEY (production\_work\_id) REFERENCES production\_work(production\_work\_id),
    CONSTRAINT fk\_production\_input\_lot
        FOREIGN KEY (lot\_id) REFERENCES lot(lot\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

> `production\_input.production\_work\_id`는 `production\_work` 생성 이후 FK를 추가하거나, 전체 DDL을 두 단계로 실행한다.

\---

# 36\. 작업자 / 교대 / 배정

## 36.1 work\_shift

```sql
CREATE TABLE work\_shift (
    shift\_id       BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    shift\_code     VARCHAR(30) NOT NULL,
    shift\_name     VARCHAR(50) NOT NULL,
    start\_time     TIME NOT NULL,
    end\_time       TIME NOT NULL,
    is\_next\_day    TINYINT(1) NOT NULL DEFAULT 0,
    is\_active      TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (shift\_id),
    UNIQUE KEY uk\_work\_shift\_code (shift\_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 36.2 work\_pattern

```sql
CREATE TABLE work\_pattern (
    work\_pattern\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    pattern\_code    VARCHAR(30) NOT NULL,
    pattern\_name    VARCHAR(50) NOT NULL,
    description     VARCHAR(255) NULL,
    is\_active       TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (work\_pattern\_id),
    UNIQUE KEY uk\_work\_pattern\_code (pattern\_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 36.3 worker\_assignment

```sql
CREATE TABLE worker\_assignment (
    worker\_assignment\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    employee\_id          BIGINT UNSIGNED NOT NULL,
    equipment\_id         BIGINT UNSIGNED NOT NULL,
    shift\_id             BIGINT UNSIGNED NOT NULL,
    work\_date            DATE NOT NULL,
    assignment\_type      VARCHAR(30) NOT NULL DEFAULT 'PRIMARY',
    is\_active            TINYINT(1) NOT NULL DEFAULT 1,
    created\_at           DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    updated\_at           DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP ON UPDATE CURRENT\_TIMESTAMP,
    PRIMARY KEY (worker\_assignment\_id),
    UNIQUE KEY uk\_worker\_assignment (employee\_id, equipment\_id, shift\_id, work\_date),
    KEY ix\_worker\_assignment\_equipment\_date (equipment\_id, work\_date),
    CONSTRAINT fk\_worker\_assignment\_employee
        FOREIGN KEY (employee\_id) REFERENCES employee(employee\_id),
    CONSTRAINT fk\_worker\_assignment\_equipment
        FOREIGN KEY (equipment\_id) REFERENCES equipment(equipment\_id),
    CONSTRAINT fk\_worker\_assignment\_shift
        FOREIGN KEY (shift\_id) REFERENCES work\_shift(shift\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

\---

# 37\. 생산 스케줄

## 37.1 production\_schedule

기존 `workplan`을 대체한다.

```sql
CREATE TABLE production\_schedule (
    schedule\_id              BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    schedule\_no              VARCHAR(50) NULL,
    work\_date                DATE NOT NULL,
    equipment\_id             BIGINT UNSIGNED NOT NULL,
    order\_item\_id            BIGINT UNSIGNED NULL,
    lot\_id                   BIGINT UNSIGNED NULL,
    temporary\_lot\_no         VARCHAR(100) NULL,
    sequence\_no              INT NOT NULL DEFAULT 1,
    planned\_qty              DECIMAL(14,3) NOT NULL DEFAULT 0,
    planned\_duration\_min     DECIMAL(10,2) NOT NULL DEFAULT 0,
    planned\_start\_at         DATETIME NOT NULL,
    planned\_end\_at           DATETIME NOT NULL,
    status                   VARCHAR(30) NOT NULL DEFAULT 'PLANNED',
    is\_rework                TINYINT(1) NOT NULL DEFAULT 0,
    created\_at               DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    updated\_at               DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP ON UPDATE CURRENT\_TIMESTAMP,
    PRIMARY KEY (schedule\_id),
    UNIQUE KEY uk\_schedule\_no (schedule\_no),
    KEY ix\_schedule\_equipment\_date (equipment\_id, work\_date, sequence\_no),
    KEY ix\_schedule\_status (status),
    CONSTRAINT fk\_schedule\_equipment
        FOREIGN KEY (equipment\_id) REFERENCES equipment(equipment\_id),
    CONSTRAINT fk\_schedule\_order\_item
        FOREIGN KEY (order\_item\_id) REFERENCES sales\_order\_item(order\_item\_id),
    CONSTRAINT fk\_schedule\_lot
        FOREIGN KEY (lot\_id) REFERENCES lot(lot\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 37.2 schedule\_board\_layout

기존 `t\_schedulebox`의 화면 배치정보를 실제 스케줄과 분리한다.

```sql
CREATE TABLE schedule\_board\_layout (
    layout\_id       BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    equipment\_id    BIGINT UNSIGNED NULL,
    board\_code      VARCHAR(50) NOT NULL,
    display\_order   INT NOT NULL DEFAULT 0,
    x               INT NOT NULL DEFAULT 0,
    y               INT NOT NULL DEFAULT 0,
    width           INT NOT NULL DEFAULT 0,
    height          INT NOT NULL DEFAULT 0,
    position\_no     INT NOT NULL DEFAULT 0,
    tag             VARCHAR(50) NULL,
    title\_template  VARCHAR(100) NULL,
    is\_active       TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (layout\_id),
    CONSTRAINT fk\_schedule\_layout\_equipment
        FOREIGN KEY (equipment\_id) REFERENCES equipment(equipment\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 37.3 schedule\_board\_item

실제 화면에 특정 Schedule을 연결해야 하는 경우 사용한다.

```sql
CREATE TABLE schedule\_board\_item (
    schedule\_board\_item\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    layout\_id              BIGINT UNSIGNED NOT NULL,
    schedule\_id            BIGINT UNSIGNED NOT NULL,
    display\_title          VARCHAR(100) NULL,
    display\_qty            DECIMAL(14,3) NULL,
    tag                    VARCHAR(50) NULL,
    hold\_flag              TINYINT(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (schedule\_board\_item\_id),
    UNIQUE KEY uk\_schedule\_board\_item (layout\_id, schedule\_id),
    CONSTRAINT fk\_schedule\_board\_item\_layout
        FOREIGN KEY (layout\_id) REFERENCES schedule\_board\_layout(layout\_id),
    CONSTRAINT fk\_schedule\_board\_item\_schedule
        FOREIGN KEY (schedule\_id) REFERENCES production\_schedule(schedule\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

\---

# 38\. 실제 생산작업

## 38.1 production\_work

기존 `t\_work`의 핵심 테이블이다.

```sql
CREATE TABLE production\_work (
    production\_work\_id     BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    schedule\_id             BIGINT UNSIGNED NULL,
    lot\_id                  BIGINT UNSIGNED NOT NULL,
    part\_id                 BIGINT UNSIGNED NOT NULL,
    process\_version\_id      BIGINT UNSIGNED NULL,
    heat\_process\_version\_id BIGINT UNSIGNED NULL,
    unit\_process\_id         BIGINT UNSIGNED NULL,
    equipment\_id            BIGINT UNSIGNED NOT NULL,
    standard\_version\_id     BIGINT UNSIGNED NULL,

    work\_date               DATE NOT NULL,
    sequence\_no             INT NOT NULL DEFAULT 0,

    status                  VARCHAR(30) NOT NULL DEFAULT 'ALLOCATED',

    planned\_start\_at        DATETIME NULL,
    planned\_end\_at          DATETIME NULL,
    actual\_start\_at         DATETIME NULL,
    actual\_end\_at           DATETIME NULL,

    expected\_duration\_min   DECIMAL(10,2) NULL,
    actual\_duration\_min     DECIMAL(10,2) NULL,

    input\_qty               DECIMAL(14,3) NOT NULL DEFAULT 0,
    input\_weight            DECIMAL(14,4) NULL,
    input\_amount            DECIMAL(15,2) NULL,

    worker\_id               BIGINT UNSIGNED NULL,

    is\_return               TINYINT(1) NOT NULL DEFAULT 0,
    is\_rework               TINYINT(1) NOT NULL DEFAULT 0,
    is\_fixed                TINYINT(1) NOT NULL DEFAULT 0,
    is\_inspection\_completed TINYINT(1) NOT NULL DEFAULT 0,

    convert\_lot\_no          VARCHAR(100) NULL,
    marking                 VARCHAR(100) NULL,

    part\_name\_snapshot      VARCHAR(100) NULL,
    part\_number\_snapshot    VARCHAR(100) NULL,
    specification\_snapshot VARCHAR(100) NULL,
    model\_snapshot          VARCHAR(100) NULL,
    material\_snapshot       VARCHAR(100) NULL,
    customer\_name\_snapshot  VARCHAR(100) NULL,
    equipment\_name\_snapshot VARCHAR(100) NULL,
    process\_name\_snapshot   VARCHAR(100) NULL,

    defect\_qty              DECIMAL(14,3) NOT NULL DEFAULT 0,
    output\_qty              DECIMAL(14,3) NOT NULL DEFAULT 0,

    remarks                 TEXT NULL,

    is\_deleted              TINYINT(1) NOT NULL DEFAULT 0,
    deleted\_by              VARCHAR(100) NULL,
    deleted\_at              DATETIME NULL,

    created\_at              DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    updated\_at              DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP ON UPDATE CURRENT\_TIMESTAMP,

    PRIMARY KEY (production\_work\_id),
    KEY ix\_work\_schedule (schedule\_id),
    KEY ix\_work\_lot (lot\_id),
    KEY ix\_work\_equipment\_date (equipment\_id, work\_date),
    KEY ix\_work\_status (status),

    CONSTRAINT fk\_work\_schedule
        FOREIGN KEY (schedule\_id) REFERENCES production\_schedule(schedule\_id),
    CONSTRAINT fk\_work\_lot
        FOREIGN KEY (lot\_id) REFERENCES lot(lot\_id),
    CONSTRAINT fk\_work\_part
        FOREIGN KEY (part\_id) REFERENCES part(part\_id),
    CONSTRAINT fk\_work\_process\_version
        FOREIGN KEY (process\_version\_id) REFERENCES process\_version(process\_version\_id),
    CONSTRAINT fk\_work\_heat\_process\_version
        FOREIGN KEY (heat\_process\_version\_id)
        REFERENCES heat\_process\_version(heat\_process\_version\_id),
    CONSTRAINT fk\_work\_unit\_process
        FOREIGN KEY (unit\_process\_id) REFERENCES unit\_process(unit\_process\_id),
    CONSTRAINT fk\_work\_equipment
        FOREIGN KEY (equipment\_id) REFERENCES equipment(equipment\_id),
    CONSTRAINT fk\_work\_standard\_version
        FOREIGN KEY (standard\_version\_id) REFERENCES standard\_version(standard\_version\_id),
    CONSTRAINT fk\_work\_worker
        FOREIGN KEY (worker\_id) REFERENCES employee(employee\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 38.2 production\_work\_item

기존 `t\_worksub`의 상세/투입 역할을 분리한다.

```sql
CREATE TABLE production\_work\_item (
    production\_work\_item\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    production\_work\_id      BIGINT UNSIGNED NOT NULL,
    lot\_id                  BIGINT UNSIGNED NOT NULL,
    input\_qty               DECIMAL(14,3) NOT NULL DEFAULT 0,
    input\_weight            DECIMAL(14,4) NULL,
    input\_amount            DECIMAL(15,2) NULL,
    unit\_price              DECIMAL(15,2) NULL,
    process\_price            DECIMAL(15,2) NULL,
    is\_rework               TINYINT(1) NOT NULL DEFAULT 0,
    is\_return               TINYINT(1) NOT NULL DEFAULT 0,
    defect\_qty              DECIMAL(14,3) NOT NULL DEFAULT 0,
    output\_qty              DECIMAL(14,3) NOT NULL DEFAULT 0,
    origin\_lot\_id           BIGINT UNSIGNED NULL,
    PRIMARY KEY (production\_work\_item\_id),
    CONSTRAINT fk\_work\_item\_work
        FOREIGN KEY (production\_work\_id) REFERENCES production\_work(production\_work\_id),
    CONSTRAINT fk\_work\_item\_lot
        FOREIGN KEY (lot\_id) REFERENCES lot(lot\_id),
    CONSTRAINT fk\_work\_item\_origin\_lot
        FOREIGN KEY (origin\_lot\_id) REFERENCES lot(lot\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

\---

# 39\. 검사

## 39.1 inspection\_template

```sql
CREATE TABLE inspection\_template (
    inspection\_template\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    template\_code          VARCHAR(50) NOT NULL,
    template\_name          VARCHAR(100) NOT NULL,
    template\_type          VARCHAR(50) NULL,
    is\_active              TINYINT(1) NOT NULL DEFAULT 1,
    created\_at             DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    PRIMARY KEY (inspection\_template\_id),
    UNIQUE KEY uk\_inspection\_template\_code (template\_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 39.2 inspection\_template\_version

```sql
CREATE TABLE inspection\_template\_version (
    inspection\_template\_version\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    inspection\_template\_id         BIGINT UNSIGNED NOT NULL,
    version\_no                     INT NOT NULL,
    effective\_from                 DATETIME NOT NULL,
    effective\_to                   DATETIME NULL,
    is\_current                     TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (inspection\_template\_version\_id),
    UNIQUE KEY uk\_inspection\_template\_version
        (inspection\_template\_id, version\_no),
    CONSTRAINT fk\_inspection\_template\_version\_template
        FOREIGN KEY (inspection\_template\_id)
        REFERENCES inspection\_template(inspection\_template\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 39.3 inspection\_criteria

```sql
CREATE TABLE inspection\_criteria (
    inspection\_criteria\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    inspection\_template\_version\_id BIGINT UNSIGNED NOT NULL,
    sequence\_no             INT NOT NULL,
    item\_code               VARCHAR(50) NULL,
    item\_name               VARCHAR(100) NOT NULL,
    criteria\_type           VARCHAR(50) NULL,
    specification\_value     VARCHAR(255) NULL,
    lower\_limit             DECIMAL(18,6) NULL,
    upper\_limit             DECIMAL(18,6) NULL,
    unit\_code               VARCHAR(20) NULL,
    sample\_count             INT NOT NULL DEFAULT 1,
    is\_active               TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (inspection\_criteria\_id),
    UNIQUE KEY uk\_inspection\_criteria
        (inspection\_template\_version\_id, sequence\_no),
    CONSTRAINT fk\_inspection\_criteria\_template\_version
        FOREIGN KEY (inspection\_template\_version\_id)
        REFERENCES inspection\_template\_version(inspection\_template\_version\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 39.4 inspection

```sql
CREATE TABLE inspection (
    inspection\_id            BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    inspection\_no             VARCHAR(50) NOT NULL,
    inspection\_sub\_no         INT NOT NULL DEFAULT 0,
    inspection\_date           DATE NOT NULL,
    production\_work\_id        BIGINT UNSIGNED NULL,
    lot\_id                    BIGINT UNSIGNED NOT NULL,
    order\_item\_id             BIGINT UNSIGNED NULL,
    customer\_id               BIGINT UNSIGNED NOT NULL,
    part\_id                   BIGINT UNSIGNED NOT NULL,
    inspection\_template\_version\_id BIGINT UNSIGNED NULL,

    customer\_lot              VARCHAR(100) NULL,
    converted\_lot\_no          VARCHAR(100) NULL,
    inspection\_qty            DECIMAL(14,3) NULL,
    inspection\_weight         DECIMAL(14,4) NULL,

    decision                  VARCHAR(30) NULL,
    status                    VARCHAR(30) NOT NULL DEFAULT 'OPEN',

    part\_name\_snapshot        VARCHAR(100) NULL,
    specification\_snapshot   VARCHAR(100) NULL,
    model\_snapshot            VARCHAR(100) NULL,
    customer\_name\_snapshot   VARCHAR(100) NULL,

    completed\_at              DATETIME NULL,
    created\_at                DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    updated\_at                DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP ON UPDATE CURRENT\_TIMESTAMP,

    PRIMARY KEY (inspection\_id),
    UNIQUE KEY uk\_inspection\_no (inspection\_no),
    KEY ix\_inspection\_work (production\_work\_id),
    KEY ix\_inspection\_lot (lot\_id),

    CONSTRAINT fk\_inspection\_work
        FOREIGN KEY (production\_work\_id) REFERENCES production\_work(production\_work\_id),
    CONSTRAINT fk\_inspection\_lot
        FOREIGN KEY (lot\_id) REFERENCES lot(lot\_id),
    CONSTRAINT fk\_inspection\_order\_item
        FOREIGN KEY (order\_item\_id) REFERENCES sales\_order\_item(order\_item\_id),
    CONSTRAINT fk\_inspection\_customer
        FOREIGN KEY (customer\_id) REFERENCES customer(customer\_id),
    CONSTRAINT fk\_inspection\_part
        FOREIGN KEY (part\_id) REFERENCES part(part\_id),
    CONSTRAINT fk\_inspection\_template\_version
        FOREIGN KEY (inspection\_template\_version\_id)
        REFERENCES inspection\_template\_version(inspection\_template\_version\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 39.5 inspection\_item

```sql
CREATE TABLE inspection\_item (
    inspection\_item\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    inspection\_id      BIGINT UNSIGNED NOT NULL,
    inspection\_criteria\_id BIGINT UNSIGNED NULL,
    sequence\_no        INT NOT NULL,
    item\_name          VARCHAR(100) NOT NULL,
    result              VARCHAR(30) NULL,
    remark              VARCHAR(255) NULL,
    PRIMARY KEY (inspection\_item\_id),
    UNIQUE KEY uk\_inspection\_item (inspection\_id, sequence\_no),
    CONSTRAINT fk\_inspection\_item\_inspection
        FOREIGN KEY (inspection\_id) REFERENCES inspection(inspection\_id),
    CONSTRAINT fk\_inspection\_item\_criteria
        FOREIGN KEY (inspection\_criteria\_id)
        REFERENCES inspection\_criteria(inspection\_criteria\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 39.6 inspection\_measurement

기존 `v1\~v10`, `p1\~p10` 같은 반복 측정 컬럼을 제거한다.

```sql
CREATE TABLE inspection\_measurement (
    inspection\_measurement\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    inspection\_item\_id        BIGINT UNSIGNED NOT NULL,
    sample\_no                 INT NOT NULL,
    measured\_value            DECIMAL(18,6) NULL,
    measured\_text             VARCHAR(255) NULL,
    unit\_code                 VARCHAR(20) NULL,
    result                    VARCHAR(30) NULL,
    measured\_at               DATETIME NULL,
    instrument\_id             BIGINT UNSIGNED NULL,
    remark                    VARCHAR(255) NULL,
    PRIMARY KEY (inspection\_measurement\_id),
    UNIQUE KEY uk\_inspection\_measurement
        (inspection\_item\_id, sample\_no),
    CONSTRAINT fk\_inspection\_measurement\_item
        FOREIGN KEY (inspection\_item\_id) REFERENCES inspection\_item(inspection\_item\_id),
    CONSTRAINT fk\_inspection\_measurement\_instrument
        FOREIGN KEY (instrument\_id) REFERENCES instrument(instrument\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 39.7 unit\_inspection\_item

```sql
CREATE TABLE unit\_inspection\_item (
    unit\_inspection\_item\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    unit\_process\_id         BIGINT UNSIGNED NULL,
    item\_code               VARCHAR(50) NOT NULL,
    item\_name               VARCHAR(100) NOT NULL,
    sort\_order              INT NOT NULL DEFAULT 0,
    is\_active               TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (unit\_inspection\_item\_id),
    CONSTRAINT fk\_unit\_inspection\_item\_process
        FOREIGN KEY (unit\_process\_id) REFERENCES unit\_process(unit\_process\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 39.8 line\_inspection

```sql
CREATE TABLE line\_inspection (
    line\_inspection\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    production\_work\_id BIGINT UNSIGNED NOT NULL,
    inspection\_date    DATE NOT NULL,
    decision           VARCHAR(30) NULL,
    status             VARCHAR(30) NOT NULL DEFAULT 'OPEN',
    created\_at         DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    PRIMARY KEY (line\_inspection\_id),
    CONSTRAINT fk\_line\_inspection\_work
        FOREIGN KEY (production\_work\_id) REFERENCES production\_work(production\_work\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;

CREATE TABLE line\_inspection\_measurement (
    line\_inspection\_measurement\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    line\_inspection\_id             BIGINT UNSIGNED NOT NULL,
    unit\_inspection\_item\_id        BIGINT UNSIGNED NULL,
    sample\_no                      INT NOT NULL DEFAULT 1,
    measured\_value                 DECIMAL(18,6) NULL,
    measured\_text                  VARCHAR(255) NULL,
    result                         VARCHAR(30) NULL,
    PRIMARY KEY (line\_inspection\_measurement\_id),
    CONSTRAINT fk\_line\_inspection\_measurement\_header
        FOREIGN KEY (line\_inspection\_id)
        REFERENCES line\_inspection(line\_inspection\_id),
    CONSTRAINT fk\_line\_inspection\_measurement\_item
        FOREIGN KEY (unit\_inspection\_item\_id)
        REFERENCES unit\_inspection\_item(unit\_inspection\_item\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

\---

# 40\. 불량 / 재작업

## 40.1 defect

불량 사유 Master와 발생 기록을 분리한다.

```sql
CREATE TABLE defect (
    defect\_id       BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    defect\_code     VARCHAR(50) NOT NULL,
    defect\_name     VARCHAR(100) NOT NULL,
    defect\_type     VARCHAR(50) NULL,
    is\_active       TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (defect\_id),
    UNIQUE KEY uk\_defect\_code (defect\_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 40.2 defect\_occurrence

```sql
CREATE TABLE defect\_occurrence (
    defect\_occurrence\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    defect\_id            BIGINT UNSIGNED NULL,
    inspection\_id        BIGINT UNSIGNED NULL,
    production\_work\_id   BIGINT UNSIGNED NULL,
    lot\_id               BIGINT UNSIGNED NOT NULL,
    defect\_date          DATE NOT NULL,

    defect\_qty           DECIMAL(14,3) NOT NULL DEFAULT 0,
    input\_qty            DECIMAL(14,3) NULL,
    input\_weight         DECIMAL(14,4) NULL,
    input\_amount         DECIMAL(15,2) NULL,

    is\_rework             TINYINT(1) NOT NULL DEFAULT 0,
    is\_return             TINYINT(1) NOT NULL DEFAULT 0,

    decision              VARCHAR(50) NULL,
    decision\_memo         TEXT NULL,
    rework\_step           VARCHAR(100) NULL,
    rework\_memo           TEXT NULL,

    determined\_by         BIGINT UNSIGNED NULL,
    determined\_at         DATETIME NULL,
    completed\_by          BIGINT UNSIGNED NULL,
    completed\_at          DATETIME NULL,

    status                VARCHAR(30) NOT NULL DEFAULT 'OPEN',

    PRIMARY KEY (defect\_occurrence\_id),
    KEY ix\_defect\_occurrence\_lot (lot\_id),
    CONSTRAINT fk\_defect\_occurrence\_defect
        FOREIGN KEY (defect\_id) REFERENCES defect(defect\_id),
    CONSTRAINT fk\_defect\_occurrence\_inspection
        FOREIGN KEY (inspection\_id) REFERENCES inspection(inspection\_id),
    CONSTRAINT fk\_defect\_occurrence\_work
        FOREIGN KEY (production\_work\_id) REFERENCES production\_work(production\_work\_id),
    CONSTRAINT fk\_defect\_occurrence\_lot
        FOREIGN KEY (lot\_id) REFERENCES lot(lot\_id),
    CONSTRAINT fk\_defect\_occurrence\_determined\_by
        FOREIGN KEY (determined\_by) REFERENCES employee(employee\_id),
    CONSTRAINT fk\_defect\_occurrence\_completed\_by
        FOREIGN KEY (completed\_by) REFERENCES employee(employee\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

\---

# 41\. 생산 결과 / 출하 / 마감

## 41.1 production\_output

```sql
CREATE TABLE production\_output (
    production\_output\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    production\_work\_id   BIGINT UNSIGNED NOT NULL,
    lot\_id               BIGINT UNSIGNED NOT NULL,
    output\_date          DATE NOT NULL,
    output\_qty           DECIMAL(14,3) NOT NULL DEFAULT 0,
    output\_weight        DECIMAL(14,4) NULL,
    status               VARCHAR(30) NOT NULL DEFAULT 'COMPLETED',
    created\_at           DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    PRIMARY KEY (production\_output\_id),
    CONSTRAINT fk\_output\_work
        FOREIGN KEY (production\_work\_id) REFERENCES production\_work(production\_work\_id),
    CONSTRAINT fk\_output\_lot
        FOREIGN KEY (lot\_id) REFERENCES lot(lot\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 41.2 shipment

```sql
CREATE TABLE shipment (
    shipment\_id       BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    shipment\_no       VARCHAR(50) NOT NULL,
    shipment\_date     DATE NOT NULL,
    customer\_id       BIGINT UNSIGNED NOT NULL,
    status            VARCHAR(30) NOT NULL DEFAULT 'OPEN',
    remark            VARCHAR(255) NULL,
    created\_at        DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    updated\_at        DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP ON UPDATE CURRENT\_TIMESTAMP,
    PRIMARY KEY (shipment\_id),
    UNIQUE KEY uk\_shipment\_no (shipment\_no),
    CONSTRAINT fk\_shipment\_customer
        FOREIGN KEY (customer\_id) REFERENCES customer(customer\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 41.3 shipment\_item

```sql
CREATE TABLE shipment\_item (
    shipment\_item\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    shipment\_id      BIGINT UNSIGNED NOT NULL,
    order\_item\_id    BIGINT UNSIGNED NULL,
    lot\_id           BIGINT UNSIGNED NOT NULL,
    part\_id          BIGINT UNSIGNED NOT NULL,
    shipment\_qty     DECIMAL(14,3) NOT NULL DEFAULT 0,
    shipment\_weight  DECIMAL(14,4) NULL,
    part\_name\_snapshot VARCHAR(100) NULL,
    specification\_snapshot VARCHAR(100) NULL,
    material\_snapshot VARCHAR(100) NULL,
    PRIMARY KEY (shipment\_item\_id),
    CONSTRAINT fk\_shipment\_item\_shipment
        FOREIGN KEY (shipment\_id) REFERENCES shipment(shipment\_id),
    CONSTRAINT fk\_shipment\_item\_order
        FOREIGN KEY (order\_item\_id) REFERENCES sales\_order\_item(order\_item\_id),
    CONSTRAINT fk\_shipment\_item\_lot
        FOREIGN KEY (lot\_id) REFERENCES lot(lot\_id),
    CONSTRAINT fk\_shipment\_item\_part
        FOREIGN KEY (part\_id) REFERENCES part(part\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 41.4 shipment\_closing

기존 `t\_outcomesum`의 월마감/Snapshot 성격을 보존한다.

```sql
CREATE TABLE shipment\_closing (
    shipment\_closing\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    closing\_year        INT NOT NULL,
    closing\_month       INT NOT NULL,
    customer\_id         BIGINT UNSIGNED NULL,
    total\_qty           DECIMAL(14,3) NOT NULL DEFAULT 0,
    total\_weight        DECIMAL(14,4) NOT NULL DEFAULT 0,
    total\_amount        DECIMAL(15,2) NOT NULL DEFAULT 0,
    closing\_status      VARCHAR(30) NOT NULL DEFAULT 'OPEN',
    closed\_at           DATETIME NULL,
    closed\_by           BIGINT UNSIGNED NULL,
    created\_at          DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    PRIMARY KEY (shipment\_closing\_id),
    UNIQUE KEY uk\_shipment\_closing
        (closing\_year, closing\_month, customer\_id),
    CONSTRAINT fk\_shipment\_closing\_customer
        FOREIGN KEY (customer\_id) REFERENCES customer(customer\_id),
    CONSTRAINT fk\_shipment\_closing\_employee
        FOREIGN KEY (closed\_by) REFERENCES employee(employee\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

\---

# 42\. 설비 비가동 / 보전

## 42.1 equipment\_downtime

```sql
CREATE TABLE equipment\_downtime (
    equipment\_downtime\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    equipment\_id          BIGINT UNSIGNED NOT NULL,
    downtime\_date         DATE NOT NULL,
    started\_at            DATETIME NULL,
    ended\_at              DATETIME NULL,
    duration\_min          DECIMAL(10,2) NULL,
    reason\_code           VARCHAR(50) NULL,
    worker\_id             BIGINT UNSIGNED NULL,
    memo                  VARCHAR(500) NULL,
    PRIMARY KEY (equipment\_downtime\_id),
    KEY ix\_downtime\_equipment\_date (equipment\_id, downtime\_date),
    CONSTRAINT fk\_downtime\_equipment
        FOREIGN KEY (equipment\_id) REFERENCES equipment(equipment\_id),
    CONSTRAINT fk\_downtime\_worker
        FOREIGN KEY (worker\_id) REFERENCES employee(employee\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 42.2 maintenance

```sql
CREATE TABLE maintenance (
    maintenance\_id       BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    equipment\_id         BIGINT UNSIGNED NOT NULL,
    maintenance\_type     VARCHAR(50) NOT NULL,
    maintenance\_date     DATE NOT NULL,
    started\_at           DATETIME NULL,
    completed\_at         DATETIME NULL,
    worker\_id            BIGINT UNSIGNED NULL,
    description          TEXT NULL,
    result                TEXT NULL,
    status                VARCHAR(30) NOT NULL DEFAULT 'OPEN',
    PRIMARY KEY (maintenance\_id),
    CONSTRAINT fk\_maintenance\_equipment
        FOREIGN KEY (equipment\_id) REFERENCES equipment(equipment\_id),
    CONSTRAINT fk\_maintenance\_worker
        FOREIGN KEY (worker\_id) REFERENCES employee(employee\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

\---

# 43\. 템플릿 / 출력

## 43.1 template\_field

```sql
CREATE TABLE template\_field (
    template\_field\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    group\_code        VARCHAR(50) NULL,
    item\_name         VARCHAR(100) NULL,
    initial\_code      VARCHAR(20) NULL,
    division          VARCHAR(50) NULL,
    field\_name        VARCHAR(100) NOT NULL,
    mapping\_data      VARCHAR(255) NOT NULL,
    source\_name       VARCHAR(100) NULL,
    field\_type        VARCHAR(50) NOT NULL,
    sort\_order        INT NOT NULL DEFAULT 0,
    remark             VARCHAR(255) NULL,
    PRIMARY KEY (template\_field\_id),
    UNIQUE KEY uk\_template\_field (field\_name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 43.2 part\_template

```sql
CREATE TABLE part\_template (
    part\_template\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    part\_id          BIGINT UNSIGNED NOT NULL,
    template\_id      BIGINT UNSIGNED NOT NULL,
    is\_active        TINYINT(1) NOT NULL DEFAULT 1,
    created\_at       DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    PRIMARY KEY (part\_template\_id),
    UNIQUE KEY uk\_part\_template (part\_id, template\_id),
    CONSTRAINT fk\_part\_template\_part
        FOREIGN KEY (part\_id) REFERENCES part(part\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 43.3 print\_template

```sql
CREATE TABLE print\_template (
    print\_template\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    template\_name     VARCHAR(100) NOT NULL,
    template\_type     VARCHAR(50) NOT NULL,
    base\_image\_path   VARCHAR(500) NULL,
    base\_pdf\_path     VARCHAR(500) NULL,
    layout\_json       LONGTEXT NULL,
    is\_active         TINYINT(1) NOT NULL DEFAULT 1,
    created\_at        DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    updated\_at        DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP ON UPDATE CURRENT\_TIMESTAMP,
    PRIMARY KEY (print\_template\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

## 43.4 print\_sheet

```sql
CREATE TABLE print\_sheet (
    print\_sheet\_id    BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    print\_template\_id BIGINT UNSIGNED NOT NULL,
    sheet\_code        VARCHAR(50) NOT NULL,
    sheet\_name        VARCHAR(100) NOT NULL,
    customer\_id       BIGINT UNSIGNED NULL,
    part\_id            BIGINT UNSIGNED NULL,
    heat\_process\_id    BIGINT UNSIGNED NULL,
    tag                VARCHAR(50) NULL,
    memo               VARCHAR(255) NULL,
    is\_active          TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (print\_sheet\_id),
    CONSTRAINT fk\_print\_sheet\_template
        FOREIGN KEY (print\_template\_id) REFERENCES print\_template(print\_template\_id),
    CONSTRAINT fk\_print\_sheet\_customer
        FOREIGN KEY (customer\_id) REFERENCES customer(customer\_id),
    CONSTRAINT fk\_print\_sheet\_part
        FOREIGN KEY (part\_id) REFERENCES part(part\_id),
    CONSTRAINT fk\_print\_sheet\_heat\_process
        FOREIGN KEY (heat\_process\_id) REFERENCES heat\_process(heat\_process\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

\---

# 44\. 표준/템플릿 추가 구조

기존 `t\_standardtemplate`는 반복 컬럼을 제거하여 다음과 같이 관리한다.

```sql
CREATE TABLE standard\_template (
    standard\_template\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    standard\_version\_id  BIGINT UNSIGNED NOT NULL,
    template\_type        VARCHAR(50) NULL,
    template\_name        VARCHAR(100) NULL,
    PRIMARY KEY (standard\_template\_id),
    CONSTRAINT fk\_standard\_template\_version
        FOREIGN KEY (standard\_version\_id)
        REFERENCES standard\_version(standard\_version\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;

CREATE TABLE standard\_template\_item (
    standard\_template\_item\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    standard\_template\_id      BIGINT UNSIGNED NOT NULL,
    sequence\_no               INT NOT NULL,
    item\_name                 VARCHAR(100) NOT NULL,
    field\_type                VARCHAR(50) NULL,
    PRIMARY KEY (standard\_template\_item\_id),
    UNIQUE KEY uk\_standard\_template\_item
        (standard\_template\_id, sequence\_no),
    CONSTRAINT fk\_standard\_template\_item\_template
        FOREIGN KEY (standard\_template\_id)
        REFERENCES standard\_template(standard\_template\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

\---

# 45\. Master 변경 이력

모든 Master에 무조건 History 테이블을 만드는 것은 아니고, 변경 이력이 중요한 Master만 적용한다.

대표적으로 품목/설비/공정/검사기준은 다음 패턴을 사용한다.

## 45.1 part\_history

```sql
CREATE TABLE part\_history (
    part\_history\_id BIGINT UNSIGNED NOT NULL AUTO\_INCREMENT,
    part\_id         BIGINT UNSIGNED NOT NULL,
    changed\_at      DATETIME NOT NULL DEFAULT CURRENT\_TIMESTAMP,
    changed\_by      BIGINT UNSIGNED NULL,
    change\_type     VARCHAR(30) NOT NULL,
    old\_data\_json   JSON NULL,
    new\_data\_json   JSON NULL,
    PRIMARY KEY (part\_history\_id),
    KEY ix\_part\_history\_part (part\_id),
    CONSTRAINT fk\_part\_history\_part
        FOREIGN KEY (part\_id) REFERENCES part(part\_id),
    CONSTRAINT fk\_part\_history\_employee
        FOREIGN KEY (changed\_by) REFERENCES employee(employee\_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4\_uca1400\_ai\_ci;
```

\---

# 46\. 생산 상태값 표준

현재 `t\_work.progressstep`, `isdone`, `isinspectiondone` 등을 통합할 때 사용할 기본 상태 목록이다.

```text
PLANNED
ALLOCATED
READY
RUNNING
PAUSED
COMPLETED
INSPECTION\_WAIT
INSPECTION\_COMPLETED
CANCELLED
```

단, **기존 프로그램에서 실제 사용 중인 상태값을 확인한 후 최종 확정**한다.

상태를 여러 Boolean의 조합으로 판단하지 않고:

```text
production\_work.status
```

를 주 상태로 사용한다.

\---

# 47\. 스케줄 상태값 표준

`production\_schedule.status`:

```text
PLANNED
CONFIRMED
ALLOCATED
RUNNING
COMPLETED
CANCELLED
```

스케줄의 계획상태와 실제 생산상태는 별도로 관리한다.

```text
production\_schedule.status
        ≠
production\_work.status
```

\---

# 48\. Snapshot 필드 적용 위치

최소한 다음은 생산/검사/출하 기록에 당시 값을 보존한다.

```text
production\_work
    customer\_name\_snapshot
    part\_name\_snapshot
    part\_number\_snapshot
    specification\_snapshot
    model\_snapshot
    material\_snapshot
    equipment\_name\_snapshot
    process\_name\_snapshot

sales\_order\_item
    part\_name\_snapshot
    part\_number\_snapshot
    specification\_snapshot
    model\_snapshot
    material\_snapshot
    heat\_process\_name\_snapshot

inspection
    customer\_name\_snapshot
    part\_name\_snapshot
    specification\_snapshot
    model\_snapshot

shipment\_item
    part\_name\_snapshot
    specification\_snapshot
    material\_snapshot
```

공정/열처리/검사기준은 Snapshot만으로 관리하지 않고 **Version ID를 함께 저장**한다.

\---

# 49\. 인덱스 설계 원칙

다음 조건은 반드시 Index를 검토한다.

```text
FK
UNIQUE 업무번호
날짜 + 설비
날짜 + 상태
LOT
고객 + 품목
생산작업 + 검사
```

대표적인 Index:

```sql
CREATE INDEX ix\_schedule\_equipment\_work\_date
ON production\_schedule (equipment\_id, work\_date, sequence\_no);

CREATE INDEX ix\_work\_equipment\_work\_date
ON production\_work (equipment\_id, work\_date);

CREATE INDEX ix\_work\_status
ON production\_work (status);

CREATE INDEX ix\_inspection\_work
ON inspection (production\_work\_id);

CREATE INDEX ix\_lot\_part
ON lot (part\_id);
```

대량 생산이력 조회가 많아지면 `work\_date`, `inspection\_date`, `shipment\_date` 기준의 복합 Index를 추가 검토한다.

\---

# 50\. 스케줄 재계산 Procedure

기존:

```text
sp\_RecalculateWorkPlanSequence
sp\_RecalculateWorkPlanTimes
```

은 신규 구조에서 다음으로 변경한다.

```sql
DELIMITER //

CREATE PROCEDURE sp\_recalculate\_schedule\_sequence(
    IN p\_equipment\_id BIGINT,
    IN p\_work\_date DATE
)
BEGIN
    -- 실제 구현은 기존 프로그램의 sequence 변경 규칙을 검증한 후 작성한다.
    -- 핵심 대상: production\_schedule.sequence\_no
END//

CREATE PROCEDURE sp\_recalculate\_schedule\_times(
    IN p\_equipment\_id BIGINT,
    IN p\_work\_date DATE,
    IN p\_from\_sequence INT
)
BEGIN
    -- 기존 workplan의 runningtime/starttime/endtime 재계산 로직을
    -- production\_schedule에 맞게 이관한다.
    --
    -- 기존 규칙:
    -- 1. 설비별 sequence 순서
    -- 2. 이전 작업 종료시간 → 다음 작업 시작시간
    -- 3. running time을 이용하여 종료시간 계산
    -- 4. 날짜가 넘어가는 경우 다음날 08:00 기준
    --
    -- 실제 SQL은 운영 데이터 검증 후 확정한다.
END//

DELIMITER ;
```

기존 Procedure의 핵심 업무규칙을 임의로 변경하지 않고, **기존 프로그램과 결과가 동일한지 검증한 후 이관**한다.

\---

# 51\. FK 생성 순서 주의

테이블 간 순환 참조 또는 생성 순서 문제를 피하기 위해 다음 순서로 생성한다.

```text
1. company
2. common\_code
3. common\_code\_item
4. department
5. position
6. employee
7. equipment\_type
8. equipment
9. instrument
10. customer
11. part
12. unit\_process
13. process
14. process\_version
15. process\_step
16. heat\_process
17. heat\_process\_version
18. heat\_process\_step
19. standard
20. standard\_version
21. standard\_step
22. sales\_order
23. sales\_order\_item
24. lot
25. work\_shift
26. work\_pattern
27. worker\_assignment
28. production\_schedule
29. production\_work
30. production\_work\_item
31. production\_input
32. work\_condition
33. work\_condition\_step
34. inspection\_template
35. inspection\_template\_version
36. inspection\_criteria
37. inspection
38. inspection\_item
39. inspection\_measurement
40. line\_inspection
41. line\_inspection\_measurement
42. defect
43. defect\_occurrence
44. production\_output
45. shipment
46. shipment\_item
47. shipment\_closing
48. equipment\_downtime
49. maintenance
50. menu
51. department\_menu
52. template\_field
53. print\_template
54. print\_sheet
55. part\_template
56. schedule\_board\_layout
57. schedule\_board\_item
58. History tables
```

\---

# 52\. 전체 생성 종료

```sql
SET FOREIGN\_KEY\_CHECKS = 1;
```

\---

# 53\. 기존 46개 → 신규 DDL 매핑

|기존|신규 CREATE TABLE|
|-|-|
|`t\_combolist`|`common\_code`, `common\_code\_item`|
|`t\_company`|`company`|
|`t\_condition\_con`|`condition` 계열 검토|
|`t\_conditiontemplate`|`condition\_template`, `condition\_template\_item`|
|`t\_customer`|`customer`|
|`t\_defect`|`defect`, `defect\_occurrence`|
|`t\_department`|`department`|
|`t\_dept`|`department`|
|`t\_dept\_menu`|`department\_menu`|
|`t\_downtime`|`equipment\_downtime`|
|`t\_employee`|`employee`|
|`t\_equipment`|`equipment\_type`, `equipment`|
|`t\_heatprocess`|`heat\_process`, `heat\_process\_version`, `heat\_process\_step`|
|`t\_income`|`sales\_order`, `sales\_order\_item`|
|`t\_inputsub`|`production\_input`|
|`t\_inputwaiting`|`production\_input\_queue`|
|`t\_inspection`|`inspection`|
|`t\_inspectioncriteria`|`inspection\_criteria`|
|`t\_inspectiondetail`|`inspection\_measurement`|
|`t\_inspectionsub`|`inspection\_item`|
|`t\_inspectiontemplate`|`inspection\_template`, `inspection\_template\_version`|
|`t\_instruments`|`instrument`|
|`t\_lineinspection`|`line\_inspection`, `line\_inspection\_measurement`|
|`t\_maintenance`|`maintenance`|
|`t\_menu\_catalog`|`menu`|
|`t\_outcome`|`production\_output`, `shipment`, `shipment\_item`|
|`t\_outcomesum`|`shipment\_closing`|
|`t\_part`|`part`, `part\_customer`|
|`t\_part\_template`|`part\_template`|
|`t\_printsheet`|`print\_sheet`|
|`t\_printtemplate`|`print\_template`|
|`t\_process\_default\_time`|`process\_default\_time`|
|`t\_schedulebox`|`schedule\_board\_layout`, `schedule\_board\_item`|
|`t\_standard`|`standard`, `standard\_version`|
|`t\_standard\_gas`|`standard\_gas`|
|`t\_standarddetail`|`standard\_step`|
|`t\_standardtemplate`|`standard\_template`, `standard\_template\_item`|
|`t\_system\_settings`|`system\_setting`|
|`t\_templatefieldname`|`template\_field`|
|`t\_unitinspectionitem`|`unit\_inspection\_item`|
|`t\_unitprocess`|`unit\_process`|
|`t\_work`|`production\_work`|
|`t\_workconditiondetail`|`work\_condition`, `work\_condition\_step`|
|`t\_workerassignment`|`worker\_assignment`|
|`t\_worksub`|`production\_work\_item` / `production\_input`|
|`workplan`|`production\_schedule`|

\---

# 54\. 현재 설계에서 아직 확정하지 않은 항목

다음은 원본 SQL만으로 업무 의미를 100% 확정하기 어려워 **일부러 빈틈을 남겨둔 부분**이다.

### `t\_condition\_con`

현재 구조상 `cc\_ccid` 단일 필드만 확인되므로 실제 연결 대상과 업무 의미를 확정하지 않았다.

### `t\_inspectionsub`, `t\_inspectiondetail`, `t\_lineinspection`

현재 컬럼의 전체 업무 의미와 화면 사용관계를 확인한 후 `inspection\_item`/`inspection\_measurement` 구조에 정확히 매핑해야 한다.

### `t\_outcome`

생산완료와 출하가 현재 하나의 업무에 섞여 있을 가능성이 있으므로 `production\_output`과 `shipment`로 분리하되 실제 프로그램 사용처 확인이 필요하다.

### `t\_part\_template`

기존 `template\_id`의 참조 대상은 현재 구조만으로 완전히 확정하지 않고 별도 검증한다.

### `t\_schedulebox`

`x/y/width/height/position/tag`가 화면 레이아웃 데이터인지 실제 업무 데이터인지 프로그램 소스 확인이 필요하다.

\---

# 55\. 운영 DB 적용 전 검증 순서

DDL을 바로 운영 DB에 실행하지 않는다.

```text
① bbakggum 원본 백업
        ↓
② bbakggum\_v2 생성
        ↓
③ 신규 DDL 실행
        ↓
④ 기존 46개 테이블 데이터 건수 비교
        ↓
⑤ Master Migration
        ↓
⑥ Order / LOT Migration
        ↓
⑦ Schedule Migration
        ↓
⑧ Production Work Migration
        ↓
⑨ Inspection Migration
        ↓
⑩ Shipment / Closing Migration
        ↓
⑪ 기존 프로그램 조회 결과와 비교
        ↓
⑫ 스케줄 계산 결과 비교
        ↓
⑬ 검사성적서 출력 결과 비교
        ↓
⑭ 신규 프로그램 연결
```

특히 **스케줄 계산은 기존 `workplan`과 신규 `production\_schedule`의 결과를 같은 날짜/설비에 대해 1:1 비교한 뒤 전환**한다.

\---

# 56\. 최종 판단

현재 DB를 단순히 테이블 이름만 변경하는 것은 적절하지 않다.

핵심 구조는 다음과 같이 변경하는 것이 목표다.

```text
\[기준정보]
customer
part
equipment
employee
process
standard
        │
        ▼
\[수주]
sales\_order
sales\_order\_item
        │
        ▼
\[LOT]
lot
        │
        ├───────────────┐
        ▼               ▼
\[생산계획]          \[작업자배정]
production\_schedule worker\_assignment
        │
        ▼
\[실제생산]
production\_work
        │
        ├── production\_input
        ├── work\_condition
        ├── inspection
        │      └── measurement
        ├── defect
        └── production\_output
                         │
                         ▼
                     shipment
                         │
                         ▼
                  shipment\_closing
```

그리고 모든 핵심 거래 데이터는 **현재 Master를 직접 복사하여 의존하지 않고**, 필요한 경우 `\*\_snapshot` 및 `\*\_version\_id`를 통해 **당시의 기준정보와 조건을 재현**할 수 있도록 한다.









# bbakggum DB 구조개편안 — React + Calendar 추가 설계사항

> 기존 `bbakggum\\\_DB\\\_정리설계안\\\_V2.md`에 \\\*\\\*추가할 내용만\\\*\\\* 정리한 문서이다.
> 기존 DB 설계 및 DDL은 변경하지 않고, 웹/React/Calendar 전환 설계에 필요한 내용을 별도 추가한다.

\---

# 57\. React + Calendar 기반 웹 프론트엔드 적용 설계

## 57.1 전체 아키텍처

현재 V2 DB 구조는 React 기반 웹 생산관리 화면과 Calendar/Gantt 기반 일정관리 화면으로 확장할 수 있다.

```text
React + TypeScript
        │
        │ REST API / SignalR
        ▼
ASP.NET Core Web API
        │
        │ Dapper / SQL
        ▼
MariaDB V2
```

권장 기술 구성:

|영역|권장 기술|
|-|-|
|Frontend|React + TypeScript|
|UI|MUI 또는 Ant Design|
|Calendar/Gantt|FullCalendar 계열 또는 생산계획용 Gantt/Timeline|
|상태관리|Zustand|
|Backend|ASP.NET Core Web API|
|DB 접근|Dapper|
|DB|MariaDB|
|인증|JWT 또는 Cookie|
|실시간 상태|SignalR|
|차트|ECharts 또는 Recharts|

**React에서 MariaDB에 직접 접속하지 않고 ASP.NET Core API를 통해 접근한다.**

\---

# 58\. React Calendar와 DB 관계

React Calendar는 `production\\\_schedule`을 API를 통해 조회한다.

예시:

```json
{
  "scheduleId": 10235,
  "scheduleNo": "PS-20260919-001",
  "title": "A-123 열처리",
  "equipmentId": 12,
  "lotId": 50123,
  "plannedQty": 850,
  "start": "2026-09-19T09:00:00",
  "end": "2026-09-19T11:30:00",
  "status": "PLANNED"
}
```

Calendar 표시 데이터와 실제 생산 진행 데이터는 분리한다.

\---

# 59\. 생산계획 Calendar의 핵심 DB

`production\\\_schedule`은 **계획**, `production\\\_work`는 **실제 작업**을 관리한다.

```text
production\\\_schedule
 ├─ planned\\\_start\\\_at
 ├─ planned\\\_end\\\_at
 └─ status

production\\\_work
 ├─ actual\\\_start\\\_at
 ├─ actual\\\_end\\\_at
 └─ status
```

이를 통해 계획 대비 실제 생산시간, 지연시간 및 설비 가동률을 분석할 수 있다.

\---

# 60\. 설비별 Calendar / Gantt

설비를 Resource, 생산계획을 Event로 사용한다.

```text
설비 A ─ 작업 001 ─ 작업 002 ─ 작업 003
설비 B ─ 작업 004 ─ 작업 005
설비 C ─ 작업 006
```

생산계획에는 일반 월간 Calendar보다 **설비별 Timeline/Gantt** 형태가 적합하다.

\---

# 61\. `t\\\_schedulebox`와 React Calendar의 관계

기존 `t\\\_schedulebox`의 다음 항목은 화면 배치 정보 성격이 강하다.

```text
x
y
width
height
position
tag
```

React Calendar/Gantt에서는 날짜/시간과 Resource를 기준으로 위치와 크기를 계산하므로 이러한 좌표를 `production\\\_schedule`에 저장하지 않는다.

화면 배치 저장이 필요한 경우에만 별도로 관리한다.

```text
schedule\\\_board\\\_layout
schedule\\\_board\\\_item
```

즉:

```text
업무 데이터
production\\\_schedule

화면 배치 데이터
schedule\\\_board\\\_layout / schedule\\\_board\\\_item
```

로 분리한다.

\---

# 62\. Calendar Drag \& Drop

Calendar에서 작업을 다른 시간 또는 다른 설비로 이동할 수 있도록 한다.

```http
PUT /api/production-schedules/{id}
```

API에서 다음을 검증한다.

1. 설비 중복 여부
2. 작업시간 중복 여부
3. LOT 상태
4. 주문 잔량
5. 계획수량
6. 공정/기준서 Version
7. 설비별 기본 작업시간
8. 재작업 여부
9. 이미 시작된 작업인지 여부
10. 변경 권한

검증을 통과한 경우에만 DB를 변경한다.

\---

# 63\. Calendar 일정 생성

일정 생성 흐름:

```text
제품
 ↓
LOT
 ↓
설비
 ↓
수량
 ↓
시작시간
 ↓
예상 작업시간
 ↓
생산계획 생성
```

API에서 다음을 확인한다.

* 주문 잔량
* LOT 상태
* 설비 사용 가능 상태
* 설비 중복계획
* 생산수량
* 표준 작업시간
* 공정
* 기준서
* 재작업 여부

\---

# 64\. Calendar 일정 클릭 → 작업 상세

Calendar 일정 클릭 시 생산계획과 실제 작업을 함께 조회한다.

```text
생산계획
 ├─ 고객
 ├─ 품목
 ├─ LOT
 ├─ 설비
 ├─ 계획수량
 ├─ 계획시간
 └─ 상태

실제작업
 ├─ 시작
 ├─ 일시정지
 ├─ 재개
 └─ 완료
```

검사 및 불량 처리 화면으로 연결한다.

\---

# 65\. 진행상태와 Calendar 표시

## Schedule Status

```text
PLANNED
CONFIRMED
ALLOCATED
RUNNING
COMPLETED
CANCELLED
```

## Work Status

```text
ALLOCATED
READY
RUNNING
PAUSED
COMPLETED
INSPECTION\\\_WAIT
INSPECTION\\\_COMPLETED
CANCELLED
```

실제 적용 상태명은 기존 프로그램의 업무 흐름을 확인한 후 확정한다.

\---

# 66\. 작업자 배정

기존 `t\\\_workerassignment`는 `worker\\\_assignment`로 전환한다.

```text
worker\\\_assignment
 ├─ employee\\\_id
 ├─ equipment\\\_id
 ├─ shift\\\_id
 └─ work\\\_date
```

주/야 2교대 및 설비별 작업자 배정 화면과 연결한다.

\---

# 67\. 실시간 진행상태

작업 시작/일시정지/완료 시 `production\\\_work.status`를 변경한다.

```text
현장 작업자
    ↓
ASP.NET Core API
    ↓
MariaDB
    ↓
SignalR
 ├─ 생산관리 화면
 ├─ 관리자 화면
 └─ 태블릿
```

\---

# 68\. API 영역

권장 API 영역:

```text
/api/auth
/api/customers
/api/parts
/api/equipment
/api/employees
/api/orders
/api/lots
/api/production-schedules
/api/production-works
/api/worker-assignments
/api/inspections
/api/defects
/api/production-outputs
/api/shipments
/api/equipment-downtime
/api/maintenance
```

Calendar는 기간/설비/상태를 조건으로 필요한 일정만 조회한다.

예:

```http
GET /api/production-schedules?from=2026-09-19\\\&to=2026-09-20

GET /api/production-schedules?equipmentId=12\\\&from=2026-09-19\\\&to=2026-09-20

GET /api/production-schedules?status=RUNNING
```

\---

# 69\. Calendar 조회 성능

권장 인덱스:

```sql
CREATE INDEX ix\\\_schedule\\\_calendar
ON production\\\_schedule
(
    equipment\\\_id,
    planned\\\_start\\\_at,
    planned\\\_end\\\_at
);

CREATE INDEX ix\\\_schedule\\\_date\\\_status
ON production\\\_schedule
(
    work\\\_date,
    status
);
```

React로 전체 생산이력을 전송하지 않고 기간, 설비, 상태 등으로 조회 범위를 제한한다.

\---

# 70\. 스케줄 재계산

기존:

```text
sp\\\_RecalculateWorkPlanSequence
sp\\\_RecalculateWorkPlanTimes
```

의 기능은 V2에서도 유지하거나 Scheduling Service로 이관한다.

신규 명칭 예:

```text
sp\\\_recalculate\\\_schedule\\\_sequence
sp\\\_recalculate\\\_schedule\\\_times
```

React가 일정 계산의 기준이 되지 않고 API/Service 또는 DB가 검증과 계산을 담당한다.

\---

# 71\. React 생산관리 화면

권장 화면 구조:

```text
생산관리
 ├─ 생산계획 Calendar
 ├─ 설비별 Gantt
 ├─ 작업현황
 ├─ 작업자 배정
 └─ 생산실적

품질관리
 ├─ 검사
 ├─ 불량
 └─ 재작업

설비관리
 ├─ 설비현황
 ├─ 비가동
 └─ 보전

기준정보
 ├─ 고객
 ├─ 품목
 ├─ 공정
 ├─ 기준서
 └─ 설비
```

\---

# 72\. 웹/모바일 확장

동일한 ASP.NET Core API를 React Web, Android, 태블릿에서 공통 사용한다.

현장 태블릿 우선 기능:

* LOT 조회
* 작업 시작
* 일시정지
* 작업 완료
* 검사
* 불량 등록
* 사진 첨부

\---

# 73\. 웹 시스템 추가 권장 테이블

로그인 계정과 사원 Master를 분리한다.

```text
user
role
user\\\_role
permission
audit\\\_log
```

다음 변경은 Audit Log를 남기는 것을 권장한다.

* 생산계획 변경
* 설비 변경
* 작업상태 변경
* 검사결과 변경
* 불량처리 변경
* 출하/마감 처리

\---

# 74\. 권한 설계

```text
User
  ↓
Role
  ↓
Permission
  ↓
Menu / API
```

화면에서 버튼을 숨기는 것만으로 권한을 처리하지 않고 **API에서도 데이터 변경 권한을 검증**한다.

\---

# 75\. 기존 화면과 DB 분리 원칙

기존 WinForms의 Box 좌표와 화면 구성은 업무 DB와 분리한다.

```text
업무 데이터
    ↓
MariaDB
    ↓
ASP.NET Core API
    ↓
React UI
```

React Calendar/Gantt를 사용하면 기존 `x/y/width/height` 방식의 화면 좌표 저장을 상당 부분 제거할 수 있다.

\---

# 76\. 최종 웹 시스템 구조

```text
React Web
   │
   ├── REST
   └── SignalR
        │
        ▼
ASP.NET Core API
        │
        └── Dapper / SQL
              │
              ▼
           MariaDB V2
```

핵심 업무 흐름:

```text
Customer
  ↓
Sales Order
  ↓
Sales Order Item
  ↓
LOT
  ↓
Production Schedule
  ↓
Production Work
  ↓
Input / Work Condition
  ↓
Inspection
  ↓
Defect / Production Output
  ↓
Shipment
  ↓
Shipment Closing
```

Calendar 관계:

```text
Equipment
   ↓
Production Schedule
   ↓
React Calendar / Gantt
   ↓
Production Work
```

\---

# 77\. React Calendar 전환 최종 판단

현재 V2 DB 구조는 React + Calendar 기반 생산관리 시스템으로 확장할 수 있다.

|기존|V2|React/Web 역할|
|-|-|-|
|`workplan`|`production\\\_schedule`|Calendar Event|
|`t\\\_schedulebox`|`schedule\\\_board\\\_layout/item`|선택적 UI Layout|
|`t\\\_work`|`production\\\_work`|실제 작업상태|
|`t\\\_workerassignment`|`worker\\\_assignment`|작업자 배정|
|`t\\\_equipment`|`equipment`|Calendar Resource|
|`t\\\_income`|`sales\\\_order/order\\\_item`|생산계획 원천|
|`t\\\_inputwaiting`|`production\\\_input\\\_queue`|투입대기|
|`t\\\_inputsub`|`production\\\_input`|실제 투입|
|`t\\\_inspection\\\*`|`inspection\\\*`|품질검사|
|`t\\\_defect`|`defect\\\_occurrence`|불량/재작업|
|`t\\\_outcome`|`production\\\_output/shipment`|생산/출하|

기본 구조:

```text
MariaDB V2
    ↓
ASP.NET Core Web API
    ↓
React + TypeScript
    ↓
Calendar / Gantt
```

\---

# 78\. 웹 전환 개발 순서

1. V2 DDL 확정
2. 기존 46개 테이블 Migration Mapping 확정
3. ASP.NET Core Web API 프로젝트 생성
4. Customer/Part/Equipment API
5. Sales Order/LOT API
6. Production Schedule API
7. Production Work API
8. React 기본 Layout
9. React Calendar
10. 설비 Resource Calendar
11. Drag \& Drop
12. 작업 시작/일시정지/완료
13. 작업자 배정
14. 검사/불량
15. 생산결과/출하
16. SignalR 실시간 상태
17. 권한/Audit Log
18. Android/Tablet 확장

\---

# 79\. React 전환 시 DB에서 추가로 확정할 사항

React Calendar 적용 전에 다음 사항을 확정한다.

1. `production\\\_schedule`의 일정 중복 허용 규칙
2. 동일 설비의 작업 순서 변경 규칙
3. 작업 시작 후 일정 Drag \& Drop 제한
4. 휴일/비가동시간/교대시간 처리
5. 설비별 작업시간 계산 규칙
6. 작업자 배정과 생산작업의 연결 시점
7. 재작업 LOT의 일정 생성 규칙
8. 일정 취소와 실제작업 취소의 차이
9. 계획수량과 실제수량의 차이 처리
10. 일정 변경 Audit Log 기준

이 항목은 기존 프로그램의 실제 업무 흐름을 확인한 후 확정한다.

\---

# 80\. 웹 시스템 도입 후 권장 데이터 흐름

```text
\\\[기준정보]
Customer / Part / Equipment / Process / Standard
                    │
                    ▼
\\\[수주]
Sales Order / Sales Order Item
                    │
                    ▼
\\\[LOT]
Lot 생성 / 분할 / 재작업
                    │
                    ▼
\\\[생산계획]
Production Schedule
                    │
          ┌─────────┴─────────┐
          ▼                   ▼
   React Calendar        Worker Assignment
          │
          ▼
\\\[실제작업]
Production Work
          │
    ┌─────┼─────┐
    ▼     ▼     ▼
 Input  Inspection  Condition
          │
          ▼
        Defect
          │
          ▼
   Production Output
          │
          ▼
       Shipment
          │
          ▼
   Shipment Closing
```

\---

# 81\. 웹 전환 시 권장 병행운영 방식

기존 WinForms 프로그램을 즉시 폐기하지 않고 단계적으로 전환한다.

```text
기존 WinForms
      │
      ├── 기존 DB
      │
      └── 신규 API/DB와 단계적 연계
                    │
                    ▼
               React Web
```

초기에는 기준정보와 조회 화면부터 React로 전환하고, 이후:

```text
생산계획 Calendar
      ↓
작업진행
      ↓
검사/불량
      ↓
생산결과/출하
```

순서로 전환한다.

기존 프로그램과 신규 웹에서 동일 업무 데이터를 동시에 수정하지 않도록 **업무 영역별 단일 수정 주체**를 지정한다.

\---

# 82\. 최종 아키텍처 결정

```text
                   ┌──────────────────┐
                   │ React Web        │
                   │ Calendar / Gantt │
                   └────────┬─────────┘
                            │
                   REST API / SignalR
                            │
                   ┌────────▼─────────┐
                   │ ASP.NET Core API │
                   │ Business Service │
                   │ Scheduling       │
                   │ Validation       │
                   └────────┬─────────┘
                            │
                         Dapper
                            │
                   ┌────────▼─────────┐
                   │ MariaDB V2       │
                   │ Master           │
                   │ Order / LOT      │
                   │ Schedule         │
                   │ Work             │
                   │ Inspection       │
                   │ Output / Shipment│
                   └──────────────────┘
```

핵심 원칙:

* DB는 업무 데이터의 기준이다.
* API는 업무규칙과 권한을 검증한다.
* React는 화면과 사용자 입력을 담당한다.
* Calendar는 생산계획을 시각화한다.
* 실제 작업상태는 `production\\\_work`가 관리한다.
* 화면 좌표는 업무 DB와 분리한다.
* 과거 생산기록은 Version/Snapshot으로 보호한다.
* 기존 46개 테이블은 모두 Migration Mapping 후 단계적으로 전환한다.
* 기존 프로그램의 업무 규칙을 확인하지 않은 상태에서 구 테이블을 임의 삭제하지 않는다.



