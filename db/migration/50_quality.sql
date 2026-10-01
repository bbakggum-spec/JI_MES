-- =====================================================================
-- ⑨ 검사 · 부적합 (설계 §10 ⑨, §5, §23.5~23.6, §25)
--   t_inspection (검사번호 × subno 행) → inspection 1건 + inspection_target N
--   t_inspectiondetail (항목 × 시료번호, v1~v10) → inspection_item + inspection_measurement (시료번호 순으로 펼침)
--   t_defect → defect_occurrence (판정 문자열 → DEFECT_ACTION 코드)
-- - 구 판정 "합격/불합격" 은 공통코드 DECISION 표시명으로 코드화, 항목 유형 번호는 INSPECTION_ITEM_TYPE 정렬순서로
-- - 경화층의 시료번호 0 행 = 측정 위치(깊이) → 측정값이 아니므로 이관 안 함 (위치는 검사기준 측정 위치)
-- - 거래 데이터는 추가만
-- =====================================================================
USE bbakggum_v2;
SET collation_connection = @@collation_database;
START TRANSACTION;

-- ---------------------------------------------------------------------
-- 검사 (검사번호 = inspection 1건)
-- ---------------------------------------------------------------------
CREATE TEMPORARY TABLE s_insp (PRIMARY KEY (lk)) AS
SELECT TRIM(t.inspectionno) AS lk, MIN(t.workdate) AS inspection_date,
       COALESCE((SELECT c.code FROM common_code c JOIN common_code_group g USING (common_code_group_id)
                  WHERE g.group_code = 'INSPECTION_TYPE' AND JSON_VALUE(c.attr_json, '$.prefix') = LEFT(TRIM(t.inspectionno), 2) LIMIT 1), 'OUTGOING') AS inspection_type,
       MIN(TRIM(t.lotno)) AS lot_no,
       -- 대상 중 하나라도 불합격이면 불합격
       CASE WHEN SUM(bbakggum_mig.code_by_name('DECISION', t.decision) = 'FAIL') > 0 THEN 'FAIL'
            WHEN SUM(bbakggum_mig.code_by_name('DECISION', t.decision) = 'CONDITIONAL') > 0 THEN 'CONDITIONAL'
            WHEN SUM(bbakggum_mig.code_by_name('DECISION', t.decision) = 'PASS') > 0 THEN 'PASS' END AS decision,
       IF(MIN(COALESCE(t.is_complete, 0)) = 1, 'COMPLETED', 'IN_PROGRESS') AS status,
       NULLIF(GROUP_CONCAT(NULLIF(TRIM(t.memo), '') ORDER BY t.subno SEPARATOR ' / '), '') AS remark,
       (SELECT m.new_id FROM migration_id_map m WHERE m.legacy_table = 't_inspection' AND m.legacy_key = TRIM(t.inspectionno) AND m.new_table = 'inspection') AS new_id
  FROM bbakggum_legacy.t_inspection t
 WHERE NULLIF(TRIM(t.inspectionno), '') IS NOT NULL
 GROUP BY TRIM(t.inspectionno);
CREATE TEMPORARY TABLE s_insp_ok (PRIMARY KEY (lk)) AS
SELECT s.*, w.production_work_id,
       -- 판정 기준 = 첫 대상 품목(+업체)의 현재 검사기준
       (SELECT v.inspection_standard_version_id
          FROM bbakggum_legacy.t_inspection t
          JOIN inspection_standard i ON i.part_id = bbakggum_mig.new_id('t_part', CAST(t.partid AS CHAR), 'part')
           AND i.customer_key IN (IFNULL(bbakggum_mig.new_id('t_customer', CAST(t.customerid AS CHAR), 'customer'), 0), 0)
          JOIN inspection_standard_version v ON v.inspection_standard_id = i.inspection_standard_id AND v.is_current = 1
         WHERE TRIM(t.inspectionno) = s.lk ORDER BY t.subno, i.customer_key DESC LIMIT 1) AS inspection_standard_version_id
  FROM s_insp s LEFT JOIN bbakggum_mig.work_by_lot w ON w.lot_no = s.lot_no;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'quality', 't_inspection', lk, 'SKIPPED', CONCAT('작업 LOT "', IFNULL(lot_no, ''), '" 없음') FROM s_insp_ok WHERE new_id IS NULL AND production_work_id IS NULL;

