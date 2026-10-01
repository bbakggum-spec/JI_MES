-- =====================================================================
-- ④⑤ 조건 항목 · 단계 템플릿 · 작업표준 · 검사기준 (설계 §10 ④⑤, §2, §22.5~22.6, §25)
-- - 조건 항목: 구 item 문자열 → condition_item (코드 = 이름). 값에 숫자 아닌 것이 있으면 TEXT
-- - 단계 템플릿: t_standardtemplate 설비·단위공정별 column 행 = 단계, row 행 = 관리항목 (재실행 시 구 값으로 다시 채움)
-- - 작업표준·검사기준: Version 1 로 한 번만 이관 (확정 후 수정 금지 — 이후 변경은 신규 시스템의 새 Version)
-- =====================================================================
USE bbakggum_v2;
SET collation_connection = @@collation_database;
START TRANSACTION;

SET @step_row = bbakggum_mig.cfg('step_row_item');
SET @p_st     = bbakggum_mig.cfg('step_template_code_prefix');
SET @p_ws     = bbakggum_mig.cfg('standard_code_prefix');

-- ---------------------------------------------------------------------
-- 조건 항목 (구 item 문자열 정규화)
-- ---------------------------------------------------------------------
-- 표 행(관리항목) 셀 값: 작업표준·작업조건의 step1~15
CREATE TEMPORARY TABLE s_cond_cell (KEY (item)) AS
SELECT 'std' AS src, d.workstandardid AS owner_key, d.id AS row_id, d.subno, TRIM(d.item) AS item, q.n,
       TRIM(ELT(q.n, d.step1, d.step2, d.step3, d.step4, d.step5, d.step6, d.step7, d.step8, d.step9, d.step10,
                     d.step11, d.step12, d.step13, d.step14, d.step15)) AS val
  FROM bbakggum_legacy.t_standarddetail d CROSS JOIN bbakggum_mig.seq q
 WHERE q.n <= 15 AND NULLIF(TRIM(d.item), '') IS NOT NULL
UNION ALL
SELECT 'work', w.lotno, w.id, w.subno, TRIM(w.item), q.n,
       TRIM(ELT(q.n, w.step1, w.step2, w.step3, w.step4, w.step5, w.step6, w.step7, w.step8, w.step9, w.step10,
                     w.step11, w.step12, w.step13, w.step14, w.step15))
  FROM bbakggum_legacy.t_workconditiondetail w CROSS JOIN bbakggum_mig.seq q
 WHERE q.n <= 15 AND NULLIF(TRIM(w.item), '') IS NOT NULL;

CREATE TEMPORARY TABLE s_ci_name (name VARCHAR(100) NOT NULL PRIMARY KEY, is_text TINYINT NOT NULL DEFAULT 0);
INSERT IGNORE INTO s_ci_name (name)
SELECT DISTINCT item FROM s_cond_cell WHERE item <> @step_row;
INSERT IGNORE INTO s_ci_name (name)
SELECT DISTINCT TRIM(ELT(q.n, t.item1, t.item2, t.item3, t.item4, t.item5, t.item6, t.item7, t.item8, t.item9, t.item10,
                              t.item11, t.item12, t.item13, t.item14, t.item15))
  FROM bbakggum_legacy.t_standardtemplate t CROSS JOIN bbakggum_mig.seq q
 WHERE t.templatetype = 'row' AND q.n <= 15
   AND NULLIF(TRIM(ELT(q.n, t.item1, t.item2, t.item3, t.item4, t.item5, t.item6, t.item7, t.item8, t.item9, t.item10,
                            t.item11, t.item12, t.item13, t.item14, t.item15)), '') IS NOT NULL;
INSERT IGNORE INTO s_ci_name (name)
SELECT DISTINCT TRIM(ELT(q.n, t.item1, t.item2, t.item3, t.item4, t.item5, t.item6, t.item7, t.item8, t.item9, t.item10,
                              t.item11, t.item12, t.item13, t.item14, t.item15))
  FROM bbakggum_legacy.t_conditiontemplate t CROSS JOIN bbakggum_mig.seq q
 WHERE t.templatetype = 'row' AND q.n <= 15
   AND NULLIF(TRIM(ELT(q.n, t.item1, t.item2, t.item3, t.item4, t.item5, t.item6, t.item7, t.item8, t.item9, t.item10,
                            t.item11, t.item12, t.item13, t.item14, t.item15)), '') IS NOT NULL;
