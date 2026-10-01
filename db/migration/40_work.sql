-- =====================================================================
-- ⑧ 작업 LOT (설계 §10 ⑧, §3, §23.4, §25)
--   t_work → production_work, t_worksub(+t_inputsub 장입·추출 시각) → production_work_input,
--   t_workconditiondetail(+t_conditiontemplate 단계 이름) → production_work_condition
-- - 구 데이터에는 주 LOT 병기가 없다: 주공정 LOT(설정 main_unit_processes)의 투입 행만 main_work_id = 자신, 나머지는 NULL
-- - 계획(workplan)은 이관하지 않으므로 production_schedule_id = NULL (구 데이터 이관분만 허용)
-- - 시간: 구 takentime·expectedtime = 시간 → 분
-- - 재실행: 새 LOT 추가 + 이미 이관한 LOT의 진행 상태·실적 시각 갱신 (작업 진행 쓰기 주체 = 구 시스템인 동안)
-- =====================================================================
USE bbakggum_v2;
SET collation_connection = @@collation_database;
START TRANSACTION;

SET @step_row = bbakggum_mig.cfg('step_row_item');

-- ---------------------------------------------------------------------
-- 작업 LOT
-- ---------------------------------------------------------------------
CREATE TEMPORARY TABLE s_work0 AS
SELECT w.*, TRIM(w.lotno) AS lot_no_trim,
       ROW_NUMBER() OVER (PARTITION BY TRIM(w.lotno) ORDER BY w.workid) AS lot_dup,
       ROW_NUMBER() OVER (PARTITION BY w.equipmentid, w.workdate, w.subno ORDER BY w.workid) AS seq_dup,
       COALESCE(bbakggum_mig.code_by_name('WORK_STATUS', w.progressstep), IF(w.isdone = 1, 'COMPLETED', 'ALLOCATED')) AS status0,
       bbakggum_mig.code_by_name('WORK_STATUS', w.progressstep) IS NULL AS status_unmatched
  FROM bbakggum_legacy.t_work w;
CREATE TEMPORARY TABLE s_work (PRIMARY KEY (lk), KEY (lot_no)) AS
SELECT CAST(w.workid AS CHAR(200)) AS lk, w.workid, m.new_id,
       IF(w.lot_dup = 1 AND w.lot_no_trim <> '', w.lot_no_trim, CONCAT(COALESCE(NULLIF(w.lot_no_trim, ''), 'LOT'), '-M', w.workid)) AS lot_no,
       w.lot_no_trim AS legacy_lot_no, IF(w.seq_dup = 1, w.subno, NULL) AS lot_seq, w.seq_dup,
       u.unit_process_id, bbakggum_mig.new_id('t_equipment', CAST(w.equipmentid AS CHAR), 'equipment') AS equipment_id,
       mu.unit_process_name IS NOT NULL AS is_main_process,
       bbakggum_mig.new_id('t_standardtemplate', CONCAT(TRIM(w.equipmentname), '|', TRIM(w.unitprocessname)), 'step_template') AS step_template_id,
       bbakggum_mig.new_id('t_standard', CAST(w.standardid AS CHAR), 'standard_version') AS standard_version_id,
       IF(w.isfixed = 1, 1, 0) AS is_standard_fixed, NULLIF(TRIM(w.convertlotno), '') AS submit_lot_no, w.workdate AS work_date,
       w.status0 AS status, w.status_unmatched, w.progressstep AS legacy_status,
       w.starttime AS actual_start_at, IF(w.endtime >= w.starttime OR w.starttime IS NULL, w.endtime, NULL) AS actual_end_at,
       w.endtime < w.starttime AS end_before_start,
       ROUND(w.expectedtime * 60, 2) AS expected_duration_min, ROUND(w.takentime * 60, 2) AS actual_duration_min,
       IF(w.isrework = 1, 1, 0) AS is_rework, NULLIF(TRIM(w.marking), '') AS marking,
       NULLIF(TRIM(w.unitprocessname), '') AS unit_process_name_snapshot, NULLIF(TRIM(w.equipmentname), '') AS equipment_name_snapshot,
       NULLIF(TRIM(w.heatprocessname), '') AS heat_process_name_snapshot, LEFT(NULLIF(TRIM(w.worker), ''), 50) AS worker_name_snapshot,
       NULLIF(TRIM(w.remarks), '') AS remark, COALESCE(w.starttime, w.workdate) AS created_at
  FROM s_work0 w
  LEFT JOIN bbakggum_mig.unit_process_by_name u ON u.unit_process_name = TRIM(w.unitprocessname)
  LEFT JOIN bbakggum_mig.main_unit_process mu ON mu.unit_process_name = TRIM(w.unitprocessname)
  LEFT JOIN migration_id_map m ON m.legacy_table = 't_work' AND m.legacy_key = CAST(w.workid AS CHAR) AND m.new_table = 'production_work';

