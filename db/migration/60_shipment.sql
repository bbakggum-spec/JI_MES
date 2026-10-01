-- =====================================================================
-- ⑩ 출하 전표·마감 + 설비 비가동·보전 (설계 §10 ⑩, §6, §23.7, §25)
--   t_outcomesum → shipment (전표), t_outcome → shipment_item
--   구 closingstatus 0/1/2 → CLOSING_STATUS (정렬순서 = 값+1). 마감완료 전표는 업체 × 마감월로 shipment_closing 1건을 만들어 연결
--   세액 = 공급가액(구 totalamount) × sales.vat_rate, sales.amount_rounding (신규 출하 서비스와 같은 계산)
--   구 출하에는 LOT 정보가 없음 → shipment_item.main_work_id = NULL
-- - 재실행: 새 전표·상세 추가 + 이미 이관한 전표의 금액·마감 상태 갱신 (상세 행은 추가만)
-- =====================================================================
USE bbakggum_v2;
SET collation_connection = @@collation_database;
START TRANSACTION;

SET @p_cl     = bbakggum_mig.cfg('closing_no_prefix');
SET @vat_rate = (SELECT CAST(COALESCE(setting_value, default_value) AS DECIMAL(9,6)) FROM system_setting WHERE setting_key = 'sales.vat_rate');
SET @rounding = (SELECT COALESCE(setting_value, default_value) FROM system_setting WHERE setting_key = 'sales.amount_rounding');

-- ---------------------------------------------------------------------
-- 출하 전표
-- ---------------------------------------------------------------------
CREATE TEMPORARY TABLE s_ship (PRIMARY KEY (lk), KEY (shipment_no)) AS
SELECT CAST(s.outcomesumid AS CHAR(200)) AS lk, s.outcomesumid,
       (SELECT m.new_id FROM migration_id_map m WHERE m.legacy_table = 't_outcomesum' AND m.legacy_key = CAST(s.outcomesumid AS CHAR) AND m.new_table = 'shipment') AS new_id,
       IF(NULLIF(TRIM(s.outcomeno), '') IS NOT NULL
          AND s.outcomesumid = (SELECT MIN(x.outcomesumid) FROM bbakggum_mig.src_outcomesum x WHERE x.outcomeno = s.outcomeno),
          TRIM(s.outcomeno), CONCAT(bbakggum_mig.cfg('sales_order_no_prefix'), 'O', s.outcomesumid)) AS shipment_no, TRIM(s.outcomeno) AS legacy_no,
       s.outdate AS shipment_date, s.customerid, bbakggum_mig.new_id('t_customer', CAST(s.customerid AS CHAR), 'customer') AS customer_id,
       COALESCE(bbakggum_mig.code_by_sort('CLOSING_STATUS', s.closing_status + 1), 'UNCLOSED') AS closing_status,
       COALESCE(s.closing_month, IF(s.closing_status IN (1, 2), s.outdate, NULL)) AS closing_month_date,
       s.closing_status IN (1, 2) AND s.closing_month IS NULL AS closing_month_guessed,
       s.closingdate AS closing_due_date, IF(s.sum_as_part = 1, 1, 0) AS print_sum_by_part,
       COALESCE(s.totalamount, 0) AS supply_amount,
       CASE @rounding WHEN 'FLOOR' THEN FLOOR(COALESCE(s.totalamount, 0) * @vat_rate) WHEN 'CEIL' THEN CEIL(COALESCE(s.totalamount, 0) * @vat_rate)
                      ELSE ROUND(COALESCE(s.totalamount, 0) * @vat_rate) END AS vat_amount,
       COALESCE(s.totalqt, 0) AS total_qty, COALESCE(s.totalweight, 0) AS total_weight,
       NULLIF(TRIM(s.customername), '') AS customer_name_snapshot, NULLIF(TRIM(s.remark), '') AS remark
  FROM bbakggum_mig.src_outcomesum s;
