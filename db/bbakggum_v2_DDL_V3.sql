-- =====================================================================
-- bbakggum_v2 신규 DB DDL  (설계안 V3 기준)
--   * 설계 문서 : bbakggum_신규 설계안_V3.md
--   * 대상      : MariaDB 10.10+ (utf8mb4_uca1400_ai_ci 사용) — 검증: 11.6
--   * 원칙      : 테이블은 FK 의존 순서대로 생성한다 (FOREIGN_KEY_CHECKS 비활성화 불필요)
--   * 운영 DB(bbakggum)에 직접 실행 금지. 신규 DB에서만 실행한다.
-- =====================================================================

CREATE DATABASE IF NOT EXISTS `bbakggum_v2`
  DEFAULT CHARACTER SET utf8mb4
  COLLATE utf8mb4_uca1400_ai_ci;

USE `bbakggum_v2`;

-- =====================================================================
-- 1. 시스템 / 공통
-- =====================================================================

CREATE TABLE company (
    company_id          BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    company_name        VARCHAR(100) NOT NULL,
    ceo_name            VARCHAR(50)  NULL,
    business_no         VARCHAR(20)  NULL,
    business_type       VARCHAR(50)  NULL,
    business_item       VARCHAR(100) NULL,
    phone               VARCHAR(50)  NULL,
    fax                 VARCHAR(50)  NULL,
    email               VARCHAR(100) NULL,
    address             VARCHAR(200) NULL,
    address_detail      VARCHAR(200) NULL,
    stamp_image         MEDIUMBLOB   NULL COMMENT '도장 이미지 (구 stamp_path — PC 경로 대신 DB 보관)',
    stamp_file_name     VARCHAR(255) NULL,
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          BIGINT UNSIGNED NULL,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    updated_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (company_id),
    UNIQUE KEY uk_company_business_no (business_no)
) ENGINE=InnoDB COMMENT='자사 정보 (t_company)';

CREATE TABLE common_code_group (
    common_code_group_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    group_code          VARCHAR(50)  NOT NULL,
    group_name          VARCHAR(100) NOT NULL,
    description         VARCHAR(255) NULL,
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    PRIMARY KEY (common_code_group_id),
    UNIQUE KEY uk_common_code_group_code (group_code)
) ENGINE=InnoDB COMMENT='공통코드 그룹 (t_combolist)';

CREATE TABLE common_code (
    common_code_id      BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    common_code_group_id BIGINT UNSIGNED NOT NULL,
    code                VARCHAR(50)  NOT NULL,
    code_name           VARCHAR(100) NOT NULL,
    sort_order          INT          NOT NULL DEFAULT 0,
    attr_json           JSON         NULL COMMENT '코드별 부가 속성 (예: 검사항목유형의 양식 prefix T1/C1)',
    is_system           TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '코드가 로직에서 참조됨 — code 변경/삭제 불가, 표시명만 수정 가능',
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    remark              VARCHAR(255) NULL,
    PRIMARY KEY (common_code_id),
    UNIQUE KEY uk_common_code (common_code_group_id, code),
    CONSTRAINT fk_common_code_group
        FOREIGN KEY (common_code_group_id) REFERENCES common_code_group (common_code_group_id)
) ENGINE=InnoDB COMMENT='공통코드 값 (t_combolist)';

CREATE TABLE system_setting (
    system_setting_id   BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    setting_key         VARCHAR(100) NOT NULL COMMENT '분류.항목 (예: schedule.refresh_interval_sec)',
    category            VARCHAR(50)  NOT NULL COMMENT '관리 화면 분류',
    setting_name        VARCHAR(100) NOT NULL COMMENT '화면 표시명',
    value_type          VARCHAR(20)  NOT NULL DEFAULT 'STRING',
    setting_value       TEXT         NULL COMMENT 'NULL이면 default_value 사용',
    default_value       TEXT         NOT NULL COMMENT '시스템 기본값 (초기화 기준)',
    min_value           DECIMAL(18,6) NULL,
    max_value           DECIMAL(18,6) NULL,
    unit_label          VARCHAR(20)  NULL,
    description         VARCHAR(500) NULL,
    is_editable         TINYINT(1)   NOT NULL DEFAULT 1 COMMENT '관리자 화면 수정 가능 여부',
    requires_restart    TINYINT(1)   NOT NULL DEFAULT 0,
    sort_order          INT          NOT NULL DEFAULT 0,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    updated_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (system_setting_id),
    UNIQUE KEY uk_system_setting_key (setting_key),
    KEY ix_system_setting_category (category, sort_order),
    CONSTRAINT ck_system_setting_value_type
        CHECK (value_type IN ('STRING','INT','DECIMAL','BOOL','TIME','PATH','JSON'))
) ENGINE=InnoDB COMMENT='관리자 설정 (t_system_settings) — 코드 하드코딩 값을 여기로 이동, 변경은 audit_log';

CREATE TABLE department (
    department_id       BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    department_code     VARCHAR(30)  NOT NULL,
    department_name     VARCHAR(50)  NOT NULL,
    sort_order          INT          NOT NULL DEFAULT 0,
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (department_id),
    UNIQUE KEY uk_department_code (department_code)
) ENGINE=InnoDB COMMENT='부서 (t_dept, 로그인 시 메뉴 접근 기준) — t_department 미사용';

CREATE TABLE job_position (
    job_position_id     BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    job_position_code   VARCHAR(30)  NOT NULL,
    job_position_name   VARCHAR(50)  NOT NULL,
    sort_order          INT          NOT NULL DEFAULT 0,
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    PRIMARY KEY (job_position_id),
    UNIQUE KEY uk_job_position_code (job_position_code)
) ENGINE=InnoDB COMMENT='직위';

CREATE TABLE employee (
    employee_id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    employee_code       VARCHAR(50)  NOT NULL,
    employee_name       VARCHAR(50)  NOT NULL,
    department_id       BIGINT UNSIGNED NULL,
    job_position_id     BIGINT UNSIGNED NULL,
    team_name           VARCHAR(50)  NULL COMMENT '기존 t_employee.part (소속 파트/조)',
    nationality         VARCHAR(50)  NULL,
    address             VARCHAR(100) NULL,
    phone               VARCHAR(50)  NULL,
    mobile_phone        VARCHAR(50)  NULL,
    employment_date     DATE         NULL,
    resignation_date    DATE         NULL,
    is_assignment_target TINYINT(1)  NOT NULL DEFAULT 0 COMMENT '작업자 배정 대상 여부',
    is_active           TINYINT(1)   NOT NULL DEFAULT 1 COMMENT '재직 여부',
    remark              TEXT         NULL,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          BIGINT UNSIGNED NULL,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    updated_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (employee_id),
    UNIQUE KEY uk_employee_code (employee_code),
    KEY ix_employee_department (department_id),
    KEY ix_employee_job_position (job_position_id),
    CONSTRAINT fk_employee_department
        FOREIGN KEY (department_id) REFERENCES department (department_id),
    CONSTRAINT fk_employee_job_position
        FOREIGN KEY (job_position_id) REFERENCES job_position (job_position_id)
) ENGINE=InnoDB COMMENT='사원 (t_employee)';

-- ---------------------------------------------------------------------
-- 1.1 사용자 / 권한 / 감사
-- ---------------------------------------------------------------------

CREATE TABLE app_user (
    app_user_id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    login_id            VARCHAR(50)  NOT NULL,
    password_hash       VARCHAR(255) NOT NULL,
    user_name           VARCHAR(50)  NOT NULL,
    employee_id         BIGINT UNSIGNED NULL,
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    last_login_at       DATETIME     NULL,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (app_user_id),
    UNIQUE KEY uk_app_user_login_id (login_id),
    KEY ix_app_user_employee (employee_id),
    CONSTRAINT fk_app_user_employee
        FOREIGN KEY (employee_id) REFERENCES employee (employee_id)
) ENGINE=InnoDB COMMENT='로그인 계정 (사원과 분리)';

CREATE TABLE role (
    role_id             BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    role_code           VARCHAR(50)  NOT NULL,
    role_name           VARCHAR(100) NOT NULL,
    description         VARCHAR(255) NULL,
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    PRIMARY KEY (role_id),
    UNIQUE KEY uk_role_code (role_code)
) ENGINE=InnoDB COMMENT='역할';

CREATE TABLE app_user_role (
    app_user_id         BIGINT UNSIGNED NOT NULL,
    role_id             BIGINT UNSIGNED NOT NULL,
    PRIMARY KEY (app_user_id, role_id),
    KEY ix_app_user_role_role (role_id),
    CONSTRAINT fk_app_user_role_user
        FOREIGN KEY (app_user_id) REFERENCES app_user (app_user_id),
    CONSTRAINT fk_app_user_role_role
        FOREIGN KEY (role_id) REFERENCES role (role_id)
) ENGINE=InnoDB COMMENT='사용자-역할 매핑';

CREATE TABLE menu (
    menu_id             BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    menu_key            VARCHAR(50)  NOT NULL,
    menu_name           VARCHAR(100) NOT NULL,
    parent_menu_id      BIGINT UNSIGNED NULL,
    route               VARCHAR(255) NULL,
    sort_order          INT          NOT NULL DEFAULT 0,
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    PRIMARY KEY (menu_id),
    UNIQUE KEY uk_menu_key (menu_key),
    KEY ix_menu_parent (parent_menu_id),
    CONSTRAINT fk_menu_parent
        FOREIGN KEY (parent_menu_id) REFERENCES menu (menu_id)
) ENGINE=InnoDB COMMENT='메뉴 (t_menu_catalog)';

CREATE TABLE role_menu (
    role_id             BIGINT UNSIGNED NOT NULL,
    menu_id             BIGINT UNSIGNED NOT NULL,
    can_read            TINYINT(1)   NOT NULL DEFAULT 1,
    can_create          TINYINT(1)   NOT NULL DEFAULT 0,
    can_update          TINYINT(1)   NOT NULL DEFAULT 0,
    can_delete          TINYINT(1)   NOT NULL DEFAULT 0,
    PRIMARY KEY (role_id, menu_id),
    KEY ix_role_menu_menu (menu_id),
    CONSTRAINT fk_role_menu_role
        FOREIGN KEY (role_id) REFERENCES role (role_id),
    CONSTRAINT fk_role_menu_menu
        FOREIGN KEY (menu_id) REFERENCES menu (menu_id)
) ENGINE=InnoDB COMMENT='역할별 메뉴 권한 (t_dept_menu 대체)';

CREATE TABLE audit_log (
    audit_log_id        BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    occurred_at         DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
    app_user_id         BIGINT UNSIGNED NULL,
    action_type         VARCHAR(30)  NOT NULL,
    table_name          VARCHAR(64)  NOT NULL,
    record_id           BIGINT UNSIGNED NOT NULL,
    before_json         JSON         NULL,
    after_json          JSON         NULL,
    reason              VARCHAR(255) NULL,
    client_ip           VARCHAR(45)  NULL,
    PRIMARY KEY (audit_log_id),
    KEY ix_audit_log_record (table_name, record_id),
    KEY ix_audit_log_user_time (app_user_id, occurred_at),
    CONSTRAINT ck_audit_log_action_type
        CHECK (action_type IN ('CREATE','UPDATE','DELETE','STATUS_CHANGE','CLOSE','REOPEN'))
) ENGINE=InnoDB COMMENT='변경 감사 로그';

CREATE TABLE migration_id_map (
    migration_id_map_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    legacy_table        VARCHAR(64)  NOT NULL,
    legacy_key          VARCHAR(200) NOT NULL COMMENT '구 PK (복합키는 | 로 연결)',
    new_table           VARCHAR(64)  NOT NULL,
    new_id              BIGINT UNSIGNED NOT NULL,
    migrated_at         DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (migration_id_map_id),
    UNIQUE KEY uk_migration_id_map (legacy_table, legacy_key, new_table),
    KEY ix_migration_id_map_new (new_table, new_id)
) ENGINE=InnoDB COMMENT='구 DB PK ↔ 신규 PK 매핑 (마이그레이션/검증/역동기화용)';

-- =====================================================================
-- 2. 기준정보
-- =====================================================================

CREATE TABLE customer (
    customer_id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    customer_code       VARCHAR(50)  NOT NULL,
    customer_type       VARCHAR(20)  NOT NULL DEFAULT 'SALES',
    customer_name       VARCHAR(100) NOT NULL,
    business_no         VARCHAR(20)  NULL,
    ceo_name            VARCHAR(50)  NULL,
    business_type       VARCHAR(50)  NULL,
    business_item       VARCHAR(50)  NULL,
    phone               VARCHAR(30)  NULL,
    fax                 VARCHAR(30)  NULL,
    email               VARCHAR(100) NULL,
    address             VARCHAR(255) NULL,
    address_detail      VARCHAR(255) NULL,
    closing_day         TINYINT UNSIGNED NULL COMMENT '마감일 (1~31, 말일=31)',
    remark              TEXT         NULL,
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          BIGINT UNSIGNED NULL,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    updated_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (customer_id),
    UNIQUE KEY uk_customer_code (customer_code),
    KEY ix_customer_name (customer_name),
    CONSTRAINT ck_customer_type CHECK (customer_type IN ('SALES','PURCHASE','BOTH')),
    CONSTRAINT ck_customer_closing_day CHECK (closing_day IS NULL OR closing_day BETWEEN 1 AND 31)
) ENGINE=InnoDB COMMENT='거래처 (t_customer)';

CREATE TABLE equipment_type (
    equipment_type_id   BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    equipment_type_code VARCHAR(30)  NOT NULL,
    equipment_type_name VARCHAR(50)  NOT NULL,
    description         VARCHAR(255) NULL,
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    PRIMARY KEY (equipment_type_id),
    UNIQUE KEY uk_equipment_type_code (equipment_type_code)
) ENGINE=InnoDB COMMENT='설비 유형';

CREATE TABLE equipment (
    equipment_id        BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    equipment_type_id   BIGINT UNSIGNED NULL,
    equipment_code      VARCHAR(50)  NOT NULL,
    equipment_no        INT          NULL,
    equipment_initial   VARCHAR(50)  NULL,
    equipment_name      VARCHAR(50)  NOT NULL,
    install_date        DATE         NULL,
    image_path          VARCHAR(500) NULL,
    sort_order          INT          NOT NULL DEFAULT 0 COMMENT 'Calendar Resource 표시 순서',
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          BIGINT UNSIGNED NULL,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    updated_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (equipment_id),
    UNIQUE KEY uk_equipment_code (equipment_code),
    KEY ix_equipment_type (equipment_type_id),
    CONSTRAINT fk_equipment_type
        FOREIGN KEY (equipment_type_id) REFERENCES equipment_type (equipment_type_id)
) ENGINE=InnoDB COMMENT='설비 (t_equipment)';

CREATE TABLE equipment_history (
    equipment_history_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    equipment_id        BIGINT UNSIGNED NOT NULL,
    changed_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    changed_by          BIGINT UNSIGNED NULL,
    change_type         VARCHAR(30)  NOT NULL,
    old_data_json       JSON         NULL,
    new_data_json       JSON         NULL,
    PRIMARY KEY (equipment_history_id),
    KEY ix_equipment_history_equipment (equipment_id, changed_at),
    CONSTRAINT fk_equipment_history_equipment
        FOREIGN KEY (equipment_id) REFERENCES equipment (equipment_id)
) ENGINE=InnoDB COMMENT='설비 변경 이력';

CREATE TABLE instrument (
    instrument_id       BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    instrument_code     VARCHAR(50)  NOT NULL,
    instrument_name     VARCHAR(100) NOT NULL,
    instrument_type     VARCHAR(50)  NULL,
    serial_no           VARCHAR(100) NULL,
    calibration_cycle_day INT        NULL,
    last_calibrated_date DATE        NULL,
    next_calibration_date DATE       NULL,
    remark              TEXT         NULL,
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (instrument_id),
    UNIQUE KEY uk_instrument_code (instrument_code)
) ENGINE=InnoDB COMMENT='측정기구 (t_instruments)';

CREATE TABLE part (
    part_id             BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    part_code           VARCHAR(50)  NOT NULL,
    part_name           VARCHAR(100) NOT NULL,
    part_number         VARCHAR(100) NULL,
    specification       VARCHAR(100) NULL,
    model               VARCHAR(100) NULL,
    material            VARCHAR(100) NULL,
    unit_weight         DECIMAL(12,4) NULL,
    unit_code           VARCHAR(20)  NULL,
    price_basis         VARCHAR(10)  NOT NULL DEFAULT 'EA' COMMENT '단가 적용 구분 (구 t_part.unit: ea/kg/ch) — 공통코드 PRICE_BASIS',
    unit_price          DECIMAL(15,2) NULL,
    drawing_no          VARCHAR(100) NULL COMMENT '도면번호 (도면·이미지 파일은 attachment)',
    hardness            VARCHAR(100) NULL,
    core_hardness       VARCHAR(100) NULL,
    effective_hardening_depth VARCHAR(100) NULL,
    grade               VARCHAR(50)  NULL,
    texture             VARCHAR(100) NULL,
    remark              TEXT         NULL,
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          BIGINT UNSIGNED NULL,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    updated_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (part_id),
    UNIQUE KEY uk_part_code (part_code),
    KEY ix_part_number (part_number),
    KEY ix_part_name (part_name),
    CONSTRAINT ck_part_price_basis CHECK (price_basis IN ('EA','KG','CHARGE'))
) ENGINE=InnoDB COMMENT='품목 (t_part) — Version 없음, History + 거래 Snapshot으로 보존';

CREATE TABLE part_customer (
    part_customer_id    BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    part_id             BIGINT UNSIGNED NOT NULL,
    customer_id         BIGINT UNSIGNED NOT NULL,
    customer_part_code  VARCHAR(100) NULL,
    is_customer_lot_required TINYINT(1) NOT NULL DEFAULT 0,
    is_primary          TINYINT(1)   NOT NULL DEFAULT 0,
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    PRIMARY KEY (part_customer_id),
    UNIQUE KEY uk_part_customer (part_id, customer_id),
    KEY ix_part_customer_customer (customer_id),
    CONSTRAINT fk_part_customer_part
        FOREIGN KEY (part_id) REFERENCES part (part_id),
    CONSTRAINT fk_part_customer_customer
        FOREIGN KEY (customer_id) REFERENCES customer (customer_id)
) ENGINE=InnoDB COMMENT='품목-거래처 (t_part 분해)';

CREATE TABLE part_history (
    part_history_id     BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    part_id             BIGINT UNSIGNED NOT NULL,
    changed_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    changed_by          BIGINT UNSIGNED NULL,
    change_type         VARCHAR(30)  NOT NULL,
    old_data_json       JSON         NULL,
    new_data_json       JSON         NULL,
    PRIMARY KEY (part_history_id),
    KEY ix_part_history_part (part_id, changed_at),
    CONSTRAINT fk_part_history_part
        FOREIGN KEY (part_id) REFERENCES part (part_id)
) ENGINE=InnoDB COMMENT='품목 변경 이력';

CREATE TABLE defect_reason (
    defect_reason_id    BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    defect_reason_code  VARCHAR(50)  NOT NULL,
    defect_reason_name  VARCHAR(100) NOT NULL,
    defect_category     VARCHAR(50)  NULL,
    sort_order          INT          NOT NULL DEFAULT 0,
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    PRIMARY KEY (defect_reason_id),
    UNIQUE KEY uk_defect_reason_code (defect_reason_code)
) ENGINE=InnoDB COMMENT='불량 사유 Master';

CREATE TABLE work_shift (
    work_shift_id       BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    work_shift_code     VARCHAR(30)  NOT NULL,
    work_shift_name     VARCHAR(50)  NOT NULL,
    start_time          TIME         NOT NULL,
    end_time            TIME         NOT NULL,
    is_next_day_end     TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '종료가 다음날인 교대 (야간)',
    sort_order          INT          NOT NULL DEFAULT 0 COMMENT '최소값 교대의 start_time = 작업일 시작시각',
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    PRIMARY KEY (work_shift_id),
    UNIQUE KEY uk_work_shift_code (work_shift_code)
) ENGINE=InnoDB COMMENT='교대 (주간/야간)';

CREATE TABLE work_calendar (
    work_calendar_id    BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    calendar_date       DATE         NOT NULL,
    day_type            VARCHAR(20)  NOT NULL DEFAULT 'WORKDAY',
    remark              VARCHAR(255) NULL,
    PRIMARY KEY (work_calendar_id),
    UNIQUE KEY uk_work_calendar_date (calendar_date),
    CONSTRAINT ck_work_calendar_day_type CHECK (day_type IN ('WORKDAY','HOLIDAY','SPECIAL'))
) ENGINE=InnoDB COMMENT='공장 휴일/특근 달력 (스케줄 계산용)';