-- 설비당 투입(진행 중) 1건 (DB UNIQUE): 가장 최근 시작한 LOT 만 투입, 나머지는 완료로
CREATE TEMPORARY TABLE s_work_input_rank (PRIMARY KEY (lk)) AS
SELECT lk, ROW_NUMBER() OVER (PARTITION BY equipment_id ORDER BY actual_start_at DESC, workid DESC) AS rn
  FROM s_work WHERE status = 'INPUT' AND equipment_id IS NOT NULL;
UPDATE s_work s JOIN s_work_input_rank r ON r.lk = s.lk SET s.status = 'COMPLETED' WHERE r.rn > 1;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'work', 't_work', s.lk, 'VALUE_CHANGED', CONCAT('LOT ', s.lot_no, ' — 같은 설비에 투입 중 LOT 이 여럿 (설비당 1건) → 완료로')
  FROM s_work s JOIN s_work_input_rank r ON r.lk = s.lk WHERE r.rn > 1;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'work', 't_work', lk, 'CODE_UNMATCHED', CONCAT('진행 단계 "', IFNULL(legacy_status, ''), '" → ', status) FROM s_work WHERE status_unmatched;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'work', 't_work', lk, 'DUPLICATE', CONCAT('LOT번호 "', IFNULL(legacy_lot_no, ''), '" 중복/없음 → ', lot_no) FROM s_work
 WHERE new_id IS NULL AND lot_no <> IFNULL(legacy_lot_no, '');
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'work', 't_work', lk, 'DUPLICATE', CONCAT('LOT ', lot_no, ' — 같은 설비·작업일에 같은 작업순번 → 순번 비움') FROM s_work WHERE new_id IS NULL AND seq_dup > 1;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'work', 't_work', lk, 'VALUE_DROPPED', CONCAT('LOT ', lot_no, ' — 종료 시각이 시작보다 앞섬 → 종료 시각 비움') FROM s_work WHERE new_id IS NULL AND end_before_start;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'work', 't_work', lk, 'SKIPPED', CONCAT('LOT ', lot_no, ' — 단위공정 "', IFNULL(unit_process_name_snapshot, ''), '" 없음') FROM s_work WHERE new_id IS NULL AND unit_process_id IS NULL;

-- 이미 이관한 LOT: 진행 상태·실적 갱신 (투입 UNIQUE 충돌을 피하려고 투입 아닌 상태 먼저)
UPDATE production_work n JOIN s_work s ON s.new_id = n.production_work_id
   SET n.status = s.status, n.actual_start_at = s.actual_start_at, n.actual_end_at = s.actual_end_at, n.actual_duration_min = s.actual_duration_min,
       n.expected_duration_min = s.expected_duration_min, n.submit_lot_no = s.submit_lot_no, n.is_standard_fixed = s.is_standard_fixed,
       n.standard_version_id = s.standard_version_id, n.worker_name_snapshot = s.worker_name_snapshot, n.remark = s.remark
 WHERE s.status <> 'INPUT';
