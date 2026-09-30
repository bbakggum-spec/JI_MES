// GET /api/master/{key}/meta 응답 (api Features/Master/MasterModel.cs)

export type MasterFieldType = 'Text' | 'TextArea' | 'Integer' | 'Decimal' | 'Bool' | 'Date' | 'Time' | 'Code' | 'Lookup'

export interface MasterField {
  name: string
  label: string
  type: MasterFieldType
  required: boolean
  maxLength: number | null
  unique: boolean
  lookup: string | null
  codeGroup: string | null
  min: number | null
  max: number | null
  default: unknown
  searchable: boolean
  inList: boolean
  width: number | null
  help: string | null
}

export interface MasterImageField {
  name: string
  label: string
  fileNameColumn: string | null
}

export interface MasterMeta {
  key: string
  label: string
  menuKey: string
  description: string
  hasActive: boolean
  allowDelete: boolean
  requireOneOf: string[] | null
  fields: MasterField[]
  images: MasterImageField[]
}

export type MasterRow = Record<string, unknown> & { id: number }

export interface MasterOption {
  value: number
  label: string
  active: boolean
}

/** master.customer → customer (API 경로 키) */
export function entityOf(menuKey: string) {
  return menuKey.replace(/^master\./, '')
}
