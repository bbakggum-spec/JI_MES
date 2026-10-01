-- =====================================================================
-- ⑪ 검증 (설계 §10 ⑪, §25.4)
--   1) 건수: 구 행 = 이관(migration_id_map) + 제외(문제 목록 SKIPPED/DUPLICATE) — 설명 안 되는 행이 있으면 FAIL
--   2) 합계: 이관된 행끼리 수량·금액·조건값·측정값 개수 비교 — 다르면 FAIL
--   3) 이관하지 않는 표·컬럼에 데이터가 있으면 WARN (사람이 확인)
--   결과 = bbakggum_mig.verify_result, FAIL 이 있으면 오류로 끝남
-- =====================================================================
USE bbakggum_v2;
SET collation_connection = @@collation_database;

CREATE TABLE IF NOT EXISTS bbakggum_mig.verify_result (
    verify_result_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    check_group  VARCHAR(20)  NOT NULL,
    check_name   VARCHAR(100) NOT NULL,
    legacy_value DECIMAL(20,3) NULL,
    new_value    DECIMAL(20,3) NULL,
    skipped      DECIMAL(20,3) NULL,
    result       VARCHAR(10)  NOT NULL COMMENT 'PASS / FAIL / WARN / INFO',
    note         VARCHAR(500) NULL,
    checked_at   DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB;
TRUNCATE TABLE bbakggum_mig.verify_result;

DELIMITER $$
-- 건수 대조: 구 키(중복 제거) 마다 이관됐거나 제외 사유가 있어야 함
CREATE OR REPLACE PROCEDURE bbakggum_mig.verify_count(IN p_name VARCHAR(100), IN p_from VARCHAR(200), IN p_key VARCHAR(200),
                                                     IN p_legacy_table VARCHAR(64), IN p_new_table VARCHAR(64))
BEGIN
    SET @sql = CONCAT(
        'INSERT INTO bbakggum_mig.verify_result (check_group, check_name, legacy_value, new_value, skipped, result, note) ',
        'SELECT ''건수'', ?, COUNT(*), COALESCE(SUM(m.k IS NOT NULL), 0), COALESCE(SUM(m.k IS NULL AND i.k IS NOT NULL), 0), ',
        '       IF(COALESCE(SUM(m.k IS NULL AND i.k IS NULL), 0) = 0, ''PASS'', ''FAIL''), ',
        '       IF(COALESCE(SUM(m.k IS NULL AND i.k IS NULL), 0) = 0, NULL, CONCAT(''설명 없는 미이관 '', SUM(m.k IS NULL AND i.k IS NULL), ''건 (예: '', MIN(IF(m.k IS NULL AND i.k IS NULL, l.k, NULL)), '')'')) ',
        '  FROM (SELECT DISTINCT CAST(', p_key, ' AS CHAR) AS k FROM ', p_from, ') l ',
        '  LEFT JOIN (SELECT DISTINCT legacy_key AS k FROM bbakggum_v2.migration_id_map WHERE legacy_table = ? AND new_table = ?) m ON m.k = l.k ',
        '  LEFT JOIN (SELECT DISTINCT legacy_key AS k FROM bbakggum_mig.issue WHERE legacy_table = ? AND issue_code IN (''SKIPPED'', ''DUPLICATE'')) i ON i.k = l.k');
    SET @n = p_name, @lt = p_legacy_table, @nt = p_new_table;
    PREPARE st FROM @sql;
    EXECUTE st USING @n, @lt, @nt, @lt;
    DEALLOCATE PREPARE st;
END$$

CREATE OR REPLACE PROCEDURE bbakggum_mig.verify_sum(IN p_name VARCHAR(100), IN p_legacy DECIMAL(20,3), IN p_new DECIMAL(20,3), IN p_note VARCHAR(500))
    INSERT INTO bbakggum_mig.verify_result (check_group, check_name, legacy_value, new_value, result, note)
    VALUES ('합계', p_name, COALESCE(p_legacy, 0), COALESCE(p_new, 0), IF(COALESCE(p_legacy, 0) = COALESCE(p_new, 0), 'PASS', 'FAIL'), p_note)$$

CREATE OR REPLACE PROCEDURE bbakggum_mig.verify_unused(IN p_name VARCHAR(100), IN p_rows BIGINT, IN p_note VARCHAR(500))
    INSERT INTO bbakggum_mig.verify_result (check_group, check_name, legacy_value, result, note)
    VALUES ('미이관', p_name, p_rows, IF(p_rows > 0, 'WARN', 'INFO'), p_note)$$
DELIMITER ;

-- ---------------------------------------------------------------------
-- 1) 건수
-- ---------------------------------------------------------------------
CALL bbakggum_mig.verify_count('거래처', 'bbakggum_legacy.t_customer', 'customerid', 't_customer', 'customer');
CALL bbakggum_mig.verify_count('설비', 'bbakggum_legacy.t_equipment', 'equipmentid', 't_equipment', 'equipment');
CALL bbakggum_mig.verify_count('단위공정', 'bbakggum_legacy.t_unitprocess', 'unitprocessid', 't_unitprocess', 'unit_process');
CALL bbakggum_mig.verify_count('공정', 'bbakggum_legacy.t_heatprocess', 'heatprocessid', 't_heatprocess', 'heat_process');
CALL bbakggum_mig.verify_count('품목', 'bbakggum_legacy.t_part', 'partid', 't_part', 'part');
CALL bbakggum_mig.verify_count('사원', 'bbakggum_legacy.t_employee', 'id', 't_employee', 'employee');
CALL bbakggum_mig.verify_count('측정기구', 'bbakggum_legacy.t_instruments', 'id', 't_instruments', 'instrument');
CALL bbakggum_mig.verify_count('기준 작업시간', 'bbakggum_legacy.t_process_default_time', 'id', 't_process_default_time', 'process_default_time');
CALL bbakggum_mig.verify_count('공정검사 항목', 'bbakggum_legacy.t_unitinspectionitem', 'id', 't_unitinspectionitem', 'unit_inspection_item');
CALL bbakggum_mig.verify_count('단계 템플릿 (설비·단위공정)', 'bbakggum_legacy.t_standardtemplate', 'CONCAT(TRIM(equipmentname), ''|'', TRIM(unitprocessname))', 't_standardtemplate', 'step_template');
CALL bbakggum_mig.verify_count('작업표준', 'bbakggum_legacy.t_standard', 'workstandardid', 't_standard', 'standard');
CALL bbakggum_mig.verify_count('검사기준 (품목·업체)', 'bbakggum_legacy.t_inspectioncriteria', 'CONCAT(partid, ''|'', IFNULL(customerid, 0))', 't_inspectioncriteria', 'inspection_standard');
CALL bbakggum_mig.verify_count('수주 품목 (입고)', 'bbakggum_legacy.t_income', 'incomeid', 't_income', 'sales_order_item');
CALL bbakggum_mig.verify_count('작업 LOT', 'bbakggum_legacy.t_work', 'workid', 't_work', 'production_work');
CALL bbakggum_mig.verify_count('투입', 'bbakggum_legacy.t_worksub', 'worksubid', 't_worksub', 'production_work_input');
CALL bbakggum_mig.verify_count('검사', 'bbakggum_legacy.t_inspection', 'TRIM(inspectionno)', 't_inspection', 'inspection');
CALL bbakggum_mig.verify_count('부적합', 'bbakggum_legacy.t_defect', 'defectid', 't_defect', 'defect_occurrence');
CALL bbakggum_mig.verify_count('출하 전표', 'bbakggum_legacy.t_outcomesum', 'outcomesumid', 't_outcomesum', 'shipment');
CALL bbakggum_mig.verify_count('출하 상세', 'bbakggum_legacy.t_outcome', 'outcomeid', 't_outcome', 'shipment_item');
CALL bbakggum_mig.verify_count('설비 비가동', 'bbakggum_legacy.t_downtime', 'id', 't_downtime', 'equipment_downtime');
CALL bbakggum_mig.verify_count('설비 보전', 'bbakggum_legacy.t_maintenance WHERE COALESCE(instrumentid, 0) = 0', 'id', 't_maintenance', 'maintenance');
CALL bbakggum_mig.verify_count('측정기구 교정', 'bbakggum_legacy.t_maintenance WHERE COALESCE(instrumentid, 0) <> 0', 'id', 't_maintenance', 'instrument_calibration');