UPDATE production_work n JOIN s_work s ON s.new_id = n.production_work_id
   SET n.status = s.status, n.actual_start_at = s.actual_start_at, n.actual_end_at = s.actual_end_at, n.actual_duration_min = s.actual_duration_min,
       n.expected_duration_min = s.expected_duration_min, n.submit_lot_no = s.submit_lot_no, n.is_standard_fixed = s.is_standard_fixed,
       n.standard_version_id = s.standard_version_id, n.worker_name_snapshot = s.worker_name_snapshot, n.remark = s.remark
 WHERE s.status = 'INPUT';

INSERT INTO production_work (lot_no, lot_seq, unit_process_id, equipment_id, is_main_process, step_template_id, standard_version_id, is_standard_fixed,
                             submit_lot_no, work_date, status, actual_start_at, actual_end_at, expected_duration_min, actual_duration_min, is_rework, marking,
                             unit_process_name_snapshot, equipment_name_snapshot, heat_process_name_snapshot, worker_name_snapshot, remark, created_at)
SELECT lot_no, lot_seq, unit_process_id, equipment_id, is_main_process, step_template_id, standard_version_id, is_standard_fixed,
       submit_lot_no, work_date, status, actual_start_at, actual_end_at, expected_duration_min, actual_duration_min, is_rework, marking,
       unit_process_name_snapshot, equipment_name_snapshot, heat_process_name_snapshot, worker_name_snapshot, remark, created_at
  FROM s_work WHERE new_id IS NULL AND unit_process_id IS NOT NULL
 ORDER BY status = 'INPUT', workid;
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT 't_work', s.lk, 'production_work', n.production_work_id FROM s_work s JOIN production_work n ON n.lot_no = s.lot_no
 WHERE s.new_id IS NULL AND s.unit_process_id IS NOT NULL;

-- 구 LOT번호 → 신규 작업 (투입·검사·부적합이 LOT번호 문자열로 참조) — 표는 01_prepare 에서 만듦
DELETE FROM bbakggum_mig.work_by_lot;
INSERT INTO bbakggum_mig.work_by_lot (lot_no, production_work_id, is_main_process, work_date)
SELECT TRIM(w.lotno), n.production_work_id, n.is_main_process, n.work_date
  FROM bbakggum_legacy.t_work w
  JOIN migration_id_map m ON m.legacy_table = 't_work' AND m.legacy_key = CAST(w.workid AS CHAR) AND m.new_table = 'production_work'
  JOIN production_work n ON n.production_work_id = m.new_id
 WHERE w.workid = (SELECT MIN(x.workid) FROM bbakggum_legacy.t_work x WHERE TRIM(x.lotno) = TRIM(w.lotno));

-- ---------------------------------------------------------------------
-- 투입 (t_worksub: 같은 LOT × 같은 입고번호 = 1행으로 합침, 장입·추출 시각 = t_inputsub 의 LOT 첫 장입·마지막 추출)
-- ---------------------------------------------------------------------
CREATE TEMPORARY TABLE s_tray (PRIMARY KEY (lot_no)) AS
SELECT TRIM(lotno) AS lot_no, LEFT(GROUP_CONCAT(NULLIF(TRIM(traymark), '') ORDER BY id SEPARATOR ','), 50) AS tray_marks,
       MIN(intime) AS intime, MAX(outtime) AS outtime
  FROM bbakggum_legacy.t_inputsub GROUP BY TRIM(lotno);
CREATE TEMPORARY TABLE s_ws (PRIMARY KEY (lk)) AS
SELECT CONCAT(TRIM(ws.lotno), '|', TRIM(ws.incomeno)) AS lk, MIN(ws.worksubid) AS first_id, COUNT(*) AS merged,
       TRIM(ws.lotno) AS lot_no, TRIM(ws.incomeno) AS income_no,
       SUM(COALESCE(ws.inputqt, 0)) AS input_qty, SUM(ws.inputweight) AS input_weight, MAX(ws.stepprice) AS unit_price_snapshot,
       SUM(ws.inputamount) AS input_amount, MAX(NULLIF(TRIM(ws.originelotno), '')) AS origin_lot_no,
       MAX(NULLIF(TRIM(ws.customername), '')) AS customer_name_snapshot, MAX(NULLIF(TRIM(ws.partname), '')) AS part_name_snapshot,
       MAX(NULLIF(TRIM(ws.partnumber), '')) AS part_number_snapshot, MAX(NULLIF(TRIM(ws.specification), '')) AS specification_snapshot,
       MAX(NULLIF(TRIM(ws.model), '')) AS model_snapshot, MAX(ws.partid) AS partid
  FROM bbakggum_legacy.t_worksub ws
 GROUP BY TRIM(ws.lotno), TRIM(ws.incomeno);
