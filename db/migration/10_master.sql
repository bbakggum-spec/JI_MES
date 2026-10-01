-- =====================================================================
-- ③ 기준정보: 자사, 공통코드(비가동 사유), 거래처, 설비유형·설비, 단위공정, 공정(+경로), 품목(+거래처·공정),
--            부서·직위·사원, 측정기구, 기준 작업시간, 공정검사 항목 (설계 §10 ③, §25)
-- 규칙: 처음 실행 = 추가 + migration_id_map 기록 / 재실행 = 매핑된 행을 구 값으로 갱신 (병행운영 중 기준정보 쓰기 주체 = 구 시스템)
-- 패턴: 임시 표(s_*)에 변환 결과 + 기존 new_id → new_id 없는 행 INSERT → 코드로 매핑 기록 → 매핑된 행 UPDATE
-- =====================================================================
USE bbakggum_v2;
SET collation_connection = @@collation_database;
START TRANSACTION;

SET @p_customer = bbakggum_mig.cfg('customer_code_prefix');
SET @p_part     = bbakggum_mig.cfg('part_code_prefix');
SET @p_hp       = bbakggum_mig.cfg('heat_process_code_prefix');
SET @p_up       = bbakggum_mig.cfg('unit_process_code_prefix');
SET @p_emp      = bbakggum_mig.cfg('employee_code_prefix');
SET @p_ins      = bbakggum_mig.cfg('instrument_code_prefix');
SET @p_eq       = bbakggum_mig.cfg('equipment_code_fallback');
SET @p_li       = bbakggum_mig.cfg('unit_inspection_item_code_prefix');

-- ---------------------------------------------------------------------
-- 자사 (t_company 첫 행 → company 1행)
-- ---------------------------------------------------------------------
CREATE TEMPORARY TABLE s_company (PRIMARY KEY (lk)) AS
SELECT CAST(c.companyid AS CHAR(200)) AS lk, m.new_id,
       COALESCE(NULLIF(TRIM(c.companyname), ''), '(회사명 없음)') AS company_name, NULLIF(TRIM(c.ceoname), '') AS ceo_name,
       NULLIF(TRIM(c.businessno), '') AS business_no, NULLIF(TRIM(c.businesstype), '') AS business_type, NULLIF(TRIM(c.businessitem), '') AS business_item,
       NULLIF(TRIM(c.phone), '') AS phone, NULLIF(TRIM(c.fax), '') AS fax, NULLIF(TRIM(c.email), '') AS email,
       NULLIF(TRIM(c.address), '') AS address, NULLIF(TRIM(c.addressdetail), '') AS address_detail
  FROM bbakggum_legacy.t_company c
  LEFT JOIN migration_id_map m ON m.legacy_table = 't_company' AND m.legacy_key = CAST(c.companyid AS CHAR) AND m.new_table = 'company'
 WHERE c.companyid = (SELECT MIN(companyid) FROM bbakggum_legacy.t_company);

INSERT INTO company (company_name, ceo_name, business_no, business_type, business_item, phone, fax, email, address, address_detail)
SELECT company_name, ceo_name, business_no, business_type, business_item, phone, fax, email, address, address_detail FROM s_company WHERE new_id IS NULL;
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT 't_company', s.lk, 'company', (SELECT MIN(n.company_id) FROM company n WHERE n.company_name = s.company_name)
  FROM s_company s WHERE s.new_id IS NULL;
UPDATE company n JOIN s_company s ON s.new_id = n.company_id
   SET n.company_name = s.company_name, n.ceo_name = s.ceo_name, n.business_no = s.business_no, n.business_type = s.business_type,
       n.business_item = s.business_item, n.phone = s.phone, n.fax = s.fax, n.email = s.email, n.address = s.address, n.address_detail = s.address_detail;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'master', 't_company', companyid, 'SKIPPED', '자사는 첫 행만 이관' FROM bbakggum_legacy.t_company WHERE companyid > (SELECT MIN(companyid) FROM bbakggum_legacy.t_company);
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'master', 't_company', companyid, 'FILE_PATH', CONCAT('도장 이미지 경로는 이관 안 함 (PC 경로) — 자사 정보 화면에서 다시 등록: ', stamppath)
  FROM bbakggum_legacy.t_company WHERE NULLIF(TRIM(stamppath), '') IS NOT NULL;

-- ---------------------------------------------------------------------
-- 공통코드 (t_combolist: comboname → 설정 combo_group_{이름} 의 그룹, 코드 = 항목 문자열)
-- ---------------------------------------------------------------------
INSERT INTO common_code (common_code_group_id, code, code_name, sort_order)
SELECT g.common_code_group_id, LEFT(TRIM(c.comboitem), 50), LEFT(TRIM(c.comboitem), 100), c.id
  FROM bbakggum_legacy.t_combolist c
  JOIN common_code_group g ON g.group_code = bbakggum_mig.cfg(CONCAT('combo_group_', TRIM(c.comboname)))
 WHERE NULLIF(TRIM(c.comboitem), '') IS NOT NULL
