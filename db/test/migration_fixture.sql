-- =====================================================================
-- 이관 스크립트 시험용 구 데이터 (설계 §25.5) — 개발 복원본 bbakggum_legacy(3307) 전용
--   2026-04-09 덤프에는 출하·부적합·비가동·보전 행이 없어 그 경로를 이 파일로 시험한다.
--   순서: dev-db.ps1 legacy -Dump <덤프> → 이 파일 → dev-db.ps1 migrate -Fresh (검증 통과해야 함)
--         → 아래 "재실행 시험" 블록 → dev-db.ps1 migrate (추가분·갱신분 반영 + 검증 통과)
--   끝나면 legacy 를 다시 복원해 원본 상태로 되돌린다. 운영 DB 이름(bbakggum)에는 절대 실행하지 않는다.
-- =====================================================================
USE bbakggum_legacy;
SET SESSION sql_mode = '';

-- 2026-04 이후 구 스키마의 마감 컬럼 (덤프에 없으면 추가 — 01_prepare 의 스키마 차이 흡수 시험)
ALTER TABLE t_outcomesum ADD COLUMN IF NOT EXISTS closingmonth DATE NULL, ADD COLUMN IF NOT EXISTS closingstatus INT NULL, ADD COLUMN IF NOT EXISTS sumaspart TINYINT NULL;

-- 부적합: 재처리 판정 / 없는 처리 문자열(CODE_UNMATCHED) / 없는 LOT(SKIPPED)
INSERT INTO t_defect (defectid, defectdate, inspectionno, worksubid, lotno, incomeno, customerid, inputqt, decision, decisionmemo, reworkstep, determiner, determinerdate, check_complete)
VALUES (901, '2026-04-10', NULL, 18, '260409-I01-002', 'I260409-002', 110687, 5, '불합격', '경도 미달', '재처리', '홍길동', '2026-04-10 10:00', 0),
       (902, '2026-04-10', NULL, NULL, '260409-I01-003', 'I260409-001', 110687, 3, '불합격', NULL, 'xxx', NULL, NULL, 1),
       (903, '2026-04-10', NULL, NULL, '999999-X01-001', 'I999', 110687, 1, NULL, NULL, NULL, NULL, NULL, 0);
-- 출하 전표: 마감완료 / 미마감 / 이월(마감월 없음 → 출하월, VALUE_CHANGED)
INSERT INTO t_outcomesum (outcomesumid, outcomeno, outdate, customerid, customername, totalqt, totalweight, totalamount, closingdate, remark, closingmonth, closingstatus, sumaspart)
VALUES (901, 'O260415-001', '2026-04-15', 110687, '(주)드림텍', 150, 10.5, 102000, '2026-04-30', 'test', '2026-04-01', 1, 1),
       (902, 'O260416-001', '2026-04-16', 110687, '(주)드림텍', 20, 2, 13600, NULL, NULL, NULL, 0, 0),
       (903, 'O260417-001', '2026-04-17', 110687, '(주)드림텍', 1, 0, 680, NULL, NULL, NULL, 2, 0);
-- 출하 상세: 시험편 / kg 단가 / 없는 전표(SKIPPED)
INSERT INTO t_outcome (outcomeid, outcomeno, outdate, subno, incomeno, customerid, partid, partname, unit, unitprice, outqt, outweight, testspecimen, outamount)
VALUES (901, 'O260415-001', '2026-04-15', 1, 'I260406-004', 110687, 32435, 'SHAFT', 'EA', 680, 100, 7, 0, 68000),
       (902, 'O260415-001', '2026-04-15', 2, 'I260409-002', 110687, 32435, 'SHAFT', 'EA', 680, 50, 3.5, 2, 34000),
       (903, 'O260416-001', '2026-04-16', 1, 'I260406-005', 110687, 32435, 'SHAFT', 'kg', 680, 20, 2, 0, 13600),
       (904, 'O999', '2026-04-16', 1, 'I260406-005', 110687, 32435, 'SHAFT', 'EA', 680, 1, 0, 0, 680),
       (905, 'O260417-001', '2026-04-17', 1, 'I260406-005', 110687, 32435, 'SHAFT', 'EA', 680, 1, 0, 0, 680);
-- 비가동: 자정 넘김 / 없는 설비(SKIPPED)
INSERT INTO t_downtime (id, downdate, equiptype, equipname, start_time, end_time, duration, reason_code, memo, worker)
VALUES (901, '2026-04-11', '가스로', '가스로1호기', '22:00', '02:00', 240, '고장', '버너', '김철수'),
       (902, '2026-04-11', '가스로', '없는설비', '08:00', '09:00', 60, '대기', NULL, NULL);
-- 보전: 설비 / 측정기구 점검(SKIPPED)
INSERT INTO t_maintenance (id, equipmentid, repairdate, repairpart, repairtype, worker, repaircost, nextrepairdate, repairmemo, recordtype, instrumentid)
VALUES (901, 3, '2026-04-12', '버너', '수리', '박', 50000, '2026-10-12', '교체', 'equipment', NULL),
       (902, NULL, '2026-04-12', NULL, '교정', NULL, NULL, NULL, NULL, 'instrument', 1);

-- ---------------------------------------------------------------------
-- 재실행 시험 (migrate -Fresh 한 뒤 이 블록만 실행하고 migrate) — 기대:
--   수주 I260409-003 이 기존 수주(MIG260409-110687) 3행으로 추가 / 재작업 LOT 260410-I01-001 의 원 LOT = 260409-I01-002
--   전표 O260415-001 에 3행 추가·공급가액 108800 / O260416-001 마감완료로 바뀌어 같은 마감에 연결 / 거래처명·LOT 진행 상태 갱신
-- ---------------------------------------------------------------------
-- INSERT INTO t_income (incomeid, incomeno, indate, insubno, customerid, partid, partname, unitprice, unit, incomeqt, weight)
-- VALUES (901, 'I260409-003', '2026-04-09', 3, 110687, 32435, 'SHAFT', 680, 'EA', 40, 4);
-- INSERT INTO t_work (workid, workdate, progressstep, subno, lotno, unitprocessname, starttime, equipmentid, equipmentname, isrework)
-- VALUES (901, '2026-04-10', '배정', 1, '260410-I01-001', '고주파', '2026-04-10 08:00', 19, '고주파1호기', 1);
-- INSERT INTO t_worksub (worksubid, lotno, incomeno, partid, inputqt, originelotno) VALUES (901, '260410-I01-001', 'I260409-003', 32435, 40, '260409-I01-002');
-- INSERT INTO t_outcome (outcomeid, outcomeno, outdate, subno, incomeno, customerid, partid, partname, unit, unitprice, outqt, outweight, testspecimen, outamount)
-- VALUES (906, 'O260415-001', '2026-04-15', 3, 'I260409-003', 110687, 32435, 'SHAFT', 'EA', 680, 10, 1, 0, 6800);
-- UPDATE t_outcomesum SET totalamount = 108800, totalqt = 160 WHERE outcomesumid = 901;
-- UPDATE t_outcomesum SET closingstatus = 1, closingmonth = '2026-04-01' WHERE outcomesumid = 902;
-- UPDATE t_customer SET customername = CONCAT(customername, '*') WHERE customerid = 110687;
-- UPDATE t_work SET progressstep = '완료', endtime = '2026-04-09 20:00' WHERE workid = 8;