CREATE TEMPORARY TABLE s_input (PRIMARY KEY (lk)) AS
SELECT s.*, w.production_work_id, w.is_main_process, w.work_date,
       (SELECT m.new_id FROM migration_id_map m JOIN bbakggum_legacy.t_income i ON m.legacy_key = CAST(i.incomeid AS CHAR)
         WHERE m.legacy_table = 't_income' AND m.new_table = 'sales_order_item' AND TRIM(i.incomeno) = s.income_no ORDER BY i.incomeid LIMIT 1) AS sales_order_item_id,
       o.production_work_id AS origin_work_id,
       (SELECT TRIM(i.customerlot) FROM bbakggum_legacy.t_income i WHERE TRIM(i.incomeno) = s.income_no ORDER BY i.incomeid LIMIT 1) AS customer_lot_snapshot,
       (SELECT TRIM(i.material) FROM bbakggum_legacy.t_income i WHERE TRIM(i.incomeno) = s.income_no ORDER BY i.incomeid LIMIT 1) AS material_snapshot,
       -- 표준 확정 품목 (구 t_work.fixedpartid): 그 품목의 첫 투입 행
       (SELECT x.fixedpartid FROM bbakggum_legacy.t_work x WHERE TRIM(x.lotno) = s.lot_no ORDER BY x.workid LIMIT 1) AS fixed_part_id,
       (SELECT m.new_id FROM migration_id_map m WHERE m.legacy_table = 't_worksub' AND m.legacy_key = CAST(s.first_id AS CHAR) AND m.new_table = 'production_work_input') AS new_id
  FROM s_ws s
  LEFT JOIN bbakggum_mig.work_by_lot w ON w.lot_no = s.lot_no
  LEFT JOIN bbakggum_mig.work_by_lot o ON o.lot_no = s.origin_lot_no;
CREATE TEMPORARY TABLE s_input_basis (PRIMARY KEY (lk)) AS
SELECT lk, ROW_NUMBER() OVER (PARTITION BY lot_no ORDER BY first_id) AS rn FROM s_input WHERE partid = fixed_part_id;

INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'work', 't_worksub', ws.worksubid, 'SKIPPED',
       CONCAT_WS(' / ', IF(s.production_work_id IS NULL, CONCAT('작업 LOT "', s.lot_no, '" 없음'), NULL),
                        IF(s.sales_order_item_id IS NULL, CONCAT('입고번호 "', s.income_no, '" 없음'), NULL))
  FROM s_input s JOIN bbakggum_legacy.t_worksub ws ON TRIM(ws.lotno) = s.lot_no AND TRIM(ws.incomeno) = s.income_no
 WHERE s.new_id IS NULL AND (s.production_work_id IS NULL OR s.sales_order_item_id IS NULL);
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'work', 't_worksub', first_id, 'MERGED', CONCAT('LOT ', lot_no, ' × ', income_no, ' 투입 ', merged, '행 → 1행 (수량 합산)')
  FROM s_input WHERE new_id IS NULL AND merged > 1;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'work', 't_worksub', first_id, 'REF_MISSING', CONCAT('재작업 원 LOT "', origin_lot_no, '" 없음 — 원 LOT 연결 안 함')
  FROM s_input WHERE new_id IS NULL AND origin_lot_no IS NOT NULL AND origin_work_id IS NULL;