-- 검사 대상: 행 단위. 검사(검사번호) 자체가 제외됐으면 그 대상도 설명됨
INSERT INTO bbakggum_mig.verify_result (check_group, check_name, legacy_value, new_value, skipped, result, note)
SELECT '건수', '검사 대상', COUNT(*), COALESCE(SUM(m.legacy_key IS NOT NULL), 0),
       COALESCE(SUM(m.legacy_key IS NULL AND (i.legacy_key IS NOT NULL OR h.legacy_key IS NOT NULL)), 0),
       IF(COALESCE(SUM(m.legacy_key IS NULL AND i.legacy_key IS NULL AND h.legacy_key IS NULL), 0) = 0, 'PASS', 'FAIL'), NULL
  FROM bbakggum_legacy.t_inspection t
  LEFT JOIN migration_id_map m ON m.legacy_table = 't_inspection' AND m.legacy_key = CAST(t.id AS CHAR) AND m.new_table = 'inspection_target'
  LEFT JOIN (SELECT DISTINCT legacy_key FROM bbakggum_mig.issue WHERE legacy_table = 't_inspection' AND issue_code IN ('SKIPPED', 'DUPLICATE')) i
         ON i.legacy_key = CAST(t.id AS CHAR)
  LEFT JOIN (SELECT DISTINCT legacy_key FROM bbakggum_mig.issue WHERE legacy_table = 't_inspection' AND issue_code = 'SKIPPED') h
         ON h.legacy_key = TRIM(t.inspectionno);

