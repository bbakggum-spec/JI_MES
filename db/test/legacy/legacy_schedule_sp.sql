-- =====================================================================
-- 구 스케줄 SP 비교용 (설계 §7, §15.1 S1 검증) — 신규 Scheduling Service 결과와 1:1 비교하는 테스트 전용
--   * 출처: D:\Programming\ProductManager\MariaDB\backup_260409_3.sql 23~141행 (SP 본문 원문 그대로)
--   * 테이블은 SP가 읽고 쓰는 컬럼만 남긴 최소 구조 (workplan 26828행, t_work 26392행 기준). 데이터 없음
--   * 운영 DB(bbakggum)와 무관. 테스트가 개발 인스턴스에 jimes_legacy_ref_test 로 만들어 쓴다
-- =====================================================================

CREATE TABLE workplan (
  planno int(11) NOT NULL AUTO_INCREMENT,
  templotno varchar(50) NOT NULL DEFAULT '',
  workdate date NOT NULL,
  equipmentid int(11) NOT NULL,
  runningtime decimal(10,2) NOT NULL COMMENT '작업소요시간(시간)',
  starttime datetime NOT NULL,
  endtime datetime NOT NULL,
  sequence int(11) NOT NULL DEFAULT 1,
  modifieddate datetime DEFAULT current_timestamp(),
  PRIMARY KEY (planno)
);

CREATE TABLE t_work (
  workid int(11) NOT NULL AUTO_INCREMENT,
  equipmentid int(11) NOT NULL DEFAULT 0,
  starttime datetime NOT NULL,
  endtime datetime DEFAULT NULL,
  PRIMARY KEY (workid)
);

DELIMITER //
CREATE PROCEDURE `sp_RecalculateWorkPlanSequence`(
    IN p_equipmentid INT,
    IN p_workdate    DATE
)
BEGIN
    CREATE TEMPORARY TABLE tmp_seq AS
        SELECT planno,
               ROW_NUMBER() OVER (ORDER BY starttime, planno) AS newseq
        FROM   workplan
        WHERE  equipmentid = p_equipmentid
          AND  workdate    = p_workdate;

    UPDATE workplan wp
    JOIN   tmp_seq  t ON wp.planno = t.planno
    SET    wp.sequence     = t.newseq,
           wp.modifieddate = NOW();

    DROP TEMPORARY TABLE IF EXISTS tmp_seq;
END//
DELIMITER ;

DELIMITER //
CREATE PROCEDURE `sp_RecalculateWorkPlanTimes`(
    IN p_equipmentid  INT,
    IN p_workdate     DATE,
    IN p_fromsequence INT
)
BEGIN
    DECLARE v_planno      INT;
    DECLARE v_runningtime DECIMAL(10,2);
    DECLARE v_starttime   DATETIME;
    DECLARE v_endtime     DATETIME;
    DECLARE v_nextdate    DATE;
    DECLARE v_workdate    DATE;
    DECLARE v_done        INT DEFAULT FALSE;

    DECLARE cur CURSOR FOR
        SELECT planno, runningtime
        FROM   workplan
        WHERE  equipmentid = p_equipmentid
          AND  workdate    = p_workdate
          AND  sequence   >= p_fromsequence
        ORDER  BY sequence;

    DECLARE CONTINUE HANDLER FOR NOT FOUND SET v_done = TRUE;

    SET v_workdate = p_workdate;

    -- 시작 기준시간 결정
    IF p_fromsequence <= 1 THEN
        -- Work(실적) + WorkPlan(배정) 최대 종료시간
        SELECT GREATEST(
            COALESCE(
                (SELECT MAX(endtime) FROM t_work
                 WHERE  equipmentid = p_equipmentid
                   AND  DATE(starttime) = p_workdate),
                TIMESTAMP(p_workdate, '08:00:00')
            ),
            COALESCE(
                (SELECT MAX(endtime) FROM workplan
                 WHERE  equipmentid = p_equipmentid
                   AND  workdate    = p_workdate
                   AND  sequence    < p_fromsequence),
                TIMESTAMP(p_workdate, '08:00:00')
            )
        ) INTO v_starttime;
    ELSE
        SELECT endtime INTO v_starttime
        FROM   workplan
        WHERE  equipmentid = p_equipmentid
          AND  workdate    = p_workdate
          AND  sequence    = p_fromsequence - 1;
    END IF;

    IF v_starttime IS NULL THEN
        SET v_starttime = TIMESTAMP(p_workdate, '08:00:00');
    END IF;

    OPEN cur;

    read_loop: LOOP
        FETCH cur INTO v_planno, v_runningtime;
        IF v_done THEN LEAVE read_loop; END IF;

        SET v_endtime = DATE_ADD(v_starttime, INTERVAL v_runningtime HOUR);

        -- 날짜 넘김: 다음날 00:00 이후면 다음날 08:00 으로 이동
        IF v_endtime >= TIMESTAMP(DATE_ADD(v_workdate, INTERVAL 1 DAY), '00:00:00') THEN
            SET v_nextdate  = DATE_ADD(v_workdate, INTERVAL 1 DAY);
            SET v_starttime = TIMESTAMP(v_nextdate, '08:00:00');
            SET v_endtime   = DATE_ADD(v_starttime, INTERVAL v_runningtime HOUR);

            UPDATE workplan
            SET    workdate      = v_nextdate,
                   starttime     = v_starttime,
                   endtime       = v_endtime,
                   modifieddate  = NOW()
            WHERE  planno = v_planno;

            SET v_workdate = v_nextdate;
        ELSE
            UPDATE workplan
            SET    starttime    = v_starttime,
                   endtime      = v_endtime,
                   modifieddate = NOW()
            WHERE  planno = v_planno;
        END IF;

        SET v_starttime = v_endtime;
    END LOOP;

    CLOSE cur;
END//
DELIMITER ;