INSERT INTO inspection (inspection_no, inspection_type, inspection_date, production_work_id, inspection_standard_version_id, status, decision, completed_at, remark, created_at)
SELECT lk, inspection_type, inspection_date, production_work_id, inspection_standard_version_id, status, decision,
       IF(status = 'COMPLETED', inspection_date, NULL), remark, inspection_date
  FROM s_insp_ok WHERE new_id IS NULL AND production_work_id IS NOT NULL;
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT 't_inspection', s.lk, 'inspection', n.inspection_id FROM s_insp_ok s JOIN inspection n ON n.inspection_no = s.lk
 WHERE s.new_id IS NULL AND s.production_work_id IS NOT NULL;

-- 검사 대상 (구 1행 = 대상 1개, 투입 행 = 같은 LOT × 같은 입고번호)
CREATE TEMPORARY TABLE s_target (PRIMARY KEY (id)) AS
SELECT t.id, TRIM(t.inspectionno) AS inspection_no, m.new_id AS inspection_id, t.subno, TRIM(t.lotno) AS lot_no, TRIM(t.incomeno) AS income_no,
       (SELECT mi.new_id FROM migration_id_map mi JOIN bbakggum_legacy.t_worksub ws ON mi.legacy_key = CAST(ws.worksubid AS CHAR)
         WHERE mi.legacy_table = 't_worksub' AND mi.new_table = 'production_work_input'
           AND TRIM(ws.lotno) = TRIM(t.lotno) AND TRIM(ws.incomeno) = TRIM(t.incomeno) LIMIT 1) AS production_work_input_id,
       bbakggum_mig.new_id('t_customer', CAST(t.customerid AS CHAR), 'customer') AS customer_id,
       t.chargeqt AS inspection_qty, NULLIF(TRIM(t.convertlot), '') AS submit_lot_no_snapshot, NULLIF(TRIM(t.customername), '') AS customer_name_snapshot,
       NULLIF(TRIM(t.customerlot), '') AS customer_lot_snapshot, NULLIF(TRIM(t.partname), '') AS part_name_snapshot,
       NULLIF(TRIM(t.specification), '') AS specification_snapshot, NULLIF(TRIM(t.model), '') AS model_snapshot,
       NOT EXISTS (SELECT 1 FROM inspection_target x WHERE x.inspection_id = m.new_id) AS is_new
  FROM bbakggum_legacy.t_inspection t
  JOIN migration_id_map m ON m.legacy_table = 't_inspection' AND m.legacy_key = TRIM(t.inspectionno) AND m.new_table = 'inspection';
CREATE TEMPORARY TABLE s_target_ok (PRIMARY KEY (id)) AS
SELECT s.*, pwi.sales_order_item_id, COALESCE(s.customer_id, so.customer_id) AS target_customer_id,
       ROW_NUMBER() OVER (PARTITION BY s.inspection_id, s.production_work_input_id ORDER BY s.subno, s.id) AS dup
  FROM s_target s
  LEFT JOIN production_work_input pwi ON pwi.production_work_input_id = s.production_work_input_id
  LEFT JOIN sales_order_item soi ON soi.sales_order_item_id = pwi.sales_order_item_id
  LEFT JOIN sales_order so ON so.sales_order_id = soi.sales_order_id
 WHERE s.is_new;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'quality', 't_inspection', id, 'SKIPPED', CONCAT('검사 ', inspection_no, ' 대상 — LOT ', lot_no, ' × ', income_no, ' 투입 행 없음')
  FROM s_target_ok WHERE production_work_input_id IS NULL;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'quality', 't_inspection', id, 'DUPLICATE', CONCAT('검사 ', inspection_no, ' — 같은 투입 행이 대상으로 또 있음 (첫 행만)')
  FROM s_target_ok WHERE production_work_input_id IS NOT NULL AND dup > 1;
