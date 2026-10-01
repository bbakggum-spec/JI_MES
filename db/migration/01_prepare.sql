-- =====================================================================
-- 이관 준비: 원본 확인, 작업 스키마(bbakggum_mig) 도구, 구 스키마 차이 흡수 (설계 §25)
-- =====================================================================

-- 원본·대상 확인 (운영 이름 bbakggum 은 쓰지 않는다)
DELIMITER $$
CREATE OR REPLACE PROCEDURE bbakggum_mig.assert_true(IN cond BOOLEAN, IN msg VARCHAR(255))
BEGIN
    IF cond IS NULL OR NOT cond THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = msg;
    END IF;
END$$
DELIMITER ;

CALL bbakggum_mig.assert_true(
    (SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'bbakggum_legacy' AND table_name IN ('t_customer','t_part','t_income','t_work')) = 4,
    '원본 bbakggum_legacy 가 없습니다 — dev-db.ps1 legacy -Dump <덤프> 로 먼저 복원하세요');
CALL bbakggum_mig.assert_true(
    (SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'bbakggum_v2' AND table_name = 'migration_id_map') = 1,
    '대상 bbakggum_v2 가 없습니다 — DDL 을 먼저 적용하세요');

-- 문제 목록 (매 실행마다 새로 — 최근 실행 결과만 남음)
CREATE TABLE IF NOT EXISTS bbakggum_mig.issue (
    issue_id     BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    step         VARCHAR(30)  NOT NULL,
    legacy_table VARCHAR(64)  NOT NULL,
    legacy_key   VARCHAR(200) NULL,
    issue_code   VARCHAR(50)  NOT NULL COMMENT 'SKIPPED = 이관 안 함 / 나머지 = 이관했지만 확인 필요',
    detail       VARCHAR(500) NULL,
    logged_at    DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    KEY ix_issue_code (issue_code, legacy_table)
) ENGINE=InnoDB;
TRUNCATE TABLE bbakggum_mig.issue;

-- 1..16 (구 step1~15, subp1~16, v1~v10 등 열 → 행 펼치기)
CREATE OR REPLACE TABLE bbakggum_mig.seq (n INT NOT NULL PRIMARY KEY) ENGINE=InnoDB;
INSERT INTO bbakggum_mig.seq (n) VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12),(13),(14),(15),(16);

DELIMITER $$
CREATE OR REPLACE FUNCTION bbakggum_mig.cfg(k VARCHAR(100)) RETURNS VARCHAR(500) READS SQL DATA
    RETURN (SELECT config_value FROM bbakggum_mig.config WHERE config_key = k)$$

-- 접두 + 구 PK (6자리 0 채움 — 더 길면 자르지 않음)
CREATE OR REPLACE FUNCTION bbakggum_mig.code(prefix VARCHAR(20), id BIGINT) RETURNS VARCHAR(60) DETERMINISTIC
    RETURN CONCAT(prefix, LPAD(id, GREATEST(6, CHAR_LENGTH(id)), '0'))$$

CREATE OR REPLACE FUNCTION bbakggum_mig.new_id(lt VARCHAR(64), lk VARCHAR(200), nt VARCHAR(64)) RETURNS BIGINT UNSIGNED READS SQL DATA
    RETURN (SELECT new_id FROM bbakggum_v2.migration_id_map WHERE legacy_table = lt AND legacy_key = lk AND new_table = nt)$$

CREATE OR REPLACE FUNCTION bbakggum_mig.nz(s TEXT) RETURNS TEXT DETERMINISTIC
    RETURN NULLIF(TRIM(s), '')$$

-- 숫자 문자열만 숫자로 (아니면 NULL — 형 변환 경고로 strict 모드가 멈추지 않게)
CREATE OR REPLACE FUNCTION bbakggum_mig.num(s VARCHAR(255)) RETURNS DECIMAL(18,6) DETERMINISTIC
    RETURN IF(TRIM(s) REGEXP '^-?[0-9]{1,12}(\\.[0-9]{1,6})?$', CAST(TRIM(s) AS DECIMAL(18,6)), NULL)$$

-- 구 문자열 일시 (yyyy-MM-dd[ HH:mm[:ss]], yyyy/MM/dd …) → DATETIME, 해석 못 하면 NULL
CREATE OR REPLACE FUNCTION bbakggum_mig.dt(s VARCHAR(100)) RETURNS DATETIME DETERMINISTIC
BEGIN
    DECLARE v VARCHAR(100) DEFAULT REPLACE(REPLACE(TRIM(s), '/', '-'), '.', '-');
    IF v REGEXP '^[0-9]{4}-[0-9]{1,2}-[0-9]{1,2} [0-9]{1,2}:[0-9]{2}:[0-9]{2}' THEN RETURN STR_TO_DATE(LEFT(v, 19), '%Y-%m-%d %H:%i:%s'); END IF;
    IF v REGEXP '^[0-9]{4}-[0-9]{1,2}-[0-9]{1,2} [0-9]{1,2}:[0-9]{2}' THEN RETURN STR_TO_DATE(LEFT(v, 16), '%Y-%m-%d %H:%i'); END IF;
    IF v REGEXP '^[0-9]{4}-[0-9]{1,2}-[0-9]{1,2}$' THEN RETURN STR_TO_DATE(v, '%Y-%m-%d'); END IF;
    RETURN NULL;
END$$