ON DUPLICATE KEY UPDATE code_name = VALUES(code_name);
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'master', 't_combolist', c.id, 'SKIPPED', CONCAT('대응 공통코드 그룹 설정 없음 (00_config combo_group_', c.comboname, '): ', c.comboitem)
  FROM bbakggum_legacy.t_combolist c
 WHERE NOT EXISTS (SELECT 1 FROM common_code_group g WHERE g.group_code = bbakggum_mig.cfg(CONCAT('combo_group_', TRIM(c.comboname))));

-- ---------------------------------------------------------------------
-- 거래처 (t_customer) — 코드 = 접두 + customerid
-- ---------------------------------------------------------------------
CREATE TEMPORARY TABLE s_customer (PRIMARY KEY (lk), KEY (customer_code)) AS
SELECT CAST(c.customerid AS CHAR(200)) AS lk, m.new_id,
       bbakggum_mig.code(@p_customer, c.customerid) AS customer_code,
       COALESCE(bbakggum_mig.code_by_name('CUSTOMER_TYPE', c.customertype), bbakggum_mig.cfg('customer_type_unmatched')) AS customer_type,
       bbakggum_mig.code_by_name('CUSTOMER_TYPE', c.customertype) IS NULL AS type_unmatched, c.customertype AS legacy_type,
       COALESCE(NULLIF(TRIM(c.customername), ''), CONCAT('(이름 없음 ', c.customerid, ')')) AS customer_name,
       NULLIF(TRIM(c.businessno), '') AS business_no, NULLIF(TRIM(c.ceoname), '') AS ceo_name,
       NULLIF(TRIM(c.businesstype), '') AS business_type, NULLIF(TRIM(c.businessitem), '') AS business_item,
       NULLIF(TRIM(c.phone), '') AS phone, NULLIF(TRIM(c.fax), '') AS fax, NULLIF(TRIM(c.email), '') AS email,
       NULLIF(TRIM(c.address), '') AS address, NULLIF(TRIM(c.addressdetail), '') AS address_detail,
       CASE WHEN TRIM(c.closingday) REGEXP '^[0-9]{1,2}$' AND CAST(TRIM(c.closingday) AS UNSIGNED) BETWEEN 1 AND 31 THEN CAST(TRIM(c.closingday) AS UNSIGNED)
            WHEN TRIM(c.closingday) = '말일' THEN 31 END AS closing_day,
       c.closingday AS legacy_closing_day,
       NULLIF(TRIM(c.remark), '') AS remark,
       IF(TRIM(c.isuse) IN ('0', 'N', 'n', 'false', '미사용'), 0, 1) AS is_active,
       COALESCE(bbakggum_mig.dt(c.createdat), NOW()) AS created_at
  FROM bbakggum_legacy.t_customer c
  LEFT JOIN migration_id_map m ON m.legacy_table = 't_customer' AND m.legacy_key = CAST(c.customerid AS CHAR) AND m.new_table = 'customer';

INSERT INTO customer (customer_code, customer_type, customer_name, business_no, ceo_name, business_type, business_item, phone, fax, email,
                      address, address_detail, closing_day, remark, is_active, created_at)
SELECT customer_code, customer_type, customer_name, business_no, ceo_name, business_type, business_item, phone, fax, email,
       address, address_detail, closing_day, remark, is_active, created_at
  FROM s_customer WHERE new_id IS NULL;
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT 't_customer', s.lk, 'customer', n.customer_id FROM s_customer s JOIN customer n ON n.customer_code = s.customer_code WHERE s.new_id IS NULL;
UPDATE customer n JOIN s_customer s ON s.new_id = n.customer_id
   SET n.customer_type = s.customer_type, n.customer_name = s.customer_name, n.business_no = s.business_no, n.ceo_name = s.ceo_name,
       n.business_type = s.business_type, n.business_item = s.business_item, n.phone = s.phone, n.fax = s.fax, n.email = s.email,
       n.address = s.address, n.address_detail = s.address_detail, n.closing_day = s.closing_day, n.remark = s.remark, n.is_active = s.is_active;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'master', 't_customer', lk, 'CODE_UNMATCHED', CONCAT('거래처 구분 "', IFNULL(legacy_type, ''), '" → ', customer_type) FROM s_customer WHERE type_unmatched;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'master', 't_customer', lk, 'VALUE_DROPPED', CONCAT('마감일 해석 불가: ', legacy_closing_day) FROM s_customer
 WHERE closing_day IS NULL AND NULLIF(TRIM(legacy_closing_day), '') IS NOT NULL;