INSERT INTO inspection_target (inspection_id, sub_no, production_work_input_id, sales_order_item_id, customer_id, inspection_qty,
                               submit_lot_no_snapshot, customer_name_snapshot, customer_lot_snapshot, part_name_snapshot,
                               specification_snapshot, model_snapshot)
SELECT inspection_id, ROW_NUMBER() OVER (PARTITION BY inspection_id ORDER BY subno, id), production_work_input_id, sales_order_item_id, target_customer_id,
       inspection_qty, submit_lot_no_snapshot, customer_name_snapshot, customer_lot_snapshot, part_name_snapshot, specification_snapshot, model_snapshot
  FROM s_target_ok WHERE production_work_input_id IS NOT NULL AND dup = 1;
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT 't_inspection', CAST(s.id AS CHAR), 'inspection_target', x.inspection_target_id
  FROM s_target_ok s JOIN inspection_target x ON x.inspection_id = s.inspection_id AND x.production_work_input_id = s.production_work_input_id
 WHERE s.production_work_input_id IS NOT NULL AND s.dup = 1;

-- 검사 항목·측정값 (검사번호 × 유형 × 항목 × 위치 = 항목 1개)
CREATE TEMPORARY TABLE s_ii (PRIMARY KEY (inspection_id, sequence_no)) AS
SELECT x.*, ROW_NUMBER() OVER (PARTITION BY x.inspection_id ORDER BY x.item_type_no, x.first_id) AS sequence_no
  FROM (SELECT m.new_id AS inspection_id, TRIM(d.itemtype) AS item_type_no, TRIM(d.item) AS item_name, TRIM(IFNULL(d.location, '')) AS location,
               MIN(d.id) AS first_id,
               MAX(NULLIF(TRIM(d.result), '')) AS result,
               CASE WHEN SUM(bbakggum_mig.code_by_name('DECISION', d.decision) = 'FAIL') > 0 THEN 'FAIL'
                    WHEN SUM(bbakggum_mig.code_by_name('DECISION', d.decision) = 'PASS') > 0 THEN 'PASS' END AS decision
          FROM bbakggum_legacy.t_inspectiondetail d
          JOIN migration_id_map m ON m.legacy_table = 't_inspection' AND m.legacy_key = TRIM(d.inspectionno) AND m.new_table = 'inspection'
         WHERE NOT EXISTS (SELECT 1 FROM inspection_item ii WHERE ii.inspection_id = m.new_id)
         GROUP BY m.new_id, TRIM(d.itemtype), TRIM(d.item), TRIM(IFNULL(d.location, ''))) x;
INSERT INTO inspection_item (inspection_id, inspection_criteria_id, sequence_no, item_type, item_name, location, result, decision)
SELECT s.inspection_id,
       (SELECT c.inspection_criteria_id FROM inspection_criteria c JOIN inspection i ON i.inspection_standard_version_id = c.inspection_standard_version_id
         WHERE i.inspection_id = s.inspection_id AND c.item_name = s.item_name AND IFNULL(c.location, '') = s.location ORDER BY c.sequence_no LIMIT 1),
       s.sequence_no,
       IF(s.item_type_no REGEXP '^[0-9]+$', bbakggum_mig.code_by_sort('INSPECTION_ITEM_TYPE', CAST(s.item_type_no AS UNSIGNED)), NULL),
       COALESCE(NULLIF(s.item_name, ''), '(항목)'), NULLIF(s.location, ''), LEFT(s.result, 30), s.decision
  FROM s_ii s;