-- =====================================================================
-- 3. 공정 / 작업표준 / 조건
--    공정 경로(heat_process_operation) / 단계 템플릿(step_template) /
--    품목별 작업표준 조건값(standard_condition) = 항목 × 단계 행렬
-- =====================================================================

CREATE TABLE unit_process (
    unit_process_id     BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    unit_process_code   VARCHAR(50)  NOT NULL,
    unit_process_name   VARCHAR(100) NOT NULL,
    sort_order          INT          NOT NULL DEFAULT 0,
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    PRIMARY KEY (unit_process_id),
    UNIQUE KEY uk_unit_process_code (unit_process_code)
) ENGINE=InnoDB COMMENT='단위공정 (t_unitprocess) 예: 세척, 침탄, 템퍼링, 쇼트 — 입고/출하는 LOT 관리 대상 아님';

CREATE TABLE process_default_time (
    process_default_time_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    equipment_type_id   BIGINT UNSIGNED NULL,
    equipment_id        BIGINT UNSIGNED NULL,
    unit_process_id     BIGINT UNSIGNED NULL,
    running_time_min    DECIMAL(10,2) NOT NULL DEFAULT 0,
    remark              VARCHAR(200) NULL,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (process_default_time_id),
    KEY ix_process_default_time_equipment_type (equipment_type_id),
    KEY ix_process_default_time_equipment (equipment_id),
    KEY ix_process_default_time_unit_process (unit_process_id),
    CONSTRAINT fk_process_default_time_equipment_type
        FOREIGN KEY (equipment_type_id) REFERENCES equipment_type (equipment_type_id),
    CONSTRAINT fk_process_default_time_equipment
        FOREIGN KEY (equipment_id) REFERENCES equipment (equipment_id),
    CONSTRAINT fk_process_default_time_unit_process
        FOREIGN KEY (unit_process_id) REFERENCES unit_process (unit_process_id),
    CONSTRAINT ck_process_default_time_target
        CHECK (equipment_type_id IS NOT NULL OR equipment_id IS NOT NULL)
) ENGINE=InnoDB COMMENT='설비(유형)별 기준 작업시간 (t_process_default_time)';

CREATE TABLE heat_process (
    heat_process_id     BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    heat_process_code   VARCHAR(50)  NOT NULL,
    heat_process_name   VARCHAR(100) NOT NULL,
    description         VARCHAR(255) NULL,
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          BIGINT UNSIGNED NULL,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    updated_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (heat_process_id),
    UNIQUE KEY uk_heat_process_code (heat_process_code)
) ENGINE=InnoDB COMMENT='공정명 (t_heatprocess) 예: 침탄, 질화';

CREATE TABLE heat_process_version (
    heat_process_version_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    heat_process_id     BIGINT UNSIGNED NOT NULL,
    version_no          INT          NOT NULL,
    effective_from      DATETIME     NOT NULL,
    effective_to        DATETIME     NULL,
    is_current          TINYINT(1)   NOT NULL DEFAULT 0,
    remark              VARCHAR(255) NULL,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (heat_process_version_id),
    UNIQUE KEY uk_heat_process_version (heat_process_id, version_no),
    CONSTRAINT fk_heat_process_version_heat_process
        FOREIGN KEY (heat_process_id) REFERENCES heat_process (heat_process_id),
    CONSTRAINT ck_heat_process_version_period
        CHECK (effective_to IS NULL OR effective_to > effective_from)
) ENGINE=InnoDB COMMENT='공정 Version (경로 변경 시 새 Version)';

CREATE TABLE heat_process_operation (
    heat_process_operation_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    heat_process_version_id BIGINT UNSIGNED NOT NULL,
    sequence_no         INT          NOT NULL,
    unit_process_id     BIGINT UNSIGNED NOT NULL,
    is_main_process     TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '주공정 (예: 침탄). 이후 공정은 주공정 LOT으로 투입',
    is_required         TINYINT(1)   NOT NULL DEFAULT 1,
    main_key            TINYINT UNSIGNED AS (IF(is_main_process = 1, 1, NULL)) PERSISTENT,
    PRIMARY KEY (heat_process_operation_id),
    UNIQUE KEY uk_heat_process_operation_seq (heat_process_version_id, sequence_no),
    UNIQUE KEY uk_heat_process_operation_main (heat_process_version_id, main_key),
    KEY ix_heat_process_operation_unit_process (unit_process_id),
    CONSTRAINT fk_heat_process_operation_version
        FOREIGN KEY (heat_process_version_id) REFERENCES heat_process_version (heat_process_version_id),
    CONSTRAINT fk_heat_process_operation_unit_process
        FOREIGN KEY (unit_process_id) REFERENCES unit_process (unit_process_id)
) ENGINE=InnoDB COMMENT='공정 경로 (신규) 예: 침탄 = 세척>침탄(주)>템퍼링>쇼트, Version당 주공정 1개';

CREATE TABLE step_template (
    step_template_id    BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    step_template_code  VARCHAR(50)  NOT NULL,
    step_template_name  VARCHAR(100) NOT NULL,
    unit_process_id     BIGINT UNSIGNED NOT NULL,
    equipment_type_id   BIGINT UNSIGNED NULL,
    equipment_id        BIGINT UNSIGNED NULL COMMENT 'NULL = 설비유형 공통',
    template_type       VARCHAR(50)  NULL COMMENT '구 t_standardtemplate.templatetype',
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (step_template_id),
    UNIQUE KEY uk_step_template_code (step_template_code),
    KEY ix_step_template_target (unit_process_id, equipment_type_id, equipment_id),
    KEY ix_step_template_equipment_type (equipment_type_id),
    KEY ix_step_template_equipment (equipment_id),
    CONSTRAINT fk_step_template_unit_process
        FOREIGN KEY (unit_process_id) REFERENCES unit_process (unit_process_id),
    CONSTRAINT fk_step_template_equipment_type
        FOREIGN KEY (equipment_type_id) REFERENCES equipment_type (equipment_type_id),
    CONSTRAINT fk_step_template_equipment
        FOREIGN KEY (equipment_id) REFERENCES equipment (equipment_id)
) ENGINE=InnoDB COMMENT='설비·단위공정별 템플릿 헤더 (t_standardtemplate) — 단계(column)는 step_template_item, 관리항목(row)은 step_template_condition. 작업표준 입력표의 초기값 (표준은 스텝·항목을 따로 가짐)';

CREATE TABLE step_template_item (
    step_template_item_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    step_template_id    BIGINT UNSIGNED NOT NULL,
    sequence_no         INT          NOT NULL,
    step_name           VARCHAR(100) NOT NULL COMMENT '예: 승온, 균열, 침탄, 확산, 강온, 소입유지, 소입',
    PRIMARY KEY (step_template_item_id),
    UNIQUE KEY uk_step_template_item (step_template_id, sequence_no),
    CONSTRAINT fk_step_template_item_template
        FOREIGN KEY (step_template_id) REFERENCES step_template (step_template_id)
) ENGINE=InnoDB COMMENT='작업단계 (t_standardtemplate.item1~15 → Row)';

CREATE TABLE condition_item (
    condition_item_id   BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    condition_item_code VARCHAR(50)  NOT NULL,
    condition_item_name VARCHAR(100) NOT NULL COMMENT '예: 온도, 시간, CP, RX, NH3',
    unit_code           VARCHAR(20)  NULL COMMENT '예: ℃, min, %',
    value_type          VARCHAR(10)  NOT NULL DEFAULT 'NUMBER',
    sort_order          INT          NOT NULL DEFAULT 0,
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    PRIMARY KEY (condition_item_id),
    UNIQUE KEY uk_condition_item_code (condition_item_code),
    CONSTRAINT ck_condition_item_value_type CHECK (value_type IN ('NUMBER','TEXT'))
) ENGINE=InnoDB COMMENT='조건 항목 Master (구 standarddetail/workconditiondetail의 item 행)';

CREATE TABLE step_template_condition (
    step_template_condition_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    step_template_id    BIGINT UNSIGNED NOT NULL,
    sequence_no         INT          NOT NULL,
    condition_item_id   BIGINT UNSIGNED NOT NULL,
    PRIMARY KEY (step_template_condition_id),
    UNIQUE KEY uk_step_template_condition_seq (step_template_id, sequence_no),
    UNIQUE KEY uk_step_template_condition_item (step_template_id, condition_item_id),
    KEY ix_step_template_condition_item (condition_item_id),
    CONSTRAINT fk_step_template_condition_template
        FOREIGN KEY (step_template_id) REFERENCES step_template (step_template_id),
    CONSTRAINT fk_step_template_condition_item
        FOREIGN KEY (condition_item_id) REFERENCES condition_item (condition_item_id)
) ENGINE=InnoDB COMMENT='템플릿의 관리항목(행) 목록 (구 t_standardtemplate templatetype=row) — 설비·단위공정마다 다름';

CREATE TABLE standard (
    standard_id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    standard_code       VARCHAR(50)  NOT NULL,
    standard_name       VARCHAR(100) NOT NULL,
    part_id             BIGINT UNSIGNED NOT NULL,
    customer_id         BIGINT UNSIGNED NULL,
    heat_process_id     BIGINT UNSIGNED NULL COMMENT '공정별로 조건이 다를 때 지정',
    unit_process_id     BIGINT UNSIGNED NOT NULL,
    equipment_type_id   BIGINT UNSIGNED NULL,
    equipment_id        BIGINT UNSIGNED NULL,
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          BIGINT UNSIGNED NULL,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    updated_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (standard_id),
    UNIQUE KEY uk_standard_code (standard_code),
    KEY ix_standard_lookup (part_id, unit_process_id, equipment_type_id, equipment_id),
    KEY ix_standard_customer (customer_id),
    KEY ix_standard_heat_process (heat_process_id),
    KEY ix_standard_unit_process (unit_process_id),
    KEY ix_standard_equipment_type (equipment_type_id),
    KEY ix_standard_equipment (equipment_id),
    CONSTRAINT fk_standard_part
        FOREIGN KEY (part_id) REFERENCES part (part_id),
    CONSTRAINT fk_standard_customer
        FOREIGN KEY (customer_id) REFERENCES customer (customer_id),
    CONSTRAINT fk_standard_heat_process
        FOREIGN KEY (heat_process_id) REFERENCES heat_process (heat_process_id),
    CONSTRAINT fk_standard_unit_process
        FOREIGN KEY (unit_process_id) REFERENCES unit_process (unit_process_id),
    CONSTRAINT fk_standard_equipment_type
        FOREIGN KEY (equipment_type_id) REFERENCES equipment_type (equipment_type_id),
    CONSTRAINT fk_standard_equipment
        FOREIGN KEY (equipment_id) REFERENCES equipment (equipment_id)
) ENGINE=InnoDB COMMENT='작업표준 (t_standard) — 품목 × 단위공정 × 설비(유형) × 공정별';

CREATE TABLE standard_version (
    standard_version_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    standard_id         BIGINT UNSIGNED NOT NULL,
    version_no          INT          NOT NULL,
    step_template_id    BIGINT UNSIGNED NULL COMMENT '입력표를 불러온 단계 템플릿 (참고) — 스텝·항목은 standard_version_step/_item',
    charge_qty          DECIMAL(14,3) NOT NULL DEFAULT 0 COMMENT '1 charge 투입 기준수량',
    charge_unit         VARCHAR(20)  NOT NULL DEFAULT 'charge',
    running_time_min    DECIMAL(10,2) NULL COMMENT '표준 작업시간 (스케줄 계산 기본값)',
    effective_from      DATETIME     NOT NULL,
    effective_to        DATETIME     NULL,
    is_current          TINYINT(1)   NOT NULL DEFAULT 0,
    remark              VARCHAR(255) NULL,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (standard_version_id),
    UNIQUE KEY uk_standard_version (standard_id, version_no),
    KEY ix_standard_version_step_template (step_template_id),
    CONSTRAINT fk_standard_version_standard
        FOREIGN KEY (standard_id) REFERENCES standard (standard_id),
    CONSTRAINT fk_standard_version_step_template
        FOREIGN KEY (step_template_id) REFERENCES step_template (step_template_id),
    CONSTRAINT ck_standard_version_period
        CHECK (effective_to IS NULL OR effective_to > effective_from)
) ENGINE=InnoDB COMMENT='작업표준 Version (확정 후 수정 금지)';

-- 작업표준 입력표는 Version 마다 스텝(열)·관리항목(행)을 직접 가진다 (구 F_WorkStandardAddForm 가변 그리드, §22.5).
-- 단계 템플릿은 표를 처음 채우는 초기값일 뿐 — 템플릿을 고쳐도 기존 표준의 스텝 이름은 바뀌지 않는다.
CREATE TABLE standard_version_step (
    standard_version_step_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    standard_version_id BIGINT UNSIGNED NOT NULL,
    sequence_no         INT          NOT NULL COMMENT '열 순서 1..N (구 Step1~15, 개수 제한 없음)',
    step_name           VARCHAR(100) NOT NULL COMMENT '예: 승온, 균열, 침탄 — 구 "스텝" 행',
    PRIMARY KEY (standard_version_step_id),
    UNIQUE KEY uk_standard_version_step (standard_version_id, sequence_no),
    CONSTRAINT fk_standard_version_step_version
        FOREIGN KEY (standard_version_id) REFERENCES standard_version (standard_version_id)
) ENGINE=InnoDB COMMENT='작업표준 Version 의 스텝(열) — 구 t_standarddetail "스텝" 행';

CREATE TABLE standard_version_item (
    standard_version_item_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    standard_version_id BIGINT UNSIGNED NOT NULL,
    sequence_no         INT          NOT NULL COMMENT '행 순서',
    condition_item_id   BIGINT UNSIGNED NOT NULL,
    PRIMARY KEY (standard_version_item_id),
    UNIQUE KEY uk_standard_version_item_seq (standard_version_id, sequence_no),
    UNIQUE KEY uk_standard_version_item (standard_version_id, condition_item_id),
    KEY ix_standard_version_item_item (condition_item_id),
    CONSTRAINT fk_standard_version_item_version
        FOREIGN KEY (standard_version_id) REFERENCES standard_version (standard_version_id),
    CONSTRAINT fk_standard_version_item_item
        FOREIGN KEY (condition_item_id) REFERENCES condition_item (condition_item_id)
) ENGINE=InnoDB COMMENT='작업표준 Version 의 관리항목(행) — 구 t_standarddetail.item (값 없는 행도 유지)';

CREATE TABLE standard_condition (
    standard_condition_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    standard_version_id BIGINT UNSIGNED NOT NULL,
    step_no             INT          NULL COMMENT 'standard_version_step.sequence_no — NULL = 단계 무관 항목 (LOT 공통 조건)',
    condition_item_id   BIGINT UNSIGNED NOT NULL,
    condition_value     VARCHAR(100) NULL,
    step_key            INT AS (IFNULL(step_no, 0)) PERSISTENT,
    PRIMARY KEY (standard_condition_id),
    UNIQUE KEY uk_standard_condition (standard_version_id, step_key, condition_item_id),
    KEY ix_standard_condition_step (standard_version_id, step_no),
    KEY ix_standard_condition_item (standard_version_id, condition_item_id),
    CONSTRAINT fk_standard_condition_version
        FOREIGN KEY (standard_version_id) REFERENCES standard_version (standard_version_id),
    CONSTRAINT fk_standard_condition_step
        FOREIGN KEY (standard_version_id, step_no) REFERENCES standard_version_step (standard_version_id, sequence_no),
    CONSTRAINT fk_standard_condition_row
        FOREIGN KEY (standard_version_id, condition_item_id) REFERENCES standard_version_item (standard_version_id, condition_item_id)
) ENGINE=InnoDB COMMENT='작업표준 조건값 = 관리항목(행) × [공통 + 스텝(열)] (t_standarddetail item × step1~15)';

-- ---------------------------------------------------------------------
-- 3.1 검사 기준 (품목·업체별)
-- ---------------------------------------------------------------------

CREATE TABLE inspection_standard (
    inspection_standard_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    part_id             BIGINT UNSIGNED NOT NULL,
    customer_id         BIGINT UNSIGNED NULL COMMENT 'NULL = 전 업체 공통',
    customer_key        BIGINT UNSIGNED AS (IFNULL(customer_id, 0)) PERSISTENT,
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (inspection_standard_id),
    UNIQUE KEY uk_inspection_standard (part_id, customer_key),
    KEY ix_inspection_standard_customer (customer_id),
    CONSTRAINT fk_inspection_standard_part
        FOREIGN KEY (part_id) REFERENCES part (part_id),
    CONSTRAINT fk_inspection_standard_customer
        FOREIGN KEY (customer_id) REFERENCES customer (customer_id)
) ENGINE=InnoDB COMMENT='품목(+업체)별 검사기준 Header';

CREATE TABLE inspection_standard_version (
    inspection_standard_version_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    inspection_standard_id BIGINT UNSIGNED NOT NULL,
    version_no          INT          NOT NULL,
    effective_from      DATETIME     NOT NULL,
    effective_to        DATETIME     NULL,
    is_current          TINYINT(1)   NOT NULL DEFAULT 0,
    remark              VARCHAR(255) NULL,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (inspection_standard_version_id),
    UNIQUE KEY uk_inspection_standard_version (inspection_standard_id, version_no),
    CONSTRAINT fk_inspection_standard_version_standard
        FOREIGN KEY (inspection_standard_id) REFERENCES inspection_standard (inspection_standard_id),
    CONSTRAINT ck_inspection_standard_version_period
        CHECK (effective_to IS NULL OR effective_to > effective_from)
) ENGINE=InnoDB COMMENT='검사기준 Version';

CREATE TABLE inspection_criteria (
    inspection_criteria_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    inspection_standard_version_id BIGINT UNSIGNED NOT NULL,
    sequence_no         INT          NOT NULL,
    item_type           VARCHAR(50)  NULL COMMENT '예: 외관, 경도, 경화깊이',
    item_name           VARCHAR(100) NOT NULL,
    location            VARCHAR(100) NULL,
    specification_value VARCHAR(255) NULL COMMENT '요구사항 원문 (성적서 표기)',
    tool_name           VARCHAR(100) NULL,
    test_value          VARCHAR(100) NULL,
    scale               VARCHAR(50)  NULL COMMENT '예: HRC, HV',
    range_type          VARCHAR(20)  NULL COMMENT '예: BETWEEN, MIN, MAX',
    lower_limit         DECIMAL(18,6) NULL,
    upper_limit         DECIMAL(18,6) NULL,
    hardness_limit      INT          NULL,
    unit_code           VARCHAR(20)  NULL,
    sample_count        INT          NOT NULL DEFAULT 1,
    test_count          INT          NOT NULL DEFAULT 1,
    PRIMARY KEY (inspection_criteria_id),
    UNIQUE KEY uk_inspection_criteria (inspection_standard_version_id, sequence_no),
    CONSTRAINT fk_inspection_criteria_version
        FOREIGN KEY (inspection_standard_version_id) REFERENCES inspection_standard_version (inspection_standard_version_id),
    CONSTRAINT ck_inspection_criteria_limit
        CHECK (lower_limit IS NULL OR upper_limit IS NULL OR lower_limit <= upper_limit)
) ENGINE=InnoDB COMMENT='검사 항목별 기준 (t_inspectioncriteria) — 측정값 판정 근거';

CREATE TABLE inspection_criteria_point (
    inspection_criteria_point_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    inspection_criteria_id BIGINT UNSIGNED NOT NULL,
    point_no            INT          NOT NULL,
    point_label         VARCHAR(100) NULL,
    PRIMARY KEY (inspection_criteria_point_id),
    UNIQUE KEY uk_inspection_criteria_point (inspection_criteria_id, point_no),
    CONSTRAINT fk_inspection_criteria_point_criteria
        FOREIGN KEY (inspection_criteria_id) REFERENCES inspection_criteria (inspection_criteria_id)
) ENGINE=InnoDB COMMENT='측정 위치 (t_inspectioncriteria.p1~p10 → Row)';