DELETE FROM s_ci_name WHERE name = @step_row;
UPDATE s_ci_name n SET n.is_text = 1
 WHERE EXISTS (SELECT 1 FROM s_cond_cell c WHERE c.item = n.name AND NULLIF(c.val, '') IS NOT NULL AND bbakggum_mig.num(c.val) IS NULL);

INSERT INTO condition_item (condition_item_code, condition_item_name, value_type)
SELECT LEFT(s.name, 50), LEFT(s.name, 100), IF(s.is_text, 'TEXT', 'NUMBER')
  FROM s_ci_name s
 WHERE NOT EXISTS (SELECT 1 FROM migration_id_map m WHERE m.legacy_table = 'item' AND m.legacy_key = s.name AND m.new_table = 'condition_item');
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT 'item', s.name, 'condition_item', n.condition_item_id
  FROM s_ci_name s JOIN condition_item n ON n.condition_item_code = LEFT(s.name, 50)
 WHERE NOT EXISTS (SELECT 1 FROM migration_id_map m WHERE m.legacy_table = 'item' AND m.legacy_key = s.name AND m.new_table = 'condition_item');
-- 재실행: 문자 값이 새로 생기면 TEXT 로 (TEXT → NUMBER 로 되돌리지는 않음)
UPDATE condition_item n JOIN migration_id_map m ON m.legacy_table = 'item' AND m.new_table = 'condition_item' AND m.new_id = n.condition_item_id
  JOIN s_ci_name s ON s.name = m.legacy_key
   SET n.value_type = 'TEXT' WHERE s.is_text = 1;

-- ---------------------------------------------------------------------
-- 단계 템플릿 (t_standardtemplate — 설비명·단위공정별 column 행 = 단계, row 행 = 관리항목)
-- ---------------------------------------------------------------------
CREATE TEMPORARY TABLE s_st0 AS
SELECT CONCAT(TRIM(t.equipmentname), '|', TRIM(t.unitprocessname)) AS lk, MIN(t.id) AS min_id,
       MAX(TRIM(t.equipmenttype)) AS equipment_type, TRIM(t.equipmentname) AS equipment_name, TRIM(t.unitprocessname) AS unit_process_name,
       MIN(CASE WHEN t.templatetype = 'column' THEN t.id END) AS column_id, MIN(CASE WHEN t.templatetype = 'row' THEN t.id END) AS row_id
  FROM bbakggum_legacy.t_standardtemplate t
 GROUP BY TRIM(t.equipmentname), TRIM(t.unitprocessname);
CREATE TEMPORARY TABLE s_st (PRIMARY KEY (lk), KEY (step_template_code)) AS
SELECT s.lk, s.column_id, s.row_id, m.new_id, bbakggum_mig.code(@p_st, s.min_id) AS step_template_code,
       LEFT(CONCAT_WS(' ', NULLIF(s.equipment_name, ''), NULLIF(s.unit_process_name, '')), 100) AS step_template_name,
       u.unit_process_id, bbakggum_mig.new_id('equipmenttype', s.equipment_type, 'equipment_type') AS equipment_type_id, e.equipment_id
  FROM s_st0 s
  LEFT JOIN bbakggum_mig.unit_process_by_name u ON u.unit_process_name = s.unit_process_name
  LEFT JOIN bbakggum_mig.equipment_by_name e ON e.equipment_name = s.equipment_name
  LEFT JOIN migration_id_map m ON m.legacy_table = 't_standardtemplate' AND m.legacy_key = s.lk AND m.new_table = 'step_template';