INSERT INTO inspection_measurement (inspection_item_id, sample_no, measured_value, measured_text)
SELECT ii.inspection_item_id, ROW_NUMBER() OVER (PARTITION BY ii.inspection_item_id ORDER BY d.number, d.id, q.n),
       bbakggum_mig.num(ELT(q.n, d.v1, d.v2, d.v3, d.v4, d.v5, d.v6, d.v7, d.v8, d.v9, d.v10)),
       IF(bbakggum_mig.num(ELT(q.n, d.v1, d.v2, d.v3, d.v4, d.v5, d.v6, d.v7, d.v8, d.v9, d.v10)) IS NULL,
          LEFT(TRIM(ELT(q.n, d.v1, d.v2, d.v3, d.v4, d.v5, d.v6, d.v7, d.v8, d.v9, d.v10)), 255), NULL)
  FROM s_ii s
  JOIN inspection_item ii ON ii.inspection_id = s.inspection_id AND ii.sequence_no = s.sequence_no
  JOIN migration_id_map m ON m.new_table = 'inspection' AND m.new_id = s.inspection_id AND m.legacy_table = 't_inspection'
  JOIN bbakggum_legacy.t_inspectiondetail d ON TRIM(d.inspectionno) = m.legacy_key AND TRIM(d.itemtype) = s.item_type_no
   AND TRIM(d.item) = s.item_name AND TRIM(IFNULL(d.location, '')) = s.location
 CROSS JOIN bbakggum_mig.seq q
 WHERE q.n <= 10 AND COALESCE(d.number, 1) > 0
   AND NULLIF(TRIM(ELT(q.n, d.v1, d.v2, d.v3, d.v4, d.v5, d.v6, d.v7, d.v8, d.v9, d.v10)), '') IS NOT NULL;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'quality', 't_inspectiondetail', d.id, 'SKIPPED', CONCAT('검사 ', d.inspectionno, ' "', d.item, '" 시료번호 0 행 (측정 위치) — 측정값 아님')
  FROM bbakggum_legacy.t_inspectiondetail d
  JOIN migration_id_map m ON m.legacy_table = 't_inspection' AND m.legacy_key = TRIM(d.inspectionno) AND m.new_table = 'inspection'
 WHERE d.number = 0;

-- ---------------------------------------------------------------------
-- 부적합 (t_defect)
-- ---------------------------------------------------------------------
CREATE TEMPORARY TABLE s_defect (PRIMARY KEY (lk)) AS
SELECT CAST(d.defectid AS CHAR(200)) AS lk, d.defectid,
       (SELECT m.new_id FROM migration_id_map m WHERE m.legacy_table = 't_defect' AND m.legacy_key = CAST(d.defectid AS CHAR) AND m.new_table = 'defect_occurrence') AS new_id,
       COALESCE(bbakggum_mig.new_id('t_worksub', CAST(d.worksubid AS CHAR), 'production_work_input'),
                (SELECT mi.new_id FROM migration_id_map mi JOIN bbakggum_legacy.t_worksub ws ON mi.legacy_key = CAST(ws.worksubid AS CHAR)
                  WHERE mi.legacy_table = 't_worksub' AND mi.new_table = 'production_work_input'
                    AND TRIM(ws.lotno) = TRIM(d.lotno) AND TRIM(ws.incomeno) = TRIM(d.incomeno) LIMIT 1)) AS production_work_input_id,
       (SELECT x.inspection_target_id FROM inspection_target x JOIN inspection i ON i.inspection_id = x.inspection_id
         WHERE i.inspection_no = TRIM(d.inspectionno) AND x.sales_order_item_id =
               (SELECT m.new_id FROM migration_id_map m JOIN bbakggum_legacy.t_income inc ON m.legacy_key = CAST(inc.incomeid AS CHAR)
                 WHERE m.legacy_table = 't_income' AND m.new_table = 'sales_order_item' AND TRIM(inc.incomeno) = TRIM(d.incomeno) LIMIT 1)
         LIMIT 1) AS inspection_target_id,
       d.defectdate, GREATEST(COALESCE(d.inputqt, 0), 0) AS defect_qty, d.inputweight AS defect_weight,
       bbakggum_mig.code_by_name('DEFECT_ACTION', d.reworkstep) AS decision, NULLIF(TRIM(d.reworkstep), '') AS legacy_action,
       NULLIF(CONCAT_WS(' / ', NULLIF(TRIM(d.decision), ''), NULLIF(TRIM(d.decisionmemo), ''), NULLIF(TRIM(d.determinermemo), '')), '') AS decision_remark,
       NULLIF(TRIM(d.reworkmemo), '') AS rework_remark, bbakggum_mig.dt(d.determinerdate) AS decided_at, bbakggum_mig.dt(d.completedate) AS completed_at,
       IF(d.check_complete = 1, 'COMPLETED', IF(bbakggum_mig.code_by_name('DEFECT_ACTION', d.reworkstep) IS NOT NULL, 'DECIDED', 'OPEN')) AS status,
       NULLIF(CONCAT_WS(' / ', IF(NULLIF(TRIM(d.determiner), '') IS NULL, NULL, CONCAT('판정자 ', TRIM(d.determiner))),
                               IF(NULLIF(TRIM(d.completewriter), '') IS NULL, NULL, CONCAT('완료자 ', TRIM(d.completewriter)))), '') AS person_note,
       TRIM(d.lotno) AS lot_no, TRIM(d.incomeno) AS income_no
  FROM bbakggum_legacy.t_defect d;