-- ---------------------------------------------------------------------
-- 설비유형 (구는 문자열 — 설비·작업표준·템플릿·기준시간에 쓰인 이름 전부, 코드 = 이름)
-- ---------------------------------------------------------------------
CREATE TEMPORARY TABLE s_equipment_type (PRIMARY KEY (lk)) AS
SELECT t.name AS lk, m.new_id
  FROM (SELECT TRIM(equipmenttype) AS name FROM bbakggum_legacy.t_equipment
        UNION SELECT TRIM(equipmenttype) FROM bbakggum_legacy.t_standard
        UNION SELECT TRIM(equipmenttype) FROM bbakggum_legacy.t_standardtemplate
        UNION SELECT TRIM(equipmenttype) FROM bbakggum_legacy.t_process_default_time) t
  LEFT JOIN migration_id_map m ON m.legacy_table = 'equipmenttype' AND m.legacy_key = t.name AND m.new_table = 'equipment_type'
 WHERE NULLIF(t.name, '') IS NOT NULL;
INSERT INTO equipment_type (equipment_type_code, equipment_type_name)
SELECT LEFT(lk, 30), LEFT(lk, 50) FROM s_equipment_type WHERE new_id IS NULL;
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT 'equipmenttype', s.lk, 'equipment_type', n.equipment_type_id FROM s_equipment_type s JOIN equipment_type n ON n.equipment_type_code = LEFT(s.lk, 30) WHERE s.new_id IS NULL;

-- ---------------------------------------------------------------------
-- 설비 (t_equipment) — 코드 = 이니셜 (비었거나 구 DB 안에서 중복이면 접두 + equipmentid)
-- ---------------------------------------------------------------------
CREATE TEMPORARY TABLE s_equipment (PRIMARY KEY (lk), KEY (equipment_code)) AS
SELECT CAST(e.equipmentid AS CHAR(200)) AS lk, m.new_id,
       IF(NULLIF(TRIM(e.equipmentinitial), '') IS NOT NULL
          AND (SELECT COUNT(*) FROM bbakggum_legacy.t_equipment x WHERE TRIM(x.equipmentinitial) = TRIM(e.equipmentinitial)) = 1,
          TRIM(e.equipmentinitial), bbakggum_mig.code(@p_eq, e.equipmentid)) AS equipment_code,
       bbakggum_mig.new_id('equipmenttype', TRIM(e.equipmenttype), 'equipment_type') AS equipment_type_id,
       e.equipmentno AS equipment_no, NULLIF(TRIM(e.equipmentinitial), '') AS equipment_initial,
       COALESCE(NULLIF(TRIM(e.equipmentname), ''), CONCAT('(설비 ', e.equipmentid, ')')) AS equipment_name,
       e.installdate AS install_date, e.equipmentid AS sort_order, IF(e.isuse = 0, 0, 1) AS is_active, NULLIF(TRIM(e.imagepath), '') AS image_path
  FROM bbakggum_legacy.t_equipment e
  LEFT JOIN migration_id_map m ON m.legacy_table = 't_equipment' AND m.legacy_key = CAST(e.equipmentid AS CHAR) AND m.new_table = 'equipment';
INSERT INTO equipment (equipment_type_id, equipment_code, equipment_no, equipment_initial, equipment_name, install_date, sort_order, is_active)
SELECT equipment_type_id, equipment_code, equipment_no, equipment_initial, equipment_name, install_date, sort_order, is_active FROM s_equipment WHERE new_id IS NULL;
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT 't_equipment', s.lk, 'equipment', n.equipment_id FROM s_equipment s JOIN equipment n ON n.equipment_code = s.equipment_code WHERE s.new_id IS NULL;
UPDATE equipment n JOIN s_equipment s ON s.new_id = n.equipment_id
   SET n.equipment_type_id = s.equipment_type_id, n.equipment_no = s.equipment_no, n.equipment_initial = s.equipment_initial,
       n.equipment_name = s.equipment_name, n.install_date = s.install_date, n.is_active = s.is_active;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'master', 't_equipment', lk, 'FILE_PATH', CONCAT('설비 이미지 경로는 이관 안 함 (PC 경로): ', image_path) FROM s_equipment WHERE image_path IS NOT NULL;

-- ---------------------------------------------------------------------
-- 단위공정 (t_unitprocess)
-- ---------------------------------------------------------------------
CREATE TEMPORARY TABLE s_unit_process (PRIMARY KEY (lk), KEY (unit_process_code)) AS
SELECT CAST(u.unitprocessid AS CHAR(200)) AS lk, m.new_id, bbakggum_mig.code(@p_up, u.unitprocessid) AS unit_process_code,
       COALESCE(NULLIF(TRIM(u.unitprocessname), ''), CONCAT('(단위공정 ', u.unitprocessid, ')')) AS unit_process_name, u.unitprocessid AS sort_order
  FROM bbakggum_legacy.t_unitprocess u
  LEFT JOIN migration_id_map m ON m.legacy_table = 't_unitprocess' AND m.legacy_key = CAST(u.unitprocessid AS CHAR) AND m.new_table = 'unit_process';
