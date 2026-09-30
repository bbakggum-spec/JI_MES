USE bbakggum_v2;
-- 기준정보
INSERT INTO customer(customer_code,customer_name) VALUES ('C1','고객A'),('C2','고객B');
INSERT INTO part(part_code,part_name) VALUES ('P1','기어A'),('P2','샤프트B');
INSERT INTO equipment_type(equipment_type_code,equipment_type_name) VALUES ('WASH','세척기'),('GAS','가스로'),('TEMP','템퍼링로');
INSERT INTO equipment(equipment_type_id,equipment_code,equipment_initial,equipment_name) VALUES
 (1,'W01','W01','세척기1'),(2,'B01','B01','가스로1'),(2,'B02','B02','가스로2'),(3,'T01','T01','템퍼링로1');
INSERT INTO unit_process(unit_process_code,unit_process_name) VALUES ('WASH','세척'),('CARB','침탄'),('TEMP','템퍼링');
INSERT INTO heat_process(heat_process_code,heat_process_name) VALUES ('HP-CARB','침탄');
INSERT INTO heat_process_version(heat_process_id,version_no,effective_from,is_current) VALUES (1,1,'2026-01-01',1);
INSERT INTO heat_process_operation(heat_process_version_id,sequence_no,unit_process_id,is_main_process) VALUES (1,10,1,0),(1,20,2,1),(1,30,3,0);
-- 단계 템플릿 (가스로 침탄) + 조건 항목
INSERT INTO step_template(step_template_code,step_template_name,unit_process_id,equipment_type_id) VALUES ('ST-GAS-CARB','가스로 침탄',2,2);
INSERT INTO step_template_item(step_template_id,sequence_no,step_name) VALUES (1,1,'승온'),(1,2,'균열'),(1,3,'침탄'),(1,4,'확산'),(1,5,'강온'),(1,6,'소입유지'),(1,7,'소입');
INSERT INTO condition_item(condition_item_code,condition_item_name,unit_code) VALUES ('TEMP','온도','℃'),('TIME','시간','min'),('CP','CP','%');
-- 작업표준: 기어A / 샤프트B (가스로 침탄) — 조건이 서로 다름
INSERT INTO standard(standard_code,standard_name,part_id,heat_process_id,unit_process_id,equipment_type_id) VALUES
 ('STD-P1-CARB','기어A 침탄',1,1,2,2),('STD-P2-CARB','샤프트B 침탄',2,1,2,2);
INSERT INTO standard_version(standard_id,version_no,step_template_id,charge_qty,running_time_min,effective_from,is_current) VALUES
 (1,1,1,1200,480,'2026-01-01',1),(2,1,1,1000,420,'2026-01-01',1);
-- 입력표 = Version 별 스텝(열)·관리항목(행) — 템플릿에서 불러온 뒤 표준마다 따로 보관
INSERT INTO standard_version_step(standard_version_id,sequence_no,step_name)
 SELECT v.standard_version_id, i.sequence_no, i.step_name FROM standard_version v JOIN step_template_item i ON i.step_template_id = v.step_template_id;
INSERT INTO standard_version_item(standard_version_id,sequence_no,condition_item_id) VALUES (1,1,1),(1,2,2),(1,3,3),(2,1,1),(2,2,2),(2,3,3);
INSERT INTO standard_condition(standard_version_id,step_no,condition_item_id,condition_value) VALUES
 (1,3,1,'920'),(1,3,2,'180'),(1,3,3,'1.10'),(1,4,1,'920'),(1,4,2,'60'),(1,4,3,'0.85'),
 (2,3,1,'930'),(2,3,2,'150'),(2,3,3,'1.05');
-- 검사기준 / 성적서 양식
INSERT INTO inspection_standard(part_id,customer_id) VALUES (1,1);
INSERT INTO inspection_standard_version(inspection_standard_id,version_no,effective_from,is_current) VALUES (1,1,'2026-01-01',1);
INSERT INTO inspection_criteria(inspection_standard_version_id,sequence_no,item_type,item_name,scale,range_type,lower_limit,upper_limit,sample_count) VALUES
 (1,1,'경도','표면경도','HRC','BETWEEN',58,62,5);