UPDATE s_ship SET closing_status = 'UNCLOSED', closing_month_date = NULL WHERE closing_status <> 'UNCLOSED' AND closing_month_date IS NULL;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'shipment', 't_outcomesum', lk, 'SKIPPED', CONCAT('전표 ', IFNULL(legacy_no, ''), ' — 거래처 없음 (구 customerid ', IFNULL(customerid, '-'), ') 또는 출하일 없음')
  FROM s_ship WHERE new_id IS NULL AND (customer_id IS NULL OR shipment_date IS NULL);
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'shipment', 't_outcomesum', lk, 'DUPLICATE', CONCAT('전표번호 "', IFNULL(legacy_no, ''), '" 중복/없음 → ', shipment_no)
  FROM s_ship WHERE new_id IS NULL AND shipment_no <> IFNULL(legacy_no, '');
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'shipment', 't_outcomesum', lk, 'VALUE_CHANGED', CONCAT('전표 ', shipment_no, ' — 마감월 없음 → 출하월로') FROM s_ship WHERE new_id IS NULL AND closing_month_guessed;

-- 마감 실행 기록 (업체 × 마감월, 구 마감완료 전표만)
CREATE TEMPORARY TABLE s_closing (PRIMARY KEY (lk)) AS
SELECT CONCAT(customerid, '|', DATE_FORMAT(closing_month_date, '%Y%m')) AS lk, MIN(customer_id) AS customer_id,
       CONCAT(@p_cl, DATE_FORMAT(closing_month_date, '%Y%m'), '-', customerid) AS closing_no,
       YEAR(closing_month_date) AS closing_year, MONTH(closing_month_date) AS closing_month,
       COALESCE(MAX(closing_due_date), LAST_DAY(MIN(closing_month_date))) AS closing_date,
       SUM(total_qty) AS total_qty, SUM(total_weight) AS total_weight, SUM(supply_amount) AS total_amount
  FROM s_ship
 WHERE customer_id IS NOT NULL AND shipment_date IS NOT NULL AND closing_status = 'CLOSED'
 GROUP BY customerid, DATE_FORMAT(closing_month_date, '%Y%m');
INSERT INTO shipment_closing (closing_no, customer_id, closing_date, closing_year, closing_month, total_qty, total_weight, total_amount,
                              closing_status, closed_at, remark)
SELECT c.closing_no, c.customer_id, c.closing_date, c.closing_year, c.closing_month, c.total_qty, c.total_weight, c.total_amount,
       'CLOSED', c.closing_date, '구 DB 이관 (마감완료 전표)'
  FROM s_closing c WHERE NOT EXISTS (SELECT 1 FROM shipment_closing x WHERE x.closing_no = c.closing_no);

INSERT INTO shipment (shipment_no, shipment_date, customer_id, shipment_closing_id, closing_status, closing_year, closing_month, closing_due_date,
                      print_sum_by_part, supply_amount, vat_amount, total_amount, status, customer_name_snapshot, customer_business_no_snapshot,
                      customer_ceo_name_snapshot, customer_business_type_snapshot, customer_business_item_snapshot, customer_address_snapshot,
                      customer_address_detail_snapshot, remark, created_at)
SELECT s.shipment_no, s.shipment_date, s.customer_id, sc.shipment_closing_id, s.closing_status,
       IF(s.closing_status = 'UNCLOSED', NULL, YEAR(s.closing_month_date)), IF(s.closing_status = 'UNCLOSED', NULL, MONTH(s.closing_month_date)),
       s.closing_due_date, s.print_sum_by_part, s.supply_amount, s.vat_amount, s.supply_amount + s.vat_amount, 'SHIPPED',
       COALESCE(s.customer_name_snapshot, c.customer_name), c.business_no, c.ceo_name, c.business_type, c.business_item, c.address, c.address_detail,
       s.remark, s.shipment_date
  FROM s_ship s
  JOIN customer c ON c.customer_id = s.customer_id
  LEFT JOIN shipment_closing sc ON s.closing_status = 'CLOSED'
   AND sc.closing_no = CONCAT(@p_cl, DATE_FORMAT(s.closing_month_date, '%Y%m'), '-', s.customerid)
 WHERE s.new_id IS NULL AND s.shipment_date IS NOT NULL;
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT 't_outcomesum', s.lk, 'shipment', n.shipment_id FROM s_ship s JOIN shipment n ON n.shipment_no = s.shipment_no
 WHERE s.new_id IS NULL AND s.customer_id IS NOT NULL AND s.shipment_date IS NOT NULL;