CREATE TABLE unit_inspection_item (
    unit_inspection_item_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    unit_process_id     BIGINT UNSIGNED NULL,
    item_code           VARCHAR(50)  NOT NULL,
    item_name           VARCHAR(100) NOT NULL,
    sort_order          INT          NOT NULL DEFAULT 0,
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    PRIMARY KEY (unit_inspection_item_id),
    UNIQUE KEY uk_unit_inspection_item_code (item_code),
    KEY ix_unit_inspection_item_unit_process (unit_process_id),
    CONSTRAINT fk_unit_inspection_item_unit_process
        FOREIGN KEY (unit_process_id) REFERENCES unit_process (unit_process_id)
) ENGINE=InnoDB COMMENT='공정검사 항목 (t_unitinspectionitem)';

-- ---------------------------------------------------------------------
-- 3.2 품목 기본 공정 / 성적서 양식
-- ---------------------------------------------------------------------

CREATE TABLE part_heat_process (
    part_heat_process_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    part_id             BIGINT UNSIGNED NOT NULL,
    heat_process_id     BIGINT UNSIGNED NOT NULL,
    is_default          TINYINT(1)   NOT NULL DEFAULT 0,
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    PRIMARY KEY (part_heat_process_id),
    UNIQUE KEY uk_part_heat_process (part_id, heat_process_id),
    KEY ix_part_heat_process_heat_process (heat_process_id),
    CONSTRAINT fk_part_heat_process_part
        FOREIGN KEY (part_id) REFERENCES part (part_id),
    CONSTRAINT fk_part_heat_process_heat_process
        FOREIGN KEY (heat_process_id) REFERENCES heat_process (heat_process_id)
) ENGINE=InnoDB COMMENT='품목별 적용 공정 (수주 등록 시 기본값)';

-- ---------------------------------------------------------------------
-- 3.3 출력 양식 (사용자 엑셀 양식 등록 방식 — 모든 출력물 공통)
--   print_data_source : 시스템 정의 데이터 공급원 (코드가 값을 채우는 단위)
--   print_field       : 데이터 공급원별 치환자 사전 ({{키}})
--   print_purpose     : 사용자 확장 가능한 출력 용도 (데이터 공급원 1개 선택)
--   print_template    : 양식 / print_template_version : 엑셀 파일(버전별, DB 저장)
--   part_print_template : 품목(+업체)·용도별 기본 양식 / print_log : 출력 이력
-- ---------------------------------------------------------------------

CREATE TABLE print_data_source (
    print_data_source_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    data_source_code    VARCHAR(50)  NOT NULL COMMENT '예: INSPECTION_TARGET, PRODUCTION_WORK, STANDARD, SHIPMENT',
    data_source_name    VARCHAR(100) NOT NULL,
    output_unit         VARCHAR(30)  NOT NULL COMMENT '1장 단위: 검사대상 / 작업LOT / 작업표준 / 출하전표 …',
    description         VARCHAR(255) NULL,
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    PRIMARY KEY (print_data_source_id),
    UNIQUE KEY uk_print_data_source_code (data_source_code)
) ENGINE=InnoDB COMMENT='출력 데이터 공급원 (시스템 정의 — 값을 채우는 코드가 존재하는 단위만 등록)';

CREATE TABLE print_field (
    print_field_id      BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    print_data_source_id BIGINT UNSIGNED NOT NULL,
    field_key           VARCHAR(100) NOT NULL COMMENT '양식 치환자 키 — 셀에 {{field_key}} 로 기입',
    field_alias         VARCHAR(100) NULL COMMENT '한글 별칭 — {{검사번호}} 처럼 사용 가능',
    field_type          VARCHAR(20)  NOT NULL DEFAULT 'TEXT',
    field_group         VARCHAR(50)  NULL COMMENT '화면 분류 (기본정보, 측정값, 기준 …)',
    format_pattern      VARCHAR(50)  NULL COMMENT '예: yyyy-MM-dd, #,##0.0',
    description         VARCHAR(255) NULL,
    sample_value        VARCHAR(255) NULL,
    sort_order          INT          NOT NULL DEFAULT 0,
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    PRIMARY KEY (print_field_id),
    UNIQUE KEY uk_print_field_key (print_data_source_id, field_key),
    UNIQUE KEY uk_print_field_alias (print_data_source_id, field_alias),
    CONSTRAINT fk_print_field_data_source
        FOREIGN KEY (print_data_source_id) REFERENCES print_data_source (print_data_source_id),
    CONSTRAINT ck_print_field_type
        CHECK (field_type IN ('TEXT','NUMBER','DATE','IMAGE','LIST'))
) ENGINE=InnoDB COMMENT='치환자 사전 (t_templatefieldname) — LIST = 반복행 영역, IMAGE = 병합영역에 이미지';

CREATE TABLE print_purpose (
    print_purpose_id    BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    purpose_code        VARCHAR(50)  NOT NULL COMMENT '예: INSPECTION_REPORT, WORK_STANDARD, WORK_ORDER, LABEL, SHIPMENT_SLIP',
    purpose_name        VARCHAR(100) NOT NULL,
    print_data_source_id BIGINT UNSIGNED NOT NULL COMMENT '이 용도의 값을 채울 데이터 공급원',
    is_system           TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '기본 제공 용도 (삭제 불가)',
    sort_order          INT          NOT NULL DEFAULT 0,
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (print_purpose_id),
    UNIQUE KEY uk_print_purpose_code (purpose_code),
    KEY ix_print_purpose_data_source (print_data_source_id),
    CONSTRAINT fk_print_purpose_data_source
        FOREIGN KEY (print_data_source_id) REFERENCES print_data_source (print_data_source_id)
) ENGINE=InnoDB COMMENT='출력 용도 — 사용자 추가 등록 가능';

CREATE TABLE print_template (
    print_template_id   BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    print_template_name VARCHAR(100) NOT NULL,
    print_purpose_id    BIGINT UNSIGNED NOT NULL,
    template_kind       VARCHAR(10)  NOT NULL DEFAULT 'EXCEL' COMMENT 'EXCEL = 사용자 엑셀 양식 / FIXED = 코드 고정 레이아웃',
    renderer_key        VARCHAR(50)  NULL COMMENT 'FIXED 전용: 코드 렌더러 식별자 (예: SALES_SLIP)',
    output_format       VARCHAR(10)  NOT NULL DEFAULT 'PDF' COMMENT '발행 형식',
    is_default          TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '용도 기본 양식 — 품목(+업체) 연결이 없을 때 사용 (§5.3 양식 선택 3순위)',
    default_key         TINYINT UNSIGNED AS (IF(is_default = 1, 1, NULL)) PERSISTENT,
    remark              VARCHAR(255) NULL,
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          BIGINT UNSIGNED NULL,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    updated_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (print_template_id),
    UNIQUE KEY uk_print_template_name (print_purpose_id, print_template_name),
    UNIQUE KEY uk_print_template_default (print_purpose_id, default_key) COMMENT '용도당 기본 양식 1개',
    CONSTRAINT fk_print_template_purpose
        FOREIGN KEY (print_purpose_id) REFERENCES print_purpose (print_purpose_id),
    CONSTRAINT ck_print_template_format CHECK (output_format IN ('PDF','XLSX')),
    CONSTRAINT ck_print_template_kind CHECK (template_kind IN ('EXCEL','FIXED')),
    CONSTRAINT ck_print_template_renderer
        CHECK ((template_kind = 'FIXED' AND renderer_key IS NOT NULL) OR (template_kind = 'EXCEL' AND renderer_key IS NULL))
) ENGINE=InnoDB COMMENT='출력 양식 (t_inspectiontemplate + t_printtemplate + PrintDoc 코드 양식) — 등록 방식 2가지';

CREATE TABLE print_template_version (
    print_template_version_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    print_template_id   BIGINT UNSIGNED NOT NULL,
    version_no          INT          NOT NULL,
    file_name           VARCHAR(255) NULL COMMENT 'EXCEL: 원본 파일명',
    file_content        LONGBLOB     NULL COMMENT 'EXCEL: 엑셀 파일 (xlsx/xlsm) — PC별 경로 대신 DB 보관',
    file_hash           CHAR(64)     NULL COMMENT 'EXCEL: SHA-256 — 동일 파일 재등록 방지',
    file_size           INT UNSIGNED NULL,
    layout_options_json JSON         NULL COMMENT 'FIXED: 관리자 조정 레이아웃 옵션 (페이지당 행수, 폰트, 도장 위치, 부수 등)',
    thumbnail_png       MEDIUMBLOB   NULL,
    placeholders_json   JSON         NULL COMMENT '업로드 시 추출한 치환자 목록',
    unknown_placeholders_json JSON   NULL COMMENT '사전(print_field)에 없는 치환자 — 경고',
    is_current          TINYINT(1)   NOT NULL DEFAULT 0,
    current_key         TINYINT UNSIGNED AS (IF(is_current = 1, 1, NULL)) PERSISTENT,
    change_note         VARCHAR(255) NULL,
    uploaded_at         DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    uploaded_by         BIGINT UNSIGNED NULL,
    PRIMARY KEY (print_template_version_id),
    UNIQUE KEY uk_print_template_version (print_template_id, version_no),
    UNIQUE KEY uk_print_template_version_current (print_template_id, current_key),
    KEY ix_print_template_version_hash (file_hash),
    CONSTRAINT fk_print_template_version_template
        FOREIGN KEY (print_template_id) REFERENCES print_template (print_template_id),
    CONSTRAINT ck_print_template_version_body
        CHECK ((file_content IS NOT NULL AND file_hash IS NOT NULL) OR layout_options_json IS NOT NULL)
) ENGINE=InnoDB COMMENT='양식 버전 — EXCEL은 파일, FIXED는 레이아웃 옵션. 수정 = 새 버전, 과거 발행물은 당시 버전으로 재출력';

CREATE TABLE workstation (
    workstation_id      BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    workstation_code    VARCHAR(50)  NOT NULL COMMENT 'PC/태블릿 식별 (머신명 등)',
    workstation_name    VARCHAR(100) NOT NULL,
    location            VARCHAR(100) NULL,
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    PRIMARY KEY (workstation_id),
    UNIQUE KEY uk_workstation_code (workstation_code)
) ENGINE=InnoDB COMMENT='단말 (PC별 설정 기준) — 구 PrinterInfo 로컬 파일 대체';

CREATE TABLE workstation_print_setting (
    workstation_print_setting_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    workstation_id      BIGINT UNSIGNED NOT NULL,
    print_purpose_id    BIGINT UNSIGNED NOT NULL,
    printer_name        VARCHAR(200) NOT NULL,
    copies              INT          NOT NULL DEFAULT 1,
    paper_size          VARCHAR(30)  NULL,
    is_auto_print       TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '미리보기 없이 바로 인쇄',
    PRIMARY KEY (workstation_print_setting_id),
    UNIQUE KEY uk_workstation_print_setting (workstation_id, print_purpose_id),
    KEY ix_workstation_print_setting_purpose (print_purpose_id),
    CONSTRAINT fk_workstation_print_setting_ws
        FOREIGN KEY (workstation_id) REFERENCES workstation (workstation_id),
    CONSTRAINT fk_workstation_print_setting_purpose
        FOREIGN KEY (print_purpose_id) REFERENCES print_purpose (print_purpose_id)
) ENGINE=InnoDB COMMENT='단말 × 출력 용도별 프린터 (구 PrinterInfo — PC 로컬 텍스트 파일)';

CREATE TABLE part_print_template (
    part_print_template_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    part_id             BIGINT UNSIGNED NOT NULL,
    customer_id         BIGINT UNSIGNED NULL COMMENT 'NULL = 전 업체 공통',
    print_purpose_id    BIGINT UNSIGNED NOT NULL,
    print_template_id   BIGINT UNSIGNED NOT NULL COMMENT 'print_template.print_purpose_id와 같은 용도여야 함 — API 검증',
    is_default          TINYINT(1)   NOT NULL DEFAULT 0,
    customer_key        BIGINT UNSIGNED AS (IFNULL(customer_id, 0)) PERSISTENT,
    default_key         TINYINT UNSIGNED AS (IF(is_default = 1, 1, NULL)) PERSISTENT,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (part_print_template_id),
    UNIQUE KEY uk_part_print_template (part_id, customer_key, print_template_id),
    UNIQUE KEY uk_part_print_template_default (part_id, customer_key, print_purpose_id, default_key),
    KEY ix_part_print_template_customer (customer_id),
    KEY ix_part_print_template_purpose (print_purpose_id),
    KEY ix_part_print_template_template (print_template_id),
    CONSTRAINT fk_part_print_template_part
        FOREIGN KEY (part_id) REFERENCES part (part_id),
    CONSTRAINT fk_part_print_template_customer
        FOREIGN KEY (customer_id) REFERENCES customer (customer_id),
    CONSTRAINT fk_part_print_template_purpose
        FOREIGN KEY (print_purpose_id) REFERENCES print_purpose (print_purpose_id),
    CONSTRAINT fk_part_print_template_template
        FOREIGN KEY (print_template_id) REFERENCES print_template (print_template_id)
) ENGINE=InnoDB COMMENT='품목(+업체)별 양식 연결 (t_part_template) — 용도별 기본 1개';

-- =====================================================================
-- 4. 수주(입고) / LOT
-- =====================================================================

CREATE TABLE sales_order (
    sales_order_id      BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    sales_order_no      VARCHAR(50)  NOT NULL,
    order_date          DATE         NOT NULL,
    received_date       DATE         NULL COMMENT '고객 소재 입고일 (임가공)',
    due_date            DATE         NULL,
    customer_id         BIGINT UNSIGNED NOT NULL,
    status              VARCHAR(30)  NOT NULL DEFAULT 'OPEN',
    remark              VARCHAR(255) NULL,
    row_version         INT UNSIGNED NOT NULL DEFAULT 0,
    is_deleted          TINYINT(1)   NOT NULL DEFAULT 0,
    deleted_at          DATETIME     NULL,
    deleted_by          BIGINT UNSIGNED NULL,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          BIGINT UNSIGNED NULL,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    updated_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (sales_order_id),
    UNIQUE KEY uk_sales_order_no (sales_order_no),
    KEY ix_sales_order_customer_date (customer_id, order_date),
    KEY ix_sales_order_status (status),
    CONSTRAINT fk_sales_order_customer
        FOREIGN KEY (customer_id) REFERENCES customer (customer_id),
    CONSTRAINT ck_sales_order_status
        CHECK (status IN ('OPEN','IN_PROGRESS','COMPLETED','CLOSED','CANCELLED'))
) ENGINE=InnoDB COMMENT='수주/입고 묶음 (한 번에 등록한 행들 — 구 t_income은 헤더 없음, 선택적 그룹)';

CREATE TABLE sales_order_item (
    sales_order_item_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    order_item_no       VARCHAR(50)  NOT NULL COMMENT '입고번호 = 현장에서 스캔하는 수주번호 (구 incomeno, 예 I260930-001)',
    sales_order_id      BIGINT UNSIGNED NOT NULL,
    line_no             INT          NOT NULL,
    part_id             BIGINT UNSIGNED NOT NULL,
    heat_process_version_id BIGINT UNSIGNED NULL,
    customer_lot        VARCHAR(100) NULL,
    coil_no             VARCHAR(100) NULL,
    order_qty           DECIMAL(14,3) NOT NULL DEFAULT 0,
    order_weight        DECIMAL(14,4) NULL,
    unit_weight         DECIMAL(12,4) NULL,
    unit_price          DECIMAL(15,2) NULL,
    unit_code           VARCHAR(20)  NULL,
    price_basis         VARCHAR(10)  NOT NULL DEFAULT 'EA' COMMENT '입고 당시 단가 적용 구분 (품목에서 복사)',
    priority            TINYINT UNSIGNED NOT NULL DEFAULT 1 COMMENT '0 여유 / 1 일반 / 2 우선 / 3 긴급 — 공통코드 PRIORITY',
    customer_work_order_no VARCHAR(100) NULL COMMENT '고객 작업지시번호 (구 workorderno)',
    is_separately_managed TINYINT(1) NOT NULL DEFAULT 0 COMMENT '별도관리 (구 grade 컬럼에 별도관리 문자열 저장)',
    is_return           TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '반입 (재입고)',
    is_rework           TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '재작업 입고',
    status              VARCHAR(30)  NOT NULL DEFAULT 'OPEN',
    part_name_snapshot  VARCHAR(100) NULL,
    part_number_snapshot VARCHAR(100) NULL,
    specification_snapshot VARCHAR(100) NULL,
    model_snapshot      VARCHAR(100) NULL,
    material_snapshot   VARCHAR(100) NULL,
    heat_process_name_snapshot VARCHAR(100) NULL,
    hardness_snapshot   VARCHAR(100) NULL COMMENT '요구경도',
    core_hardness_snapshot VARCHAR(100) NULL COMMENT '심부경도',
    case_depth_snapshot VARCHAR(100) NULL COMMENT '경화층',
    texture_snapshot    VARCHAR(100) NULL COMMENT '조직',
    remark              VARCHAR(255) NULL,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (sales_order_item_id),
    UNIQUE KEY uk_sales_order_item_no (order_item_no),
    UNIQUE KEY uk_sales_order_item (sales_order_id, line_no),
    KEY ix_sales_order_item_part (part_id),
    KEY ix_sales_order_item_heat_process_version (heat_process_version_id),
    KEY ix_sales_order_item_status (status),
    CONSTRAINT fk_sales_order_item_order
        FOREIGN KEY (sales_order_id) REFERENCES sales_order (sales_order_id),
    CONSTRAINT fk_sales_order_item_part
        FOREIGN KEY (part_id) REFERENCES part (part_id),
    CONSTRAINT fk_sales_order_item_heat_process_version
        FOREIGN KEY (heat_process_version_id) REFERENCES heat_process_version (heat_process_version_id),
    CONSTRAINT ck_sales_order_item_status
        CHECK (status IN ('OPEN','IN_PROGRESS','COMPLETED','CANCELLED')),
    CONSTRAINT ck_sales_order_item_qty CHECK (order_qty >= 0),
    CONSTRAINT ck_sales_order_item_price_basis CHECK (price_basis IN ('EA','KG','CHARGE')),
    CONSTRAINT ck_sales_order_item_priority CHECK (priority BETWEEN 0 AND 3)
) ENGINE=InnoDB COMMENT='수주(입고) 품목 행 = 구 t_income 1행 (진행수량은 VIEW 계산)';

CREATE TABLE production_input_queue (
    production_input_queue_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    sales_order_item_id BIGINT UNSIGNED NOT NULL,
    unit_process_id     BIGINT UNSIGNED NULL,
    equipment_type_id   BIGINT UNSIGNED NULL,
    allocated_qty       DECIMAL(14,3) NOT NULL DEFAULT 0,
    status              VARCHAR(30)  NOT NULL DEFAULT 'WAITING',
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          BIGINT UNSIGNED NULL,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    updated_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (production_input_queue_id),
    KEY ix_production_input_queue_order_item (sales_order_item_id),
    KEY ix_production_input_queue_status (status),
    KEY ix_production_input_queue_unit_process (unit_process_id),
    KEY ix_production_input_queue_equipment_type (equipment_type_id),
    CONSTRAINT fk_production_input_queue_order_item
        FOREIGN KEY (sales_order_item_id) REFERENCES sales_order_item (sales_order_item_id),
    CONSTRAINT fk_production_input_queue_unit_process
        FOREIGN KEY (unit_process_id) REFERENCES unit_process (unit_process_id),
    CONSTRAINT fk_production_input_queue_equipment_type
        FOREIGN KEY (equipment_type_id) REFERENCES equipment_type (equipment_type_id),
    CONSTRAINT ck_production_input_queue_status
        CHECK (status IN ('WAITING','PARTIAL','SCHEDULED','CANCELLED'))
) ENGINE=InnoDB COMMENT='단위공정 투입 대기 (t_inputwaiting) — 잔량은 계산';

-- =====================================================================
-- 5. 생산 계획 / 작업자 배정
-- =====================================================================

CREATE TABLE worker_assignment (
    worker_assignment_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    work_date           DATE         NOT NULL,
    work_shift_id       BIGINT UNSIGNED NOT NULL,
    equipment_id        BIGINT UNSIGNED NOT NULL,
    employee_id         BIGINT UNSIGNED NOT NULL,
    assignment_type     VARCHAR(30)  NOT NULL DEFAULT 'PRIMARY',
    remark              VARCHAR(255) NULL,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          BIGINT UNSIGNED NULL,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    updated_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (worker_assignment_id),
    UNIQUE KEY uk_worker_assignment (work_date, work_shift_id, equipment_id, employee_id),
    KEY ix_worker_assignment_equipment_date (equipment_id, work_date),
    KEY ix_worker_assignment_employee_date (employee_id, work_date),
    KEY ix_worker_assignment_shift (work_shift_id),
    CONSTRAINT fk_worker_assignment_shift
        FOREIGN KEY (work_shift_id) REFERENCES work_shift (work_shift_id),
    CONSTRAINT fk_worker_assignment_equipment
        FOREIGN KEY (equipment_id) REFERENCES equipment (equipment_id),
    CONSTRAINT fk_worker_assignment_employee
        FOREIGN KEY (employee_id) REFERENCES employee (employee_id),
    CONSTRAINT ck_worker_assignment_type
        CHECK (assignment_type IN ('PRIMARY','SUPPORT'))
) ENGINE=InnoDB COMMENT='설비별 작업자 배정 (t_workerassignment)';