INSERT INTO unit_process (unit_process_code, unit_process_name, sort_order)
SELECT unit_process_code, unit_process_name, sort_order FROM s_unit_process WHERE new_id IS NULL;
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT 't_unitprocess', s.lk, 'unit_process', n.unit_process_id FROM s_unit_process s JOIN unit_process n ON n.unit_process_code = s.unit_process_code WHERE s.new_id IS NULL;
UPDATE unit_process n JOIN s_unit_process s ON s.new_id = n.unit_process_id SET n.unit_process_name = s.unit_process_name;

-- 구 데이터는 단위공정·설비를 이름으로 참조 → 이름 → 신규 id (같은 이름이 여럿이면 가장 작은 구 id). 표는 01_prepare 에서 만듦
DELETE FROM bbakggum_mig.unit_process_by_name;
INSERT INTO bbakggum_mig.unit_process_by_name (unit_process_name, unit_process_id)
SELECT TRIM(u.unitprocessname), bbakggum_mig.new_id('t_unitprocess', CAST(MIN(u.unitprocessid) AS CHAR), 'unit_process')
  FROM bbakggum_legacy.t_unitprocess u WHERE NULLIF(TRIM(u.unitprocessname), '') IS NOT NULL GROUP BY TRIM(u.unitprocessname);
DELETE FROM bbakggum_mig.equipment_by_name;
INSERT INTO bbakggum_mig.equipment_by_name (equipment_name, equipment_id)
SELECT TRIM(e.equipmentname), bbakggum_mig.new_id('t_equipment', CAST(MIN(e.equipmentid) AS CHAR), 'equipment')
  FROM bbakggum_legacy.t_equipment e WHERE NULLIF(TRIM(e.equipmentname), '') IS NOT NULL GROUP BY TRIM(e.equipmentname);

-- ---------------------------------------------------------------------
-- 공정 (t_heatprocess) + 경로 (subp1~16 → heat_process_version 1 + operation, 이름이 단위공정과 맞는 것만)
-- ---------------------------------------------------------------------
CREATE TEMPORARY TABLE s_heat_process (PRIMARY KEY (lk), KEY (heat_process_code)) AS
SELECT CAST(h.heatprocessid AS CHAR(200)) AS lk, m.new_id, bbakggum_mig.code(@p_hp, h.heatprocessid) AS heat_process_code,
       COALESCE(NULLIF(TRIM(h.heatprocessname), ''), CONCAT('(공정 ', h.heatprocessid, ')')) AS heat_process_name
  FROM bbakggum_legacy.t_heatprocess h
  LEFT JOIN migration_id_map m ON m.legacy_table = 't_heatprocess' AND m.legacy_key = CAST(h.heatprocessid AS CHAR) AND m.new_table = 'heat_process';
INSERT INTO heat_process (heat_process_code, heat_process_name)
SELECT heat_process_code, heat_process_name FROM s_heat_process WHERE new_id IS NULL;
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT 't_heatprocess', s.lk, 'heat_process', n.heat_process_id FROM s_heat_process s JOIN heat_process n ON n.heat_process_code = s.heat_process_code WHERE s.new_id IS NULL;
UPDATE heat_process n JOIN s_heat_process s ON s.new_id = n.heat_process_id SET n.heat_process_name = s.heat_process_name;

CREATE TEMPORARY TABLE s_route AS
SELECT h.heatprocessid, q.n AS sequence_no,
       TRIM(ELT(q.n, h.subp1, h.subp2, h.subp3, h.subp4, h.subp5, h.subp6, h.subp7, h.subp8,
                     h.subp9, h.subp10, h.subp11, h.subp12, h.subp13, h.subp14, h.subp15, h.subp16)) AS step_name
  FROM bbakggum_legacy.t_heatprocess h CROSS JOIN bbakggum_mig.seq q
 WHERE NULLIF(TRIM(ELT(q.n, h.subp1, h.subp2, h.subp3, h.subp4, h.subp5, h.subp6, h.subp7, h.subp8,
                            h.subp9, h.subp10, h.subp11, h.subp12, h.subp13, h.subp14, h.subp15, h.subp16)), '') IS NOT NULL;
-- 경로는 처음 한 번만 (이후 경로 변경은 신규 시스템의 새 Version 으로)
INSERT INTO heat_process_version (heat_process_id, version_no, effective_from, is_current, remark)
SELECT DISTINCT bbakggum_mig.new_id('t_heatprocess', CAST(r.heatprocessid AS CHAR), 'heat_process'), 1, '2000-01-01', 1, '구 DB 이관 (t_heatprocess.subp)'
  FROM s_route r JOIN bbakggum_mig.unit_process_by_name u ON u.unit_process_name = r.step_name
 WHERE NOT EXISTS (SELECT 1 FROM heat_process_version v WHERE v.heat_process_id = bbakggum_mig.new_id('t_heatprocess', CAST(r.heatprocessid AS CHAR), 'heat_process'));