INSERT INTO step_template (step_template_code, step_template_name, unit_process_id, equipment_type_id, equipment_id)
SELECT step_template_code, step_template_name, unit_process_id, equipment_type_id, equipment_id FROM s_st WHERE new_id IS NULL AND unit_process_id IS NOT NULL;
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT 't_standardtemplate', s.lk, 'step_template', n.step_template_id FROM s_st s JOIN step_template n ON n.step_template_code = s.step_template_code
 WHERE s.new_id IS NULL AND s.unit_process_id IS NOT NULL;
UPDATE s_st s JOIN migration_id_map m ON m.legacy_table = 't_standardtemplate' AND m.legacy_key = s.lk AND m.new_table = 'step_template'
   SET s.new_id = m.new_id WHERE s.new_id IS NULL;
UPDATE step_template n JOIN s_st s ON s.new_id = n.step_template_id
   SET n.step_template_name = s.step_template_name, n.unit_process_id = s.unit_process_id, n.equipment_type_id = s.equipment_type_id, n.equipment_id = s.equipment_id;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'process', 't_standardtemplate', lk, 'SKIPPED', '단위공정 이름이 단위공정 기준정보에 없음' FROM s_st WHERE unit_process_id IS NULL;

-- 단계·관리항목은 구 값으로 다시 채움 (기준정보 쓰기 주체 = 구 시스템인 동안)
DELETE x FROM step_template_item x JOIN s_st s ON s.new_id = x.step_template_id;
DELETE x FROM step_template_condition x JOIN s_st s ON s.new_id = x.step_template_id;
INSERT INTO step_template_item (step_template_id, sequence_no, step_name)
SELECT s.new_id, ROW_NUMBER() OVER (PARTITION BY s.new_id ORDER BY q.n),
       LEFT(TRIM(ELT(q.n, t.item1, t.item2, t.item3, t.item4, t.item5, t.item6, t.item7, t.item8, t.item9, t.item10,
                          t.item11, t.item12, t.item13, t.item14, t.item15)), 100)
  FROM s_st s JOIN bbakggum_legacy.t_standardtemplate t ON t.id = s.column_id CROSS JOIN bbakggum_mig.seq q
 WHERE s.new_id IS NOT NULL AND q.n <= 15
   AND NULLIF(TRIM(ELT(q.n, t.item1, t.item2, t.item3, t.item4, t.item5, t.item6, t.item7, t.item8, t.item9, t.item10,
                            t.item11, t.item12, t.item13, t.item14, t.item15)), '') IS NOT NULL;
INSERT INTO step_template_condition (step_template_id, sequence_no, condition_item_id)
SELECT x.step_template_id, ROW_NUMBER() OVER (PARTITION BY x.step_template_id ORDER BY x.n), x.condition_item_id
  FROM (SELECT s.new_id AS step_template_id, q.n, ci.new_id AS condition_item_id,
               ROW_NUMBER() OVER (PARTITION BY s.new_id, ci.new_id ORDER BY q.n) AS dup
          FROM s_st s JOIN bbakggum_legacy.t_standardtemplate t ON t.id = s.row_id CROSS JOIN bbakggum_mig.seq q
          JOIN migration_id_map ci ON ci.legacy_table = 'item' AND ci.new_table = 'condition_item'
           AND ci.legacy_key = TRIM(ELT(q.n, t.item1, t.item2, t.item3, t.item4, t.item5, t.item6, t.item7, t.item8, t.item9, t.item10,
                                             t.item11, t.item12, t.item13, t.item14, t.item15))
         WHERE s.new_id IS NOT NULL AND q.n <= 15) x
 WHERE x.dup = 1;