CREATE TABLE production_schedule (
    production_schedule_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    schedule_no         VARCHAR(50)  NULL,
    work_date           DATE         NOT NULL COMMENT '작업일 = 첫 교대 시작 기준 생산일자',
    equipment_id        BIGINT UNSIGNED NOT NULL,
    unit_process_id     BIGINT UNSIGNED NOT NULL,
    sequence_no         INT          NOT NULL DEFAULT 1,
    planned_lot_no      VARCHAR(100) NULL COMMENT '계획 단계 임시 LOT번호 (workplan.templotno)',
    planned_qty         DECIMAL(14,3) NOT NULL DEFAULT 0,
    planned_duration_min DECIMAL(10,2) NOT NULL DEFAULT 0,
    duration_source     VARCHAR(20)  NULL COMMENT '작업시간 결정 출처 (공통코드 RUNNING_TIME_SOURCE) — 병합 시 최대값을 준 품목의 출처',
    planned_start_at    DATETIME     NOT NULL,
    planned_end_at      DATETIME     NOT NULL,
    status              VARCHAR(30)  NOT NULL DEFAULT 'PLANNED',
    is_time_locked      TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '재계산 시 위치/시간 고정 (신규 기능, 구 t_work.isfixed와 무관)',
    is_rework           TINYINT(1)   NOT NULL DEFAULT 0,
    remark              VARCHAR(255) NULL,
    row_version         INT UNSIGNED NOT NULL DEFAULT 0 COMMENT '낙관적 잠금 (Drag & Drop)',
    is_deleted          TINYINT(1)   NOT NULL DEFAULT 0,
    deleted_at          DATETIME     NULL,
    deleted_by          BIGINT UNSIGNED NULL,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          BIGINT UNSIGNED NULL,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    updated_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (production_schedule_id),
    UNIQUE KEY uk_production_schedule_no (schedule_no),
    KEY ix_production_schedule_equipment_date (equipment_id, work_date, sequence_no),
    KEY ix_production_schedule_calendar (equipment_id, planned_start_at, planned_end_at),
    KEY ix_production_schedule_date_status (work_date, status),
    KEY ix_production_schedule_unit_process (unit_process_id),
    CONSTRAINT fk_production_schedule_equipment
        FOREIGN KEY (equipment_id) REFERENCES equipment (equipment_id),
    CONSTRAINT fk_production_schedule_unit_process
        FOREIGN KEY (unit_process_id) REFERENCES unit_process (unit_process_id),
    CONSTRAINT ck_production_schedule_status
        CHECK (status IN ('PLANNED','CONFIRMED','RELEASED','CANCELLED')),
    CONSTRAINT ck_production_schedule_period
        CHECK (planned_end_at >= planned_start_at)
) ENGINE=InnoDB COMMENT='생산 계획 블록 (workplan) — 설비 × 단위공정 1회, 작업 LOT과 1:1';

CREATE TABLE production_schedule_item (
    production_schedule_item_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    production_schedule_id BIGINT UNSIGNED NOT NULL,
    sales_order_item_id BIGINT UNSIGNED NOT NULL,
    planned_qty         DECIMAL(14,3) NOT NULL DEFAULT 0,
    PRIMARY KEY (production_schedule_item_id),
    UNIQUE KEY uk_production_schedule_item (production_schedule_id, sales_order_item_id),
    KEY ix_production_schedule_item_order_item (sales_order_item_id),
    CONSTRAINT fk_production_schedule_item_schedule
        FOREIGN KEY (production_schedule_id) REFERENCES production_schedule (production_schedule_id),
    CONSTRAINT fk_production_schedule_item_order_item
        FOREIGN KEY (sales_order_item_id) REFERENCES sales_order_item (sales_order_item_id)
) ENGINE=InnoDB COMMENT='계획 블록에 담을 수주품목 (한 LOT에 여러 수주)';

CREATE TABLE schedule_board_layout (
    schedule_board_layout_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    board_code          VARCHAR(50)  NOT NULL,
    equipment_id        BIGINT UNSIGNED NULL,
    display_order       INT          NOT NULL DEFAULT 0,
    x                   INT          NOT NULL DEFAULT 0,
    y                   INT          NOT NULL DEFAULT 0,
    width               INT          NOT NULL DEFAULT 0,
    height              INT          NOT NULL DEFAULT 0,
    position_no         INT          NOT NULL DEFAULT 0,
    tag                 VARCHAR(50)  NULL,
    title_template      VARCHAR(100) NULL,
    is_active           TINYINT(1)   NOT NULL DEFAULT 1,
    PRIMARY KEY (schedule_board_layout_id),
    KEY ix_schedule_board_layout_board (board_code, display_order),
    KEY ix_schedule_board_layout_equipment (equipment_id),
    CONSTRAINT fk_schedule_board_layout_equipment
        FOREIGN KEY (equipment_id) REFERENCES equipment (equipment_id)
) ENGINE=InnoDB COMMENT='[WinForms 병행기간 한정] 스케줄 화면 Box 배치 (t_schedulebox)';

CREATE TABLE schedule_board_item (
    schedule_board_item_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    schedule_board_layout_id BIGINT UNSIGNED NOT NULL,
    production_schedule_id BIGINT UNSIGNED NOT NULL,
    display_title       VARCHAR(100) NULL,
    display_qty         DECIMAL(14,3) NULL,
    tag                 VARCHAR(50)  NULL,
    is_hold             TINYINT(1)   NOT NULL DEFAULT 0,
    PRIMARY KEY (schedule_board_item_id),
    UNIQUE KEY uk_schedule_board_item (schedule_board_layout_id, production_schedule_id),
    KEY ix_schedule_board_item_schedule (production_schedule_id),
    CONSTRAINT fk_schedule_board_item_layout
        FOREIGN KEY (schedule_board_layout_id) REFERENCES schedule_board_layout (schedule_board_layout_id),
    CONSTRAINT fk_schedule_board_item_schedule
        FOREIGN KEY (production_schedule_id) REFERENCES production_schedule (production_schedule_id)
) ENGINE=InnoDB COMMENT='[WinForms 병행기간 한정] 화면 Box ↔ 스케줄 연결';

-- =====================================================================
-- 6. 실제 생산
-- =====================================================================

CREATE TABLE production_work (
    production_work_id  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    lot_no              VARCHAR(100) NOT NULL COMMENT '작업 LOT번호 = YYMMDD-설비이니셜-작업순번 (예: 260930-B01-001)',
    lot_seq             INT          NULL COMMENT 'LOT번호의 작업순번 (설비 × 작업일 내)',
    production_schedule_id BIGINT UNSIGNED NULL COMMENT '1:1. 구 데이터 이관분만 NULL 허용',
    unit_process_id     BIGINT UNSIGNED NOT NULL,
    equipment_id        BIGINT UNSIGNED NULL COMMENT '설비 없는 단위공정은 NULL',
    is_main_process     TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '주공정 LOT 여부 (예: 침탄)',
    heat_process_version_id BIGINT UNSIGNED NULL,
    step_template_id    BIGINT UNSIGNED NULL COMMENT '적용 단계 템플릿',
    standard_version_id BIGINT UNSIGNED NULL COMMENT '투입 품목 중 선택·확정한 작업표준 (production_work_input.is_standard_basis)',
    is_standard_fixed   TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '표준 확정 여부 (구 t_work.isfixed)',
    standard_fixed_at   DATETIME     NULL,
    submit_lot_no       VARCHAR(100) NULL COMMENT '고객 제출용 LOT번호 (구 convertlotno) — 주공정 투입(Gas 폼) 시 자유입력, 규칙 없음',
    work_date           DATE         NOT NULL,
    status              VARCHAR(30)  NOT NULL DEFAULT 'ALLOCATED',
    input_key           TINYINT UNSIGNED AS (IF(status = 'INPUT', 1, NULL)) PERSISTENT,
    actual_start_at     DATETIME     NULL,
    actual_end_at       DATETIME     NULL,
    expected_duration_min DECIMAL(10,2) NULL,
    actual_duration_min DECIMAL(10,2) NULL COMMENT '일시정지 제외 가동시간 (production_work_event 기준 계산)',
    worker_employee_id  BIGINT UNSIGNED NULL COMMENT '대표 작업자',
    is_rework           TINYINT(1)   NOT NULL DEFAULT 0,
    marking             VARCHAR(100) NULL,
    unit_process_name_snapshot VARCHAR(100) NULL,
    equipment_name_snapshot VARCHAR(100) NULL,
    heat_process_name_snapshot VARCHAR(100) NULL,
    worker_name_snapshot VARCHAR(50) NULL,
    remark              TEXT         NULL,
    row_version         INT UNSIGNED NOT NULL DEFAULT 0,
    is_deleted          TINYINT(1)   NOT NULL DEFAULT 0,
    deleted_at          DATETIME     NULL,
    deleted_by          BIGINT UNSIGNED NULL,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          BIGINT UNSIGNED NULL,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    updated_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (production_work_id),
    UNIQUE KEY uk_production_work_lot_no (lot_no),
    UNIQUE KEY uk_production_work_schedule (production_schedule_id),
    UNIQUE KEY uk_production_work_lot_seq (equipment_id, work_date, lot_seq),
    UNIQUE KEY uk_production_work_one_input (equipment_id, input_key) COMMENT '설비당 진행 중(투입) 작업 1건',
    KEY ix_production_work_step_template (step_template_id),
    KEY ix_production_work_date_equipment (work_date, equipment_id),
    KEY ix_production_work_date_status (work_date, status),
    KEY ix_production_work_unit_process (unit_process_id),
    KEY ix_production_work_heat_process_version (heat_process_version_id),
    KEY ix_production_work_standard_version (standard_version_id),
    KEY ix_production_work_worker (worker_employee_id),
    KEY ix_production_work_submit_lot (submit_lot_no),
    CONSTRAINT fk_production_work_schedule
        FOREIGN KEY (production_schedule_id) REFERENCES production_schedule (production_schedule_id),
    CONSTRAINT fk_production_work_unit_process
        FOREIGN KEY (unit_process_id) REFERENCES unit_process (unit_process_id),
    CONSTRAINT fk_production_work_equipment
        FOREIGN KEY (equipment_id) REFERENCES equipment (equipment_id),
    CONSTRAINT fk_production_work_heat_process_version
        FOREIGN KEY (heat_process_version_id) REFERENCES heat_process_version (heat_process_version_id),
    CONSTRAINT fk_production_work_step_template
        FOREIGN KEY (step_template_id) REFERENCES step_template (step_template_id),
    CONSTRAINT fk_production_work_standard_version
        FOREIGN KEY (standard_version_id) REFERENCES standard_version (standard_version_id),
    CONSTRAINT fk_production_work_worker
        FOREIGN KEY (worker_employee_id) REFERENCES employee (employee_id),
    CONSTRAINT ck_production_work_status
        CHECK (status IN ('ALLOCATED','INPUT','COMPLETED','CANCELLED')),
    CONSTRAINT ck_production_work_period
        CHECK (actual_end_at IS NULL OR actual_start_at IS NULL OR actual_end_at >= actual_start_at)
) ENGINE=InnoDB COMMENT='작업 = 작업 LOT (t_work). 단위공정 1회 수행, 여러 수주 투입 가능';

CREATE TABLE production_work_event (
    production_work_event_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    production_work_id  BIGINT UNSIGNED NOT NULL,
    event_type          VARCHAR(20)  NOT NULL,
    event_at            DATETIME     NOT NULL,
    employee_id         BIGINT UNSIGNED NULL,
    remark              VARCHAR(255) NULL,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (production_work_event_id),
    KEY ix_production_work_event_work (production_work_id, event_at),
    KEY ix_production_work_event_employee (employee_id),
    CONSTRAINT fk_production_work_event_work
        FOREIGN KEY (production_work_id) REFERENCES production_work (production_work_id),
    CONSTRAINT fk_production_work_event_employee
        FOREIGN KEY (employee_id) REFERENCES employee (employee_id),
    CONSTRAINT ck_production_work_event_type
        CHECK (event_type IN ('ALLOCATE','INPUT','START','PAUSE','RESUME','COMPLETE','CANCEL'))
) ENGINE=InnoDB COMMENT='작업 시작/일시정지/재개/완료 이력 (V3 신규)';

CREATE TABLE production_work_input (
    production_work_input_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    production_work_id  BIGINT UNSIGNED NOT NULL,
    sales_order_item_id BIGINT UNSIGNED NOT NULL COMMENT '투입 시 스캔한 수주번호',
    main_work_id        BIGINT UNSIGNED NULL COMMENT '주공정 LOT 병기. 주공정 행=자신, 후공정=주LOT 입력/선택, 전공정=NULL(수주번호로 연결)',
    main_input_id       BIGINT UNSIGNED NULL COMMENT '후공정: 주공정 LOT 투입목록에서 선택한 행',
    origin_work_id      BIGINT UNSIGNED NULL COMMENT '재작업 시 초기 작업 LOT (구 originelotno). 재작업의 재작업도 최초 LOT',
    production_input_queue_id BIGINT UNSIGNED NULL,
    is_standard_basis   TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '표준 확정 품목 (구 t_work.fixedpartid), LOT당 1행',
    basis_key           TINYINT UNSIGNED AS (IF(is_standard_basis = 1, 1, NULL)) PERSISTENT,
    main_key            BIGINT UNSIGNED AS (IFNULL(main_work_id, 0)) PERSISTENT COMMENT '전공정(주 LOT 없음) = 0',
    tray_mark           VARCHAR(50)  NULL,
    input_qty           DECIMAL(14,3) NOT NULL DEFAULT 0,
    input_weight        DECIMAL(14,4) NULL,
    unit_price_snapshot DECIMAL(15,2) NULL,
    input_amount        DECIMAL(15,2) NULL,
    scanned_at          DATETIME     NULL,
    loaded_at           DATETIME     NULL COMMENT '장입 시각 (t_inputsub in)',
    unloaded_at         DATETIME     NULL COMMENT '추출 시각 (t_inputsub out)',
    status              VARCHAR(30)  NOT NULL DEFAULT 'INPUT',
    customer_name_snapshot VARCHAR(100) NULL,
    customer_lot_snapshot VARCHAR(100) NULL,
    part_name_snapshot  VARCHAR(100) NULL,
    part_number_snapshot VARCHAR(100) NULL,
    specification_snapshot VARCHAR(100) NULL,
    model_snapshot      VARCHAR(100) NULL,
    material_snapshot   VARCHAR(100) NULL,
    remark              VARCHAR(255) NULL,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          BIGINT UNSIGNED NULL,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    updated_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (production_work_input_id),
    UNIQUE KEY uk_production_work_input_basis (production_work_id, basis_key),
    UNIQUE KEY uk_production_work_input_order (production_work_id, sales_order_item_id, main_key) COMMENT '주 LOT 전: LOT당 같은 수주 1회 / 주 LOT 후: 같은 주 LOT의 같은 수주 1회',
    KEY ix_production_work_input_order_item (sales_order_item_id),
    KEY ix_production_work_input_main (main_work_id),
    KEY ix_production_work_input_main_input (main_input_id),
    KEY ix_production_work_input_origin (origin_work_id),
    KEY ix_production_work_input_queue (production_input_queue_id),
    CONSTRAINT fk_production_work_input_work
        FOREIGN KEY (production_work_id) REFERENCES production_work (production_work_id),
    CONSTRAINT fk_production_work_input_order_item
        FOREIGN KEY (sales_order_item_id) REFERENCES sales_order_item (sales_order_item_id),
    CONSTRAINT fk_production_work_input_main
        FOREIGN KEY (main_work_id) REFERENCES production_work (production_work_id),
    CONSTRAINT fk_production_work_input_main_input
        FOREIGN KEY (main_input_id) REFERENCES production_work_input (production_work_input_id),
    CONSTRAINT fk_production_work_input_origin
        FOREIGN KEY (origin_work_id) REFERENCES production_work (production_work_id),
    CONSTRAINT fk_production_work_input_queue
        FOREIGN KEY (production_input_queue_id) REFERENCES production_input_queue (production_input_queue_id),
    CONSTRAINT ck_production_work_input_status
        CHECK (status IN ('INPUT','LOADED','UNLOADED','CANCELLED')),
    CONSTRAINT ck_production_work_input_qty CHECK (input_qty >= 0),
    CONSTRAINT ck_production_work_input_period
        CHECK (unloaded_at IS NULL OR loaded_at IS NULL OR unloaded_at >= loaded_at)
) ENGINE=InnoDB COMMENT='작업 LOT × 수주 투입 (t_worksub + t_inputsub). 양품 = 투입 - 해당 행 부적합';

CREATE TABLE production_work_condition (
    production_work_condition_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    production_work_id  BIGINT UNSIGNED NOT NULL,
    condition_item_id   BIGINT UNSIGNED NOT NULL,
    step_sequence_no    INT          NULL COMMENT '단계 순서 (표준 standard_version_step 에서 복사) — NULL = 단계 무관 LOT 공통 조건',
    step_name_snapshot  VARCHAR(100) NULL COMMENT '단계 이름 (복사 당시)',
    set_value           VARCHAR(100) NULL COMMENT '확정 조건값 (선택 표준에서 복사, 작업자 수정 가능)',
    actual_value        VARCHAR(100) NULL COMMENT '실측/실적값 (선택)',
    step_key            INT AS (IFNULL(step_sequence_no, 0)) PERSISTENT,
    PRIMARY KEY (production_work_condition_id),
    UNIQUE KEY uk_production_work_condition (production_work_id, step_key, condition_item_id),
    KEY ix_production_work_condition_item (condition_item_id),
    CONSTRAINT fk_production_work_condition_work
        FOREIGN KEY (production_work_id) REFERENCES production_work (production_work_id),
    CONSTRAINT fk_production_work_condition_item
        FOREIGN KEY (condition_item_id) REFERENCES condition_item (condition_item_id)
) ENGINE=InnoDB COMMENT='작업 LOT 확정 조건 = 항목 × 단계 (t_conditiontemplate + t_workconditiondetail)';

-- =====================================================================
-- 7. 검사
-- =====================================================================

CREATE TABLE inspection (
    inspection_id       BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    inspection_no       VARCHAR(50)  NOT NULL COMMENT '검사번호 {구분}{yyMMdd}-{NNN} (구 t_inspection.inspectionno)',
    inspection_type     VARCHAR(20)  NOT NULL DEFAULT 'OUTGOING' COMMENT '입고/공정/출하검사 (구 TI/TP/TO) — 공통코드 INSPECTION_TYPE',
    inspection_date     DATE         NOT NULL,
    production_work_id  BIGINT UNSIGNED NOT NULL COMMENT '입력한 작업 LOT (보통 주 LOT)',
    inspection_standard_version_id BIGINT UNSIGNED NULL COMMENT '판정 기준 (검사 결과는 대상 전체에 공통 적용)',
    reinspection_of_id  BIGINT UNSIGNED NULL COMMENT '확정 후 수정 = 재검사: 원 검사',
    inspector_employee_id BIGINT UNSIGNED NULL,
    status              VARCHAR(30)  NOT NULL DEFAULT 'WAITING',
    decision            VARCHAR(30)  NULL COMMENT '대상 중 하나라도 FAIL이면 FAIL',
    completed_at        DATETIME     NULL,
    remark              TEXT         NULL,
    row_version         INT UNSIGNED NOT NULL DEFAULT 0,
    is_deleted          TINYINT(1)   NOT NULL DEFAULT 0,
    deleted_at          DATETIME     NULL,
    deleted_by          BIGINT UNSIGNED NULL,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          BIGINT UNSIGNED NULL,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    updated_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (inspection_id),
    UNIQUE KEY uk_inspection_no (inspection_no),
    KEY ix_inspection_work (production_work_id),
    KEY ix_inspection_date_status (inspection_date, status),
    KEY ix_inspection_type_date (inspection_type, inspection_date),
    KEY ix_inspection_inspector (inspector_employee_id),
    KEY ix_inspection_standard_version (inspection_standard_version_id),
    KEY ix_inspection_reinspection (reinspection_of_id),
    CONSTRAINT fk_inspection_work
        FOREIGN KEY (production_work_id) REFERENCES production_work (production_work_id),
    CONSTRAINT fk_inspection_standard_version
        FOREIGN KEY (inspection_standard_version_id) REFERENCES inspection_standard_version (inspection_standard_version_id),
    CONSTRAINT fk_inspection_reinspection
        FOREIGN KEY (reinspection_of_id) REFERENCES inspection (inspection_id),
    CONSTRAINT ck_inspection_type CHECK (inspection_type IN ('INCOMING','PROCESS','OUTGOING')),
    CONSTRAINT fk_inspection_inspector
        FOREIGN KEY (inspector_employee_id) REFERENCES employee (employee_id),
    CONSTRAINT ck_inspection_status
        CHECK (status IN ('WAITING','IN_PROGRESS','COMPLETED','CANCELLED')),
    CONSTRAINT ck_inspection_decision
        CHECK (decision IS NULL OR decision IN ('PASS','FAIL','CONDITIONAL'))
) ENGINE=InnoDB COMMENT='검사 1회 (LOT 입력 → 투입내역 중 대상 선택). 측정·판정은 공통, 대상은 inspection_target';

