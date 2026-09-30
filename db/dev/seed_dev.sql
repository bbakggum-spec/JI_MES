-- =====================================================================
-- 개발 화면 확인용 시드 (생산계획 Gantt 등) — dev-db.ps1 seed 가 DDL 재적용 직후 실행
--   * 날짜는 실행 시점(NOW) 기준 상대값 → 언제 실행해도 오늘·내일 화면에 보인다
--   * 스모크 시나리오(db/test/smoke_scenario.sql)와 별개. 운영·이관 데이터 아님
-- =====================================================================
USE bbakggum_v2;

INSERT INTO customer (customer_code, customer_name) VALUES
 ('C-HD', '한독기어'), ('C-SM', '삼미정밀'), ('C-DY', '대영오토');

INSERT INTO equipment_type (equipment_type_code, equipment_type_name) VALUES
 ('WASH', '세척기'), ('GAS', '가스로'), ('TEMP', '템퍼링로');

INSERT INTO equipment (equipment_type_id, equipment_code, equipment_initial, equipment_name, sort_order)
SELECT t.equipment_type_id, v.code, v.code, v.name, v.ord
  FROM (SELECT 'W01' code, '세척기1' name, 'WASH' type, 10 ord
        UNION ALL SELECT 'B01', '가스로1', 'GAS', 20
        UNION ALL SELECT 'B02', '가스로2', 'GAS', 21
        UNION ALL SELECT 'B03', '가스로3', 'GAS', 22
        UNION ALL SELECT 'T01', '템퍼링로1', 'TEMP', 30
        UNION ALL SELECT 'T02', '템퍼링로2', 'TEMP', 31) v
  JOIN equipment_type t ON t.equipment_type_code = v.type;

INSERT INTO unit_process (unit_process_code, unit_process_name, sort_order) VALUES
 ('WASH', '세척', 10), ('CARB', '침탄', 20), ('TEMP', '템퍼링', 30);

INSERT INTO step_template (step_template_code, step_template_name, unit_process_id, equipment_type_id)
SELECT v.code, v.name, u.unit_process_id, t.equipment_type_id
  FROM (SELECT 'ST-WASH' code, '세척' name, 'WASH' up, 'WASH' type
        UNION ALL SELECT 'ST-CARB', '가스 침탄', 'CARB', 'GAS'
        UNION ALL SELECT 'ST-TEMP', '템퍼링', 'TEMP', 'TEMP') v
  JOIN unit_process u ON u.unit_process_code = v.up
  JOIN equipment_type t ON t.equipment_type_code = v.type;

INSERT INTO heat_process (heat_process_code, heat_process_name) VALUES ('HP-CARB', '침탄 소입소려');
INSERT INTO heat_process_version (heat_process_id, version_no, effective_from, is_current)
SELECT heat_process_id, 1, '2026-01-01', 1 FROM heat_process WHERE heat_process_code = 'HP-CARB';
INSERT INTO heat_process_operation (heat_process_version_id, sequence_no, unit_process_id, is_main_process)
SELECT hv.heat_process_version_id, v.seq, u.unit_process_id, v.main
  FROM (SELECT 10 seq, 'WASH' up, 0 main UNION ALL SELECT 20, 'CARB', 1 UNION ALL SELECT 30, 'TEMP', 0) v
  JOIN unit_process u ON u.unit_process_code = v.up
  CROSS JOIN heat_process_version hv;

INSERT INTO part (part_code, part_name, part_number) VALUES
 ('P-GEAR-A', '헬리컬 기어 A', 'HG-100'),
 ('P-SHAFT-B', '출력 샤프트 B', 'OS-220'),
 ('P-PIN-C', '피니언 C', 'PN-030'),
 ('P-RING-D', '링기어 D', 'RG-410');