-- ---------------------------------------------------------------------
-- 작업표준 (t_standard → standard + Version 1, t_standarddetail → 스텝·관리항목·조건값)
-- ---------------------------------------------------------------------
CREATE TEMPORARY TABLE s_std (PRIMARY KEY (lk), KEY (standard_code)) AS
SELECT CAST(s.workstandardid AS CHAR(200)) AS lk, s.workstandardid, m.new_id, bbakggum_mig.code(@p_ws, s.workstandardid) AS standard_code,
       LEFT(CONCAT_WS(' ', NULLIF(TRIM(s.partname), ''), NULLIF(TRIM(s.unitprocessname), ''), NULLIF(TRIM(s.equipmentname), '')), 100) AS standard_name,
       bbakggum_mig.new_id('t_part', CAST(s.partid AS CHAR), 'part') AS part_id,
       bbakggum_mig.new_id('t_customer', CAST(s.customerid AS CHAR), 'customer') AS customer_id,
       u.unit_process_id, bbakggum_mig.new_id('equipmenttype', TRIM(s.equipmenttype), 'equipment_type') AS equipment_type_id, e.equipment_id,
       bbakggum_mig.new_id('t_standardtemplate', CONCAT(TRIM(s.equipmentname), '|', TRIM(s.unitprocessname)), 'step_template') AS step_template_id,
       COALESCE(s.chargeqt, 0) AS charge_qty, COALESCE(NULLIF(TRIM(s.chargeunit), ''), 'charge') AS charge_unit,
       ROUND(s.runningtime * 60, 2) AS running_time_min, COALESCE(s.writedate, '2000-01-01') AS effective_from,
       s.partid AS legacy_part, TRIM(s.unitprocessname) AS legacy_unit_process
  FROM bbakggum_legacy.t_standard s
  LEFT JOIN bbakggum_mig.unit_process_by_name u ON u.unit_process_name = TRIM(s.unitprocessname)
  LEFT JOIN bbakggum_mig.equipment_by_name e ON e.equipment_name = TRIM(s.equipmentname)
  LEFT JOIN migration_id_map m ON m.legacy_table = 't_standard' AND m.legacy_key = CAST(s.workstandardid AS CHAR) AND m.new_table = 'standard';
INSERT INTO standard (standard_code, standard_name, part_id, customer_id, unit_process_id, equipment_type_id, equipment_id)
SELECT standard_code, COALESCE(NULLIF(standard_name, ''), standard_code), part_id, customer_id, unit_process_id, equipment_type_id, equipment_id
  FROM s_std WHERE new_id IS NULL AND part_id IS NOT NULL AND unit_process_id IS NOT NULL;
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT 't_standard', s.lk, 'standard', n.standard_id FROM s_std s JOIN standard n ON n.standard_code = s.standard_code
 WHERE s.new_id IS NULL AND s.part_id IS NOT NULL AND s.unit_process_id IS NOT NULL;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'process', 't_standard', lk, 'SKIPPED', CONCAT('품목(구 partid ', IFNULL(legacy_part, '-'), ') 또는 단위공정("', IFNULL(legacy_unit_process, ''), '") 없음')
  FROM s_std WHERE new_id IS NULL AND (part_id IS NULL OR unit_process_id IS NULL);

-- 이번에 새로 매핑된 표준만 Version 1 생성
CREATE TEMPORARY TABLE s_std_new (PRIMARY KEY (workstandardid)) AS
SELECT s.workstandardid, s.lk, m.new_id AS standard_id, s.step_template_id, s.charge_qty, s.charge_unit, s.running_time_min, s.effective_from
  FROM s_std s JOIN migration_id_map m ON m.legacy_table = 't_standard' AND m.legacy_key = s.lk AND m.new_table = 'standard'
 WHERE s.new_id IS NULL;
INSERT INTO standard_version (standard_id, version_no, step_template_id, charge_qty, charge_unit, running_time_min, effective_from, is_current, remark)
SELECT standard_id, 1, step_template_id, charge_qty, charge_unit, running_time_min, effective_from, 1, '구 DB 이관 (t_standard)' FROM s_std_new;
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT 't_standard', s.lk, 'standard_version', v.standard_version_id
  FROM s_std_new s JOIN standard_version v ON v.standard_id = s.standard_id AND v.version_no = 1;

-- 스텝(열): 스텝 행 이름 또는 값이 있는 열. 이름 = 스텝 행 → 단계 템플릿 같은 순번 → '#열번호'
CREATE TEMPORARY TABLE s_std_step_name (PRIMARY KEY (workstandardid, n)) AS
SELECT owner_key AS workstandardid, n, MIN(val) AS step_name FROM s_cond_cell
 WHERE src = 'std' AND item = @step_row AND NULLIF(val, '') IS NOT NULL GROUP BY owner_key, n;