-- 작업일 + 구 시각 문자열 (HH:mm[:ss]) → DATETIME
CREATE OR REPLACE FUNCTION bbakggum_mig.at_time(d DATE, t VARCHAR(50)) RETURNS DATETIME DETERMINISTIC
    RETURN IF(d IS NOT NULL AND TRIM(t) REGEXP '^[0-9]{1,2}:[0-9]{2}(:[0-9]{2})?$', TIMESTAMP(d, TRIM(t)), bbakggum_mig.dt(t))$$

-- 공통코드: 표시명 / 정렬순서 → 코드 (구 문자열·번호를 코드값으로)
CREATE OR REPLACE FUNCTION bbakggum_mig.code_by_name(grp VARCHAR(50), nm VARCHAR(100)) RETURNS VARCHAR(50) READS SQL DATA
    RETURN (SELECT c.code FROM bbakggum_v2.common_code c JOIN bbakggum_v2.common_code_group g ON g.common_code_group_id = c.common_code_group_id
             WHERE g.group_code = grp AND c.code_name = TRIM(nm) ORDER BY c.sort_order LIMIT 1)$$

CREATE OR REPLACE FUNCTION bbakggum_mig.code_by_sort(grp VARCHAR(50), ord INT) RETURNS VARCHAR(50) READS SQL DATA
    RETURN (SELECT c.code FROM bbakggum_v2.common_code c JOIN bbakggum_v2.common_code_group g ON g.common_code_group_id = c.common_code_group_id
             WHERE g.group_code = grp AND c.sort_order = ord LIMIT 1)$$

-- 단가 구분: 구 ea/kg/ch (대소문자 무관) → PRICE_BASIS 코드 (공통코드 attr legacy)
CREATE OR REPLACE FUNCTION bbakggum_mig.price_basis(u VARCHAR(50)) RETURNS VARCHAR(10) READS SQL DATA
    RETURN (SELECT c.code FROM bbakggum_v2.common_code c JOIN bbakggum_v2.common_code_group g ON g.common_code_group_id = c.common_code_group_id
             WHERE g.group_code = 'PRICE_BASIS' AND LOWER(JSON_VALUE(c.attr_json, '$.legacy')) = LOWER(TRIM(u)) LIMIT 1)$$
DELIMITER ;

-- 주공정 단위공정 (설정 main_unit_processes). 주공정은 신규에서 도입한 개념 — 구 데이터에서 추정하지 않으므로 기본은 비어 있음 (설계 §12 ⑪)
CREATE OR REPLACE TABLE bbakggum_mig.main_unit_process (unit_process_name VARCHAR(50) NOT NULL PRIMARY KEY) ENGINE=InnoDB;
INSERT INTO bbakggum_mig.main_unit_process (unit_process_name)
SELECT DISTINCT TRIM(u.unitprocessname)
  FROM bbakggum_legacy.t_unitprocess u
 WHERE FIND_IN_SET(TRIM(u.unitprocessname), REPLACE(bbakggum_mig.cfg('main_unit_processes'), ', ', ',')) > 0;

-- 구 이름 참조 → 신규 id (10_master 가 채움, 이후 단계가 사용)
CREATE TABLE IF NOT EXISTS bbakggum_mig.unit_process_by_name (unit_process_name VARCHAR(100) NOT NULL PRIMARY KEY, unit_process_id BIGINT UNSIGNED NOT NULL) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS bbakggum_mig.equipment_by_name (equipment_name VARCHAR(100) NOT NULL PRIMARY KEY, equipment_id BIGINT UNSIGNED NOT NULL) ENGINE=InnoDB;
CREATE TABLE IF NOT EXISTS bbakggum_mig.work_by_lot (lot_no VARCHAR(100) NOT NULL PRIMARY KEY, production_work_id BIGINT UNSIGNED NOT NULL,
                                                     is_main_process TINYINT NOT NULL, work_date DATE NOT NULL) ENGINE=InnoDB;

-- 구 스키마 버전 차이: 마감 컬럼(closingmonth·closingstatus·sumaspart)은 2026-04 이후 추가 → 없으면 미마감
SET @cols = (SELECT GROUP_CONCAT(column_name) FROM information_schema.columns
              WHERE table_schema = 'bbakggum_legacy' AND table_name = 't_outcomesum' AND column_name IN ('closingmonth','closingstatus','sumaspart'));
SET @sql = CONCAT(
    'CREATE OR REPLACE TABLE bbakggum_mig.src_outcomesum ENGINE=InnoDB AS SELECT s.outcomesumid, s.outcomeno, s.outdate, s.customerid, s.customername, ',
    's.totalqt, s.totalweight, s.totalamount, s.closingdate, s.remark, ',
    IF(FIND_IN_SET('closingstatus', IFNULL(@cols, '')) > 0, 'CAST(s.closingstatus AS SIGNED)', '0'), ' AS closing_status, ',
    IF(FIND_IN_SET('closingmonth',  IFNULL(@cols, '')) > 0, 'CAST(s.closingmonth AS DATE)', 'CAST(NULL AS DATE)'), ' AS closing_month, ',
    IF(FIND_IN_SET('sumaspart',     IFNULL(@cols, '')) > 0, 'CAST(s.sumaspart AS SIGNED)', '0'), ' AS sum_as_part ',
    'FROM bbakggum_legacy.t_outcomesum s');
PREPARE st FROM @sql;
EXECUTE st;
DEALLOCATE PREPARE st;
