import { PrinterOutlined } from '@ant-design/icons'
import { useQuery } from '@tanstack/react-query'
import { App, Button, Select, Space, Tooltip } from 'antd'
import { useState, type ReactNode } from 'react'
import { ApiError, api, apiFile, saveFile } from '../api/client'
import { queryKeys } from '../queryKeys'

interface Choice { printTemplateId: number; printTemplateName: string; templateKind: string; isDefault: boolean }

/**
 * 업무 화면 출력 버튼 (설계 §29) — 용도의 양식이 둘 이상이면 양식 선택 콤보를 함께 보인다 (기본 양식이 먼저 선택).
 * 여러 건을 고르면 한 파일로 나온다. PDF 는 새 탭으로 열어 바로 인쇄, 엑셀은 내려받기.
 * 권한 = 이 화면(데이터 공급원) 읽기 권한 — 서버가 확인.
 */
export default function PrintButton({ purposeCode, sourceIds, children, size, disabledReason }: {
  purposeCode: string
  sourceIds: number[]
  children: ReactNode
  size?: 'small' | 'middle'
  /** 누를 수 없을 때 이유 (예: 고른 행 없음) */
  disabledReason?: string
}) {
  const { message } = App.useApp()
  const [templateId, setTemplateId] = useState<number>()
  const [busy, setBusy] = useState(false)
  const choices = useQuery({
    queryKey: [...queryKeys.print, 'choices', purposeCode],
    queryFn: ({ signal }) => api<Choice[]>(`/api/print/choices?purposeCode=${purposeCode}`, { signal }),
    staleTime: 60_000,
  })
  const list = choices.data ?? []
  const selected = templateId ?? list.find((c) => c.isDefault)?.printTemplateId ?? list[0]?.printTemplateId
  const reason = disabledReason ?? (sourceIds.length === 0 ? '출력할 행을 고르세요.' : choices.isSuccess && list.length === 0 ? '등록된 양식이 없습니다 (시스템 > 출력 양식).' : undefined)

  const print = async () => {
    setBusy(true)
    try {
      const file = await apiFile('/api/print/documents', { method: 'POST', body: { purposeCode, sourceIds, printTemplateId: selected ?? null } })
      if (file.blob.type === 'application/pdf') {
        const url = URL.createObjectURL(file.blob)
        // 팝업이 막히면 내려받기
        if (!window.open(url, '_blank')) saveFile(file)
        setTimeout(() => URL.revokeObjectURL(url), 60_000)
      } else {
        saveFile(file)
      }
    } catch (e) {
      message.error(e instanceof ApiError && e.errors ? Object.values(e.errors).flat().join(' ') : e instanceof Error ? e.message : String(e))
    } finally {
      setBusy(false)
    }
  }

  const button = (
    <Button size={size} icon={<PrinterOutlined />} loading={busy} disabled={!!reason} onClick={() => void print()}>
      {children}{sourceIds.length > 1 && ` (${sourceIds.length})`}
    </Button>
  )
  return (
    <Tooltip title={reason}>
      {list.length > 1 ? (
        <Space.Compact size={size}>
          <Select aria-label="출력 양식" style={{ width: 170 }} value={selected} onChange={setTemplateId} popupMatchSelectWidth={false}
            options={list.map((c) => ({ value: c.printTemplateId, label: c.isDefault ? `${c.printTemplateName} (기본)` : c.printTemplateName }))} />
          {button}
        </Space.Compact>
      ) : button}
    </Tooltip>
  )
}