CREATE TEMPORARY TABLE s_std_col (workstandardid INT NOT NULL, n INT NOT NULL, PRIMARY KEY (workstandardid, n));
INSERT IGNORE INTO s_std_col SELECT workstandardid, n FROM s_std_step_name;
INSERT IGNORE INTO s_std_col SELECT owner_key, n FROM s_cond_cell WHERE src = 'std' AND item <> @step_row AND NULLIF(val, '') IS NOT NULL;
INSERT INTO standard_version_step (standard_version_id, sequence_no, step_name)
SELECT v.new_id, c.n, LEFT(COALESCE(sn.step_name, ti.step_name, CONCAT('#', c.n)), 100)
  FROM s_std_col c
  JOIN s_std_new s ON s.workstandardid = c.workstandardid
  JOIN migration_id_map v ON v.legacy_table = 't_standard' AND v.legacy_key = s.lk AND v.new_table = 'standard_version'
  LEFT JOIN s_std_step_name sn ON sn.workstandardid = c.workstandardid AND sn.n = c.n
  LEFT JOIN step_template_item ti ON ti.step_template_id = s.step_template_id AND ti.sequence_no = c.n;

-- 관리항목(행): 스텝 행 외 행 (값 없는 행도 유지), 같은 항목이 두 번이면 첫 행만
CREATE TEMPORARY TABLE s_std_row (PRIMARY KEY (row_id)) AS
SELECT x.row_id, x.workstandardid, x.item, ROW_NUMBER() OVER (PARTITION BY x.workstandardid ORDER BY x.subno, x.row_id) AS sequence_no
  FROM (SELECT d.id AS row_id, d.workstandardid, TRIM(d.item) AS item, d.subno,
               ROW_NUMBER() OVER (PARTITION BY d.workstandardid, TRIM(d.item) ORDER BY d.subno, d.id) AS dup
          FROM bbakggum_legacy.t_standarddetail d
         WHERE NULLIF(TRIM(d.item), '') IS NOT NULL AND TRIM(d.item) <> @step_row) x
 WHERE x.dup = 1;
INSERT INTO standard_version_item (standard_version_id, sequence_no, condition_item_id)
SELECT v.new_id, r.sequence_no, ci.new_id
  FROM s_std_row r
  JOIN s_std_new s ON s.workstandardid = r.workstandardid
  JOIN migration_id_map v ON v.legacy_table = 't_standard' AND v.legacy_key = s.lk AND v.new_table = 'standard_version'
  JOIN migration_id_map ci ON ci.legacy_table = 'item' AND ci.legacy_key = r.item AND ci.new_table = 'condition_item';
INSERT INTO standard_condition (standard_version_id, step_no, condition_item_id, condition_value)
SELECT v.new_id, c.n, ci.new_id, LEFT(c.val, 100)
  FROM s_cond_cell c
  JOIN s_std_row r ON r.row_id = c.row_id
  JOIN s_std_new s ON s.workstandardid = r.workstandardid
  JOIN migration_id_map v ON v.legacy_table = 't_standard' AND v.legacy_key = s.lk AND v.new_table = 'standard_version'
  JOIN migration_id_map ci ON ci.legacy_table = 'item' AND ci.legacy_key = r.item AND ci.new_table = 'condition_item'
 WHERE c.src = 'std' AND NULLIF(c.val, '') IS NOT NULL;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'process', 't_standarddetail', d.id, 'DUPLICATE', CONCAT('같은 작업표준(', d.workstandardid, ')에 관리항목 "', d.item, '" 이 또 있음 — 첫 행만 이관')
  FROM bbakggum_legacy.t_standarddetail d
 WHERE NULLIF(TRIM(d.item), '') IS NOT NULL AND TRIM(d.item) <> @step_row
   AND d.workstandardid IN (SELECT workstandardid FROM s_std_new)
   AND NOT EXISTS (SELECT 1 FROM s_std_row r WHERE r.row_id = d.id);