INSERT INTO production_work_input (production_work_id, sales_order_item_id, main_work_id, origin_work_id, is_standard_basis, tray_mark,
                                   input_qty, input_weight, unit_price_snapshot, input_amount, loaded_at, unloaded_at, status,
                                   customer_name_snapshot, customer_lot_snapshot, part_name_snapshot, part_number_snapshot,
                                   specification_snapshot, model_snapshot, material_snapshot, created_at)
SELECT s.production_work_id, s.sales_order_item_id, IF(s.is_main_process, s.production_work_id, NULL), s.origin_work_id, IF(b.rn = 1, 1, 0),
       -- 트레이는 LOT 단위 기록이라 투입 행이 1개인 LOT 에만 붙임
       IF((SELECT COUNT(*) FROM bbakggum_legacy.t_worksub x WHERE TRIM(x.lotno) = s.lot_no) = s.merged, t.tray_marks, NULL),
       s.input_qty, s.input_weight, s.unit_price_snapshot, s.input_amount,
       bbakggum_mig.at_time(s.work_date, t.intime),
       IF(bbakggum_mig.at_time(s.work_date, t.outtime) >= bbakggum_mig.at_time(s.work_date, t.intime), bbakggum_mig.at_time(s.work_date, t.outtime), NULL),
       'INPUT', s.customer_name_snapshot, s.customer_lot_snapshot, s.part_name_snapshot, s.part_number_snapshot, s.specification_snapshot,
       s.model_snapshot, s.material_snapshot, s.work_date
  FROM s_input s
  LEFT JOIN s_input_basis b ON b.lk = s.lk
  LEFT JOIN s_tray t ON t.lot_no = s.lot_no
 WHERE s.new_id IS NULL AND s.production_work_id IS NOT NULL AND s.sales_order_item_id IS NOT NULL;
-- 매핑: 합친 구 행 모두 → 같은 신규 행
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT 't_worksub', CAST(ws.worksubid AS CHAR), 'production_work_input', n.production_work_input_id
  FROM s_input s
  JOIN production_work_input n ON n.production_work_id = s.production_work_id AND n.sales_order_item_id = s.sales_order_item_id
   AND n.main_key = IF(s.is_main_process, s.production_work_id, 0)
  JOIN bbakggum_legacy.t_worksub ws ON TRIM(ws.lotno) = s.lot_no AND TRIM(ws.incomeno) = s.income_no
 WHERE s.new_id IS NULL AND s.production_work_id IS NOT NULL AND s.sales_order_item_id IS NOT NULL;

-- ---------------------------------------------------------------------
-- 작업 조건 (LOT별 관리항목 × 단계) — 조건이 아직 없는 LOT 만
-- ---------------------------------------------------------------------
CREATE TEMPORARY TABLE s_wc_cell (KEY (row_id)) AS
SELECT TRIM(w.lotno) AS lot_no, w.id AS row_id, w.subno, TRIM(w.item) AS item, q.n,
       TRIM(ELT(q.n, w.step1, w.step2, w.step3, w.step4, w.step5, w.step6, w.step7, w.step8, w.step9, w.step10,
                     w.step11, w.step12, w.step13, w.step14, w.step15)) AS val
  FROM bbakggum_legacy.t_workconditiondetail w CROSS JOIN bbakggum_mig.seq q
 WHERE q.n <= 15 AND NULLIF(TRIM(w.item), '') IS NOT NULL;
CREATE TEMPORARY TABLE s_wc_lot (PRIMARY KEY (lot_no)) AS
SELECT w.lot_no, w.production_work_id, n.step_template_id
  FROM bbakggum_mig.work_by_lot w JOIN production_work n ON n.production_work_id = w.production_work_id
 WHERE NOT EXISTS (SELECT 1 FROM production_work_condition c WHERE c.production_work_id = w.production_work_id);