-- 작업표준: C 는 표준 없음 (생산계획에서 작업시간 입력 흐름 확인용)
INSERT INTO standard (standard_code, standard_name, part_id, unit_process_id, equipment_type_id)
SELECT CONCAT('STD-', p.part_code, '-', u.unit_process_code), CONCAT(p.part_name, ' ', u.unit_process_name),
       p.part_id, u.unit_process_id, st.equipment_type_id
  FROM part p
  JOIN unit_process u ON u.unit_process_code IN ('WASH','CARB','TEMP')
  JOIN step_template st ON st.unit_process_id = u.unit_process_id
 WHERE p.part_code <> 'P-PIN-C';
INSERT INTO standard_version (standard_id, version_no, charge_qty, running_time_min, effective_from, is_current)
SELECT s.standard_id, 1,
       CASE p.part_code WHEN 'P-GEAR-A' THEN 400 WHEN 'P-SHAFT-B' THEN 150 ELSE 60 END,
       CASE u.unit_process_code WHEN 'WASH' THEN 60 WHEN 'TEMP' THEN 180
            ELSE CASE p.part_code WHEN 'P-RING-D' THEN 600 WHEN 'P-SHAFT-B' THEN 480 ELSE 420 END END,
       '2026-01-01', 1
  FROM standard s JOIN part p ON p.part_id = s.part_id JOIN unit_process u ON u.unit_process_id = s.unit_process_id;

INSERT INTO sales_order (sales_order_no, order_date, due_date, customer_id)
SELECT CONCAT('SO-DEV-', c.customer_code), CURDATE(), CURDATE() + INTERVAL c.customer_id + 2 DAY, c.customer_id FROM customer c;

INSERT INTO sales_order_item (order_item_no, sales_order_id, line_no, part_id, heat_process_version_id, order_qty, priority, part_name_snapshot)
SELECT CONCAT('I', DATE_FORMAT(CURDATE(), '%y%m%d'), '-', LPAD(v.n, 3, '0')), so.sales_order_id, v.line, p.part_id,
       (SELECT heat_process_version_id FROM heat_process_version LIMIT 1), v.qty, v.pri, p.part_name
  FROM (SELECT 1 n, 'C-HD' c, 1 line, 'P-GEAR-A' part, 1000 qty, 1 pri
        UNION ALL SELECT 2, 'C-HD', 2, 'P-SHAFT-B', 300, 2
        UNION ALL SELECT 3, 'C-SM', 1, 'P-RING-D', 120, 3
        UNION ALL SELECT 4, 'C-SM', 2, 'P-PIN-C', 500, 1
        UNION ALL SELECT 5, 'C-DY', 1, 'P-GEAR-A', 600, 0
        UNION ALL SELECT 6, 'C-DY', 2, 'P-SHAFT-B', 150, 1) v
  JOIN customer c ON c.customer_code = v.c
  JOIN sales_order so ON so.customer_id = c.customer_id
  JOIN part p ON p.part_code = v.part;

-- 진행 중 작업 1건 (가스로1, 2시간 전 시작, 예상 7시간) → 뒤 계획은 예상 종료 이후로 배치됨
INSERT INTO production_work (lot_no, lot_seq, unit_process_id, equipment_id, is_main_process, work_date, status, actual_start_at, expected_duration_min)
SELECT CONCAT(DATE_FORMAT(CURDATE(), '%y%m%d'), '-B01-001'), 1, u.unit_process_id, e.equipment_id, 1, CURDATE(), 'INPUT',
       DATE_FORMAT(NOW() - INTERVAL 2 HOUR, '%Y-%m-%d %H:%i:00'), 420
  FROM unit_process u, equipment e WHERE u.unit_process_code = 'CARB' AND e.equipment_code = 'B01';

-- 가스로2 계획 비가동 (내일 10:00~14:00)
INSERT INTO equipment_downtime (equipment_id, downtime_date, started_at, ended_at, is_planned, remark)
SELECT equipment_id, CURDATE() + INTERVAL 1 DAY, TIMESTAMP(CURDATE() + INTERVAL 1 DAY, '10:00:00'),
       TIMESTAMP(CURDATE() + INTERVAL 1 DAY, '14:00:00'), 1, '정기 점검'
  FROM equipment WHERE equipment_code = 'B02';
