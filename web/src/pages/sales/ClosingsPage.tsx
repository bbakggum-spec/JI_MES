import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Checkbox, DatePicker, Input, Popconfirm, Space, Table, Tag, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import { ApiError, api } from '../../api/client'
import { useCan } from '../../auth/useAuth'
import EditorWindow from '../../components/EditorWindow'
import { useCommonCodes } from '../../hooks/useCommonCodes'
import { queryKeys } from '../../queryKeys'
import type { PageProps } from '../registry'
import type { Closing, ClosingCustomer, Shipment } from './shipmentTypes'

const DATE = 'YYYY-MM-DD'
const won = (n: number | null | undefined) => (n == null ? '' : n.toLocaleString())
const errorText = (e: unknown) => (e instanceof ApiError && e.errors ? Object.values(e.errors).flat().join(' ') : e instanceof Error ? e.message : String(e))

/** 마감 — 구 F_MonthlyClosing. 업체별·전표 단위 마감 / 이월 / 마감 취소 (설계 §6·§23.7) */
export default function ClosingsPage({ menuKey }: PageProps) {
  const queryClient = useQueryClient()
  const [month, setMonth] = useState<Dayjs>(dayjs().startOf('month'))
  const [customer, setCustomer] = useState<ClosingCustomer | null>(null)
  const year = month.year()
  const m = month.month() + 1
  const summary = useQuery({
    queryKey: [...queryKeys.closings, 'customers', year, m],
    queryFn: ({ signal }) => api<ClosingCustomer[]>(`/api/closings/customers?year=${year}&month=${m}`, { signal }),
  })
  const rows = summary.data ?? []

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 8 }} wrap>
        <Typography.Title level={4} style={{ margin: 0 }}>마감</Typography.Title>
        <Space>
          <DatePicker picker="month" value={month} allowClear={false} onChange={(v) => v && setMonth(v.startOf('month'))} />
        </Space>
      </Space>
      <Table<ClosingCustomer> rowKey="customerId" size="small" loading={summary.isFetching} dataSource={rows} pagination={false}
        onRow={(r) => ({ onClick: () => setCustomer(r), style: { cursor: 'pointer' } })}
        summary={() => rows.length > 0 && (
          <Table.Summary.Row>
            <Table.Summary.Cell index={0} colSpan={2}>합계</Table.Summary.Cell>
            <Table.Summary.Cell index={2} align="right">{rows.reduce((s, r) => s + r.openCount, 0)}</Table.Summary.Cell>
            <Table.Summary.Cell index={3} align="right">{won(rows.reduce((s, r) => s + r.openAmount, 0))}</Table.Summary.Cell>
            <Table.Summary.Cell index={4} align="right">{rows.reduce((s, r) => s + r.closedCount, 0)}</Table.Summary.Cell>
            <Table.Summary.Cell index={5} align="right">{won(rows.reduce((s, r) => s + r.closedAmount, 0))}</Table.Summary.Cell>
          </Table.Summary.Row>
        )}
        locale={{ emptyText: '이 달까지 출하한 전표가 없습니다' }}
        columns={[
          { title: '거래처', dataIndex: 'customerName' },
          { title: '마감 기준일', dataIndex: 'closingDate', width: 130, render: (d: string, r) => <>{dayjs(d).format(DATE)}<Typography.Text type="secondary"> ({r.closingDay ?? '말'}일)</Typography.Text></> },
          { title: '미마감 건', dataIndex: 'openCount', width: 90, align: 'right', render: (n: number) => (n > 0 ? <Typography.Text type="warning" strong>{n}</Typography.Text> : 0) },
          { title: '미마감 금액', dataIndex: 'openAmount', width: 130, align: 'right', render: won },
          { title: '마감 건', dataIndex: 'closedCount', width: 90, align: 'right' },
          { title: '마감 금액', dataIndex: 'closedAmount', width: 130, align: 'right', render: won },
        ]} />
      {customer && <ClosingWindow customer={customer} year={year} month={m} menuKey={menuKey} onClose={() => setCustomer(null)}
        onChanged={() => void queryClient.invalidateQueries({ queryKey: queryKeys.closings })} />}
    </>
  )
}

