-- =====================================================================
-- ⑥ 수주 (t_income 1행 = sales_order_item 1행, 같은 업체·같은 입고일 = sales_order 1건) (설계 §10 ⑥, §23.2, §25)
-- - 입고번호(incomeno) = 수주번호(order_item_no) 그대로 — 현장 바코드·구 출력물과 같은 번호
-- - 잔량(inputremainqt·planremainqt·outremainqt)·금액(amount)은 저장하지 않음 (VIEW 계산)
-- - 거래 데이터는 추가만 (이미 이관한 행은 다시 쓰지 않음 — 전환 시점에는 -Fresh 로 전체 이관)
-- =====================================================================
USE bbakggum_v2;
SET collation_connection = @@collation_database;
START TRANSACTION;

SET @p_so = bbakggum_mig.cfg('sales_order_no_prefix');

CREATE TEMPORARY TABLE s_income (PRIMARY KEY (lk), KEY (order_item_no), KEY (group_key)) AS
SELECT CAST(i.incomeid AS CHAR(200)) AS lk, i.incomeid, m.new_id, i.indate, i.insubno,
       CONCAT(i.customerid, '|', i.indate) AS group_key,
       CONCAT(@p_so, DATE_FORMAT(i.indate, '%y%m%d'), '-', i.customerid) AS sales_order_no,
       -- 같은 입고번호가 구 DB 안에서 두 번 이상이면 두 번째부터 접두 + incomeid
       IF(NULLIF(TRIM(i.incomeno), '') IS NOT NULL
          AND i.incomeid = (SELECT MIN(x.incomeid) FROM bbakggum_legacy.t_income x WHERE x.incomeno = i.incomeno),
          TRIM(i.incomeno), CONCAT(@p_so, 'I', i.incomeid)) AS order_item_no, i.incomeno AS legacy_no,
       bbakggum_mig.new_id('t_customer', CAST(i.customerid AS CHAR), 'customer') AS customer_id,
       bbakggum_mig.new_id('t_part', CAST(i.partid AS CHAR), 'part') AS part_id, i.customerid, i.partid,
       (SELECT v.heat_process_version_id FROM heat_process_version v
         WHERE v.heat_process_id = bbakggum_mig.new_id('t_heatprocess', CAST(i.heatprocessid AS CHAR), 'heat_process') AND v.is_current = 1 LIMIT 1) AS heat_process_version_id,
       NULLIF(TRIM(i.customerlot), '') AS customer_lot, NULLIF(TRIM(i.coilno), '') AS coil_no,
       GREATEST(COALESCE(i.incomeqt, 0), 0) AS order_qty, i.weight AS order_weight, i.unitweight AS unit_weight, i.unitprice AS unit_price,
       NULLIF(TRIM(i.unit), '') AS unit_code,
       COALESCE(bbakggum_mig.price_basis(i.unit), (SELECT p.price_basis FROM part p WHERE p.part_id = bbakggum_mig.new_id('t_part', CAST(i.partid AS CHAR), 'part')), 'EA') AS price_basis,
       LEAST(GREATEST(COALESCE(i.priority, 1), 0), 3) AS priority, NULLIF(TRIM(i.workorderno), '') AS customer_work_order_no,
       IF(TRIM(i.grade) = '별도관리', 1, 0) AS is_separately_managed, IF(i.isreturn = 1, 1, 0) AS is_return, IF(i.isrework = 1, 1, 0) AS is_rework,
       NULLIF(TRIM(i.partname), '') AS part_name_snapshot, NULLIF(TRIM(i.partnumber), '') AS part_number_snapshot,
       NULLIF(TRIM(i.specification), '') AS specification_snapshot, NULLIF(TRIM(i.model), '') AS model_snapshot,
       NULLIF(TRIM(i.material), '') AS material_snapshot, NULLIF(TRIM(i.heatprocessname), '') AS heat_process_name_snapshot,
       NULLIF(TRIM(i.hardness), '') AS hardness_snapshot, NULLIF(TRIM(i.corehard), '') AS core_hardness_snapshot,
       NULLIF(TRIM(i.hardendepth), '') AS case_depth_snapshot, NULLIF(TRIM(i.texture), '') AS texture_snapshot,
       NULLIF(TRIM(i.remark), '') AS remark
  FROM bbakggum_legacy.t_income i
  LEFT JOIN migration_id_map m ON m.legacy_table = 't_income' AND m.legacy_key = CAST(i.incomeid AS CHAR) AND m.new_table = 'sales_order_item';

INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'order', 't_income', lk, 'SKIPPED',
       CONCAT_WS(' / ', IF(customer_id IS NULL, CONCAT('거래처 없음 (구 customerid ', IFNULL(customerid, '-'), ')'), NULL),
                        IF(part_id IS NULL, CONCAT('품목 없음 (구 partid ', IFNULL(partid, '-'), ')'), NULL),
                        IF(indate IS NULL, '입고일 없음', NULL))
  FROM s_income WHERE new_id IS NULL AND (customer_id IS NULL OR part_id IS NULL OR indate IS NULL);
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'order', 't_income', lk, 'DUPLICATE', CONCAT('입고번호 "', IFNULL(legacy_no, ''), '" 중복/없음 → ', order_item_no)
  FROM s_income WHERE new_id IS NULL AND order_item_no <> IFNULL(TRIM(legacy_no), '');

-- 수주 헤더 (업체 × 입고일)
INSERT INTO sales_order (sales_order_no, order_date, received_date, customer_id, status, created_at)
SELECT s.sales_order_no, MIN(s.indate), MIN(s.indate), MIN(s.customer_id), 'OPEN', MIN(s.indate)
  FROM s_income s
 WHERE s.new_id IS NULL AND s.customer_id IS NOT NULL AND s.part_id IS NOT NULL AND s.indate IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM migration_id_map m WHERE m.legacy_table = 't_income.group' AND m.legacy_key = s.group_key AND m.new_table = 'sales_order')
 GROUP BY s.group_key, s.sales_order_no;
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT DISTINCT 't_income.group', s.group_key, 'sales_order', o.sales_order_id
  FROM s_income s JOIN sales_order o ON o.sales_order_no = s.sales_order_no
 WHERE s.new_id IS NULL AND s.customer_id IS NOT NULL AND s.part_id IS NOT NULL AND s.indate IS NOT NULL
   AND NOT EXISTS (SELECT 1 FROM migration_id_map m WHERE m.legacy_table = 't_income.group' AND m.legacy_key = s.group_key AND m.new_table = 'sales_order');

-- 품목 행 (행 번호 = 그 수주의 마지막 행 다음부터, 구 insubno·incomeid 순)
CREATE TEMPORARY TABLE s_line_max (PRIMARY KEY (sales_order_id)) AS
SELECT sales_order_id, MAX(line_no) AS max_line FROM sales_order_item GROUP BY sales_order_id;
INSERT INTO sales_order_item (order_item_no, sales_order_id, line_no, part_id, heat_process_version_id, customer_lot, coil_no, order_qty, order_weight,
                              unit_weight, unit_price, unit_code, price_basis, priority, customer_work_order_no, is_separately_managed, is_return, is_rework,
                              status, part_name_snapshot, part_number_snapshot, specification_snapshot, model_snapshot, material_snapshot,
                              heat_process_name_snapshot, hardness_snapshot, core_hardness_snapshot, case_depth_snapshot, texture_snapshot, remark, created_at)
SELECT s.order_item_no, g.new_id, COALESCE(lm.max_line, 0) + ROW_NUMBER() OVER (PARTITION BY g.new_id ORDER BY s.insubno, s.incomeid),
       s.part_id, s.heat_process_version_id, s.customer_lot, s.coil_no, s.order_qty, s.order_weight, s.unit_weight, s.unit_price, s.unit_code,
       s.price_basis, s.priority, s.customer_work_order_no, s.is_separately_managed, s.is_return, s.is_rework, 'OPEN',
       s.part_name_snapshot, s.part_number_snapshot, s.specification_snapshot, s.model_snapshot, s.material_snapshot, s.heat_process_name_snapshot,
       s.hardness_snapshot, s.core_hardness_snapshot, s.case_depth_snapshot, s.texture_snapshot, s.remark, s.indate
  FROM s_income s
  JOIN migration_id_map g ON g.legacy_table = 't_income.group' AND g.legacy_key = s.group_key AND g.new_table = 'sales_order'
  LEFT JOIN s_line_max lm ON lm.sales_order_id = g.new_id
 WHERE s.new_id IS NULL AND s.customer_id IS NOT NULL AND s.part_id IS NOT NULL;
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT 't_income', s.lk, 'sales_order_item', n.sales_order_item_id
  FROM s_income s JOIN sales_order_item n ON n.order_item_no = s.order_item_no
 WHERE s.new_id IS NULL AND s.customer_id IS NOT NULL AND s.part_id IS NOT NULL AND s.indate IS NOT NULL;

COMMIT;