-- 단계 이름: 스텝 행 → t_conditiontemplate column 행 → 단계 템플릿 같은 순번 → '#열번호'
CREATE TEMPORARY TABLE s_wc_step_name (PRIMARY KEY (lot_no, n)) AS
SELECT lot_no, n, MIN(val) AS step_name FROM s_wc_cell WHERE item = @step_row AND NULLIF(val, '') IS NOT NULL GROUP BY lot_no, n;
CREATE TEMPORARY TABLE s_ct_step_name (PRIMARY KEY (lot_no, n)) AS
SELECT TRIM(t.lotno) AS lot_no, q.n,
       MIN(TRIM(ELT(q.n, t.item1, t.item2, t.item3, t.item4, t.item5, t.item6, t.item7, t.item8, t.item9, t.item10,
                         t.item11, t.item12, t.item13, t.item14, t.item15))) AS step_name
  FROM bbakggum_legacy.t_conditiontemplate t CROSS JOIN bbakggum_mig.seq q
 WHERE t.templatetype = 'column' AND q.n <= 15
   AND NULLIF(TRIM(ELT(q.n, t.item1, t.item2, t.item3, t.item4, t.item5, t.item6, t.item7, t.item8, t.item9, t.item10,
                            t.item11, t.item12, t.item13, t.item14, t.item15)), '') IS NOT NULL
 GROUP BY TRIM(t.lotno), q.n;
CREATE TEMPORARY TABLE s_wc_col (lot_no VARCHAR(100) NOT NULL, n INT NOT NULL, PRIMARY KEY (lot_no, n));
INSERT IGNORE INTO s_wc_col SELECT lot_no, n FROM s_wc_step_name;
INSERT IGNORE INTO s_wc_col SELECT lot_no, n FROM s_wc_cell WHERE item <> @step_row AND NULLIF(val, '') IS NOT NULL;
CREATE TEMPORARY TABLE s_wc_col_named (PRIMARY KEY (lot_no, n)) AS
SELECT c.lot_no, c.n, LEFT(COALESCE(sn.step_name, ct.step_name, ti.step_name, CONCAT('#', c.n)), 100) AS step_name
  FROM s_wc_col c
  JOIN s_wc_lot l ON l.lot_no = c.lot_no
  LEFT JOIN s_wc_step_name sn ON sn.lot_no = c.lot_no AND sn.n = c.n
  LEFT JOIN s_ct_step_name ct ON ct.lot_no = c.lot_no AND ct.n = c.n
  LEFT JOIN step_template_item ti ON ti.step_template_id = l.step_template_id AND ti.sequence_no = c.n;
-- 관리항목 행 (같은 항목이 두 번이면 첫 행)
CREATE TEMPORARY TABLE s_wc_row (PRIMARY KEY (row_id)) AS
SELECT x.row_id, x.lot_no, x.item, ROW_NUMBER() OVER (PARTITION BY x.lot_no ORDER BY x.subno, x.row_id) AS item_sequence_no
  FROM (SELECT w.id AS row_id, TRIM(w.lotno) AS lot_no, TRIM(w.item) AS item, w.subno,
               ROW_NUMBER() OVER (PARTITION BY TRIM(w.lotno), TRIM(w.item) ORDER BY w.subno, w.id) AS dup
          FROM bbakggum_legacy.t_workconditiondetail w
         WHERE NULLIF(TRIM(w.item), '') IS NOT NULL AND TRIM(w.item) <> @step_row) x
 WHERE x.dup = 1;
-- 행 × 열 전부 (값 없는 칸도 — 표 모양 유지)
INSERT INTO production_work_condition (production_work_id, condition_item_id, item_sequence_no, step_sequence_no, step_name_snapshot, set_value)
SELECT l.production_work_id, ci.new_id, r.item_sequence_no, c.n, c.step_name, LEFT(NULLIF(v.val, ''), 100)
  FROM s_wc_row r
  JOIN s_wc_lot l ON l.lot_no = r.lot_no
  JOIN migration_id_map ci ON ci.legacy_table = 'item' AND ci.legacy_key = r.item AND ci.new_table = 'condition_item'
  JOIN s_wc_col_named c ON c.lot_no = r.lot_no
  LEFT JOIN s_wc_cell v ON v.row_id = r.row_id AND v.n = c.n;

COMMIT;