INSERT INTO print_template(print_template_name,print_purpose_id) SELECT '고객A 성적서', print_purpose_id FROM print_purpose WHERE purpose_code='INSPECTION_REPORT';
SET @tA := (SELECT print_template_id FROM print_template WHERE print_template_name='고객A 성적서');
INSERT INTO print_template_version(print_template_id,version_no,file_name,file_content,file_hash,file_size,placeholders_json,is_current) VALUES
 (@tA,1,'고객A_성적서.xlsx',X'504B0304',SHA2('v1',256),4,'["InspectionNo","PartName","T1_1_P1"]',0),
 (@tA,2,'고객A_성적서_v2.xlsx',X'504B0304',SHA2('v2',256),4,'["InspectionNo","PartName","T1_1_P1","ConvertLot"]',1);
INSERT INTO part_print_template(part_id,customer_id,print_purpose_id,print_template_id,is_default) SELECT 1,1,print_purpose_id,@tA,1 FROM print_purpose WHERE purpose_code='INSPECTION_REPORT';
INSERT INTO print_template(print_template_name,print_purpose_id) SELECT '작업지시서', print_purpose_id FROM print_purpose WHERE purpose_code='WORK_ORDER';
INSERT INTO part_print_template(part_id,customer_id,print_purpose_id,print_template_id,is_default) SELECT 1,NULL,print_purpose_id,(SELECT print_template_id FROM print_template WHERE print_template_name='작업지시서'),1 FROM print_purpose WHERE purpose_code='WORK_ORDER';
-- 사용자 확장: 새 용도 '열처리 기록지' (작업 LOT 데이터 공급원) 추가
INSERT INTO print_purpose(purpose_code,purpose_name,print_data_source_id) SELECT 'HT_RECORD','열처리 기록지',print_data_source_id FROM print_data_source WHERE data_source_code='PRODUCTION_WORK';
INSERT INTO print_template(print_template_name,print_purpose_id) SELECT '열처리 기록지 A형', print_purpose_id FROM print_purpose WHERE purpose_code='HT_RECORD';
INSERT INTO print_template_version(print_template_id,version_no,file_name,file_content,file_hash,file_size,is_current) VALUES ((SELECT print_template_id FROM print_template WHERE print_template_name='열처리 기록지 A형'),1,'ht.xlsx',X'504B0304',SHA2('ht',256),4,1);
-- 수주(=입고)
INSERT INTO sales_order(sales_order_no,order_date,customer_id) VALUES ('SO-0930-01','2026-09-30',1),('SO-0930-02','2026-09-30',2);
INSERT INTO sales_order_item(order_item_no,sales_order_id,line_no,part_id,order_qty,customer_lot) VALUES ('I260930-001',1,1,1,1000,'CL-77'),('I260930-002',1,2,2,600,'CL-78'),('I260930-003',2,1,1,400,'K-5521');
-- 작업 LOT (lot_no = YYMMDD-설비이니셜-순번)
INSERT INTO production_work(lot_no,lot_seq,unit_process_id,equipment_id,is_main_process,is_rework,work_date,status,step_template_id,standard_version_id,is_standard_fixed,submit_lot_no) VALUES
 ('260930-W01-001',1,1,1,0,0,'2026-09-30','COMPLETED',NULL,NULL,0,NULL),
 ('260930-B01-001',1,2,2,1,0,'2026-09-30','COMPLETED',1,1,1,'GA0930-A'),
 ('260930-B02-001',1,2,3,1,0,'2026-09-30','COMPLETED',1,1,1,'GA0930-B'),
 ('260930-T01-001',1,3,4,0,0,'2026-09-30','COMPLETED',NULL,NULL,0,NULL),
 ('261001-B01-001',1,2,2,1,1,'2026-10-01','INPUT',1,1,1,NULL);