function ClosingWindow({ customer, year, month, menuKey, onClose, onChanged }: {
  customer: ClosingCustomer; year: number; month: number; menuKey: string; onClose: () => void; onChanged: () => void
}) {
  const { message } = App.useApp()
  const canCreate = useCan(menuKey, 'create')
  const canUpdate = useCan(menuKey, 'update')
  const canDelete = useCan(menuKey, 'delete')
  const codes = useCommonCodes()
  const data = useQuery({
    queryKey: [...queryKeys.closings, 'candidates', customer.customerId, year, month],
    queryFn: ({ signal }) => api<{ closingDate: string; shipments: Shipment[]; closings: Closing[] }>(
      `/api/closings/candidates?customerId=${customer.customerId}&year=${year}&month=${month}`, { signal }),
  })
  const [selected, setSelected] = useState<number[] | null>(null)
  const [closingDate, setClosingDate] = useState<Dayjs | null>(null)
  const [carryOver, setCarryOver] = useState(true)
  const [remark, setRemark] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const d = data.data
  const cutDate = closingDate ?? (d ? dayjs(d.closingDate) : null)
  // 기본 선택 = 마감 기준일까지 출하한 전표 (이월된 전표 포함)
  const effectiveSelected = selected ?? (d && cutDate ? d.shipments.filter((s) => !dayjs(s.shipmentDate).isAfter(cutDate, 'day')).map((s) => s.shipmentId) : [])
  const total = (d?.shipments ?? []).filter((s) => effectiveSelected.includes(s.shipmentId)).reduce((sum, s) => sum + (s.totalAmount ?? 0), 0)
  const next = dayjs(new Date(year, month - 1, 1)).add(1, 'month')

  const run = async (fn: () => Promise<string>) => {
    setBusy(true)
    setError(null)
    try {
      message.success(await fn())
      setSelected(null)
      await data.refetch()
      onChanged()
    } catch (e) {
      setError(errorText(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <EditorWindow onClose={onClose} size={1100} title={`${customer.customerName} — ${year}년 ${month}월 마감`}>
      {d && (
        <>
          <Space wrap style={{ marginBottom: 8 }}>
            마감 기준일 <DatePicker value={cutDate} allowClear={false} onChange={(v) => { setClosingDate(v); setSelected(null) }} />
            <Checkbox checked={carryOver} onChange={(e) => setCarryOver(e.target.checked)}>선택하지 않은 전표는 {next.format('YYYY-MM')} 으로 이월</Checkbox>
            <Input placeholder="비고" style={{ width: 200 }} value={remark} onChange={(e) => setRemark(e.target.value)} />
          </Space>
          <Table<Shipment> size="small" rowKey="shipmentId" pagination={false} dataSource={d.shipments} scroll={{ y: 360 }}
            rowSelection={{ selectedRowKeys: effectiveSelected, onChange: (keys) => setSelected(keys as number[]) }}
            locale={{ emptyText: '마감할 전표가 없습니다' }}
            columns={[
              { title: '출하일', dataIndex: 'shipmentDate', width: 100, render: (v: string) => dayjs(v).format(DATE) },
              { title: '전표번호', dataIndex: 'shipmentNo', width: 125 },
              { title: '품목', ellipsis: true, render: (_: unknown, s) => <>{s.itemSummary}<Typography.Text type="secondary"> ({s.itemCount})</Typography.Text></> },
              {
                title: '상태', width: 140, render: (_: unknown, s) => <Space size={2}><Tag color={codes.attr<{ color?: string }>('CLOSING_STATUS', s.closingStatus)?.color}>{codes.name('CLOSING_STATUS', s.closingStatus)}</Tag>
                  {s.closingYear && <Typography.Text type="secondary" style={{ fontSize: 12 }}>{s.closingYear}-{String(s.closingMonth).padStart(2, '0')}</Typography.Text>}</Space>,
              },
              { title: '합계 금액', dataIndex: 'totalAmount', width: 120, align: 'right', render: won },
            ]} />
          <Space style={{ width: '100%', justifyContent: 'space-between', marginTop: 8 }} wrap>
            <Typography.Text>선택 {effectiveSelected.length}건 · <b>{won(total)}</b>원</Typography.Text>
            <Space>
              {canUpdate && (
                <Button disabled={effectiveSelected.length === 0} loading={busy} onClick={() => void run(async () => {
                  const r = await api<{ carried: number }>('/api/closings/carry-over', { method: 'POST', body: { shipmentIds: effectiveSelected, year: next.year(), month: next.month() + 1 } })
                  return `${r.carried}건을 ${next.format('YYYY-MM')} 으로 이월했습니다.`
                })}>선택 전표 이월</Button>
              )}
              {canCreate && (
                <Popconfirm title={`${effectiveSelected.length}건 · ${won(total)}원 마감`} description="마감된 전표는 마감을 취소하기 전까지 수정·취소할 수 없습니다."
                  onConfirm={() => void run(async () => {
                    const r = await api<{ closingNo: string; carried: number }>('/api/closings', {
                      method: 'POST',
                      body: { customerId: customer.customerId, year, month, closingDate: cutDate?.format(DATE), shipmentIds: effectiveSelected, carryOverOthers: carryOver, remark },
                    })
                    return `마감 ${r.closingNo}${r.carried ? ` · ${r.carried}건 이월` : ''}`
                  })}>
                  <Button type="primary" disabled={effectiveSelected.length === 0} loading={busy}>마감</Button>
                </Popconfirm>
              )}
            </Space>
          </Space>
          {error && <Alert style={{ marginTop: 8 }} type="error" showIcon title={error} />}

          <Typography.Title level={5} style={{ marginTop: 20 }}>이 달 마감 기록</Typography.Title>
          <Table<Closing> size="small" rowKey="shipmentClosingId" pagination={false} dataSource={d.closings} locale={{ emptyText: '없음' }}
            columns={[
              { title: '마감번호', dataIndex: 'closingNo', width: 130 },
              { title: '기준일', dataIndex: 'closingDate', width: 100, render: (v: string) => dayjs(v).format(DATE) },
              { title: '전표', dataIndex: 'shipmentCount', width: 60, align: 'right' },
              { title: '금액', dataIndex: 'totalAmount', width: 120, align: 'right', render: won },
              { title: '상태', dataIndex: 'closingStatus', width: 90, render: (st: string) => <Tag color={codes.attr<{ color?: string }>('CLOSING_RUN_STATUS', st)?.color}>{codes.name('CLOSING_RUN_STATUS', st)}</Tag> },
              { title: '마감', render: (_: unknown, c) => c.closedAt && `${c.closedByName ?? ''} · ${dayjs(c.closedAt).format('MM-DD HH:mm')}` },
              {
                title: '', width: 100, render: (_: unknown, c) => canDelete && c.closingStatus === 'CLOSED' && (
                  <Popconfirm title="마감을 취소합니다" description="이 마감의 전표가 미마감으로 돌아갑니다." onConfirm={() => void run(async () => {
                    await api(`/api/closings/${c.shipmentClosingId}/reopen`, { method: 'POST', body: { rowVersion: c.rowVersion } })
                    return `${c.closingNo} 마감을 취소했습니다.`
                  })}>
                    <Button size="small" danger>마감 취소</Button>
                  </Popconfirm>
                ),
              },
            ]} />
        </>
      )}
    </EditorWindow>
  )
}