CREATE TABLE inspection_target (
    inspection_target_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    inspection_id       BIGINT UNSIGNED NOT NULL,
    sub_no              INT          NOT NULL COMMENT '검사번호 내 순번 (구 t_inspection.subno)',
    production_work_input_id BIGINT UNSIGNED NOT NULL COMMENT '선택한 검사 대상 (주 LOT 투입 행)',
    sales_order_item_id BIGINT UNSIGNED NOT NULL,
    customer_id         BIGINT UNSIGNED NOT NULL COMMENT '성적서 수신처',
    print_template_id   BIGINT UNSIGNED NULL COMMENT '성적서 양식 (용도 INSPECTION_REPORT). 발행 이력은 print_log',
    inspection_qty      DECIMAL(14,3) NULL,
    inspection_weight   DECIMAL(14,4) NULL,
    report_issued_at    DATETIME     NULL COMMENT '성적서 발행 일시 (대상별 1장)',
    report_issue_count  INT          NOT NULL DEFAULT 0,
    submit_lot_no_snapshot VARCHAR(100) NULL COMMENT '성적서에 기재한 고객 제출 LOT번호',
    customer_name_snapshot VARCHAR(100) NULL,
    customer_lot_snapshot VARCHAR(100) NULL,
    part_name_snapshot  VARCHAR(100) NULL,
    part_number_snapshot VARCHAR(100) NULL,
    specification_snapshot VARCHAR(100) NULL,
    model_snapshot      VARCHAR(100) NULL,
    material_snapshot   VARCHAR(100) NULL,
    PRIMARY KEY (inspection_target_id),
    UNIQUE KEY uk_inspection_target_sub (inspection_id, sub_no),
    UNIQUE KEY uk_inspection_target_input (inspection_id, production_work_input_id),
    KEY ix_inspection_target_input (production_work_input_id),
    KEY ix_inspection_target_order_item (sales_order_item_id),
    KEY ix_inspection_target_customer (customer_id),
    KEY ix_inspection_target_print_template (print_template_id),
    CONSTRAINT fk_inspection_target_inspection
        FOREIGN KEY (inspection_id) REFERENCES inspection (inspection_id),
    CONSTRAINT fk_inspection_target_input
        FOREIGN KEY (production_work_input_id) REFERENCES production_work_input (production_work_input_id),
    CONSTRAINT fk_inspection_target_order_item
        FOREIGN KEY (sales_order_item_id) REFERENCES sales_order_item (sales_order_item_id),
    CONSTRAINT fk_inspection_target_customer
        FOREIGN KEY (customer_id) REFERENCES customer (customer_id),
    CONSTRAINT fk_inspection_target_print_template
        FOREIGN KEY (print_template_id) REFERENCES print_template (print_template_id)
) ENGINE=InnoDB COMMENT='검사 대상 품목 (구 t_inspection의 LOT × 수주 행) — 판정은 inspection 공통, 성적서는 대상별 1장';

CREATE TABLE inspection_item (
    inspection_item_id  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    inspection_id       BIGINT UNSIGNED NOT NULL COMMENT '검사 결과는 대상 전체 공통',
    inspection_criteria_id BIGINT UNSIGNED NULL,
    sequence_no         INT          NOT NULL,
    item_type           VARCHAR(50)  NULL,
    item_name           VARCHAR(100) NOT NULL,
    location            VARCHAR(100) NULL,
    result              VARCHAR(30)  NULL COMMENT '측정 결과 요약 (원문)',
    decision            VARCHAR(30)  NULL COMMENT '기준 대비 판정',
    remark              VARCHAR(255) NULL,
    PRIMARY KEY (inspection_item_id),
    UNIQUE KEY uk_inspection_item (inspection_id, sequence_no),
    KEY ix_inspection_item_criteria (inspection_criteria_id),
    CONSTRAINT fk_inspection_item_inspection
        FOREIGN KEY (inspection_id) REFERENCES inspection (inspection_id),
    CONSTRAINT fk_inspection_item_criteria
        FOREIGN KEY (inspection_criteria_id) REFERENCES inspection_criteria (inspection_criteria_id),
    CONSTRAINT ck_inspection_item_decision
        CHECK (decision IS NULL OR decision IN ('PASS','FAIL','NA'))
) ENGINE=InnoDB COMMENT='검사 항목 결과 (t_inspectiondetail의 항목부)';

CREATE TABLE inspection_measurement (
    inspection_measurement_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    inspection_item_id  BIGINT UNSIGNED NOT NULL,
    sample_no           INT          NOT NULL,
    measured_value      DECIMAL(18,6) NULL,
    measured_text       VARCHAR(255) NULL,
    unit_code           VARCHAR(20)  NULL,
    result              VARCHAR(30)  NULL,
    measured_at         DATETIME     NULL,
    instrument_id       BIGINT UNSIGNED NULL,
    remark              VARCHAR(255) NULL,
    PRIMARY KEY (inspection_measurement_id),
    UNIQUE KEY uk_inspection_measurement (inspection_item_id, sample_no),
    KEY ix_inspection_measurement_instrument (instrument_id),
    CONSTRAINT fk_inspection_measurement_item
        FOREIGN KEY (inspection_item_id) REFERENCES inspection_item (inspection_item_id),
    CONSTRAINT fk_inspection_measurement_instrument
        FOREIGN KEY (instrument_id) REFERENCES instrument (instrument_id),
    CONSTRAINT ck_inspection_measurement_result
        CHECK (result IS NULL OR result IN ('OK','NG','NA'))
) ENGINE=InnoDB COMMENT='검사 측정값 (t_inspectiondetail, v1~v10/p1~p10 → Row)';

CREATE TABLE line_inspection (
    line_inspection_id  BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    production_work_id  BIGINT UNSIGNED NOT NULL,
    inspection_date     DATE         NOT NULL,
    inspector_employee_id BIGINT UNSIGNED NULL,
    status              VARCHAR(30)  NOT NULL DEFAULT 'WAITING',
    decision            VARCHAR(30)  NULL,
    remark              VARCHAR(255) NULL,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          BIGINT UNSIGNED NULL,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    updated_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (line_inspection_id),
    KEY ix_line_inspection_work (production_work_id),
    KEY ix_line_inspection_date (inspection_date),
    KEY ix_line_inspection_inspector (inspector_employee_id),
    CONSTRAINT fk_line_inspection_work
        FOREIGN KEY (production_work_id) REFERENCES production_work (production_work_id),
    CONSTRAINT fk_line_inspection_inspector
        FOREIGN KEY (inspector_employee_id) REFERENCES employee (employee_id),
    CONSTRAINT ck_line_inspection_status
        CHECK (status IN ('WAITING','IN_PROGRESS','COMPLETED','CANCELLED')),
    CONSTRAINT ck_line_inspection_decision
        CHECK (decision IS NULL OR decision IN ('PASS','FAIL','CONDITIONAL'))
) ENGINE=InnoDB COMMENT='공정검사 Header (t_lineinspection)';

CREATE TABLE line_inspection_measurement (
    line_inspection_measurement_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    line_inspection_id  BIGINT UNSIGNED NOT NULL,
    unit_inspection_item_id BIGINT UNSIGNED NULL,
    sample_no           INT          NOT NULL DEFAULT 1,
    measured_value      DECIMAL(18,6) NULL,
    measured_text       VARCHAR(255) NULL,
    result              VARCHAR(30)  NULL,
    PRIMARY KEY (line_inspection_measurement_id),
    KEY ix_line_inspection_measurement_header (line_inspection_id),
    KEY ix_line_inspection_measurement_item (unit_inspection_item_id),
    CONSTRAINT fk_line_inspection_measurement_header
        FOREIGN KEY (line_inspection_id) REFERENCES line_inspection (line_inspection_id),
    CONSTRAINT fk_line_inspection_measurement_item
        FOREIGN KEY (unit_inspection_item_id) REFERENCES unit_inspection_item (unit_inspection_item_id),
    CONSTRAINT ck_line_inspection_measurement_result
        CHECK (result IS NULL OR result IN ('OK','NG','NA'))
) ENGINE=InnoDB COMMENT='공정검사 측정값 (hn1x1 등 → Row)';

-- =====================================================================
-- 8. 불량 / 생산실적 / 출하 / 마감
-- =====================================================================

CREATE TABLE defect_occurrence (
    defect_occurrence_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    defect_reason_id    BIGINT UNSIGNED NULL,
    sales_order_item_id BIGINT UNSIGNED NOT NULL,
    production_work_id  BIGINT UNSIGNED NULL COMMENT '부적합 발생 작업 LOT',
    production_work_input_id BIGINT UNSIGNED NULL COMMENT '발생 LOT의 해당 수주 투입 행 — 공정별 양품 계산 기준',
    inspection_target_id BIGINT UNSIGNED NULL COMMENT '검사에서 발견된 경우 해당 검사 대상',
    main_work_id        BIGINT UNSIGNED NULL COMMENT '추적용 주공정 LOT',
    defect_date         DATE         NOT NULL,
    defect_qty          DECIMAL(14,3) NOT NULL DEFAULT 0,
    defect_weight       DECIMAL(14,4) NULL,
    status              VARCHAR(30)  NOT NULL DEFAULT 'OPEN',
    decision            VARCHAR(30)  NULL,
    decision_remark     TEXT         NULL,
    rework_input_id     BIGINT UNSIGNED NULL COMMENT '재작업 LOT에 투입된 행 (재작업 LOT = 해당 행의 production_work_id)',
    rework_remark       TEXT         NULL,
    decided_employee_id BIGINT UNSIGNED NULL,
    decided_at          DATETIME     NULL,
    completed_employee_id BIGINT UNSIGNED NULL,
    completed_at        DATETIME     NULL,
    row_version         INT UNSIGNED NOT NULL DEFAULT 0,
    is_deleted          TINYINT(1)   NOT NULL DEFAULT 0,
    deleted_at          DATETIME     NULL,
    deleted_by          BIGINT UNSIGNED NULL,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          BIGINT UNSIGNED NULL,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    updated_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (defect_occurrence_id),
    KEY ix_defect_occurrence_order_item (sales_order_item_id),
    KEY ix_defect_occurrence_work (production_work_id),
    KEY ix_defect_occurrence_input (production_work_input_id),
    KEY ix_defect_occurrence_inspection (inspection_target_id),
    KEY ix_defect_occurrence_main (main_work_id),
    KEY ix_defect_occurrence_date_status (defect_date, status),
    KEY ix_defect_occurrence_reason (defect_reason_id),
    KEY ix_defect_occurrence_rework (rework_input_id),
    KEY ix_defect_occurrence_decided (decided_employee_id),
    KEY ix_defect_occurrence_completed (completed_employee_id),
    CONSTRAINT fk_defect_occurrence_reason
        FOREIGN KEY (defect_reason_id) REFERENCES defect_reason (defect_reason_id),
    CONSTRAINT fk_defect_occurrence_order_item
        FOREIGN KEY (sales_order_item_id) REFERENCES sales_order_item (sales_order_item_id),
    CONSTRAINT fk_defect_occurrence_work
        FOREIGN KEY (production_work_id) REFERENCES production_work (production_work_id),
    CONSTRAINT fk_defect_occurrence_input
        FOREIGN KEY (production_work_input_id) REFERENCES production_work_input (production_work_input_id),
    CONSTRAINT fk_defect_occurrence_inspection
        FOREIGN KEY (inspection_target_id) REFERENCES inspection_target (inspection_target_id),
    CONSTRAINT fk_defect_occurrence_main
        FOREIGN KEY (main_work_id) REFERENCES production_work (production_work_id),
    CONSTRAINT fk_defect_occurrence_rework
        FOREIGN KEY (rework_input_id) REFERENCES production_work_input (production_work_input_id),
    CONSTRAINT fk_defect_occurrence_decided
        FOREIGN KEY (decided_employee_id) REFERENCES employee (employee_id),
    CONSTRAINT fk_defect_occurrence_completed
        FOREIGN KEY (completed_employee_id) REFERENCES employee (employee_id),
    CONSTRAINT ck_defect_occurrence_status
        CHECK (status IN ('OPEN','DECIDED','REWORKING','COMPLETED','CANCELLED')),
    CONSTRAINT ck_defect_occurrence_decision
        CHECK (decision IS NULL OR decision IN ('REWORK','SHIP','SORT','HOLD','SCRAP','RETURN')),
    CONSTRAINT ck_defect_occurrence_source
        CHECK (production_work_input_id IS NOT NULL OR inspection_target_id IS NOT NULL),
    CONSTRAINT ck_defect_occurrence_qty CHECK (defect_qty >= 0)
) ENGINE=InnoDB COMMENT='부적합 발생/처리 (t_defect) — 매 공정 투입 행 단위';

CREATE TABLE shipment_closing (
    shipment_closing_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    closing_no          VARCHAR(50)  NOT NULL,
    customer_id         BIGINT UNSIGNED NOT NULL COMMENT '업체별 마감 (업체마다 마감일 다름: customer.closing_day)',
    closing_date        DATE         NOT NULL COMMENT '마감 기준일',
    closing_year        SMALLINT UNSIGNED NOT NULL COMMENT '귀속 마감월 — 전표 출하월과 다를 수 있음 (이월)',
    closing_month       TINYINT UNSIGNED NOT NULL,
    total_qty           DECIMAL(14,3) NOT NULL DEFAULT 0,
    total_weight        DECIMAL(14,4) NOT NULL DEFAULT 0,
    total_amount        DECIMAL(15,2) NOT NULL DEFAULT 0,
    closing_status      VARCHAR(30)  NOT NULL DEFAULT 'OPEN',
    closed_at           DATETIME     NULL,
    closed_by           BIGINT UNSIGNED NULL,
    remark              VARCHAR(255) NULL,
    row_version         INT UNSIGNED NOT NULL DEFAULT 0,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (shipment_closing_id),
    UNIQUE KEY uk_shipment_closing_no (closing_no),
    KEY ix_shipment_closing_customer_period (customer_id, closing_year, closing_month),
    KEY ix_shipment_closing_period (closing_year, closing_month),
    CONSTRAINT fk_shipment_closing_customer
        FOREIGN KEY (customer_id) REFERENCES customer (customer_id),
    CONSTRAINT ck_shipment_closing_month CHECK (closing_month BETWEEN 1 AND 12),
    CONSTRAINT ck_shipment_closing_status
        CHECK (closing_status IN ('OPEN','CLOSED','REOPENED'))
) ENGINE=InnoDB COMMENT='업체별 마감 — 출하전표를 선택하여 마감, 미선택 전표는 다음 마감으로 이월. 합계는 마감 시점 Snapshot';

CREATE TABLE shipment (
    shipment_id         BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    shipment_no         VARCHAR(50)  NOT NULL,
    shipment_date       DATE         NOT NULL,
    customer_id         BIGINT UNSIGNED NOT NULL,
    shipment_closing_id BIGINT UNSIGNED NULL COMMENT '마감 실행 기록 (마감 완료 시 설정)',
    closing_status      VARCHAR(20)  NOT NULL DEFAULT 'UNCLOSED' COMMENT '전표 단위 마감 상태 (구 closingstatus 0/1/2) — 공통코드 CLOSING_STATUS',
    closing_year        SMALLINT UNSIGNED NULL COMMENT '귀속 마감월 (마감 또는 이월 대상 월)',
    closing_month       TINYINT UNSIGNED NULL,
    closing_due_date    DATE         NULL COMMENT '전표 등록 시 지정한 마감일 (구 closingdate)',
    print_sum_by_part   TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '거래명세표 품목 합산 출력 (구 sumaspart)',
    supply_amount       DECIMAL(15,2) NULL COMMENT '공급가액 = 상세 금액 합 — 출하 서비스가 계산·저장, 출력은 저장값만 표시 (§15.3 F2)',
    vat_amount          DECIMAL(15,2) NULL COMMENT '세액 = 공급가액 × sales.vat_rate (sales.amount_rounding) — 구 출력 코드 TAX_RATE 대체',
    total_amount        DECIMAL(15,2) NULL COMMENT '합계 = 공급가액 + 세액',
    status              VARCHAR(30)  NOT NULL DEFAULT 'DRAFT',
    customer_name_snapshot VARCHAR(100) NULL,
    customer_business_no_snapshot VARCHAR(20) NULL,
    customer_ceo_name_snapshot VARCHAR(50) NULL,
    customer_business_type_snapshot VARCHAR(50) NULL,
    customer_business_item_snapshot VARCHAR(50) NULL,
    customer_address_snapshot VARCHAR(255) NULL,
    customer_address_detail_snapshot VARCHAR(255) NULL,
    remark              VARCHAR(255) NULL,
    row_version         INT UNSIGNED NOT NULL DEFAULT 0,
    is_deleted          TINYINT(1)   NOT NULL DEFAULT 0,
    deleted_at          DATETIME     NULL,
    deleted_by          BIGINT UNSIGNED NULL,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          BIGINT UNSIGNED NULL,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    updated_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (shipment_id),
    UNIQUE KEY uk_shipment_no (shipment_no),
    KEY ix_shipment_customer_date (customer_id, shipment_date),
    KEY ix_shipment_date_status (shipment_date, status),
    KEY ix_shipment_closing (shipment_closing_id),
    KEY ix_shipment_closing_state (customer_id, closing_status, closing_year, closing_month),
    CONSTRAINT fk_shipment_customer
        FOREIGN KEY (customer_id) REFERENCES customer (customer_id),
    CONSTRAINT fk_shipment_closing
        FOREIGN KEY (shipment_closing_id) REFERENCES shipment_closing (shipment_closing_id),
    CONSTRAINT ck_shipment_status
        CHECK (status IN ('DRAFT','CONFIRMED','SHIPPED','CANCELLED')),
    CONSTRAINT ck_shipment_closing_status
        CHECK (closing_status IN ('UNCLOSED','CLOSED','CARRIED_OVER')),
    CONSTRAINT ck_shipment_closing_link
        CHECK ((closing_status = 'CLOSED') = (shipment_closing_id IS NOT NULL)),
    CONSTRAINT ck_shipment_closing_period
        CHECK (closing_status = 'UNCLOSED' OR (closing_year IS NOT NULL AND closing_month BETWEEN 1 AND 12))
) ENGINE=InnoDB COMMENT='출하(=납품) 전표 (t_outcomesum)';