-- 버전당 주공정 1개: 경로 안의 주공정 단위공정 중 첫 번째
CREATE TEMPORARY TABLE s_route_main (PRIMARY KEY (heatprocessid)) AS
SELECT r.heatprocessid, MIN(r.sequence_no) AS main_seq
  FROM s_route r JOIN bbakggum_mig.main_unit_process mu ON mu.unit_process_name = r.step_name GROUP BY r.heatprocessid;
INSERT INTO heat_process_operation (heat_process_version_id, sequence_no, unit_process_id, is_main_process)
SELECT v.heat_process_version_id, ROW_NUMBER() OVER (PARTITION BY r.heatprocessid ORDER BY r.sequence_no), u.unit_process_id,
       IF(rm.main_seq = r.sequence_no, 1, 0)
  FROM s_route r
  JOIN bbakggum_mig.unit_process_by_name u ON u.unit_process_name = r.step_name
  LEFT JOIN s_route_main rm ON rm.heatprocessid = r.heatprocessid
  JOIN heat_process_version v ON v.heat_process_id = bbakggum_mig.new_id('t_heatprocess', CAST(r.heatprocessid AS CHAR), 'heat_process') AND v.version_no = 1
 WHERE NOT EXISTS (SELECT 1 FROM heat_process_operation o WHERE o.heat_process_version_id = v.heat_process_version_id);
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'master', 't_heatprocess', r.heatprocessid, 'REF_MISSING', CONCAT('경로 단계 "', r.step_name, '" — 같은 이름의 단위공정 없음 (경로에서 제외)')
  FROM s_route r WHERE NOT EXISTS (SELECT 1 FROM bbakggum_mig.unit_process_by_name u WHERE u.unit_process_name = r.step_name);

-- ---------------------------------------------------------------------
-- 품목 (t_part) — 코드 = 접두 + partid. 구 품목은 거래처별 행이라 같은 품번이 여러 업체에 있으면 품목도 여러 개 (합치지 않음)
-- ---------------------------------------------------------------------
CREATE TEMPORARY TABLE s_part (PRIMARY KEY (lk), KEY (part_code)) AS
SELECT CAST(p.partid AS CHAR(200)) AS lk, p.partid, m.new_id, bbakggum_mig.code(@p_part, p.partid) AS part_code,
       COALESCE(NULLIF(TRIM(p.partname), ''), NULLIF(TRIM(p.partnumber), ''), CONCAT('(품명 없음 ', p.partid, ')')) AS part_name,
       NULLIF(TRIM(p.partnumber), '') AS part_number, NULLIF(TRIM(p.specification), '') AS specification, NULLIF(TRIM(p.model), '') AS model,
       NULLIF(TRIM(p.material), '') AS material, p.unitweight AS unit_weight, NULLIF(TRIM(p.outcomeunit), '') AS unit_code,
       COALESCE(bbakggum_mig.price_basis(p.unit), 'EA') AS price_basis, p.unit AS legacy_unit, bbakggum_mig.price_basis(p.unit) IS NULL AS basis_unmatched,
       p.unitprice AS unit_price, NULLIF(TRIM(p.drawingno), '') AS drawing_no, NULLIF(TRIM(p.hardness), '') AS hardness,
       NULLIF(TRIM(p.corehard), '') AS core_hardness, NULLIF(TRIM(p.hardendepth), '') AS effective_hardening_depth,
       NULLIF(TRIM(p.texture), '') AS texture, NULLIF(TRIM(p.remark), '') AS remark,
       IF(TRIM(p.isuse) IN ('0', 'N', 'n', 'false', '미사용'), 0, 1) AS is_active, COALESCE(p.createat, NOW()) AS created_at,
       p.customerid, NULLIF(TRIM(p.customername), '') AS customer_name, NULLIF(TRIM(p.customercode), '') AS customer_part_code,
       p.heatprocessid, NULLIF(TRIM(p.heatprocessname), '') AS heat_process_name, mc.new_id AS customer_id
  FROM bbakggum_legacy.t_part p
  LEFT JOIN migration_id_map m ON m.legacy_table = 't_part' AND m.legacy_key = CAST(p.partid AS CHAR) AND m.new_table = 'part'
  LEFT JOIN migration_id_map mc ON mc.legacy_table = 't_customer' AND mc.legacy_key = CAST(p.customerid AS CHAR) AND mc.new_table = 'customer';
-- 구 customerid 가 거래처에 없는 품목 = 삭제된 거래처의 품목 → 이관 안 함 (필요하면 신규에서 등록, 설계 §12 ⑫)
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'master', 't_part', lk, 'SKIPPED', CONCAT('거래처 없음 (구 customerid ', IFNULL(customerid, '-'), ', "', IFNULL(customer_name, ''), '") — 이관 안 함')
  FROM s_part WHERE new_id IS NULL AND customer_id IS NULL;