-- 이미 이관한 전표: 금액·마감 상태를 구 값으로 (출하·마감 쓰기 주체 = 구 시스템인 동안 — 구에서 상세 추가·마감하면 따라감)
UPDATE shipment n JOIN s_ship s ON s.new_id = n.shipment_id
  LEFT JOIN shipment_closing sc ON s.closing_status = 'CLOSED'
   AND sc.closing_no = CONCAT(@p_cl, DATE_FORMAT(s.closing_month_date, '%Y%m'), '-', s.customerid)
   SET n.supply_amount = s.supply_amount, n.vat_amount = s.vat_amount, n.total_amount = s.supply_amount + s.vat_amount,
       n.closing_due_date = s.closing_due_date, n.print_sum_by_part = s.print_sum_by_part, n.remark = s.remark,
       n.closing_status = s.closing_status, n.shipment_closing_id = sc.shipment_closing_id,
       n.closing_year = IF(s.closing_status = 'UNCLOSED', NULL, YEAR(s.closing_month_date)),
       n.closing_month = IF(s.closing_status = 'UNCLOSED', NULL, MONTH(s.closing_month_date));

-- 출하 상세 (전표번호로 연결, 행 번호 = 구 subno·outcomeid 순)
CREATE TEMPORARY TABLE s_ship_item (PRIMARY KEY (lk)) AS
SELECT CAST(o.outcomeid AS CHAR(200)) AS lk, o.outcomeid, TRIM(o.outcomeno) AS outcome_no, TRIM(o.incomeno) AS income_no, o.subno,
       (SELECT m.new_id FROM migration_id_map m JOIN bbakggum_mig.src_outcomesum x ON m.legacy_key = CAST(x.outcomesumid AS CHAR)
         WHERE m.legacy_table = 't_outcomesum' AND m.new_table = 'shipment' AND TRIM(x.outcomeno) = TRIM(o.outcomeno) ORDER BY x.outcomesumid LIMIT 1) AS shipment_id,
       (SELECT m.new_id FROM migration_id_map m JOIN bbakggum_legacy.t_income i ON m.legacy_key = CAST(i.incomeid AS CHAR)
         WHERE m.legacy_table = 't_income' AND m.new_table = 'sales_order_item' AND TRIM(i.incomeno) = TRIM(o.incomeno) ORDER BY i.incomeid LIMIT 1) AS sales_order_item_id,
       GREATEST(COALESCE(o.outqt, 0), 0) AS shipment_qty, GREATEST(COALESCE(o.testspecimen, 0), 0) AS test_specimen_qty, o.outweight AS shipment_weight,
       bbakggum_mig.price_basis(o.unit) AS price_basis, o.unitprice AS unit_price_snapshot, o.outamount AS amount,
       NULLIF(TRIM(o.customerlot), '') AS customer_lot_snapshot, NULLIF(TRIM(o.partname), '') AS part_name_snapshot,
       NULLIF(TRIM(o.partnumber), '') AS part_number_snapshot, NULLIF(TRIM(o.specification), '') AS specification_snapshot,
       NULLIF(TRIM(o.material), '') AS material_snapshot,
       (SELECT m.new_id FROM migration_id_map m WHERE m.legacy_table = 't_outcome' AND m.legacy_key = CAST(o.outcomeid AS CHAR) AND m.new_table = 'shipment_item') AS new_id
  FROM bbakggum_legacy.t_outcome o;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'shipment', 't_outcome', lk,
       'SKIPPED', CONCAT_WS(' / ', IF(shipment_id IS NULL, CONCAT('전표 "', IFNULL(outcome_no, ''), '" 없음'), NULL),
                                   IF(sales_order_item_id IS NULL, CONCAT('입고번호 "', IFNULL(income_no, ''), '" 없음'), NULL))
  FROM s_ship_item WHERE new_id IS NULL AND (shipment_id IS NULL OR sales_order_item_id IS NULL);