-- 투입: 세척=수주번호, 침탄(주)=수주번호→자신, 템퍼링=주LOT 입력 후 목록 선택, 재작업=원 LOT
INSERT INTO production_work_input(production_work_id,sales_order_item_id,main_work_id,main_input_id,origin_work_id,is_standard_basis,input_qty) VALUES
 (1,1,NULL,NULL,NULL,0,1000),(1,2,NULL,NULL,NULL,0,600),(1,3,NULL,NULL,NULL,0,400),
 (2,1,2,NULL,NULL,1,600),(2,2,2,NULL,NULL,0,600),
 (3,1,3,NULL,NULL,1,400),(3,3,3,NULL,NULL,0,400),
 (4,1,2,4,NULL,0,570),(4,2,2,5,NULL,0,600),(4,1,3,6,NULL,0,400),(4,3,3,7,NULL,0,390),
 (5,1,5,NULL,2,1,30),(5,3,5,NULL,3,0,10);
-- 확정 조건 (기어A 표준 선택 → 복사, 확산 CP 수정)
INSERT INTO production_work_condition(production_work_id,condition_item_id,step_sequence_no,step_name_snapshot,set_value) VALUES
 (2,1,3,'침탄','920'),(2,2,3,'침탄','180'),(2,3,3,'침탄','1.10'),(2,1,4,'확산','920'),(2,2,4,'확산','60'),(2,3,4,'확산','0.80');
-- 주 LOT B01에 수주 2건 추가 투입 → 투입 대상 4개 (input id 14, 15)
INSERT INTO sales_order(sales_order_no,order_date,customer_id) VALUES ('SO-0930-03','2026-09-30',1);
INSERT INTO sales_order_item(order_item_no,sales_order_id,line_no,part_id,order_qty) VALUES ('I260930-004',3,1,1,100),('I260930-005',3,2,2,100);
INSERT INTO production_work_input(production_work_id,sales_order_item_id,main_work_id,input_qty) VALUES (2,4,2,100),(2,5,2,100);
-- 검사 1: 4개 중 3개 선택 / 검사 2: 나머지 1개
INSERT INTO inspection(inspection_no,inspection_type,inspection_date,production_work_id,inspection_standard_version_id,status,decision) VALUES
 ('TO260930-001','OUTGOING','2026-09-30',2,1,'COMPLETED','FAIL'),('TO260930-002','OUTGOING','2026-09-30',2,1,'COMPLETED','PASS');
INSERT INTO inspection_target(inspection_id,sub_no,production_work_input_id,sales_order_item_id,customer_id,print_template_id,submit_lot_no_snapshot,part_name_snapshot,report_issued_at,report_issue_count) VALUES
 (1,1,4,1,1,@tA,'GA0930-A','기어A',NULL,0),
 (1,2,5,2,1,@tA,'GA0930-A','샤프트B','2026-09-30 17:00',1),
 (1,3,14,4,1,@tA,'GA0930-A','기어A','2026-09-30 17:00',1),
 (2,1,15,5,1,@tA,'GA0930-A','샤프트B','2026-09-30 18:00',1);
INSERT INTO inspection_item(inspection_id,inspection_criteria_id,sequence_no,item_type,item_name,decision) VALUES (1,1,1,'경도','표면경도','FAIL');
INSERT INTO inspection_measurement(inspection_item_id,sample_no,measured_value) VALUES (1,1,59.5),(1,2,57.2),(1,3,60.1);
INSERT INTO defect_occurrence(sales_order_item_id,production_work_id,production_work_input_id,inspection_target_id,main_work_id,defect_date,defect_qty,status,decision,rework_input_id) VALUES
 (1,2,4,1,2,'2026-09-30',30,'REWORKING','REWORK',12),
 (3,3,7,NULL,3,'2026-09-30',10,'REWORKING','REWORK',13),
 (2,4,9,NULL,2,'2026-09-30',5,'OPEN',NULL,NULL);
-- 출하 전표 2건 (9월), 업체별 마감: 1건만 선택 → 1건 이월
INSERT INTO shipment_closing(closing_no,customer_id,closing_date,closing_year,closing_month,closing_status) VALUES ('CL-C1-2609',1,'2026-09-25',2026,9,'CLOSED');
INSERT INTO shipment(shipment_no,shipment_date,customer_id,status,shipment_closing_id,closing_status,closing_year,closing_month) VALUES
 ('SH-0924-01','2026-09-24',1,'SHIPPED',1,'CLOSED',2026,9),('SH-0930-01','2026-09-30',1,'SHIPPED',NULL,'CARRIED_OVER',2026,10);