INSERT INTO part (part_code, part_name, part_number, specification, model, material, unit_weight, unit_code, price_basis, unit_price,
                  drawing_no, hardness, core_hardness, effective_hardening_depth, texture, remark, is_active, created_at)
SELECT part_code, part_name, part_number, specification, model, material, unit_weight, unit_code, price_basis, unit_price,
       drawing_no, hardness, core_hardness, effective_hardening_depth, texture, remark, is_active, created_at
  FROM s_part WHERE new_id IS NULL AND customer_id IS NOT NULL;
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT 't_part', s.lk, 'part', n.part_id FROM s_part s JOIN part n ON n.part_code = s.part_code WHERE s.new_id IS NULL AND s.customer_id IS NOT NULL;
UPDATE s_part s JOIN migration_id_map m ON m.legacy_table = 't_part' AND m.legacy_key = s.lk AND m.new_table = 'part' SET s.new_id = m.new_id WHERE s.new_id IS NULL;
UPDATE part n JOIN s_part s ON s.new_id = n.part_id
   SET n.part_name = s.part_name, n.part_number = s.part_number, n.specification = s.specification, n.model = s.model, n.material = s.material,
       n.unit_weight = s.unit_weight, n.unit_code = s.unit_code, n.price_basis = s.price_basis, n.unit_price = s.unit_price, n.drawing_no = s.drawing_no,
       n.hardness = s.hardness, n.core_hardness = s.core_hardness, n.effective_hardening_depth = s.effective_hardening_depth,
       n.texture = s.texture, n.remark = s.remark, n.is_active = s.is_active;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'master', 't_part', lk, 'CODE_UNMATCHED', CONCAT('단가 구분 "', legacy_unit, '" → EA') FROM s_part WHERE basis_unmatched AND NULLIF(TRIM(legacy_unit), '') IS NOT NULL;

-- 품목-거래처 (구 품목 1행 = 거래처 1곳 → 주 거래처)
UPDATE part_customer x JOIN s_part s ON s.new_id = x.part_id
   SET x.is_primary = 0 WHERE x.customer_id <> s.customer_id;
INSERT INTO part_customer (part_id, customer_id, customer_part_code, is_primary)
SELECT new_id, customer_id, customer_part_code, 1 FROM s_part WHERE new_id IS NOT NULL AND customer_id IS NOT NULL
ON DUPLICATE KEY UPDATE customer_part_code = VALUES(customer_part_code), is_primary = 1;

-- 품목 기본 공정: 구 heatprocessid → 없으면 이름이 같은 공정이 하나뿐일 때 이름으로
CREATE TEMPORARY TABLE s_hp_by_name (PRIMARY KEY (heat_process_name)) AS
SELECT TRIM(heatprocessname) AS heat_process_name, MIN(heatprocessid) AS heatprocessid FROM bbakggum_legacy.t_heatprocess
 WHERE NULLIF(TRIM(heatprocessname), '') IS NOT NULL GROUP BY TRIM(heatprocessname) HAVING COUNT(*) = 1;
CREATE TEMPORARY TABLE s_part_hp (PRIMARY KEY (lk)) AS
SELECT s.lk, s.new_id AS part_id, s.heatprocessid AS legacy_hp, s.heat_process_name,
       COALESCE(mh.new_id, mn.new_id) AS heat_process_id, mh.new_id IS NULL AND mn.new_id IS NOT NULL AS by_name
  FROM s_part s
  LEFT JOIN migration_id_map mh ON mh.legacy_table = 't_heatprocess' AND mh.legacy_key = CAST(s.heatprocessid AS CHAR) AND mh.new_table = 'heat_process'
  LEFT JOIN s_hp_by_name hn ON hn.heat_process_name = s.heat_process_name
  LEFT JOIN migration_id_map mn ON mn.legacy_table = 't_heatprocess' AND mn.legacy_key = CAST(hn.heatprocessid AS CHAR) AND mn.new_table = 'heat_process'
 WHERE s.new_id IS NOT NULL AND (COALESCE(s.heatprocessid, 0) <> 0 OR s.heat_process_name IS NOT NULL);
UPDATE part_heat_process x JOIN s_part_hp s ON s.part_id = x.part_id
   SET x.is_default = 0 WHERE x.heat_process_id <> COALESCE(s.heat_process_id, 0);