-- 신규 쪽에 매핑 없이 남은 행 (이관 대상 표에서) — 다른 경로로 들어온 데이터가 섞였는지
INSERT INTO bbakggum_mig.verify_result (check_group, check_name, new_value, result, note)
SELECT '건수', CONCAT('매핑 없는 신규 행: ', t.name), t.cnt, IF(t.cnt = 0, 'PASS', 'WARN'), '이관 외 경로로 생긴 행 (병행운영 중 신규 시스템 입력 등)'
  FROM (SELECT 'customer' AS name, (SELECT COUNT(*) FROM customer n WHERE NOT EXISTS (SELECT 1 FROM migration_id_map m WHERE m.new_table = 'customer' AND m.new_id = n.customer_id)) AS cnt
        UNION ALL SELECT 'part', (SELECT COUNT(*) FROM part n WHERE NOT EXISTS (SELECT 1 FROM migration_id_map m WHERE m.new_table = 'part' AND m.new_id = n.part_id))
        UNION ALL SELECT 'sales_order_item', (SELECT COUNT(*) FROM sales_order_item n WHERE NOT EXISTS (SELECT 1 FROM migration_id_map m WHERE m.new_table = 'sales_order_item' AND m.new_id = n.sales_order_item_id))
        UNION ALL SELECT 'production_work', (SELECT COUNT(*) FROM production_work n WHERE NOT EXISTS (SELECT 1 FROM migration_id_map m WHERE m.new_table = 'production_work' AND m.new_id = n.production_work_id))
        UNION ALL SELECT 'shipment', (SELECT COUNT(*) FROM shipment n WHERE NOT EXISTS (SELECT 1 FROM migration_id_map m WHERE m.new_table = 'shipment' AND m.new_id = n.shipment_id))) t;

-- ---------------------------------------------------------------------
-- 2) 합계 (이관된 행끼리)
-- ---------------------------------------------------------------------
CALL bbakggum_mig.verify_sum('입고 수량 = 수주 수량',
    (SELECT SUM(i.incomeqt) FROM bbakggum_legacy.t_income i JOIN migration_id_map m ON m.legacy_table = 't_income' AND m.legacy_key = CAST(i.incomeid AS CHAR) AND m.new_table = 'sales_order_item'),
    (SELECT SUM(n.order_qty) FROM sales_order_item n JOIN migration_id_map m ON m.new_table = 'sales_order_item' AND m.new_id = n.sales_order_item_id AND m.legacy_table = 't_income'), NULL);