-- ---------------------------------------------------------------------
-- 검사기준 (t_inspectioncriteria: 품목 × 업체 = 기준 1개, Version 1, 행 = 항목, p1~p10 = 측정 위치)
-- ---------------------------------------------------------------------
CREATE TEMPORARY TABLE s_is (PRIMARY KEY (lk)) AS
SELECT CONCAT(c.partid, '|', IFNULL(c.customerid, 0)) AS lk, c.partid, MAX(c.customerid) AS customerid,
       bbakggum_mig.new_id('t_part', CAST(c.partid AS CHAR), 'part') AS part_id,
       bbakggum_mig.new_id('t_customer', CAST(MAX(c.customerid) AS CHAR), 'customer') AS customer_id,
       (SELECT m.new_id FROM migration_id_map m WHERE m.legacy_table = 't_inspectioncriteria' AND m.legacy_key = CONCAT(c.partid, '|', IFNULL(c.customerid, 0))
           AND m.new_table = 'inspection_standard') AS new_id
  FROM bbakggum_legacy.t_inspectioncriteria c
 GROUP BY c.partid, IFNULL(c.customerid, 0);
-- 같은 품목에 업체를 못 찾은 기준이 여럿이면 (part, 공통) 이 겹침 → 첫 번째만
CREATE TEMPORARY TABLE s_is_ok (PRIMARY KEY (lk)) AS
SELECT x.* FROM (SELECT s.*, ROW_NUMBER() OVER (PARTITION BY s.part_id, IFNULL(s.customer_id, 0) ORDER BY s.lk) AS dup FROM s_is s WHERE s.part_id IS NOT NULL) x
 WHERE x.dup = 1;
INSERT INTO inspection_standard (part_id, customer_id)
SELECT part_id, customer_id FROM s_is_ok o
 WHERE new_id IS NULL AND NOT EXISTS (SELECT 1 FROM inspection_standard i WHERE i.part_id = o.part_id AND i.customer_key = IFNULL(o.customer_id, 0));
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT 't_inspectioncriteria', o.lk, 'inspection_standard', i.inspection_standard_id
  FROM s_is_ok o JOIN inspection_standard i ON i.part_id = o.part_id AND i.customer_key = IFNULL(o.customer_id, 0)
 WHERE o.new_id IS NULL;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'process', 't_inspectioncriteria', lk, 'SKIPPED', CONCAT('품목 없음 (구 partid ', partid, ')') FROM s_is WHERE part_id IS NULL;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'process', 't_inspectioncriteria', lk, 'REF_MISSING', CONCAT('업체 없음 (구 customerid ', customerid, ') — 전 업체 공통 기준으로 이관')
  FROM s_is WHERE part_id IS NOT NULL AND customer_id IS NULL AND COALESCE(customerid, 0) <> 0;

CREATE TEMPORARY TABLE s_is_new (PRIMARY KEY (lk)) AS
SELECT o.lk, o.partid, o.customerid, m.new_id AS inspection_standard_id
  FROM s_is_ok o JOIN migration_id_map m ON m.legacy_table = 't_inspectioncriteria' AND m.legacy_key = o.lk AND m.new_table = 'inspection_standard'
 WHERE o.new_id IS NULL
   AND NOT EXISTS (SELECT 1 FROM inspection_standard_version v WHERE v.inspection_standard_id = m.new_id);
INSERT INTO inspection_standard_version (inspection_standard_id, version_no, effective_from, is_current, remark)
SELECT inspection_standard_id, 1, '2000-01-01', 1, '구 DB 이관 (t_inspectioncriteria)' FROM s_is_new;