CREATE TABLE shipment_item (
    shipment_item_id    BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    shipment_id         BIGINT UNSIGNED NOT NULL,
    line_no             INT          NOT NULL,
    sales_order_item_id BIGINT UNSIGNED NOT NULL,
    main_work_id        BIGINT UNSIGNED NULL COMMENT '출하 LOT (주공정 LOT) — 어떤 LOT이 출하되는지 관리',
    shipment_qty        DECIMAL(14,3) NOT NULL DEFAULT 0,
    test_specimen_qty   DECIMAL(14,3) NOT NULL DEFAULT 0 COMMENT '시험편 — 출하잔량에서 차감, 금액 미포함 (구 testspecimen)',
    shipment_weight     DECIMAL(14,4) NULL,
    charge_count        DECIMAL(10,2) NULL COMMENT '단가 구분 CHARGE일 때 과금 charge 수',
    price_basis_snapshot VARCHAR(10) NOT NULL DEFAULT 'EA' COMMENT 'EA: 수량x단가 / KG: 중량x단가 / CHARGE: charge수x단가',
    unit_price_snapshot DECIMAL(15,2) NULL,
    amount              DECIMAL(15,2) NULL COMMENT '단가 구분에 따라 출하 서비스가 계산 (구: 항상 수량x단가)',
    submit_lot_no_snapshot VARCHAR(100) NULL COMMENT '출하 시 고객에게 제출한 LOT번호',
    customer_lot_snapshot VARCHAR(100) NULL,
    part_name_snapshot  VARCHAR(100) NULL,
    part_number_snapshot VARCHAR(100) NULL,
    specification_snapshot VARCHAR(100) NULL,
    material_snapshot   VARCHAR(100) NULL,
    PRIMARY KEY (shipment_item_id),
    UNIQUE KEY uk_shipment_item (shipment_id, line_no),
    KEY ix_shipment_item_order_item (sales_order_item_id),
    KEY ix_shipment_item_main (main_work_id),
    CONSTRAINT fk_shipment_item_shipment
        FOREIGN KEY (shipment_id) REFERENCES shipment (shipment_id),
    CONSTRAINT fk_shipment_item_order_item
        FOREIGN KEY (sales_order_item_id) REFERENCES sales_order_item (sales_order_item_id),
    CONSTRAINT fk_shipment_item_main
        FOREIGN KEY (main_work_id) REFERENCES production_work (production_work_id),
    CONSTRAINT ck_shipment_item_qty CHECK (shipment_qty >= 0 AND test_specimen_qty >= 0),
    CONSTRAINT ck_shipment_item_price_basis CHECK (price_basis_snapshot IN ('EA','KG','CHARGE'))
) ENGINE=InnoDB COMMENT='출하 전표 상세 (t_outcome) — 수주품목 × 출하 LOT';

-- =====================================================================
-- 9. 설비 비가동 / 보전
-- =====================================================================

CREATE TABLE equipment_downtime (
    equipment_downtime_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    equipment_id        BIGINT UNSIGNED NOT NULL,
    downtime_date       DATE         NOT NULL,
    started_at          DATETIME     NULL,
    ended_at            DATETIME     NULL,
    duration_min        DECIMAL(10,2) NULL,
    is_planned          TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '계획 비가동 = 스케줄 계산에서 제외 구간',
    reason_code         VARCHAR(50)  NULL COMMENT 'common_code (DOWNTIME_REASON)',
    reporter_employee_id BIGINT UNSIGNED NULL,
    remark              VARCHAR(500) NULL,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          BIGINT UNSIGNED NULL,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    updated_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (equipment_downtime_id),
    KEY ix_equipment_downtime_equipment_date (equipment_id, downtime_date),
    KEY ix_equipment_downtime_period (equipment_id, started_at, ended_at),
    KEY ix_equipment_downtime_reporter (reporter_employee_id),
    CONSTRAINT fk_equipment_downtime_equipment
        FOREIGN KEY (equipment_id) REFERENCES equipment (equipment_id),
    CONSTRAINT fk_equipment_downtime_reporter
        FOREIGN KEY (reporter_employee_id) REFERENCES employee (employee_id),
    CONSTRAINT ck_equipment_downtime_period
        CHECK (ended_at IS NULL OR started_at IS NULL OR ended_at >= started_at)
) ENGINE=InnoDB COMMENT='설비 비가동 (t_downtime)';

CREATE TABLE maintenance (
    maintenance_id      BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    equipment_id        BIGINT UNSIGNED NOT NULL,
    maintenance_type    VARCHAR(50)  NOT NULL,
    maintenance_date    DATE         NOT NULL,
    started_at          DATETIME     NULL,
    completed_at        DATETIME     NULL,
    worker_employee_id  BIGINT UNSIGNED NULL,
    description         TEXT         NULL,
    result              TEXT         NULL,
    status              VARCHAR(30)  NOT NULL DEFAULT 'OPEN',
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          BIGINT UNSIGNED NULL,
    updated_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    updated_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (maintenance_id),
    KEY ix_maintenance_equipment_date (equipment_id, maintenance_date),
    KEY ix_maintenance_worker (worker_employee_id),
    CONSTRAINT fk_maintenance_equipment
        FOREIGN KEY (equipment_id) REFERENCES equipment (equipment_id),
    CONSTRAINT fk_maintenance_worker
        FOREIGN KEY (worker_employee_id) REFERENCES employee (employee_id),
    CONSTRAINT ck_maintenance_status
        CHECK (status IN ('OPEN','IN_PROGRESS','COMPLETED','CANCELLED')),
    CONSTRAINT ck_maintenance_period
        CHECK (completed_at IS NULL OR started_at IS NULL OR completed_at >= started_at)
) ENGINE=InnoDB COMMENT='설비 보전/수리 (t_maintenance)';

-- =====================================================================
-- 9.5 출력 이력
-- =====================================================================

CREATE TABLE print_log (
    print_log_id        BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    print_template_version_id BIGINT UNSIGNED NOT NULL COMMENT '실제 사용한 양식 버전',
    print_purpose_id    BIGINT UNSIGNED NOT NULL,
    source_table        VARCHAR(64)  NOT NULL COMMENT '예: inspection_target, production_work, shipment',
    source_id           BIGINT UNSIGNED NOT NULL,
    output_format       VARCHAR(10)  NOT NULL DEFAULT 'PDF',
    output_file_name    VARCHAR(255) NULL,
    output_file_hash    CHAR(64)     NULL,
    output_content      LONGBLOB     NULL COMMENT '발행본 보관이 필요한 용도만 저장 (예: 성적서)',
    data_snapshot_json  JSON         NULL COMMENT '치환에 사용한 값 — 재발행 시 동일 내용 보장',
    copies              INT          NOT NULL DEFAULT 1,
    reprint_of_id       BIGINT UNSIGNED NULL,
    printed_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    printed_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (print_log_id),
    KEY ix_print_log_source (source_table, source_id),
    KEY ix_print_log_version (print_template_version_id),
    KEY ix_print_log_purpose_time (print_purpose_id, printed_at),
    KEY ix_print_log_reprint (reprint_of_id),
    CONSTRAINT fk_print_log_version
        FOREIGN KEY (print_template_version_id) REFERENCES print_template_version (print_template_version_id),
    CONSTRAINT fk_print_log_purpose
        FOREIGN KEY (print_purpose_id) REFERENCES print_purpose (print_purpose_id),
    CONSTRAINT fk_print_log_reprint
        FOREIGN KEY (reprint_of_id) REFERENCES print_log (print_log_id),
    CONSTRAINT ck_print_log_format CHECK (output_format IN ('PDF','XLSX'))
) ENGINE=InnoDB COMMENT='출력 이력 — 어떤 양식 버전으로 무엇을 누가 언제 발행했는지';

-- 기본 데이터 공급원 / 용도 (시스템 제공, 용도는 사용자가 추가 가능)
INSERT INTO print_data_source (data_source_code, data_source_name, output_unit, description) VALUES
 ('INSPECTION_TARGET', '검사 대상',   '검사대상 1건', '검사 Header + 대상 + 항목/측정값 + 기준'),
 ('PRODUCTION_WORK',   '작업 LOT',    '작업 LOT 1건', 'LOT + 투입 목록 + 확정 조건'),
 ('STANDARD',          '작업표준',    '작업표준 1건', '작업표준 Version + 항목 × 단계 조건'),
 ('SHIPMENT',          '출하 전표',   '전표 1건',     '전표 + 상세'),
 ('SHIPMENT_CLOSING',  '마감',        '마감 1건',     '마감 + 포함 전표');

INSERT INTO print_purpose (purpose_code, purpose_name, print_data_source_id, is_system, sort_order)
SELECT v.code, v.name, ds.print_data_source_id, 1, v.ord
  FROM (SELECT 'INSPECTION_REPORT' code, '검사성적서' name, 'INSPECTION_TARGET' ds, 1 ord
        UNION ALL SELECT 'WORK_ORDER',    '작업지시서', 'PRODUCTION_WORK', 2
        UNION ALL SELECT 'LOT_LABEL',     'LOT 라벨',   'PRODUCTION_WORK', 3
        UNION ALL SELECT 'WORK_STANDARD', '작업표준서', 'STANDARD',        4
        UNION ALL SELECT 'SHIPMENT_SLIP', '출하전표',   'SHIPMENT',        5
        UNION ALL SELECT 'CLOSING_REPORT','마감내역서', 'SHIPMENT_CLOSING',6) v
  JOIN print_data_source ds ON ds.data_source_code = v.ds;

-- 고정 양식(FIXED)용 데이터 공급원·용도 추가 (구 PrintDoc 코드 출력물)
INSERT INTO print_data_source (data_source_code, data_source_name, output_unit, description) VALUES
 ('SALES_ORDER',  '수주(입고)',   '수주 1건',      '공정이동표·제품라벨 (구 F_IncomeAddForm 출력)'),
 ('SCHEDULE_DAY', '일자별 계획', '작업일 × 설비', '작업 진행 현황표');

INSERT INTO print_purpose (purpose_code, purpose_name, print_data_source_id, is_system, sort_order)
SELECT v.code, v.name, ds.print_data_source_id, 1, v.ord
  FROM (SELECT 'PROCESS_SHEET' code, '공정이동표' name, 'SALES_ORDER' ds, 7 ord
        UNION ALL SELECT 'PRODUCT_LABEL',  '제품표시 라벨',  'SALES_ORDER',     8
        UNION ALL SELECT 'WORK_DAILY',     '작업일보',       'PRODUCTION_WORK', 9
        UNION ALL SELECT 'PROGRESS_SHEET', '작업 진행 현황표','SCHEDULE_DAY',   10) v
  JOIN print_data_source ds ON ds.data_source_code = v.ds;

-- 고정 양식 등록 (레이아웃 옵션 = 구 코드 상수값, 관리자 화면에서 조정)
INSERT INTO print_template (print_template_name, print_purpose_id, template_kind, renderer_key, is_default)
SELECT v.name, p.print_purpose_id, 'FIXED', v.renderer, 1
  FROM (SELECT '거래명세표 (기본)' name, 'SHIPMENT_SLIP' purpose, 'SALES_SLIP' renderer
        UNION ALL SELECT '공정이동표 (기본)',     'PROCESS_SHEET',  'PROCESS_SHEET'
        UNION ALL SELECT '제품표시 라벨 (기본)',  'PRODUCT_LABEL',  'PRODUCT_LABEL'
        UNION ALL SELECT '작업일보 (기본)',       'WORK_DAILY',     'WORK_DAILY'
        UNION ALL SELECT '작업 진행 현황표 (기본)','PROGRESS_SHEET', 'PROGRESS_SHEET'
        UNION ALL SELECT '작업표준서 (기본)',     'WORK_STANDARD',  'WORK_STANDARD') v
  JOIN print_purpose p ON p.purpose_code = v.purpose;

INSERT INTO print_template_version (print_template_id, version_no, layout_options_json, is_current, change_note)
SELECT t.print_template_id, 1,
       CASE t.renderer_key
         -- 공통 스키마: page{size,orientation,margin|width_mm,height_mm,margin_mm} / font{family,…size} /
         --             title / sections[{key,title,visible}] / columns[{key,title,width|relative,visible}] / barcode{…}
         WHEN 'SALES_SLIP' THEN '{
            "page": {"size": "A4", "orientation": "PORTRAIT", "margin": 20},
            "font": {"family": ["굴림체", "GulimChe", "맑은 고딕"], "default": 11, "title": 15, "header_label": 9, "header_value": 9,
                     "storage_type": 9, "company_label": 7, "company_value": 9, "item_header": 9, "item": 9,
                     "summary_label": 9, "summary_value": 9, "page_no": 9},
            "title": "거 래 명 세 표",
            "items_per_page": 6,
            "copies": [{"label": "(공급자 보관용)", "border_color": "#000000"},
                       {"label": "(거래처 보관용)", "border_color": "#E53935"}],
            "spacing": {"top": 10, "cut_line_top": 15, "cut_line_bottom": 20, "header_to_company": 4, "company_to_items": 4},
            "row_height": {"header": 15, "company": 12, "item_header": 14, "item": 30},
            "border": {"cell": 0.5, "divider": 1.0},
            "background_color": "#EEEEEE",
            "stamp": {"show": true, "size": 50, "offset_x": -250, "top_y": 65, "bottom_y": 480},
            "merge_multiple_slips": true
         }'
         WHEN 'PROCESS_SHEET' THEN '{
            "page": {"size": "A4", "orientation": "PORTRAIT", "margin": 20},
            "font": {"family": ["맑은 고딕", "Malgun Gothic"], "default": 12, "title": 40, "header": 10, "content": 10,
                     "item_title": 30, "security": 32},
            "title": "공정이동표",
            "show_security_box": true, "security_label": "보안품",
            "priority_code_group": "PRIORITY",
            "left_labels": ["거래처","품명","규격","기종","재질","수량","중량","단중","요구경도","심부경도",
                            "경화층","조직","공정","고객로트","코일번호",""],
            "process_rows": 16,
            "columns": [{"key":"label","title":"","width":70},{"key":"value","title":"","width":100},
                        {"key":"seq","title":"순서","width":30},{"key":"unit_process","title":"공정명","width":50},
                        {"key":"lot_no","title":"작업로트","width":80},{"key":"tray","title":"T.NO","width":40},
                        {"key":"qty","title":"작업수량","width":60},{"key":"worker","title":"작업자","width":80},
                        {"key":"remark","title":"비고","relative":1}],
            "barcode": {"format": "CODE_128", "content": "sales_order_no", "width": 200, "height": 70, "scale": 4}
         }'
         WHEN 'PRODUCT_LABEL' THEN '{
            "page": {"width_mm": 65, "height_mm": 80, "margin_mm": 5},
            "font": {"family": ["맑은 고딕", "Malgun Gothic"], "title": 14, "label": 7, "value": 8, "order_no": 7},
            "title": "제  품  표  시",
            "label_column_width_mm": 22,
            "fields": [{"key":"sales_order_no","title":"수 주 번 호"},{"key":"customer_name","title":"거  래  처"},
                       {"key":"part_name","title":"품       명"},{"key":"model","title":"기       종"},
                       {"key":"material","title":"재       질"},{"key":"qty","title":"수       량"},
                       {"key":"weight","title":"중       량"},{"key":"unit_weight","title":"단       중"},
                       {"key":"hardness","title":"요 구 시 험"},{"key":"core_hardness","title":"심  부  경  도"},
                       {"key":"case_depth","title":"경  화  층"},{"key":"heat_process","title":"공       정"},
                       {"key":"customer_lot","title":"고  객  로  트"},{"key":"coil_no","title":"코  일  번  호"}],
            "barcode": {"format": "CODE_128", "content": "sales_order_no", "width": 300, "height": 60, "scale": 3}
         }'
         WHEN 'WORK_DAILY' THEN '{
            "page": {"size": "A4", "orientation": "LANDSCAPE", "margin": 8},
            "font": {"family": ["맑은 고딕", "Malgun Gothic"], "title": 14, "section": 8, "label": 7, "value": 7, "table": 7},
            "title": "작  업  일  보",
            "sections": [{"key":"lot_info","title":"로  트  정  보","visible":true},
                         {"key":"input","title":"투  입  정  보","visible":true},
                         {"key":"split","title":"분  할  정  보","visible":true},
                         {"key":"standard","title":"작  업  표  준  사  항","visible":true},
                         {"key":"condition","title":"작  업  조  건","visible":true},
                         {"key":"inspection","title":"검  사  내  역","visible":true},
                         {"key":"defect","title":"불  량  내  역","visible":true},
                         {"key":"remark","title":"특  기  사  항","visible":true}],
            "input_columns": [{"key":"sales_order_no","title":"수주번호","width":70},{"key":"customer_name","title":"거래처","relative":2},
                              {"key":"part_name","title":"품명","relative":3},{"key":"part_number","title":"품번","relative":2},
                              {"key":"specification","title":"규격","relative":2},{"key":"qty","title":"수량","width":40},
                              {"key":"defect_qty","title":"불량수량","width":40},{"key":"weight","title":"중량","width":55}],
            "split_columns": [{"key":"tray","title":"트레이번호","relative":2},{"key":"qty","title":"수량","width":60},
                              {"key":"loaded_at","title":"투입시간","width":80},{"key":"unloaded_at","title":"출고시간","width":80}],
            "condition_item_column_width": 60,
            "line_inspection_points": 5,
            "empty_text": {"input":"투입 내역 없음","split":"분할 데이터 없음","standard":"작업표준 데이터 없음","condition":"작업조건 데이터 없음"}
         }'
         WHEN 'PROGRESS_SHEET' THEN '{
            "page": {"size": "A4", "orientation": "PORTRAIT", "margin": 20},
            "font": {"family": ["맑은 고딕", "Malgun Gothic"], "title": 20, "header": 12, "content": 10},
            "title": "작업 진행 현황표",
            "status_code_group": "WORK_STATUS",
            "columns": [{"key":"no","title":"번호","width":50},{"key":"work_name","title":"작업명","relative":2},
                        {"key":"worker","title":"담당자","relative":1},{"key":"start","title":"시작일","width":80},
                        {"key":"end","title":"완료일","width":80},{"key":"progress","title":"진행률","width":70},
                        {"key":"status","title":"상태","width":60}],
            "empty_text": "데이터가 없습니다."
         }'
         WHEN 'WORK_STANDARD' THEN '{
            "page": {"size": "A4", "orientation": "LANDSCAPE", "margin": 8},
            "font": {"family": ["맑은 고딕", "Malgun Gothic"], "title": 14, "section": 7, "label": 7, "value": 7, "standard_table": 7},
            "title": "작  업  표  준  서",
            "sections": [{"key":"part_info","title":"품  목  정  보","visible":true},
                         {"key":"requirement","title":"요  구  사  항","visible":true},
                         {"key":"equipment_cycle","title":"작  업  설  비  /  사이클","visible":true},
                         {"key":"process","title":"작  업  공  정","visible":true},
                         {"key":"standard","title":"작  업  표  준","visible":true}],
            "label_width": 42,
            "condition_item_column_width": 48,
            "condition_item_header": "관리항목",
            "empty_text": "작업표준 상세 데이터가 없습니다."
         }'
       END,
       1, '구 PrintDoc 상수값 이관'
  FROM print_template t WHERE t.template_kind = 'FIXED';