CALL bbakggum_mig.verify_sum('입고 중량 = 수주 중량',
    (SELECT SUM(i.weight) FROM bbakggum_legacy.t_income i JOIN migration_id_map m ON m.legacy_table = 't_income' AND m.legacy_key = CAST(i.incomeid AS CHAR) AND m.new_table = 'sales_order_item'),
    (SELECT SUM(n.order_weight) FROM sales_order_item n JOIN migration_id_map m ON m.new_table = 'sales_order_item' AND m.new_id = n.sales_order_item_id AND m.legacy_table = 't_income'), NULL);
CALL bbakggum_mig.verify_sum('투입 수량',
    (SELECT SUM(w.inputqt) FROM bbakggum_legacy.t_worksub w JOIN migration_id_map m ON m.legacy_table = 't_worksub' AND m.legacy_key = CAST(w.worksubid AS CHAR) AND m.new_table = 'production_work_input'),
    (SELECT SUM(n.input_qty) FROM production_work_input n WHERE n.production_work_input_id IN
        (SELECT new_id FROM migration_id_map WHERE legacy_table = 't_worksub' AND new_table = 'production_work_input')), '같은 LOT × 입고번호 행은 합쳐서 1행');
CALL bbakggum_mig.verify_sum('투입 LOT 별 작업 = 구 작업',
    (SELECT COUNT(DISTINCT TRIM(w.lotno)) FROM bbakggum_legacy.t_worksub w JOIN migration_id_map m ON m.legacy_table = 't_worksub' AND m.legacy_key = CAST(w.worksubid AS CHAR) AND m.new_table = 'production_work_input'),
    (SELECT COUNT(DISTINCT n.production_work_id) FROM production_work_input n WHERE n.production_work_input_id IN
        (SELECT new_id FROM migration_id_map WHERE legacy_table = 't_worksub' AND new_table = 'production_work_input')), NULL);
CALL bbakggum_mig.verify_sum('부적합 수량',
    (SELECT SUM(GREATEST(COALESCE(d.inputqt, 0), 0)) FROM bbakggum_legacy.t_defect d JOIN migration_id_map m ON m.legacy_table = 't_defect' AND m.legacy_key = CAST(d.defectid AS CHAR) AND m.new_table = 'defect_occurrence'),
    (SELECT SUM(n.defect_qty) FROM defect_occurrence n JOIN migration_id_map m ON m.new_table = 'defect_occurrence' AND m.new_id = n.defect_occurrence_id AND m.legacy_table = 't_defect'), NULL);
CALL bbakggum_mig.verify_sum('출하 전표 공급가액',
    (SELECT SUM(s.totalamount) FROM bbakggum_mig.src_outcomesum s JOIN migration_id_map m ON m.legacy_table = 't_outcomesum' AND m.legacy_key = CAST(s.outcomesumid AS CHAR) AND m.new_table = 'shipment'),
    (SELECT SUM(n.supply_amount) FROM shipment n JOIN migration_id_map m ON m.new_table = 'shipment' AND m.new_id = n.shipment_id AND m.legacy_table = 't_outcomesum'), NULL);
CALL bbakggum_mig.verify_sum('출하 수량',
    (SELECT SUM(GREATEST(COALESCE(o.outqt, 0), 0)) FROM bbakggum_legacy.t_outcome o JOIN migration_id_map m ON m.legacy_table = 't_outcome' AND m.legacy_key = CAST(o.outcomeid AS CHAR) AND m.new_table = 'shipment_item'),
    (SELECT SUM(n.shipment_qty) FROM shipment_item n JOIN migration_id_map m ON m.new_table = 'shipment_item' AND m.new_id = n.shipment_item_id AND m.legacy_table = 't_outcome'), NULL);
CALL bbakggum_mig.verify_sum('출하 금액',
    (SELECT SUM(o.outamount) FROM bbakggum_legacy.t_outcome o JOIN migration_id_map m ON m.legacy_table = 't_outcome' AND m.legacy_key = CAST(o.outcomeid AS CHAR) AND m.new_table = 'shipment_item'),
    (SELECT SUM(n.amount) FROM shipment_item n JOIN migration_id_map m ON m.new_table = 'shipment_item' AND m.new_id = n.shipment_item_id AND m.legacy_table = 't_outcome'), NULL);