INSERT INTO shipment_item(shipment_id,line_no,sales_order_item_id,main_work_id,shipment_qty,submit_lot_no_snapshot) VALUES
 (1,1,4,2,100,'GA0930-A'),(2,1,2,2,595,'GA0930-A');

SELECT lot_no, unit_process_name AS proc, status, display_status, good_qty, inspection_count AS insp_targets, open_defect_count AS open_def, shipment_qty AS shipped FROM vw_work_lot_status ORDER BY production_work_id;
SELECT '검사 내역 / 성적서 (대상별 1장)' AS q;
SELECT i.inspection_no, t.sub_no, t.part_name_snapshot AS part, t.sales_order_item_id AS soi, i.decision, p.print_template_name AS 양식, t.report_issued_at
  FROM inspection i JOIN inspection_target t USING (inspection_id) LEFT JOIN print_template p USING (print_template_id)
 ORDER BY i.inspection_no, t.sub_no;
SELECT '품목별 출력 양식 (용도별 기본)' AS q;
SELECT pp.part_id, pp.customer_id, pu.purpose_name, p.print_template_name FROM part_print_template pp JOIN print_template p USING (print_template_id) JOIN print_purpose pu ON pu.print_purpose_id = pp.print_purpose_id WHERE pp.is_default = 1;
SELECT '마감 / 이월' AS q;
SELECT s.shipment_no, s.shipment_date, COALESCE(c.closing_no,'-') AS closing, s.closing_status, s.closing_year, s.closing_month
  FROM shipment s LEFT JOIN shipment_closing c USING (shipment_closing_id);
-- 성적서 발행 이력 (현재 버전 v2 사용)
INSERT INTO print_log(print_template_version_id,print_purpose_id,source_table,source_id,output_format,data_snapshot_json)
SELECT (SELECT print_template_version_id FROM print_template_version WHERE print_template_id=@tA AND is_current=1), print_purpose_id, 'inspection_target', 2, 'PDF', '{"PartName":"샤프트B","ConvertLot":"GA0930-A"}' FROM print_purpose WHERE purpose_code='INSPECTION_REPORT';
SELECT '출력 용도 (시스템 + 사용자 추가)' AS q;
SELECT pu.purpose_code, pu.purpose_name, ds.data_source_code, pu.is_system FROM print_purpose pu JOIN print_data_source ds USING (print_data_source_id) ORDER BY pu.sort_order, pu.print_purpose_id;
SELECT '양식 버전 / 발행 이력' AS q;
SELECT t.print_template_name, v.version_no, v.file_name, v.is_current, (SELECT COUNT(*) FROM print_log l WHERE l.print_template_version_id=v.print_template_version_id) AS printed
  FROM print_template t JOIN print_template_version v USING (print_template_id) ORDER BY t.print_template_id, v.version_no;


SELECT '양식 등록 방식별 (EXCEL / FIXED)' AS q;
SELECT p.purpose_name, t.print_template_name, t.template_kind, t.renderer_key, v.version_no,
       IF(t.template_kind='FIXED', JSON_VALUE(v.layout_options_json,'$.items_per_page'), v.file_name) AS 내용
  FROM print_template t JOIN print_purpose p USING (print_purpose_id)
  JOIN print_template_version v ON v.print_template_id = t.print_template_id AND v.is_current = 1
 ORDER BY t.template_kind, p.sort_order;
SELECT '관리자 설정 (유효값)' AS q;
SELECT category, setting_key, COALESCE(setting_value, default_value) AS value, unit_label FROM system_setting ORDER BY category, sort_order;

SELECT '검사 공통 결과 (대상 4건 / 측정 항목은 검사 단위)' AS q;
SELECT i.inspection_no, i.inspection_type, i.decision, COUNT(DISTINCT t.inspection_target_id) AS targets, COUNT(DISTINCT ii.inspection_item_id) AS items
  FROM inspection i JOIN inspection_target t USING (inspection_id) LEFT JOIN inspection_item ii ON ii.inspection_id = i.inspection_id
 GROUP BY i.inspection_id;