-- 치환자 사전 (§15.2 P2·P3). 목록(LIST) 안의 항목은 '목록.항목' 키 — 양식에서는 {{#목록}} … {{/목록}} 행 안에 {{항목}} 또는 {{목록.항목}}
--   검사 대상의 구 좌표형 키 {{T1_3_P2}}, {{C1_2_Spec}} 은 사전 없이 호환 허용 (구 InspectionPrintService.BuildPlaceholders)
INSERT INTO print_field (print_data_source_id, field_key, field_alias, field_type, field_group, format_pattern, description, sample_value, sort_order)
SELECT ds.print_data_source_id, v.k, v.a, v.t, v.g, v.f, v.d, v.s, v.o
  FROM (SELECT 'INSPECTION_TARGET' ds, 'InspectionNo' k, '검사번호' a, 'TEXT' t, '기본정보' g, NULL f, NULL d, 'TO260930-001' s, 10 o
        UNION ALL SELECT 'INSPECTION_TARGET', 'InspectionType', '검사구분', 'TEXT', '기본정보', NULL, '공통코드 INSPECTION_TYPE 표시명', '출하검사', 20
        UNION ALL SELECT 'INSPECTION_TARGET', 'InspectionDate', '검사일자', 'DATE', '기본정보', 'yyyy-MM-dd', NULL, '2026-09-30', 30
        UNION ALL SELECT 'INSPECTION_TARGET', 'WorkDate', '작업일자', 'DATE', '기본정보', 'yyyy-MM-dd', '작업 LOT 작업일', '2026-09-30', 40
        UNION ALL SELECT 'INSPECTION_TARGET', 'IssueDate', '발행일자', 'DATE', '기본정보', 'yyyy-MM-dd', '성적서 발행 시각', '2026-09-30', 50
        UNION ALL SELECT 'INSPECTION_TARGET', 'LotNo', '작업LOT', 'TEXT', '기본정보', NULL, '검사한 작업 LOT (주 LOT)', '260930-B01-001', 60
        UNION ALL SELECT 'INSPECTION_TARGET', 'ConvertLot', '제출LOT', 'TEXT', '기본정보', NULL, '고객 제출 LOT (구 convertlot)', 'HD-0930-01', 70
        UNION ALL SELECT 'INSPECTION_TARGET', 'CustomerName', '거래처', 'TEXT', '대상', NULL, NULL, '한독기어', 80
        UNION ALL SELECT 'INSPECTION_TARGET', 'CustomerLot', '고객LOT', 'TEXT', '대상', NULL, NULL, 'CL-77', 90
        UNION ALL SELECT 'INSPECTION_TARGET', 'PartName', '품명', 'TEXT', '대상', NULL, NULL, '헬리컬 기어 A', 100
        UNION ALL SELECT 'INSPECTION_TARGET', 'PartNumber', '품번', 'TEXT', '대상', NULL, NULL, 'HG-100', 110
        UNION ALL SELECT 'INSPECTION_TARGET', 'Specification', '규격', 'TEXT', '대상', NULL, NULL, NULL, 120
        UNION ALL SELECT 'INSPECTION_TARGET', 'Model', '기종', 'TEXT', '대상', NULL, NULL, NULL, 130
        UNION ALL SELECT 'INSPECTION_TARGET', 'Material', '재질', 'TEXT', '대상', NULL, NULL, 'SCM420H', 140
        UNION ALL SELECT 'INSPECTION_TARGET', 'ChargeQt', '검사수량', 'NUMBER', '대상', '#,##0', '검사 대상 수량 (구 chargeqt)', '400', 150
        UNION ALL SELECT 'INSPECTION_TARGET', 'Decision', '판정', 'TEXT', '판정', NULL, '공통코드 DECISION 표시명', '합격', 160
        UNION ALL SELECT 'INSPECTION_TARGET', 'Measurements', '측정', 'LIST', '측정값', NULL, '검사 항목별 1행', NULL, 200
        UNION ALL SELECT 'INSPECTION_TARGET', 'Measurements.Seq', NULL, 'NUMBER', '측정값', NULL, '항목 순번', '1', 201
        UNION ALL SELECT 'INSPECTION_TARGET', 'Measurements.ItemType', NULL, 'TEXT', '측정값', NULL, '공통코드 INSPECTION_ITEM_TYPE 표시명', '외관·경도·치수', 202
        UNION ALL SELECT 'INSPECTION_TARGET', 'Measurements.Item', NULL, 'TEXT', '측정값', NULL, '항목명', '표면경도', 203
        UNION ALL SELECT 'INSPECTION_TARGET', 'Measurements.Location', NULL, 'TEXT', '측정값', NULL, NULL, NULL, 204
        UNION ALL SELECT 'INSPECTION_TARGET', 'Measurements.Spec', NULL, 'TEXT', '측정값', NULL, '기준 요구사항 원문', 'HRC 58~62', 205
        UNION ALL SELECT 'INSPECTION_TARGET', 'Measurements.P1', NULL, 'TEXT', '측정값', NULL, '시료 1 측정값 (P1~P10)', '60.1', 206
        UNION ALL SELECT 'INSPECTION_TARGET', 'Measurements.P2', NULL, 'TEXT', '측정값', NULL, NULL, NULL, 207
        UNION ALL SELECT 'INSPECTION_TARGET', 'Measurements.P3', NULL, 'TEXT', '측정값', NULL, NULL, NULL, 208
        UNION ALL SELECT 'INSPECTION_TARGET', 'Measurements.P4', NULL, 'TEXT', '측정값', NULL, NULL, NULL, 209
        UNION ALL SELECT 'INSPECTION_TARGET', 'Measurements.P5', NULL, 'TEXT', '측정값', NULL, NULL, NULL, 210
        UNION ALL SELECT 'INSPECTION_TARGET', 'Measurements.P6', NULL, 'TEXT', '측정값', NULL, NULL, NULL, 211
        UNION ALL SELECT 'INSPECTION_TARGET', 'Measurements.P7', NULL, 'TEXT', '측정값', NULL, NULL, NULL, 212
        UNION ALL SELECT 'INSPECTION_TARGET', 'Measurements.P8', NULL, 'TEXT', '측정값', NULL, NULL, NULL, 213
        UNION ALL SELECT 'INSPECTION_TARGET', 'Measurements.P9', NULL, 'TEXT', '측정값', NULL, NULL, NULL, 214
        UNION ALL SELECT 'INSPECTION_TARGET', 'Measurements.P10', NULL, 'TEXT', '측정값', NULL, NULL, NULL, 215
        UNION ALL SELECT 'INSPECTION_TARGET', 'Measurements.Result', NULL, 'TEXT', '측정값', NULL, '측정 결과 요약', NULL, 216
        UNION ALL SELECT 'INSPECTION_TARGET', 'Measurements.Decision', NULL, 'TEXT', '측정값', NULL, '항목 판정 표시명', '합격', 217
        UNION ALL SELECT 'INSPECTION_TARGET', 'HardnessChart', '경화층차트', 'IMAGE', '이미지', NULL, '첨부 HARDNESS_CHART — 셀 병합 영역에 맞춰 삽입', NULL, 300
        UNION ALL SELECT 'INSPECTION_TARGET', 'StructurePhoto', '조직사진', 'IMAGE', '이미지', NULL, '첨부 STRUCTURE_PHOTO', NULL, 310
        UNION ALL SELECT 'SHIPMENT', 'ShipmentNo', '전표번호', 'TEXT', '전표', NULL, NULL, 'O260930-001', 10
        UNION ALL SELECT 'SHIPMENT', 'ShipmentDate', '출하일자', 'DATE', '전표', 'yyyy년 M월 d일', '구 거래명세표는 인쇄 시각을 찍음 → 출하일로 수정', '2026년 9월 30일', 20
        UNION ALL SELECT 'SHIPMENT', 'SupplierName', '공급자상호', 'TEXT', '공급자', NULL, 'company', NULL, 30
        UNION ALL SELECT 'SHIPMENT', 'SupplierBusinessNo', '공급자등록번호', 'TEXT', '공급자', NULL, NULL, NULL, 31
        UNION ALL SELECT 'SHIPMENT', 'SupplierCeoName', '공급자성명', 'TEXT', '공급자', NULL, NULL, NULL, 32
        UNION ALL SELECT 'SHIPMENT', 'SupplierAddress', '공급자주소', 'TEXT', '공급자', NULL, NULL, NULL, 33
        UNION ALL SELECT 'SHIPMENT', 'SupplierBusinessType', '공급자업태', 'TEXT', '공급자', NULL, NULL, NULL, 34
        UNION ALL SELECT 'SHIPMENT', 'SupplierBusinessItem', '공급자종목', 'TEXT', '공급자', NULL, NULL, NULL, 35
        UNION ALL SELECT 'SHIPMENT', 'CustomerName', '거래처', 'TEXT', '공급받는자', NULL, '전표 당시 Snapshot', NULL, 40
        UNION ALL SELECT 'SHIPMENT', 'CustomerBusinessNo', '거래처등록번호', 'TEXT', '공급받는자', NULL, NULL, NULL, 41
        UNION ALL SELECT 'SHIPMENT', 'CustomerCeoName', '거래처성명', 'TEXT', '공급받는자', NULL, NULL, NULL, 42
        UNION ALL SELECT 'SHIPMENT', 'CustomerAddress', '거래처주소', 'TEXT', '공급받는자', NULL, NULL, NULL, 43
        UNION ALL SELECT 'SHIPMENT', 'CustomerBusinessType', '거래처업태', 'TEXT', '공급받는자', NULL, NULL, NULL, 44
        UNION ALL SELECT 'SHIPMENT', 'CustomerBusinessItem', '거래처종목', 'TEXT', '공급받는자', NULL, NULL, NULL, 45
        UNION ALL SELECT 'SHIPMENT', 'SupplyAmount', '공급가액', 'NUMBER', '합계', '#,##0', '저장값 (출하 서비스 계산)', '1200000', 50
        UNION ALL SELECT 'SHIPMENT', 'VatAmount', '세액', 'NUMBER', '합계', '#,##0', NULL, '120000', 51
        UNION ALL SELECT 'SHIPMENT', 'TotalAmount', '합계금액', 'NUMBER', '합계', '#,##0', NULL, '1320000', 52
        UNION ALL SELECT 'SHIPMENT', 'Items', '품목', 'LIST', '품목', NULL, '전표 상세 1행 (품목 합산 출력이면 품목·단가별 합산)', NULL, 100
        UNION ALL SELECT 'SHIPMENT', 'Items.No', NULL, 'NUMBER', '품목', NULL, NULL, '1', 101
        UNION ALL SELECT 'SHIPMENT', 'Items.PartName', NULL, 'TEXT', '품목', NULL, NULL, '헬리컬 기어 A', 102
        UNION ALL SELECT 'SHIPMENT', 'Items.PartNumber', NULL, 'TEXT', '품목', NULL, NULL, NULL, 103
        UNION ALL SELECT 'SHIPMENT', 'Items.Specification', NULL, 'TEXT', '품목', NULL, NULL, NULL, 104
        UNION ALL SELECT 'SHIPMENT', 'Items.Model', NULL, 'TEXT', '품목', NULL, NULL, NULL, 105
        UNION ALL SELECT 'SHIPMENT', 'Items.ProcessName', NULL, 'TEXT', '품목', NULL, '열처리 공정명', NULL, 106
        UNION ALL SELECT 'SHIPMENT', 'Items.Qty', NULL, 'NUMBER', '품목', '#,##0', '출하수량 (시험편 제외)', '400', 107
        UNION ALL SELECT 'SHIPMENT', 'Items.Weight', NULL, 'NUMBER', '품목', '#,##0.0', NULL, NULL, 108
        UNION ALL SELECT 'SHIPMENT', 'Items.PriceUnit', NULL, 'TEXT', '품목', NULL, '단가 구분 표시명 (공통코드 PRICE_BASIS)', 'ea', 109
        UNION ALL SELECT 'SHIPMENT', 'Items.UnitPrice', NULL, 'NUMBER', '품목', '#,##0', NULL, NULL, 110
        UNION ALL SELECT 'SHIPMENT', 'Items.Amount', NULL, 'NUMBER', '품목', '#,##0', NULL, NULL, 111
        UNION ALL SELECT 'SHIPMENT', 'Items.Vat', NULL, 'NUMBER', '품목', '#,##0', '행 세액 = 금액 × 세율 (표시용)', NULL, 112
        UNION ALL SELECT 'SHIPMENT', 'Items.SubmitLot', NULL, 'TEXT', '품목', NULL, NULL, NULL, 113
        UNION ALL SELECT 'SHIPMENT', 'Items.CustomerLot', NULL, 'TEXT', '품목', NULL, NULL, NULL, 114
        UNION ALL SELECT 'SHIPMENT', 'Stamp', '도장', 'IMAGE', '공급자', NULL, 'company.stamp_image', NULL, 120) v
  JOIN print_data_source ds ON ds.data_source_code = v.ds;

-- =====================================================================
-- 9.6 관리자 설정 초기값 (구 코드 하드코딩 값 — 설계안 §15.4)
-- =====================================================================

INSERT INTO system_setting (setting_key, category, setting_name, value_type, default_value, min_value, max_value, unit_label, description, requires_restart, sort_order) VALUES
 ('schedule.day_start_time',          '스케줄', '작업일 시작 시각 (교대 미설정 시)', 'TIME',    '08:00', NULL, NULL, NULL, '구 Hour < 8 / AddHours(8) — work_shift 첫 교대가 있으면 그 값 우선', 0, 10),
 ('schedule.refresh_interval_sec',    '스케줄', '현황 자동 새로고침 주기',           'INT',     '60',    10, 3600, '초', '구 Timer Interval 60_000 (웹은 SignalR 푸시 + 보조 폴링)', 0, 20),
 ('schedule.default_running_time_min','스케줄', '표준 없을 때 기본 작업시간',        'DECIMAL', '480',   1, 10080, '분', '구 RunningTime ?? 8.0m', 0, 30),
 ('schedule.temp_lot_prefix',         '스케줄', '임시 LOT번호 접두어',              'STRING',  'P',     NULL, NULL, NULL, '구 $"P{yyMMdd}-…"', 0, 40),
 ('schedule.board_days',              '스케줄', '생산계획 화면 기본 표시 일수',      'INT',     '2',     1, 14, '일', '구 화면 범위 08:00~익일 08:00 (1일) 고정', 0, 50),
 ('lot.number_format',                'LOT',    '작업 LOT번호 형식',                 'STRING',  '{yyMMdd}-{EQUIP}-{SEQ:000}', NULL, NULL, NULL, '치환: {yyMMdd} 작업일, {EQUIP} 설비이니셜, {SEQ:000} 설비·일자 순번', 0, 10),
 ('downtime.default_duration_min',    '설비',   '비가동 입력 기본 시간',             'INT',     '60',    1, 1440, '분', '구 08:00~09:00 기본값', 0, 10),
 ('sales.vat_rate',                   '영업',   '부가세율',                          'DECIMAL', '0.10',  0, 1, NULL, '구 OutputSheet TAX_RATE 0.1m', 0, 10),
 ('sales.amount_rounding',            '영업',   '금액 반올림 방식',                  'STRING',  'ROUND', NULL, NULL, NULL, 'ROUND / FLOOR / CEIL', 0, 20),
 ('print.pdf_converter_path',         '출력',   'PDF 변환기 경로 (서버)',            'PATH',    'C:\\Program Files\\LibreOffice\\program\\soffice.exe', NULL, NULL, NULL, '구 클라이언트 고정 경로 2곳 탐색 → 서버 1곳 설정', 1, 10),
 ('print.pdf_convert_timeout_sec',    '출력',   'PDF 변환 제한시간',                 'INT',     '60',    5, 600, '초', '구 WaitForExit(60_000)', 0, 20),
 ('print.max_template_file_mb',       '출력',   '양식 파일 최대 크기',               'INT',     '10',    1, 100, 'MB', 'DB max_allowed_packet 이하로 설정', 0, 30),
 ('print.keep_issued_output',         '출력',   '발행본 PDF 보관 용도',              'JSON',    '["INSPECTION_REPORT"]', NULL, NULL, NULL, 'print_log.output_content 저장 대상 용도 코드', 0, 40),
 ('file.storage_root',                '파일',   '첨부 파일 저장 위치 (서버)',        'PATH',    'D:\\MES\\Files', NULL, NULL, NULL, '조직사진·경화층 차트 등 — 구 BaseDirectory\\Files 하위 (PC별)', 1, 10),
 ('file.max_attachment_mb',           '파일',   '첨부 파일 최대 크기',               'INT',     '20',    1, 200, 'MB', '도면·이미지 등 attachment 1건 — DB max_allowed_packet 이하', 0, 20),
 ('log.retention_days',               '시스템', '로그 보관 일수',                    'INT',     '7',     1, 365, '일', '구 AppLogger 7일', 1, 10),
 ('work.complete_time_round_min',     '생산',   '완료시각 단위 (내림)',              'INT',     '5',     1, 60, '분', '구 RoundToNearest5Minutes (실제 동작은 내림)', 0, 10),
 ('sales_order.item_number_format',   '영업',   '입고(수주)번호 형식',               'STRING',  'I{yyMMdd}-{SEQ:000}', NULL, NULL, NULL, '구 IncomeAddService', 0, 30),
 ('shipment.number_format',           '영업',   '출하 전표번호 형식',                'STRING',  'O{yyMMdd}-{SEQ:000}', NULL, NULL, NULL, '구 OutcomeAddService', 0, 40),
 ('standard.code_format',             '생산',   '작업표준 코드 형식',                'STRING',  'STD-{PART}-{UNIT}-{SEQ:00}', NULL, NULL, NULL, '치환: {PART} 품목 코드, {UNIT} 단위공정 코드, {SEQ:00} 같은 품목·공정 순번', 0, 20),
 ('inspection.number_format',         '품질',   '검사번호 형식',                     'STRING',  '{TYPE}{yyMMdd}-{SEQ:000}', NULL, NULL, NULL, '{TYPE} = 공통코드 INSPECTION_TYPE attr prefix (TI/TP/TO)', 0, 10),
 ('auth.session_timeout_min',         '시스템', '로그인 세션 유지 시간',             'INT',     '480',   5, 1440, '분', '신규 — 요청이 있으면 연장 (sliding)', 0, 20),
 ('auth.permission_cache_sec',        '시스템', '권한 캐시 유지 시간',               'INT',     '60',    0, 3600, '초', '신규 — 역할·메뉴 권한 변경이 반영되기까지 최대 시간 (API에서 변경 시 즉시 반영)', 0, 30),
 ('auth.login_max_attempts_per_min',  '시스템', '로그인 시도 제한 (IP당 1분)',       'INT',     '10',    1, 1000, '회', '신규 — 비밀번호 대입 방지', 1, 40),
 ('auth.password_min_length',         '시스템', '비밀번호 최소 길이',                'INT',     '8',     4, 128, '자', '신규', 0, 50);

-- 로직에서 참조하는 공통코드 (is_system = 1: 코드 고정, 표시명만 수정 가능)
INSERT INTO common_code_group (group_code, group_name, description) VALUES
 ('DECISION',            '판정',          '구 코드의 "합격"/"불합격" 문자열 비교 대체'),
 ('DEFECT_ACTION',       '부적합 처리',   '구 cmbReworkStep 하드코딩 항목'),
 ('INSPECTION_ITEM_TYPE','검사 항목 유형','구 TabPrefixMap / CriteriaPrefixMap / InspectionItemHelper.Map'),
 ('WORK_STATUS',         '작업 LOT 상태', '배정/투입/완료 표시명'),
 ('PRIORITY',            '우선순위',      '구 ProcessSheet Priority switch (표시명·색상)'),
 ('INSPECTION_TYPE',     '검사 구분',     '구 TI/TP/TO'),
 ('CLOSING_STATUS',      '마감 상태',     '구 ClosingStatus enum (미마감/마감완료/이월)'),
 ('PRICE_BASIS',         '단가 적용 구분', '구 t_part.unit (ea/kg/ch)'),
 ('SCHEDULE_STATUS',     '계획 상태',     '계획 블록 PLANNED/CONFIRMED/RELEASED/CANCELLED (설계 §4)'),
 ('RUNNING_TIME_SOURCE', '작업시간 출처', '스케줄 작업시간 결정 5단계 (설계 §7)'),
 ('CUSTOMER_TYPE',       '거래처 구분',   'customer.customer_type CHECK 값'),
 ('DAY_TYPE',            '달력 일 구분',  'work_calendar.day_type CHECK 값'),
 ('CONDITION_VALUE_TYPE','조건값 형식',   'condition_item.value_type CHECK 값'),
 ('ATTACHMENT_KIND',     '첨부 종류',     'attachment.attachment_kind — 구 PC 로컬 폴더(PartDrawingFolder 등) 대체'),
 ('RANGE_TYPE',          '판정 방식',     'inspection_criteria.range_type — 측정값 자동 판정 기준');

INSERT INTO common_code (common_code_group_id, code, code_name, sort_order, attr_json, is_system)
SELECT g.common_code_group_id, v.code, v.name, v.ord, v.attr, 1
  FROM (SELECT 'DECISION' grp, 'PASS' code, '합격' name, 1 ord, NULL attr
        UNION ALL SELECT 'DECISION', 'FAIL', '불합격', 2, NULL
        UNION ALL SELECT 'DECISION', 'CONDITIONAL', '조건부합격', 3, NULL
        UNION ALL SELECT 'DEFECT_ACTION', 'REWORK', '재처리', 1, NULL
        UNION ALL SELECT 'DEFECT_ACTION', 'SHIP', '출하', 2, NULL
        UNION ALL SELECT 'DEFECT_ACTION', 'SORT', '선별', 3, NULL
        UNION ALL SELECT 'DEFECT_ACTION', 'HOLD', '보류', 4, NULL
        UNION ALL SELECT 'INSPECTION_ITEM_TYPE', 'APPEARANCE', '외관·경도·치수', 1, '{"result_prefix":"T1","criteria_prefix":"C1"}'
        UNION ALL SELECT 'INSPECTION_ITEM_TYPE', 'CASE_DEPTH', '경화층', 2, '{"result_prefix":"T2","criteria_prefix":"C2"}'
        UNION ALL SELECT 'INSPECTION_ITEM_TYPE', 'STRUCTURE', '조직', 3, '{"result_prefix":"T3","criteria_prefix":"C3"}'
        UNION ALL SELECT 'INSPECTION_ITEM_TYPE', 'LINE', '라인검사', 4, '{"result_prefix":"T4","criteria_prefix":"C4"}'
        UNION ALL SELECT 'INSPECTION_ITEM_TYPE', 'ETC', '추가사항', 5, '{"result_prefix":"T5","criteria_prefix":"C5"}'
        UNION ALL SELECT 'WORK_STATUS', 'ALLOCATED', '배정', 1, NULL
        UNION ALL SELECT 'WORK_STATUS', 'INPUT', '투입', 2, NULL
        UNION ALL SELECT 'WORK_STATUS', 'COMPLETED', '완료', 3, NULL
        UNION ALL SELECT 'WORK_STATUS', 'CANCELLED', '취소', 4, NULL
        UNION ALL SELECT 'PRIORITY', '0', '여유', 1, '{"color":"#9E9E9E"}'
        UNION ALL SELECT 'PRIORITY', '1', '일반', 2, '{"color":"#000000"}'
        UNION ALL SELECT 'PRIORITY', '2', '우선', 3, '{"color":"#FB8C00"}'
        UNION ALL SELECT 'PRIORITY', '3', '긴급', 4, '{"color":"#E53935"}'
        UNION ALL SELECT 'INSPECTION_TYPE', 'INCOMING', '입고검사', 1, '{"prefix":"TI"}'
        UNION ALL SELECT 'INSPECTION_TYPE', 'PROCESS', '공정검사', 2, '{"prefix":"TP"}'
        UNION ALL SELECT 'INSPECTION_TYPE', 'OUTGOING', '출하검사', 3, '{"prefix":"TO"}'
        UNION ALL SELECT 'CLOSING_STATUS', 'UNCLOSED', '미마감', 1, NULL
        UNION ALL SELECT 'CLOSING_STATUS', 'CLOSED', '마감완료', 2, NULL
        UNION ALL SELECT 'CLOSING_STATUS', 'CARRIED_OVER', '이월', 3, NULL
        UNION ALL SELECT 'PRICE_BASIS', 'EA', 'ea', 1, '{"legacy":"ea"}'
        UNION ALL SELECT 'PRICE_BASIS', 'KG', 'kg', 2, '{"legacy":"kg"}'
        UNION ALL SELECT 'PRICE_BASIS', 'CHARGE', 'ch', 3, '{"legacy":"ch"}'
        UNION ALL SELECT 'DEFECT_ACTION', 'SCRAP', '폐기', 5, NULL
        UNION ALL SELECT 'DEFECT_ACTION', 'RETURN', '반송', 6, NULL
        UNION ALL SELECT 'SCHEDULE_STATUS', 'PLANNED', '계획', 1, '{"color":"#1677FF"}'
        UNION ALL SELECT 'SCHEDULE_STATUS', 'CONFIRMED', '확정', 2, '{"color":"#722ED1"}'
        UNION ALL SELECT 'SCHEDULE_STATUS', 'RELEASED', '작업지시', 3, '{"color":"#52C41A"}'
        UNION ALL SELECT 'SCHEDULE_STATUS', 'CANCELLED', '취소', 4, '{"color":"#BFBFBF"}'
        UNION ALL SELECT 'RUNNING_TIME_SOURCE', 'STANDARD', '작업표준', 1, NULL
        UNION ALL SELECT 'RUNNING_TIME_SOURCE', 'PREVIOUS_WORK', '직전 작업', 2, NULL
        UNION ALL SELECT 'RUNNING_TIME_SOURCE', 'DEFAULT_TIME', '설비 기준시간', 3, NULL
        UNION ALL SELECT 'RUNNING_TIME_SOURCE', 'USER_INPUT', '사용자 입력', 4, NULL
        UNION ALL SELECT 'RUNNING_TIME_SOURCE', 'SETTING', '기본 설정값', 5, NULL
        UNION ALL SELECT 'CUSTOMER_TYPE', 'SALES', '매출처', 1, NULL
        UNION ALL SELECT 'CUSTOMER_TYPE', 'PURCHASE', '매입처', 2, NULL
        UNION ALL SELECT 'CUSTOMER_TYPE', 'BOTH', '매출·매입', 3, NULL
        UNION ALL SELECT 'DAY_TYPE', 'WORKDAY', '근무일', 1, NULL
        UNION ALL SELECT 'DAY_TYPE', 'HOLIDAY', '휴일', 2, '{"color":"#E53935"}'
        UNION ALL SELECT 'DAY_TYPE', 'SPECIAL', '특근', 3, '{"color":"#1677FF"}'
        UNION ALL SELECT 'CONDITION_VALUE_TYPE', 'NUMBER', '숫자', 1, NULL
        UNION ALL SELECT 'CONDITION_VALUE_TYPE', 'TEXT', '문자', 2, NULL
        UNION ALL SELECT 'ATTACHMENT_KIND', 'PART_DRAWING', '도면', 1, '{"owner":"part"}'
        UNION ALL SELECT 'ATTACHMENT_KIND', 'PART_IMAGE', '품목 이미지', 2, '{"owner":"part","image":true}'
        UNION ALL SELECT 'ATTACHMENT_KIND', 'STRUCTURE_PHOTO', '조직사진', 3, '{"owner":"inspection","image":true}'
        UNION ALL SELECT 'ATTACHMENT_KIND', 'HARDNESS_CHART', '경화층 차트', 4, '{"owner":"inspection","image":true}'
        UNION ALL SELECT 'ATTACHMENT_KIND', 'EQUIPMENT_IMAGE', '설비 이미지', 5, '{"owner":"equipment","image":true}'
        UNION ALL SELECT 'ATTACHMENT_KIND', 'ETC', '기타', 9, NULL
        UNION ALL SELECT 'RANGE_TYPE', 'BETWEEN', '범위 (하한~상한)', 1, '{"lower":true,"upper":true}'
        UNION ALL SELECT 'RANGE_TYPE', 'MIN', '하한 이상', 2, '{"lower":true}'
        UNION ALL SELECT 'RANGE_TYPE', 'MAX', '상한 이하', 3, '{"upper":true}'
        UNION ALL SELECT 'RANGE_TYPE', 'NONE', '기록만 (판정 없음)', 4, NULL) v
  JOIN common_code_group g ON g.group_code = v.grp;

-- =====================================================================
-- 9.7 공통 첨부파일 (품목 도면·이미지, 조직사진, 경화층 차트, 설비 이미지 등)
-- =====================================================================

CREATE TABLE attachment (
    attachment_id       BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    owner_table         VARCHAR(64)  NOT NULL COMMENT '예: part, inspection, equipment',
    owner_id            BIGINT UNSIGNED NOT NULL,
    attachment_kind     VARCHAR(30)  NOT NULL COMMENT 'PART_DRAWING / PART_IMAGE / STRUCTURE_PHOTO / HARDNESS_CHART / EQUIPMENT_IMAGE / ETC',
    file_name           VARCHAR(255) NOT NULL,
    content_type        VARCHAR(100) NULL,
    file_content        LONGBLOB     NULL COMMENT 'DB 보관 (작은 파일)',
    storage_path        VARCHAR(500) NULL COMMENT '서버 저장소 상대경로 (file.storage_root 기준, 큰 파일)',
    file_hash           CHAR(64)     NULL,
    file_size           INT UNSIGNED NULL,
    caption             VARCHAR(255) NULL COMMENT '예: 조직사진 위치·배율',
    sort_order          INT          NOT NULL DEFAULT 0,
    created_at          DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    created_by          BIGINT UNSIGNED NULL,
    PRIMARY KEY (attachment_id),
    KEY ix_attachment_owner (owner_table, owner_id, attachment_kind),
    CONSTRAINT ck_attachment_body CHECK (file_content IS NOT NULL OR storage_path IS NOT NULL)
) ENGINE=InnoDB COMMENT='공통 첨부 — 구 PC 로컬 폴더 파일 대체 (PartDrawingFolder, PartImageFolder, Structure, HardnessChart)';

-- =====================================================================
-- 9.8 권한 초기 데이터 (설계안 §18.3)
--   * 메뉴는 화면 단계마다 이 절에 추가한다 (menu_key = API 권한 키).
--   * 관리자 역할은 이 절 끝에서 모든 메뉴의 전체 권한을 받는다 — 역할 코드로 우회하는 로직은 두지 않음 (fail-closed).
--   * 관리자 계정은 DDL에 넣지 않는다. API 첫 기동 시 app_user가 비어 있으면 설정(Bootstrap:*)으로 생성.
-- =====================================================================

INSERT INTO role (role_code, role_name, description) VALUES
 ('ADMIN', '시스템관리자', '전체 메뉴 권한 (구 F_Option 비밀번호 대체)');

INSERT INTO menu (menu_key, menu_name, parent_menu_id, route, sort_order) VALUES
 ('master',     '기준정보', NULL, NULL, 10),
 ('sales',      '영업',     NULL, NULL, 20),
 ('production', '생산',     NULL, NULL, 30),
 ('quality',    '품질',     NULL, NULL, 40),
 ('equipment',  '설비',     NULL, NULL, 50),
 ('report',     '조회',     NULL, NULL, 60),
 ('system',     '시스템',   NULL, NULL, 90);

INSERT INTO menu (menu_key, menu_name, parent_menu_id, route, sort_order)
SELECT v.k, v.n, p.menu_id, v.r, v.o
  FROM (SELECT 'production.schedule' k, '생산계획' n, 'production' parent, '/production/schedule' r, 10 o
        UNION ALL SELECT 'master.company',          '자사 정보',     'master', '/master/company', 10
        UNION ALL SELECT 'master.customer',         '거래처',        'master', '/master/customer', 20
        UNION ALL SELECT 'master.part',             '품목',          'master', '/master/part', 25
        UNION ALL SELECT 'master.equipment_type',   '설비 유형',     'master', '/master/equipment-type', 30
        UNION ALL SELECT 'master.equipment',        '설비',          'master', '/master/equipment', 40
        UNION ALL SELECT 'master.unit_process',     '단위공정',      'master', '/master/unit-process', 50
        UNION ALL SELECT 'master.heat_process',     '공정 경로',     'master', '/master/heat-process', 55
        UNION ALL SELECT 'master.condition_item',   '조건 항목',     'master', '/master/condition-item', 60
        UNION ALL SELECT 'master.step_template',    '단계 템플릿',   'master', '/master/step-template', 62
        UNION ALL SELECT 'master.standard',         '작업표준',      'master', '/master/standard', 65
        UNION ALL SELECT 'master.inspection_standard', '검사기준',   'master', '/master/inspection-standard', 67
        UNION ALL SELECT 'master.process_default_time', '설비 기준시간', 'master', '/master/process-default-time', 70
        UNION ALL SELECT 'master.defect_reason',    '불량 사유',     'master', '/master/defect-reason', 80
        UNION ALL SELECT 'master.instrument',       '측정기구',      'master', '/master/instrument', 90
        UNION ALL SELECT 'master.work_shift',       '교대',          'master', '/master/work-shift', 100
        UNION ALL SELECT 'master.work_calendar',    '공장 달력',     'master', '/master/work-calendar', 110
        UNION ALL SELECT 'master.department',       '부서',          'master', '/master/department', 120
        UNION ALL SELECT 'master.job_position',     '직위',          'master', '/master/job-position', 130
        UNION ALL SELECT 'master.employee',         '사원',          'master', '/master/employee', 140
        UNION ALL SELECT 'system.user', '사용자 관리', 'system', '/system/users', 10
        UNION ALL SELECT 'system.role',    '역할·권한',   'system', '/system/roles',      20
        UNION ALL SELECT 'system.setting', '관리자 설정', 'system', '/system/settings',   30
        UNION ALL SELECT 'system.code',    '공통코드',    'system', '/system/codes',      40
        UNION ALL SELECT 'system.audit',   '변경 이력',   'system', '/system/audit-logs', 50
        UNION ALL SELECT 'system.print',   '출력 양식',   'system', '/system/print-templates', 60) v
  JOIN menu p ON p.menu_key = v.parent;

-- 관리자 = 모든 메뉴 전체 권한 (메뉴 INSERT 뒤에 둔다)
INSERT INTO role_menu (role_id, menu_id, can_read, can_create, can_update, can_delete)
SELECT r.role_id, m.menu_id, 1, 1, 1, 1
  FROM role r CROSS JOIN menu m
 WHERE r.role_code = 'ADMIN';

-- =====================================================================
-- 10. 집계 VIEW (저장 수량 대신 계산)
-- =====================================================================

-- 투입 행별 양품 (매 공정: 양품 = 투입 - 해당 행 부적합)
CREATE OR REPLACE VIEW vw_production_work_input_qty AS
SELECT
    pwi.production_work_input_id,
    pwi.production_work_id,
    pwi.sales_order_item_id,
    pwi.main_work_id,
    pwi.input_qty,
    COALESCE(d.defect_qty, 0)                 AS defect_qty,
    pwi.input_qty - COALESCE(d.defect_qty, 0) AS good_qty
FROM production_work_input pwi
LEFT JOIN (SELECT production_work_input_id, SUM(defect_qty) AS defect_qty
             FROM defect_occurrence
            WHERE status <> 'CANCELLED' AND is_deleted = 0 AND production_work_input_id IS NOT NULL
            GROUP BY production_work_input_id) d
       ON d.production_work_input_id = pwi.production_work_input_id
WHERE pwi.status <> 'CANCELLED';

-- 작업 LOT별 합계 + 구 isdone / isinspectiondone 호환
CREATE OR REPLACE VIEW vw_production_work_summary AS
SELECT
    w.production_work_id,
    w.lot_no,
    w.production_schedule_id,
    w.unit_process_id,
    w.equipment_id,
    w.work_date,
    w.status,
    w.is_main_process,
    w.is_rework,
    COALESCE(q.input_qty, 0)   AS input_qty,
    COALESCE(q.defect_qty, 0)  AS defect_qty,
    COALESCE(q.good_qty, 0)    AS good_qty,
    COALESCE(q.order_count, 0) AS order_count,
    (w.status = 'COMPLETED')   AS is_done,
    EXISTS (SELECT 1 FROM line_inspection x
             WHERE x.production_work_id = w.production_work_id
               AND x.status = 'COMPLETED') AS is_inspection_done  -- 구 isinspectiondone = 라인검사 완료
FROM production_work w
LEFT JOIN (SELECT production_work_id,
                  SUM(input_qty) AS input_qty, SUM(defect_qty) AS defect_qty, SUM(good_qty) AS good_qty,
                  COUNT(DISTINCT sales_order_item_id) AS order_count
             FROM vw_production_work_input_qty
            GROUP BY production_work_id) q ON q.production_work_id = w.production_work_id
WHERE w.is_deleted = 0;

-- 수주품목 × 단위공정 진행 (정상작업 기준)
CREATE OR REPLACE VIEW vw_sales_order_item_process_progress AS
SELECT
    q.sales_order_item_id,
    w.unit_process_id,
    COUNT(DISTINCT w.production_work_id) AS lot_count,
    SUM(q.input_qty)  AS input_qty,
    SUM(q.defect_qty) AS defect_qty,
    SUM(q.good_qty)   AS good_qty
FROM vw_production_work_input_qty q
JOIN production_work w ON w.production_work_id = q.production_work_id
WHERE w.is_deleted = 0 AND w.is_rework = 0
GROUP BY q.sales_order_item_id, w.unit_process_id;

-- 수주품목 진행 (주공정 투입 기준 잔량 + 출하 잔량)
CREATE OR REPLACE VIEW vw_sales_order_item_progress AS
SELECT
    soi.sales_order_item_id,
    soi.sales_order_id,
    soi.part_id,
    soi.order_qty,
    COALESCE(m.input_qty, 0)    AS main_input_qty,
    COALESCE(d.defect_qty, 0)   AS defect_qty,
    COALESCE(s.shipment_qty, 0) AS shipment_qty,
    COALESCE(s.test_specimen_qty, 0) AS test_specimen_qty,
    soi.order_qty - COALESCE(m.input_qty, 0)    AS remaining_input_qty,
    soi.order_qty - COALESCE(s.shipment_qty, 0) - COALESCE(s.test_specimen_qty, 0) AS remaining_shipment_qty
FROM sales_order_item soi
LEFT JOIN (SELECT pwi.sales_order_item_id, SUM(pwi.input_qty) AS input_qty
             FROM production_work_input pwi
             JOIN production_work w ON w.production_work_id = pwi.production_work_id
            WHERE pwi.status <> 'CANCELLED' AND w.is_deleted = 0
              AND w.is_main_process = 1 AND w.is_rework = 0
            GROUP BY pwi.sales_order_item_id) m ON m.sales_order_item_id = soi.sales_order_item_id
LEFT JOIN (SELECT sales_order_item_id, SUM(defect_qty) AS defect_qty
             FROM defect_occurrence
            WHERE status <> 'CANCELLED' AND is_deleted = 0
            GROUP BY sales_order_item_id) d ON d.sales_order_item_id = soi.sales_order_item_id
LEFT JOIN (SELECT si.sales_order_item_id, SUM(si.shipment_qty) AS shipment_qty, SUM(si.test_specimen_qty) AS test_specimen_qty
             FROM shipment_item si
             JOIN shipment sh ON sh.shipment_id = si.shipment_id
            WHERE sh.status <> 'CANCELLED' AND sh.is_deleted = 0
            GROUP BY si.sales_order_item_id) s ON s.sales_order_item_id = soi.sales_order_item_id;

-- 작업 LOT 진행 상태 표시: 단위공정별 배정 → 투입 → 완료 (구 progressstep)
-- 검사/부적합/출하는 상태가 아닌 참고 정보 컬럼으로 제공
CREATE OR REPLACE VIEW vw_work_lot_status AS
SELECT
    w.production_work_id,
    w.lot_no,
    w.work_date,
    w.equipment_id,
    up.unit_process_name,
    w.is_main_process,
    w.is_rework,
    w.status,
    COALESCE(q.good_qty, 0)            AS good_qty,
    COALESCE(pp.post_lot_count, 0)     AS post_lot_count,
    COALESCE(pp.post_done_count, 0)    AS post_done_count,
    COALESCE(ins.inspection_count, 0)  AS inspection_count,
    COALESCE(ins.inspection_done, 0)   AS inspection_done_count,
    COALESCE(df.open_defect_count, 0)  AS open_defect_count,
    COALESCE(sh.shipment_qty, 0)       AS shipment_qty,
    COALESCE((SELECT cc.code_name FROM common_code cc
                JOIN common_code_group cg ON cg.common_code_group_id = cc.common_code_group_id
               WHERE cg.group_code = 'WORK_STATUS' AND cc.code = w.status), w.status) AS display_status
FROM production_work w
JOIN unit_process up ON up.unit_process_id = w.unit_process_id
LEFT JOIN (SELECT production_work_id, SUM(good_qty) AS good_qty
             FROM vw_production_work_input_qty GROUP BY production_work_id) q
       ON q.production_work_id = w.production_work_id
LEFT JOIN (SELECT pwi.main_work_id,
                  COUNT(DISTINCT pw.production_work_id) AS post_lot_count,
                  COUNT(DISTINCT CASE WHEN pw.status = 'COMPLETED' THEN pw.production_work_id END) AS post_done_count
             FROM production_work_input pwi
             JOIN production_work pw ON pw.production_work_id = pwi.production_work_id
            WHERE pwi.main_work_id <> pwi.production_work_id AND pw.is_deleted = 0 AND pw.status <> 'CANCELLED'
            GROUP BY pwi.main_work_id) pp ON pp.main_work_id = w.production_work_id
LEFT JOIN (SELECT i.production_work_id, COUNT(*) AS inspection_count,
                  SUM(i.status = 'COMPLETED') AS inspection_done
             FROM inspection_target t JOIN inspection i ON i.inspection_id = t.inspection_id
            WHERE i.is_deleted = 0 AND i.status <> 'CANCELLED'
            GROUP BY i.production_work_id) ins ON ins.production_work_id = w.production_work_id
LEFT JOIN (SELECT production_work_id, COUNT(*) AS open_defect_count
             FROM defect_occurrence
            WHERE is_deleted = 0 AND status IN ('OPEN','DECIDED','REWORKING')
            GROUP BY production_work_id) df ON df.production_work_id = w.production_work_id
LEFT JOIN (SELECT si.main_work_id, SUM(si.shipment_qty) AS shipment_qty
             FROM shipment_item si JOIN shipment s ON s.shipment_id = si.shipment_id
            WHERE s.is_deleted = 0 AND s.status <> 'CANCELLED'
            GROUP BY si.main_work_id) sh ON sh.main_work_id = w.production_work_id
WHERE w.is_deleted = 0;

-- 끝. (스케줄 재계산은 Stored Procedure가 아닌 API Scheduling Service가 단일 담당 — 설계안 V3 §5)