CALL bbakggum_mig.verify_sum('출하 전표 상세 금액 합 = 전표 공급가액',
    (SELECT SUM(s.supply_amount) FROM shipment s JOIN migration_id_map m ON m.new_table = 'shipment' AND m.new_id = s.shipment_id AND m.legacy_table = 't_outcomesum'),
    (SELECT SUM(si.amount) FROM shipment_item si JOIN shipment s USING (shipment_id) JOIN migration_id_map m ON m.new_table = 'shipment' AND m.new_id = s.shipment_id AND m.legacy_table = 't_outcomesum'),
    '구 전표 합계와 상세 합이 원래 다르면 구 데이터 문제');
CALL bbakggum_mig.verify_sum('마감 전표 = 마감완료 전표',
    (SELECT COUNT(*) FROM bbakggum_mig.src_outcomesum s JOIN migration_id_map m ON m.legacy_table = 't_outcomesum' AND m.legacy_key = CAST(s.outcomesumid AS CHAR) AND m.new_table = 'shipment'
      WHERE s.closing_status = 1),
    (SELECT COUNT(*) FROM shipment n JOIN migration_id_map m ON m.new_table = 'shipment' AND m.new_id = n.shipment_id AND m.legacy_table = 't_outcomesum'
      WHERE n.closing_status = 'CLOSED'), '마감월 없는 마감완료 전표는 출하월로 (문제 목록 VALUE_CHANGED)');

-- 작업표준 조건값: 이관된 표준의 값 있는 칸 (스텝 행·중복 항목 행 제외)
CALL bbakggum_mig.verify_sum('작업표준 조건값 칸 수',
    (SELECT COUNT(*) FROM bbakggum_legacy.t_standarddetail d
       JOIN migration_id_map m ON m.legacy_table = 't_standard' AND m.legacy_key = CAST(d.workstandardid AS CHAR) AND m.new_table = 'standard'
      CROSS JOIN bbakggum_mig.seq q
      WHERE q.n <= 15 AND NULLIF(TRIM(d.item), '') IS NOT NULL AND TRIM(d.item) <> bbakggum_mig.cfg('step_row_item')
        AND d.id = (SELECT MIN(x.id) FROM bbakggum_legacy.t_standarddetail x WHERE x.workstandardid = d.workstandardid AND TRIM(x.item) = TRIM(d.item)
                     AND x.subno = (SELECT MIN(y.subno) FROM bbakggum_legacy.t_standarddetail y WHERE y.workstandardid = d.workstandardid AND TRIM(y.item) = TRIM(d.item)))
        AND NULLIF(TRIM(ELT(q.n, d.step1, d.step2, d.step3, d.step4, d.step5, d.step6, d.step7, d.step8, d.step9, d.step10,
                                 d.step11, d.step12, d.step13, d.step14, d.step15)), '') IS NOT NULL),
    (SELECT COUNT(*) FROM standard_condition c JOIN standard_version v USING (standard_version_id)
       JOIN migration_id_map m ON m.new_table = 'standard' AND m.new_id = v.standard_id AND m.legacy_table = 't_standard' WHERE v.version_no = 1), NULL);
-- 작업 조건값: 이관된 LOT 의 값 있는 칸
CALL bbakggum_mig.verify_sum('작업 조건값 칸 수',
    (SELECT COUNT(*) FROM bbakggum_legacy.t_workconditiondetail d
       JOIN bbakggum_mig.work_by_lot w ON w.lot_no = TRIM(d.lotno)
      CROSS JOIN bbakggum_mig.seq q
      WHERE q.n <= 15 AND NULLIF(TRIM(d.item), '') IS NOT NULL AND TRIM(d.item) <> bbakggum_mig.cfg('step_row_item')
        AND d.id = (SELECT MIN(x.id) FROM bbakggum_legacy.t_workconditiondetail x WHERE TRIM(x.lotno) = TRIM(d.lotno) AND TRIM(x.item) = TRIM(d.item)
                     AND x.subno = (SELECT MIN(y.subno) FROM bbakggum_legacy.t_workconditiondetail y WHERE TRIM(y.lotno) = TRIM(d.lotno) AND TRIM(y.item) = TRIM(d.item)))
        AND NULLIF(TRIM(ELT(q.n, d.step1, d.step2, d.step3, d.step4, d.step5, d.step6, d.step7, d.step8, d.step9, d.step10,
                                 d.step11, d.step12, d.step13, d.step14, d.step15)), '') IS NOT NULL),
    (SELECT COUNT(c.set_value) FROM production_work_condition c JOIN bbakggum_mig.work_by_lot w ON w.production_work_id = c.production_work_id), NULL);
