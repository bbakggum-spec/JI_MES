-- =====================================================================
-- 구 DB(bbakggum_legacy) → bbakggum_v2 이관 설정 (설계 §25)
--   이관 규칙을 바꿀 때는 이 파일만 고친다. 실행할 때마다 다시 적용된다.
--   원본은 항상 덤프를 복원한 bbakggum_legacy (운영 bbakggum 에는 접속하지 않는다).
--   작업 스키마 bbakggum_mig = 설정·문제 목록·검증 결과 (bbakggum_v2 에는 migration_id_map 만 남김)
-- =====================================================================

-- 문자셋·정렬은 서버 기본 (원본·대상과 같아야 문자열 비교가 섞이지 않음)
CREATE DATABASE IF NOT EXISTS bbakggum_mig;

CREATE TABLE IF NOT EXISTS bbakggum_mig.config (
    config_key   VARCHAR(100) NOT NULL PRIMARY KEY,
    config_value VARCHAR(500) NOT NULL,
    description  VARCHAR(255) NULL
) ENGINE=InnoDB;

DELETE FROM bbakggum_mig.config;
INSERT INTO bbakggum_mig.config (config_key, config_value, description) VALUES
 -- 구 DB 에 코드가 없는 기준정보의 코드 = 접두 + 구 PK (6자리 0 채움, 길면 그대로)
 ('customer_code_prefix',      'C',   '거래처 코드 (구 customerid)'),
 ('part_code_prefix',          'P',   '품목 코드 (구 partid)'),
 ('heat_process_code_prefix',  'HP',  '공정 코드 (구 heatprocessid)'),
 ('unit_process_code_prefix',  'UP',  '단위공정 코드 (구 unitprocessid)'),
 ('step_template_code_prefix', 'ST',  '단계 템플릿 코드 (구 t_standardtemplate 의 설비·단위공정별 첫 id)'),
 ('standard_code_prefix',      'WS',  '작업표준 코드 (구 workstandardid)'),
 ('employee_code_prefix',      'E',   '사원 코드 (구 t_employee.id)'),
 ('instrument_code_prefix',    'INS', '측정기구 코드 (구 t_instruments.id)'),
 ('unit_inspection_item_code_prefix', 'LI', '공정검사 항목 코드 (구 t_unitinspectionitem.id)'),
 ('equipment_code_fallback',   'EQ',  '설비 코드 = 구 설비 이니셜, 비었거나 중복이면 접두 + equipmentid'),
 -- 구 입고(t_income)는 헤더가 없다 → 같은 업체·같은 입고일 행을 수주 1건으로 묶는다
 ('sales_order_no_prefix',     'MIG', '수주번호 = 접두 + yyMMdd + "-" + 구 customerid (신규 채번 형식과 겹치지 않게)'),
 ('closing_no_prefix',         'MIGCL', '마감번호 = 접두 + yyyyMM + "-" + 구 customerid (구 마감완료 전표의 업체·마감월 묶음)'),
 -- 공통코드 표시명과 맞지 않는 구 거래처 구분 (예: 비매출처)
 ('customer_type_unmatched',   'PURCHASE', '구 customertype 이 CUSTOMER_TYPE 표시명과 다를 때 넣을 코드 (문제 목록에 기록)'),
 -- 주공정 단위공정 이름 (쉼표 구분). 주공정은 신규 개념이라 구 데이터에는 없음 → 기본 빈 값 = 이관 LOT 은 모두 주공정 아님
 -- (주 LOT 추적은 전환 후 신규 공정 경로 등록부터, 설계 §12 ⑪)
 ('main_unit_processes',       '',    '주공정 LOT 판정 — 지정하면 그 단위공정 투입 행은 main_work_id = 자신'),
 -- 구 작업표준·작업조건 표에서 단계 이름을 담은 행의 item 값 (나머지 행 = 관리항목)
 ('step_row_item',             '스텝', 't_standarddetail / t_workconditiondetail 의 단계 이름 행'),
 -- 구 검사기준 판정 방식 문자열 (F_InspectionCriteriaForm cmbRangeType) → RANGE_TYPE 코드. 빈 값이면 상·하한 유무로 정함
 ('range_type_Range',          'BETWEEN', ''),
 ('range_type_Min',            'MIN',     ''),
 ('range_type_Max',            'MAX',     ''),
 -- 비가동 사유 공통코드 (t_combolist.comboname → common_code_group)
 ('combo_group_비가동사유',     'DOWNTIME_REASON', 't_combolist 비가동사유 → 공통코드 그룹');