INSERT INTO part_heat_process (part_id, heat_process_id, is_default)
SELECT part_id, heat_process_id, 1 FROM s_part_hp WHERE heat_process_id IS NOT NULL
ON DUPLICATE KEY UPDATE is_default = 1;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'master', 't_part', lk, 'REF_BY_NAME', CONCAT('구 heatprocessid ', IFNULL(legacy_hp, '-'), ' 없음 → 이름 "', heat_process_name, '" 으로 연결') FROM s_part_hp WHERE by_name;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'master', 't_part', lk, 'REF_MISSING', CONCAT('공정 없음 (구 heatprocessid ', IFNULL(legacy_hp, '-'), ', "', IFNULL(heat_process_name, ''), '") — 기본 공정 연결 안 함')
  FROM s_part_hp WHERE heat_process_id IS NULL;

-- ---------------------------------------------------------------------
-- 부서·직위 (구 t_employee.department / rank 문자열, 코드 = 이름) · 사원 (t_employee — 주민번호 rpn, 숙소·휴가는 이관 안 함)
-- ---------------------------------------------------------------------
INSERT IGNORE INTO department (department_code, department_name)
SELECT DISTINCT LEFT(TRIM(department), 30), LEFT(TRIM(department), 50) FROM bbakggum_legacy.t_employee WHERE NULLIF(TRIM(department), '') IS NOT NULL;
INSERT IGNORE INTO job_position (job_position_code, job_position_name)
SELECT DISTINCT LEFT(TRIM(`rank`), 30), LEFT(TRIM(`rank`), 50) FROM bbakggum_legacy.t_employee WHERE NULLIF(TRIM(`rank`), '') IS NOT NULL;

CREATE TEMPORARY TABLE s_employee (PRIMARY KEY (lk), KEY (employee_code)) AS
SELECT CAST(e.id AS CHAR(200)) AS lk, m.new_id, bbakggum_mig.code(@p_emp, e.id) AS employee_code,
       COALESCE(NULLIF(TRIM(e.name), ''), CONCAT('(사원 ', e.id, ')')) AS employee_name,
       d.department_id, j.job_position_id, NULLIF(TRIM(e.part), '') AS team_name, NULLIF(TRIM(e.nationality), '') AS nationality,
       NULLIF(TRIM(e.address), '') AS address, NULLIF(TRIM(e.phone), '') AS phone, NULLIF(TRIM(e.mobilephone), '') AS mobile_phone,
       e.employmentdate AS employment_date, e.quitdate AS resignation_date, IF(e.assignment = 1, 1, 0) AS is_assignment_target,
       IF(e.isuse = 0 OR e.working = 0 OR (e.quitdate IS NOT NULL AND e.quitdate <= CURDATE()), 0, 1) AS is_active
  FROM bbakggum_legacy.t_employee e
  LEFT JOIN department d ON d.department_code = LEFT(TRIM(e.department), 30)
  LEFT JOIN job_position j ON j.job_position_code = LEFT(TRIM(e.`rank`), 30)
  LEFT JOIN migration_id_map m ON m.legacy_table = 't_employee' AND m.legacy_key = CAST(e.id AS CHAR) AND m.new_table = 'employee';
INSERT INTO employee (employee_code, employee_name, department_id, job_position_id, team_name, nationality, address, phone, mobile_phone,
                      employment_date, resignation_date, is_assignment_target, is_active)
SELECT employee_code, employee_name, department_id, job_position_id, team_name, nationality, address, phone, mobile_phone,
       employment_date, resignation_date, is_assignment_target, is_active FROM s_employee WHERE new_id IS NULL;
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT 't_employee', s.lk, 'employee', n.employee_id FROM s_employee s JOIN employee n ON n.employee_code = s.employee_code WHERE s.new_id IS NULL;
UPDATE employee n JOIN s_employee s ON s.new_id = n.employee_id
   SET n.employee_name = s.employee_name, n.department_id = s.department_id, n.job_position_id = s.job_position_id, n.team_name = s.team_name,
       n.nationality = s.nationality, n.address = s.address, n.phone = s.phone, n.mobile_phone = s.mobile_phone,
       n.employment_date = s.employment_date, n.resignation_date = s.resignation_date, n.is_assignment_target = s.is_assignment_target, n.is_active = s.is_active;

-- ---------------------------------------------------------------------
-- 측정기구 (t_instruments)
-- ---------------------------------------------------------------------
CREATE TEMPORARY TABLE s_instrument (PRIMARY KEY (lk), KEY (instrument_code)) AS
SELECT CAST(i.id AS CHAR(200)) AS lk, m.new_id, bbakggum_mig.code(@p_ins, i.id) AS instrument_code,
       COALESCE(NULLIF(TRIM(i.toolname), ''), CONCAT('(기구 ', i.id, ')')) AS instrument_name, NULLIF(TRIM(i.tooltype), '') AS instrument_type,
       NULLIF(CONCAT_WS(' / ', NULLIF(TRIM(i.model), ''), NULLIF(TRIM(i.specification), ''),
                        IF(NULLIF(TRIM(i.installdate), '') IS NULL, NULL, CONCAT('설치 ', TRIM(i.installdate))), NULLIF(TRIM(i.remark), '')), '') AS remark
  FROM bbakggum_legacy.t_instruments i
  LEFT JOIN migration_id_map m ON m.legacy_table = 't_instruments' AND m.legacy_key = CAST(i.id AS CHAR) AND m.new_table = 'instrument';