-- 검사 측정값: 시료번호 0(측정 위치) 제외한 값 있는 칸
CALL bbakggum_mig.verify_sum('검사 측정값 개수',
    (SELECT COUNT(*) FROM bbakggum_legacy.t_inspectiondetail d
       JOIN migration_id_map m ON m.legacy_table = 't_inspection' AND m.legacy_key = TRIM(d.inspectionno) AND m.new_table = 'inspection'
      CROSS JOIN bbakggum_mig.seq q
      WHERE q.n <= 10 AND COALESCE(d.number, 1) > 0
        AND NULLIF(TRIM(ELT(q.n, d.v1, d.v2, d.v3, d.v4, d.v5, d.v6, d.v7, d.v8, d.v9, d.v10)), '') IS NOT NULL),
    (SELECT COUNT(*) FROM inspection_measurement x JOIN inspection_item ii USING (inspection_item_id)
       JOIN migration_id_map m ON m.new_table = 'inspection' AND m.new_id = ii.inspection_id AND m.legacy_table = 't_inspection'), NULL);
CALL bbakggum_mig.verify_sum('검사기준 항목 수',
    (SELECT COUNT(*) FROM bbakggum_legacy.t_inspectioncriteria c
       JOIN migration_id_map m ON m.legacy_table = 't_inspectioncriteria' AND m.legacy_key = CONCAT(c.partid, '|', IFNULL(c.customerid, 0)) AND m.new_table = 'inspection_standard'),
    (SELECT COUNT(*) FROM migration_id_map WHERE legacy_table = 't_inspectioncriteria' AND new_table = 'inspection_criteria'), NULL);
-- 품목 ↔ 거래처: 이관한 품목은 모두 거래처가 있어야 함 (거래처 없는 구 품목은 제외 — §12 ⑫)
CALL bbakggum_mig.verify_sum('거래처 연결 안 된 품목', 0,
    (SELECT COUNT(*) FROM part p JOIN migration_id_map m ON m.new_table = 'part' AND m.new_id = p.part_id AND m.legacy_table = 't_part'
      WHERE NOT EXISTS (SELECT 1 FROM part_customer pc WHERE pc.part_id = p.part_id)), NULL);

-- ---------------------------------------------------------------------
-- 3) 이관하지 않는 표·컬럼 (데이터가 있으면 WARN)
-- ---------------------------------------------------------------------
CALL bbakggum_mig.verify_unused('t_condition_con', (SELECT COUNT(*) FROM bbakggum_legacy.t_condition_con), '폐기 — 구 소스 미사용 (원본은 bbakggum_legacy 덤프에 보존)');
CALL bbakggum_mig.verify_unused('t_department', (SELECT COUNT(*) FROM bbakggum_legacy.t_department), '폐기 — 구 소스 미사용');
CALL bbakggum_mig.verify_unused('t_inspectionsub', (SELECT COUNT(*) FROM bbakggum_legacy.t_inspectionsub), '폐기 — 구 소스 미사용');
CALL bbakggum_mig.verify_unused('t_printsheet', (SELECT COUNT(*) FROM bbakggum_legacy.t_printsheet), '폐기 — 구 소스 미사용');
CALL bbakggum_mig.verify_unused('t_printtemplate', (SELECT COUNT(*) FROM bbakggum_legacy.t_printtemplate), '좌표형 양식 폐기 — 엑셀 양식으로 다시 등록');
CALL bbakggum_mig.verify_unused('t_part_template', (SELECT COUNT(*) FROM bbakggum_legacy.t_part_template), '양식 연결 — 양식을 다시 등록한 뒤 품목별 연결');
CALL bbakggum_mig.verify_unused('workplan', (SELECT COUNT(*) FROM bbakggum_legacy.workplan), '계획은 전환 시 신규 생산계획에서 다시 수립');
CALL bbakggum_mig.verify_unused('t_schedulebox', (SELECT COUNT(*) FROM bbakggum_legacy.t_schedulebox), '구 화면 배치 — 신규 Gantt 사용');
CALL bbakggum_mig.verify_unused('t_inputwaiting', (SELECT COUNT(*) FROM bbakggum_legacy.t_inputwaiting WHERE COALESCE(remainqt, 0) > 0),
                                '투입 대기 — 신규는 잔량을 계산 (잔량 있는 행 수)');