CREATE TEMPORARY TABLE s_line_max (PRIMARY KEY (shipment_id)) AS
SELECT shipment_id, MAX(line_no) AS max_line FROM shipment_item GROUP BY shipment_id;
INSERT INTO shipment_item (shipment_id, line_no, sales_order_item_id, shipment_qty, test_specimen_qty, shipment_weight, price_basis_snapshot,
                           unit_price_snapshot, amount, customer_lot_snapshot, part_name_snapshot, part_number_snapshot, specification_snapshot, material_snapshot)
SELECT s.shipment_id, COALESCE(lm.max_line, 0) + ROW_NUMBER() OVER (PARTITION BY s.shipment_id ORDER BY s.subno, s.outcomeid), s.sales_order_item_id,
       s.shipment_qty, s.test_specimen_qty, s.shipment_weight, COALESCE(s.price_basis, soi.price_basis), s.unit_price_snapshot, s.amount,
       s.customer_lot_snapshot, s.part_name_snapshot, s.part_number_snapshot, s.specification_snapshot, s.material_snapshot
  FROM s_ship_item s
  JOIN sales_order_item soi ON soi.sales_order_item_id = s.sales_order_item_id
  LEFT JOIN s_line_max lm ON lm.shipment_id = s.shipment_id
 WHERE s.new_id IS NULL AND s.shipment_id IS NOT NULL;
-- 매핑: 이번에 넣은 행 = 전표별 기존 마지막 행 다음부터 같은 순서
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT 't_outcome', x.lk, 'shipment_item', si.shipment_item_id
  FROM (SELECT s.lk, s.shipment_id, COALESCE(lm.max_line, 0) + ROW_NUMBER() OVER (PARTITION BY s.shipment_id ORDER BY s.subno, s.outcomeid) AS line_no
          FROM s_ship_item s LEFT JOIN s_line_max lm ON lm.shipment_id = s.shipment_id
         WHERE s.new_id IS NULL AND s.shipment_id IS NOT NULL AND s.sales_order_item_id IS NOT NULL) x
  JOIN shipment_item si ON si.shipment_id = x.shipment_id AND si.line_no = x.line_no;

-- 이관 마감의 합계 = 연결된 전표 합계 (재실행으로 같은 업체·마감월 전표가 늘어도 맞게)
UPDATE shipment_closing x
   SET x.total_qty    = (SELECT COALESCE(SUM(si.shipment_qty), 0) FROM shipment s JOIN shipment_item si USING (shipment_id) WHERE s.shipment_closing_id = x.shipment_closing_id),
       x.total_weight = (SELECT COALESCE(SUM(si.shipment_weight), 0) FROM shipment s JOIN shipment_item si USING (shipment_id) WHERE s.shipment_closing_id = x.shipment_closing_id),
       x.total_amount = (SELECT COALESCE(SUM(s.supply_amount), 0) FROM shipment s WHERE s.shipment_closing_id = x.shipment_closing_id)
 WHERE x.closing_no LIKE CONCAT(@p_cl, '%');

-- ---------------------------------------------------------------------
-- 설비 비가동 (t_downtime — 설비를 이름으로 참조, 종료가 시작보다 앞이면 다음날 종료)
-- ---------------------------------------------------------------------
CREATE TEMPORARY TABLE s_downtime (PRIMARY KEY (lk)) AS
SELECT CAST(d.id AS CHAR(200)) AS lk, e.equipment_id, d.downdate,
       TIMESTAMP(d.downdate, d.start_time) AS started_at,
       IF(d.end_time IS NULL, NULL, IF(d.end_time < d.start_time, TIMESTAMP(d.downdate + INTERVAL 1 DAY, d.end_time), TIMESTAMP(d.downdate, d.end_time))) AS ended_at,
       d.duration AS duration_min, LEFT(NULLIF(TRIM(d.reason_code), ''), 50) AS reason_code,
       LEFT(NULLIF(CONCAT_WS(' / ', NULLIF(TRIM(d.memo), ''), IF(NULLIF(TRIM(d.worker), '') IS NULL, NULL, CONCAT('작업자 ', TRIM(d.worker)))), ''), 500) AS remark,
       TRIM(d.equipname) AS equip_name,
       (SELECT m.new_id FROM migration_id_map m WHERE m.legacy_table = 't_downtime' AND m.legacy_key = CAST(d.id AS CHAR) AND m.new_table = 'equipment_downtime') AS new_id
  FROM bbakggum_legacy.t_downtime d
  LEFT JOIN bbakggum_mig.equipment_by_name e ON e.equipment_name = TRIM(d.equipname);
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'shipment', 't_downtime', lk, 'SKIPPED', CONCAT('설비 "', IFNULL(equip_name, ''), '" 없음') FROM s_downtime WHERE new_id IS NULL AND (equipment_id IS NULL OR downdate IS NULL);
INSERT INTO equipment_downtime (equipment_id, downtime_date, started_at, ended_at, duration_min, reason_code, remark)
SELECT equipment_id, downdate, started_at, ended_at, duration_min, reason_code, CONCAT('#mig:', lk)
  FROM s_downtime WHERE new_id IS NULL AND equipment_id IS NOT NULL AND downdate IS NOT NULL;
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT 't_downtime', s.lk, 'equipment_downtime', n.equipment_downtime_id
  FROM s_downtime s JOIN equipment_downtime n ON n.remark = CONCAT('#mig:', s.lk) WHERE s.new_id IS NULL;