CREATE TEMPORARY TABLE s_ic (PRIMARY KEY (id)) AS
SELECT c.id, v.inspection_standard_version_id,
       ROW_NUMBER() OVER (PARTITION BY v.inspection_standard_version_id ORDER BY c.subno, c.id) AS sequence_no,
       COALESCE(IF(TRIM(c.itemtype) REGEXP '^[0-9]+$', bbakggum_mig.code_by_sort('INSPECTION_ITEM_TYPE', CAST(TRIM(c.itemtype) AS UNSIGNED) + 1), NULL),
                bbakggum_mig.code_by_name('INSPECTION_ITEM_TYPE', REPLACE(c.itemtype, ',', '·'))) AS item_type,
       c.itemtype AS legacy_item_type,
       COALESCE(NULLIF(TRIM(c.item), ''), '(항목)') AS item_name, NULLIF(TRIM(c.location), '') AS location, NULLIF(TRIM(c.spec), '') AS specification_value,
       NULLIF(TRIM(c.tool), '') AS tool_name, NULLIF(TRIM(c.testvalue), '') AS test_value, NULLIF(TRIM(c.scale), '') AS scale,
       bbakggum_mig.num(c.Min) AS lower_limit, bbakggum_mig.num(c.Max) AS upper_limit,
       NULLIF(TRIM(c.rangetype), '') AS legacy_range, IF(TRIM(c.hardnesslimit) REGEXP '^[0-9]{1,9}$', CAST(TRIM(c.hardnesslimit) AS SIGNED), NULL) AS hardness_limit,
       GREATEST(COALESCE(c.sample, 1), 1) AS sample_count, GREATEST(COALESCE(c.testcount, 1), 1) AS test_count,
       c.p1, c.p2, c.p3, c.p4, c.p5, c.p6, c.p7, c.p8, c.p9, c.p10
  FROM bbakggum_legacy.t_inspectioncriteria c
  JOIN s_is_new s ON s.lk = CONCAT(c.partid, '|', IFNULL(c.customerid, 0))
  JOIN inspection_standard_version v ON v.inspection_standard_id = s.inspection_standard_id AND v.version_no = 1;
-- 하한 > 상한 이면 판정 불가 → 둘 다 비우고 문제 목록에
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'process', 't_inspectioncriteria', id, 'VALUE_DROPPED', CONCAT('하한 ', lower_limit, ' > 상한 ', upper_limit, ' — 상·하한 비움') FROM s_ic
 WHERE lower_limit > upper_limit;
UPDATE s_ic SET lower_limit = NULL, upper_limit = NULL WHERE lower_limit > upper_limit;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'process', 't_inspectioncriteria', id, 'CODE_UNMATCHED', CONCAT('검사 항목 유형 "', IFNULL(legacy_item_type, ''), '" — 화면에서 다시 선택') FROM s_ic WHERE item_type IS NULL;
INSERT INTO inspection_criteria (inspection_standard_version_id, sequence_no, item_type, item_name, location, specification_value, tool_name, test_value,
                                 scale, range_type, lower_limit, upper_limit, hardness_limit, sample_count, test_count)
SELECT inspection_standard_version_id, sequence_no, item_type, item_name, location, specification_value, tool_name, test_value, scale,
       COALESCE(bbakggum_mig.cfg(CONCAT('range_type_', legacy_range)),
                CASE WHEN lower_limit IS NOT NULL AND upper_limit IS NOT NULL THEN 'BETWEEN' WHEN lower_limit IS NOT NULL THEN 'MIN'
                     WHEN upper_limit IS NOT NULL THEN 'MAX' ELSE 'NONE' END),
       lower_limit, upper_limit, hardness_limit, sample_count, test_count
  FROM s_ic;
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT 't_inspectioncriteria', CAST(s.id AS CHAR), 'inspection_criteria', c.inspection_criteria_id
  FROM s_ic s JOIN inspection_criteria c ON c.inspection_standard_version_id = s.inspection_standard_version_id AND c.sequence_no = s.sequence_no;
INSERT INTO inspection_criteria_point (inspection_criteria_id, point_no, point_label)
SELECT c.inspection_criteria_id, q.n, LEFT(TRIM(ELT(q.n, s.p1, s.p2, s.p3, s.p4, s.p5, s.p6, s.p7, s.p8, s.p9, s.p10)), 100)
  FROM s_ic s JOIN inspection_criteria c ON c.inspection_standard_version_id = s.inspection_standard_version_id AND c.sequence_no = s.sequence_no
 CROSS JOIN bbakggum_mig.seq q
 WHERE q.n <= 10 AND NULLIF(TRIM(ELT(q.n, s.p1, s.p2, s.p3, s.p4, s.p5, s.p6, s.p7, s.p8, s.p9, s.p10)), '') IS NOT NULL;

COMMIT;