INSERT INTO instrument (instrument_code, instrument_name, instrument_type, remark)
SELECT instrument_code, instrument_name, instrument_type, remark FROM s_instrument WHERE new_id IS NULL;
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT 't_instruments', s.lk, 'instrument', n.instrument_id FROM s_instrument s JOIN instrument n ON n.instrument_code = s.instrument_code WHERE s.new_id IS NULL;
UPDATE instrument n JOIN s_instrument s ON s.new_id = n.instrument_id
   SET n.instrument_name = s.instrument_name, n.instrument_type = s.instrument_type, n.remark = s.remark;

-- ---------------------------------------------------------------------
-- 기준 작업시간 (t_process_default_time.runningtime = 시간 → 분)
-- ---------------------------------------------------------------------
-- 자연키가 없는 표: 비고에 임시 표식(#mig:구id)을 넣어 매핑한 뒤 원래 비고로 되돌린다
CREATE TEMPORARY TABLE s_pdt (PRIMARY KEY (lk)) AS
SELECT CAST(t.id AS CHAR(200)) AS lk, m.new_id,
       bbakggum_mig.new_id('equipmenttype', TRIM(t.equipmenttype), 'equipment_type') AS equipment_type_id,
       bbakggum_mig.new_id('t_equipment', CAST(t.equipmentid AS CHAR), 'equipment') AS equipment_id,
       ROUND(COALESCE(t.runningtime, 0) * 60, 2) AS running_time_min, NULLIF(TRIM(t.remark), '') AS remark
  FROM bbakggum_legacy.t_process_default_time t
  LEFT JOIN migration_id_map m ON m.legacy_table = 't_process_default_time' AND m.legacy_key = CAST(t.id AS CHAR) AND m.new_table = 'process_default_time';
INSERT INTO process_default_time (equipment_type_id, equipment_id, running_time_min, remark)
SELECT equipment_type_id, equipment_id, running_time_min, CONCAT('#mig:', lk) FROM s_pdt
 WHERE new_id IS NULL AND (equipment_type_id IS NOT NULL OR equipment_id IS NOT NULL);
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT 't_process_default_time', s.lk, 'process_default_time', n.process_default_time_id
  FROM s_pdt s JOIN process_default_time n ON n.remark = CONCAT('#mig:', s.lk) WHERE s.new_id IS NULL;
UPDATE process_default_time n JOIN migration_id_map m ON m.new_table = 'process_default_time' AND m.new_id = n.process_default_time_id
  JOIN s_pdt s ON s.lk = m.legacy_key AND m.legacy_table = 't_process_default_time'
   SET n.equipment_type_id = s.equipment_type_id, n.equipment_id = s.equipment_id, n.running_time_min = s.running_time_min, n.remark = s.remark;
INSERT INTO bbakggum_mig.issue (step, legacy_table, legacy_key, issue_code, detail)
SELECT 'master', 't_process_default_time', lk, 'SKIPPED', '설비유형·설비 모두 찾을 수 없음' FROM s_pdt
 WHERE new_id IS NULL AND equipment_type_id IS NULL AND equipment_id IS NULL;

-- ---------------------------------------------------------------------
-- 공정검사 항목 (t_unitinspectionitem)
-- ---------------------------------------------------------------------
CREATE TEMPORARY TABLE s_unit_inspection_item (PRIMARY KEY (lk), KEY (item_code)) AS
SELECT CAST(u.id AS CHAR(200)) AS lk, m.new_id, bbakggum_mig.code(@p_li, u.id) AS item_code,
       COALESCE(NULLIF(TRIM(u.unitinspectionitem), ''), CONCAT('(항목 ', u.id, ')')) AS item_name, u.id AS sort_order
  FROM bbakggum_legacy.t_unitinspectionitem u
  LEFT JOIN migration_id_map m ON m.legacy_table = 't_unitinspectionitem' AND m.legacy_key = CAST(u.id AS CHAR) AND m.new_table = 'unit_inspection_item';
INSERT INTO unit_inspection_item (item_code, item_name, sort_order)
SELECT item_code, item_name, sort_order FROM s_unit_inspection_item WHERE new_id IS NULL;
INSERT INTO migration_id_map (legacy_table, legacy_key, new_table, new_id)
SELECT 't_unitinspectionitem', s.lk, 'unit_inspection_item', n.unit_inspection_item_id
  FROM s_unit_inspection_item s JOIN unit_inspection_item n ON n.item_code = s.item_code WHERE s.new_id IS NULL;
UPDATE unit_inspection_item n JOIN s_unit_inspection_item s ON s.new_id = n.unit_inspection_item_id SET n.item_name = s.item_name;

COMMIT;
