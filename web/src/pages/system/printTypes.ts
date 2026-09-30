// /api/print/* 응답 (api Features/Printing/PrintEndpoints.cs)

export interface PrintField {
  fieldKey: string
  fieldAlias: string | null
  fieldType: 'TEXT' | 'NUMBER' | 'DATE' | 'IMAGE' | 'LIST'
  fieldGroup: string | null
  formatPattern: string | null
  description: string | null
  sampleValue: string | null
}

export interface PrintPurpose {
  printPurposeId: number
  purposeCode: string
  purposeName: string
  dataSourceCode: string
  dataSourceName: string
  isSystem: boolean
  dataSourceImplemented: boolean
  fields: PrintField[]
}

export interface PrintTemplate {
  printTemplateId: number
  printTemplateName: string
  purposeCode: string
  templateKind: 'EXCEL' | 'FIXED'
  rendererKey: string | null
  outputFormat: 'PDF' | 'XLSX'
  isDefault: boolean
  isActive: boolean
  currentVersionId: number | null
  currentVersionNo: number | null
  uploadedAt: string | null
  fileName: string | null
  unknownPlaceholdersJson: string | null
  partLinkCount: number
}

export interface PrintTemplateVersion {
  printTemplateVersionId: number
  versionNo: number
  fileName: string | null
  fileSize: number | null
  isCurrent: boolean
  changeNote: string | null
  uploadedAt: string
  uploadedByName: string | null
  placeholdersJson: string | null
  unknownPlaceholdersJson: string | null
  layoutOptionsJson: string | null
}

export interface UploadResult {
  printTemplateVersionId: number
  versionNo: number
  placeholders: string[]
  unknown: string[]
}

export function parseList(json: string | null): string[] {
  if (!json) return []
  try {
    const v: unknown = JSON.parse(json)
    return Array.isArray(v) ? v.map(String) : []
  } catch {
    return []
  }
}