UPDATE equipment_downtime n JOIN migration_id_map m ON m.new_table = 'equipment_downtime' AND m.new_id = n.equipment_downtime_id AND m.legacy_table = 't_downtime'
  JOIN s_downtime s ON s.lk = m.legacy_key AND s.new_id IS NULL
   SET n.remark = s.remark;

-- ---------------------------------------------------------------------
-- 설비 보전 (t_maintenance — 설비 대상만. 측정기구 점검 기록(instrumentid)은 신규 maintenance 대상이 아님)
-- ---------------------------------------------------------------------
CREATE TEMPORARY TABLE s_maint (PRIMARY KEY (lk)) AS
SELECT CAST(t.id AS CHAR(200)) AS lk, bbakggum_mig.new_id('t_equipment', CAST(t.equipmentid AS CHAR), 'equipment') AS equipment_id,
       LEFT(COALESCE(NULLIF(TRIM(t.repairtype), ''), NULLIF(TRIM(t.recordtype), ''), '수리'), 50) AS maintenance_type, t.repairdate,
       NULLIF(CONCAT_WS(' / ', NULLIF(TRIM(t.repairpart), ''), NULLIF(TRIM(t.repairmemo), ''),
                               IF(NULLIF(TRIM(t.worker), '') IS NULL, NULL, CONCAT('작업자 ', TRIM(t.worker)))), '') AS description,
       NULLIF(CONCAT_WS(' / ', IF(t.repaircost IS NULL, NULL, CONCAT('비용 ', t.repaircost)),
                               IF(t.nextrepairdate IS NULL, NULL, CONCAT('다음 점검 ', t.nextrepairdate))), '') AS result,
       t.instrumentid,
       (SELECT m.new_id FROM migration_id_map m WHERE m.legacy_table = 't_maintenance' AND m.legacy_key = CAST(t.id AS CHAR) AND m.new_table = 'maintenance') AS new_id
  FROM bbakggum_legacy.t_maintenance t;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'shipment', 't_maintenance', lk, 'SKIPPED', IF(COALESCE(instrumentid, 0) <> 0, '측정기구 점검 기록 — 설비 보전 아님', '설비 없음')
  FROM s_maint WHERE new_id IS NULL AND (equipment_id IS NULL OR repairdate IS NULL);
INSERT INTO maintenance (equipment_id, maintenance_type, maintenance_date, description, result, status)
SELECT equipment_id, maintenance_type, repairdate, CONCAT('#mig:', lk), result, 'COMPLETED'
  FROM s_maint WHERE new_id IS NULL AND equipment_id IS NOT NULL AND repairdate IS NOT NULL;
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT 't_maintenance', s.lk, 'maintenance', n.maintenance_id
  FROM s_maint s JOIN maintenance n ON n.description = CONCAT('#mig:', s.lk) WHERE s.new_id IS NULL;
UPDATE maintenance n JOIN migration_id_map m ON m.new_table = 'maintenance' AND m.new_id = n.maintenance_id AND m.legacy_table = 't_maintenance'
  JOIN s_maint s ON s.lk = m.legacy_key AND s.new_id IS NULL
   SET n.description = s.description;

COMMIT;
