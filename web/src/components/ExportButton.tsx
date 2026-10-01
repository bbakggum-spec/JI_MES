import { DownloadOutlined } from '@ant-design/icons'
import { App, Button, Dropdown } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { isValidElement, useState, type ReactNode } from 'react'
import { ApiError, apiFile, saveFile } from '../api/client'

/** React 노드 → 화면에 보이는 글자 (Tag·Typography 등 안쪽 글자를 이어 붙임) */
function textOf(node: ReactNode): string {
  if (node == null || typeof node === 'boolean') return ''
  if (typeof node === 'string' || typeof node === 'number') return String(node)
  if (Array.isArray(node)) return node.map(textOf).filter(Boolean).join(' ')
  if (isValidElement<{ children?: ReactNode }>(node)) return textOf(node.props.children)
  return ''
}

type Leaf<T> = { title?: ReactNode; dataIndex?: unknown; render?: (value: unknown, row: T, index: number) => ReactNode; key?: unknown }

const NUMERIC = /^-?[\d,]+(\.\d+)?$/

/**
 * 열 값 = 화면 표시(render) 글자. 값이 숫자이고 화면에도 숫자로 보이면 숫자 그대로 (엑셀 숫자 셀) —
 * 우선순위처럼 숫자 코드를 이름으로 보이는 열은 표시 글자로.
 */
function cellOf<T>(col: Leaf<T>, row: T, index: number): string | number | null {
  const path = Array.isArray(col.dataIndex) ? col.dataIndex : col.dataIndex != null ? [col.dataIndex] : []
  const raw = path.reduce<unknown>((v, k) => (v as Record<string, unknown> | null | undefined)?.[k as string], row)
  if (!col.render) return typeof raw === 'number' ? raw : raw == null ? null : String(raw)
  const text = textOf(col.render(raw, row, index)).trim()
  if (typeof raw === 'number' && NUMERIC.test(text)) return raw
  return text === '' ? null : text
}

/**
 * 목록 내보내기 — 엑셀·CSV·PDF (구 PrintDoc/ExportHelper, 설계 §29.3). 화면 표와 같은 열·표시 글자로 내보낸다.
 * 제목 없는 열(버튼 칸)은 뺀다. 서버 쪽 페이지 목록은 fetchRows 로 전체를 읽어서.
 */
export default function ExportButton<T>({ title, columns, rows, fetchRows, size }: {
  title: string
  columns: ColumnsType<T>
  rows?: T[]
  /** 화면은 한 쪽만 들고 있는 목록 — 조건에 맞는 전체 행 */
  fetchRows?: () => Promise<T[]>
  size?: 'small' | 'middle'
}) {
  const { message } = App.useApp()
  const [busy, setBusy] = useState(false)

  const run = async (format: 'XLSX' | 'CSV' | 'PDF') => {
    setBusy(true)
    try {
      const data = fetchRows ? await fetchRows() : rows ?? []
      const leaves = (columns as Leaf<T>[]).filter((c) => textOf(c.title).trim() !== '')
      const body = {
        title,
        format,
        columns: leaves.map((c) => ({ title: textOf(c.title).trim(), number: data.some((r, i) => typeof cellOf(c, r, i) === 'number') })),
        rows: data.map((r, i) => leaves.map((c) => cellOf(c, r, i))),
      }
      saveFile(await apiFile('/api/export', { method: 'POST', body }))
    } catch (e) {
      message.error(e instanceof ApiError && e.errors ? Object.values(e.errors).flat().join(' ') : e instanceof Error ? e.message : String(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dropdown trigger={['click']} menu={{
      items: [{ key: 'XLSX', label: '엑셀 (xlsx)' }, { key: 'CSV', label: 'CSV' }, { key: 'PDF', label: 'PDF' }],
      onClick: ({ key }) => void run(key as 'XLSX' | 'CSV' | 'PDF'),
    }}>
      <Button size={size} icon={<DownloadOutlined />} loading={busy}>내보내기</Button>
    </Dropdown>
  )
}