CALL bbakggum_mig.verify_unused('t_workerassignment', (SELECT COUNT(*) FROM bbakggum_legacy.t_workerassignment), '작업자 배정 — 교대(work_shift) 등록 후 다시 입력');
CALL bbakggum_mig.verify_unused('t_lineinspection', (SELECT COUNT(*) FROM bbakggum_legacy.t_lineinspection
                                                     WHERE CONCAT_WS('', hn1x1, hn1x2, hn1x3, hn2x1, hn3x1, cn1x1, cn2x1, cn3x1, dn1x1, dn2x1, dn3x1, tn1x1) <> ''),
                                '공정검사 측정값 (값 있는 행 수) — 구 열 의미(hn/cn/dn/tn) 확인 후 별도 이관');
CALL bbakggum_mig.verify_unused('t_standard_gas', (SELECT COUNT(*) FROM bbakggum_legacy.t_standard_gas), '구 가스 조건 — 작업표준 조건으로 다시 입력');
CALL bbakggum_mig.verify_unused('t_system_settings', (SELECT COUNT(*) FROM bbakggum_legacy.t_system_settings WHERE setting_key NOT LIKE 'path.%'),
                                '구 PC 경로(path.*) 외 설정 수 — 신규 설정 화면에서 확인');
CALL bbakggum_mig.verify_unused('t_templatefieldname', 0, '치환자 사전 — 신규 print_field 로 대체 (구 38행은 참고용)');
CALL bbakggum_mig.verify_unused('t_part.massstatus (양산 외)', (SELECT COUNT(*) FROM bbakggum_legacy.t_part WHERE NULLIF(TRIM(massstatus), '') IS NOT NULL AND TRIM(massstatus) <> '양산'),
                                '§12 ⑨ — 양산 외 값이 있으면 part.mass_status 추가 검토');
CALL bbakggum_mig.verify_unused('t_part.subp/subprice', (SELECT COUNT(*) FROM bbakggum_legacy.t_part
                                                         WHERE CONCAT_WS('', subp1, subp2, subp3, subp4, subp5, subp6, subp7, subp8) <> ''
                                                            OR COALESCE(subprice1, 0) + COALESCE(subprice2, 0) + COALESCE(subprice3, 0) + COALESCE(subprice4, 0) > 0),
                                '§12 ⑧ — 공정 단계별 단가. 값이 있으면 part_process_price 추가 검토');
CALL bbakggum_mig.verify_unused('t_part.bundleqt', (SELECT COUNT(*) FROM bbakggum_legacy.t_part WHERE COALESCE(bundleqt, 0) > 0), '묶음 수량 — 신규 컬럼 없음');
CALL bbakggum_mig.verify_unused('t_part 파일 경로', (SELECT COUNT(*) FROM bbakggum_legacy.t_part
                                                     WHERE CONCAT_WS('', drawing, partimage, certform) <> ''), '도면·이미지·성적서 양식 경로 (PC 경로) — 첨부(attachment)로 다시 등록');

SELECT check_group AS 구분, check_name AS 항목, legacy_value AS 구, new_value AS 신규, skipped AS 제외, result AS 결과, note AS 비고
  FROM bbakggum_mig.verify_result ORDER BY verify_result_id;
SELECT issue_code AS 문제, legacy_table AS 구표, COUNT(*) AS 건수, MIN(detail) AS 예
  FROM bbakggum_mig.issue GROUP BY issue_code, legacy_table ORDER BY issue_code, legacy_table;

CALL bbakggum_mig.assert_true((SELECT COUNT(*) FROM bbakggum_mig.verify_result WHERE result = 'FAIL') = 0,
                              '이관 검증 실패 — bbakggum_mig.verify_result 의 FAIL 행을 확인하세요');
