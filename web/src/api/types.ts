// API 응답 계약 (api/src/JiMes.Api/Features/* 의 DTO 와 같은 모양)

export interface MenuPermission {
  read: boolean
  create: boolean
  update: boolean
  delete: boolean
}

export interface MenuNode {
  menuKey: string
  menuName: string
  route: string | null
  children: MenuNode[]
}

export interface Me {
  appUserId: number
  loginId: string
  userName: string
  roles: string[]
  menus: MenuNode[]
  permissions: Record<string, MenuPermission>
}

export type SettingValueType = 'STRING' | 'INT' | 'DECIMAL' | 'BOOL' | 'TIME' | 'PATH' | 'JSON'

export interface Setting {
  settingKey: string
  category: string
  settingName: string
  valueType: SettingValueType
  settingValue: string | null
  defaultValue: string
  effectiveValue: string
  isFallback: boolean
  minValue: number | null
  maxValue: number | null
  unitLabel: string | null
  description: string | null
  isEditable: boolean
  requiresRestart: boolean
  sortOrder: number
  updatedAt: string
  updatedBy: number | null
}

export interface CommonCode {
  commonCodeId: number
  code: string
  codeName: string
  sortOrder: number
  attrJson: string | null
  isSystem: boolean
  isActive: boolean
  remark: string | null
}

export interface CommonCodeGroup {
  groupCode: string
  groupName: string
  description: string | null
  isActive: boolean
  codes: CommonCode[]
}

export interface AuditLog {
  auditLogId: number
  occurredAt: string
  appUserId: number | null
  userName: string | null
  actionType: string
  tableName: string
  recordId: number
  beforeJson: string | null
  afterJson: string | null
  reason: string | null
  clientIp: string | null
}

export interface Page<T> {
  items: T[]
  total: number
  page: number
  pageSize: number
}

/** GET /api/client-settings — 키는 서버 SettingKeys.ClientVisible */
export interface ClientSettings {
  'schedule.day_start_time': string
  'schedule.refresh_interval_sec': string
  'schedule.board_days': string
}