CREATE TEMPORARY TABLE s_defect_ok (PRIMARY KEY (lk)) AS
SELECT s.*, COALESCE(pwi.sales_order_item_id, tg.sales_order_item_id) AS sales_order_item_id, pwi.production_work_id, pwi.main_work_id,
       -- 재처리: 이 LOT 을 원 LOT 으로 하는 재작업 투입 행 (같은 수주)
       (SELECT r.production_work_input_id FROM production_work_input r
         WHERE r.origin_work_id = pwi.production_work_id AND r.sales_order_item_id = pwi.sales_order_item_id ORDER BY r.production_work_input_id LIMIT 1) AS rework_input_id
  FROM s_defect s
  LEFT JOIN production_work_input pwi ON pwi.production_work_input_id = s.production_work_input_id
  LEFT JOIN inspection_target tg ON tg.inspection_target_id = s.inspection_target_id
 WHERE s.new_id IS NULL;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'quality', 't_defect', lk, 'SKIPPED', CONCAT('LOT ', IFNULL(lot_no, ''), ' × ', IFNULL(income_no, ''), ' 투입 행·검사 대상 없음')
  FROM s_defect_ok WHERE sales_order_item_id IS NULL;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'quality', 't_defect', lk, 'CODE_UNMATCHED', CONCAT('처리 "', legacy_action, '" — DEFECT_ACTION 에 없음 (판정 없이 이관)')
  FROM s_defect_ok WHERE sales_order_item_id IS NOT NULL AND decision IS NULL AND legacy_action IS NOT NULL;
-- 자연키가 없어 비고에 임시 표식(#mig:구id)으로 매핑 후 원래 값으로
INSERT INTO defect_occurrence (sales_order_item_id, production_work_id, production_work_input_id, inspection_target_id, main_work_id, defect_date,
                               defect_qty, defect_weight, remark, status, decision, decision_remark, rework_input_id, rework_remark, decided_at, completed_at, created_at)
SELECT sales_order_item_id, production_work_id, production_work_input_id, inspection_target_id, main_work_id, COALESCE(defectdate, CURDATE()),
       defect_qty, defect_weight, CONCAT('#mig:', lk), status, decision, decision_remark,
       IF(decision = 'REWORK', rework_input_id, NULL), rework_remark, decided_at, IF(status = 'COMPLETED', completed_at, NULL), COALESCE(defectdate, NOW())
  FROM s_defect_ok WHERE sales_order_item_id IS NOT NULL;
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT 't_defect', s.lk, 'defect_occurrence', n.defect_occurrence_id
  FROM s_defect_ok s JOIN defect_occurrence n ON n.remark = CONCAT('#mig:', s.lk) WHERE s.sales_order_item_id IS NOT NULL;
UPDATE defect_occurrence n JOIN migration_id_map m ON m.new_table = 'defect_occurrence' AND m.new_id = n.defect_occurrence_id AND m.legacy_table = 't_defect'
  JOIN s_defect_ok s ON s.lk = m.legacy_key
   SET n.remark = s.person_note;

COMMIT;
