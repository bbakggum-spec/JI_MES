import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Col, DatePicker, Descriptions, Divider, Form, Input, Row, Select, Space, Switch, Table, Tag, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import { ApiError, api } from '../../api/client'
import { useCan } from '../../auth/useAuth'
import EditorWindow from '../../components/EditorWindow'
import { useClientSettings } from '../../hooks/useClientSettings'
import { useCommonCodes } from '../../hooks/useCommonCodes'
import { useOptions } from '../../hooks/useOptions'
import { queryKeys } from '../../queryKeys'
import type { PageProps } from '../registry'

interface Defect {
  defectOccurrenceId: number
  defectDate: string
  salesOrderItemId: number
  orderItemNo: string
  customerName: string | null
  partName: string | null
  partNumber: string | null
  customerLot: string | null
  productionWorkId: number | null
  lotNo: string | null
  unitProcessName: string | null
  mainLotNo: string | null
  inspectionId: number | null
  inspectionNo: string | null
  defectQty: number
  defectReasonId: number | null
  defectReasonName: string | null
  remark: string | null
  status: 'OPEN' | 'DECIDED' | 'REWORKING' | 'COMPLETED' | 'CANCELLED'
  decision: string | null
  decisionRemark: string | null
  decidedEmployeeId: number | null
  decidedByName: string | null
  decidedAt: string | null
  completedByName: string | null
  completedAt: string | null
  reworkRemark: string | null
  reworkWorkId: number | null
  reworkLotNo: string | null
  reworkStatus: string | null
  rowVersion: number
}

const DATE = 'YYYY-MM-DD'
const qty = (n: number | null | undefined) => (n == null ? '' : n.toLocaleString(undefined, { maximumFractionDigits: 3 }))
const errorText = (e: unknown) => (e instanceof ApiError && e.errors ? Object.values(e.errors).flat().join(' ') : e instanceof Error ? e.message : String(e))

/** 부적합 — 구 F_Defect(목록) + F_DefectAdd(판정·완료) + 재처리 → 재작업 LOT (설계 §23.6) */
export default function DefectsPage({ menuKey }: PageProps) {
  const codes = useCommonCodes()
  const settings = useClientSettings()
  const queryClient = useQueryClient()
  const [openOnly, setOpenOnly] = useState(true)
  const [range, setRange] = useState<[Dayjs, Dayjs] | null>(null)
  const [status, setStatus] = useState<string | undefined>()
  const [decision, setDecision] = useState<string | undefined>()
  const [search, setSearch] = useState('')
  const [selected, setSelected] = useState<number | null>(null)
  const effectiveRange = range ?? (settings.orderListDays ? [dayjs().subtract(settings.orderListDays, 'day'), dayjs()] as [Dayjs, Dayjs] : null)
  const params = new URLSearchParams()
  if (openOnly) params.set('openOnly', 'true')
  else if (effectiveRange) { params.set('from', effectiveRange[0].format(DATE)); params.set('to', effectiveRange[1].format(DATE)) }
  if (status) params.set('status', status)
  if (decision) params.set('decision', decision)
  if (search) params.set('search', search)
  const list = useQuery({
    queryKey: [...queryKeys.defects, 'list', params.toString()],
    queryFn: ({ signal }) => api<Defect[]>(`/api/defects?${params}`, { signal }),
    enabled: openOnly || effectiveRange !== null,
  })
  const tag = (group: string, code: string | null) => code && <Tag color={codes.attr<{ color?: string }>(group, code)?.color}>{codes.name(group, code)}</Tag>

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 8 }} wrap>
        <Typography.Title level={4} style={{ margin: 0 }}>부적합</Typography.Title>
        <Space wrap>
          <Space size={4}><Switch size="small" checked={openOnly} onChange={setOpenOnly} />미처리만 (미결정·결정·재작업 중)</Space>
          {!openOnly && <DatePicker.RangePicker value={effectiveRange} allowClear={false} onChange={(v) => v?.[0] && v[1] && setRange([v[0], v[1]])} />}
          <Select allowClear placeholder="상태" style={{ width: 110 }} value={status} onChange={setStatus}
            options={codes.options('DEFECT_STATUS').map((c) => ({ value: c.code, label: c.codeName }))} />
          <Select allowClear placeholder="처리구분" style={{ width: 110 }} value={decision} onChange={setDecision}
            options={codes.options('DEFECT_ACTION').map((c) => ({ value: c.code, label: c.codeName }))} />
          <Input.Search allowClear placeholder="입고번호·LOT·품명·거래처·검사번호" style={{ width: 240 }} onSearch={(v) => setSearch(v.trim())} />
        </Space>
      </Space>
      <Table<Defect> rowKey="defectOccurrenceId" size="small" loading={list.isFetching} dataSource={list.data ?? []} scroll={{ x: 1300 }}
        pagination={{ defaultPageSize: 50, showSizeChanger: true, showTotal: (n) => `${n}건` }}
        onRow={(r) => ({ onClick: () => setSelected(r.defectOccurrenceId), style: { cursor: 'pointer', opacity: r.status === 'CANCELLED' ? 0.5 : 1 } })}
        columns={[
          { title: '발생일', dataIndex: 'defectDate', width: 95, render: (d: string) => dayjs(d).format(DATE) },
          { title: '입고번호', dataIndex: 'orderItemNo', width: 125 },
          { title: '거래처', dataIndex: 'customerName', width: 100, ellipsis: true },
          { title: '품명', render: (_: unknown, r) => <>{r.partName}{r.partNumber && <Typography.Text type="secondary"> {r.partNumber}</Typography.Text>}</> },
          { title: '발생 LOT', width: 170, render: (_: unknown, r) => <>{r.lotNo}{r.unitProcessName && <Typography.Text type="secondary"> {r.unitProcessName}</Typography.Text>}</> },
          { title: '주 LOT', dataIndex: 'mainLotNo', width: 125 },
          { title: '출처', width: 130, render: (_: unknown, r) => r.inspectionNo ? <>검사 {r.inspectionNo}</> : '작업' },
          { title: '수량', dataIndex: 'defectQty', width: 70, align: 'right', render: qty },
          { title: '사유', width: 110, ellipsis: true, render: (_: unknown, r) => r.defectReasonName ?? r.remark },
          { title: '상태', dataIndex: 'status', width: 90, render: (s: string) => tag('DEFECT_STATUS', s) },
          { title: '처리', dataIndex: 'decision', width: 80, render: (d: string | null) => d && codes.name('DEFECT_ACTION', d) },
          { title: '재작업 LOT', dataIndex: 'reworkLotNo', width: 125 },
        ]} />
      {selected !== null && <DefectWindow id={selected} menuKey={menuKey} onClose={() => setSelected(null)}
        onChanged={() => void queryClient.invalidateQueries({ queryKey: queryKeys.defects })} />}
    </>
  )
}

function DefectWindow({ id, menuKey, onClose, onChanged }: { id: number; menuKey: string; onClose: () => void; onChanged: () => void }) {
  const { message } = App.useApp()
  const canUpdate = useCan(menuKey, 'update')
  const canDelete = useCan(menuKey, 'delete')
  const codes = useCommonCodes()
  const employees = useOptions('/api/master/employee/options')
  const equipment = useOptions('/api/master/equipment/options')
  const units = useOptions('/api/master/unit_process/options')
  const detail = useQuery({ queryKey: [...queryKeys.defects, 'detail', id], queryFn: ({ signal }) => api<Defect>(`/api/defects/${id}`, { signal }) })
  const [decideForm] = Form.useForm<{ decision: string; employeeId: number; remark?: string }>()
  const [doneForm] = Form.useForm<{ employeeId: number; remark?: string }>()
  const [reworkForm] = Form.useForm<{ equipmentId: number; unitProcessId: number }>()
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const d = detail.data
  if (!d) return null

  const run = async (fn: () => Promise<unknown>, success: string) => {
    setBusy(true)
    setError(null)
    try {
      await fn()
      message.success(success)
      await detail.refetch()
      onChanged()
    } catch (e) {
      setError(errorText(e))
    } finally {
      setBusy(false)
    }
  }
  const decidable = canUpdate && (d.status === 'OPEN' || d.status === 'DECIDED')
  const reworkable = canUpdate && d.status === 'DECIDED' && d.decision === 'REWORK'
  const completable = canUpdate && (d.status === 'DECIDED' || d.status === 'REWORKING')

  return (
    <EditorWindow onClose={onClose} size={900}
      title={<Space>부적합 #{d.defectOccurrenceId}<Tag color={codes.attr<{ color?: string }>('DEFECT_STATUS', d.status)?.color}>{codes.name('DEFECT_STATUS', d.status)}</Tag></Space>}
      extra={canDelete && decidable && (
        <Button danger loading={busy} onClick={() => void run(() => api(`/api/defects/${id}/cancel`, { method: 'POST', body: { rowVersion: d.rowVersion } }), '취소했습니다.')}>부적합 취소</Button>
      )}>
      <Descriptions size="small" bordered column={{ xs: 1, md: 3 }} items={[
        { label: '발생일', children: dayjs(d.defectDate).format(DATE) },
        { label: '입고번호', children: d.orderItemNo },
        { label: '거래처', children: d.customerName },
        { label: '품명', span: 2, children: <>{d.partName} <Typography.Text type="secondary">{d.partNumber}</Typography.Text></> },
        { label: '고객LOT', children: d.customerLot },
        { label: '발생 LOT', children: `${d.lotNo ?? ''} ${d.unitProcessName ?? ''}` },
        { label: '주 LOT', children: d.mainLotNo },
        { label: '출처', children: d.inspectionNo ? `검사 ${d.inspectionNo}` : '작업' },
        { label: '수량', children: qty(d.defectQty) },
        { label: '사유', span: 2, children: [d.defectReasonName, d.remark].filter(Boolean).join(' · ') },
        { label: '처리구분', children: d.decision && codes.name('DEFECT_ACTION', d.decision) },
        { label: '판정', span: 2, children: d.decidedByName && `${d.decidedByName} · ${dayjs(d.decidedAt).format('MM-DD HH:mm')}${d.decisionRemark ? ` · ${d.decisionRemark}` : ''}` },
        { label: '재작업 LOT', children: d.reworkLotNo && `${d.reworkLotNo} (${codes.name('WORK_STATUS', d.reworkStatus ?? '')})` },
        { label: '완료', span: 2, children: d.completedAt && `${d.completedByName ?? '재작업 LOT 완료'} · ${dayjs(d.completedAt).format('MM-DD HH:mm')}${d.reworkRemark ? ` · ${d.reworkRemark}` : ''}` },
      ]} />
      {error && <Alert style={{ marginTop: 12 }} type="error" showIcon title={error} />}

      {decidable && (
        <>
          <Divider titlePlacement="start" plain>판정</Divider>
          <Form form={decideForm} layout="vertical" initialValues={{ decision: d.decision ?? undefined, employeeId: d.decidedEmployeeId ?? undefined, remark: d.decisionRemark ?? undefined }}
            onFinish={(v) => void run(() => api(`/api/defects/${id}/decide`, { method: 'PUT', body: { ...v, rowVersion: d.rowVersion } }), '판정했습니다.')}>
            <Row gutter={12}>
              <Col span={6}><Form.Item name="decision" label="처리구분" rules={[{ required: true }]}><Select options={codes.options('DEFECT_ACTION').map((c) => ({ value: c.code, label: c.codeName }))} /></Form.Item></Col>
              <Col span={6}><Form.Item name="employeeId" label="판정자" rules={[{ required: true }]}><Select showSearch optionFilterProp="label" options={employees.options} /></Form.Item></Col>
              <Col span={9}><Form.Item name="remark" label="메모"><Input /></Form.Item></Col>
              <Col span={3}><Form.Item label=" "><Button htmlType="submit" type="primary" loading={busy}>판정</Button></Form.Item></Col>
            </Row>
          </Form>
        </>
      )}
      {reworkable && (
        <>
          <Divider titlePlacement="start" plain>재작업 LOT 만들기</Divider>
          <Form form={reworkForm} layout="vertical"
            onFinish={(v) => void run(async () => {
              const r = await api<{ lotNo: string }>(`/api/defects/${id}/rework`, { method: 'POST', body: { ...v, rowVersion: d.rowVersion } })
              message.info(`재작업 LOT ${r.lotNo} — 작업(투입) 화면에서 시작·완료하면 이 부적합도 완료됩니다.`)
            }, '재작업 LOT 을 만들었습니다.')}>
            <Row gutter={12}>
              <Col span={8}><Form.Item name="equipmentId" label="설비" rules={[{ required: true }]}><Select showSearch optionFilterProp="label" options={equipment.options} /></Form.Item></Col>
              <Col span={8}><Form.Item name="unitProcessId" label="단위공정" rules={[{ required: true }]}><Select showSearch optionFilterProp="label" options={units.options} /></Form.Item></Col>
              <Col span={8}><Form.Item label=" "><Button htmlType="submit" loading={busy}>재작업 LOT 만들기 ({qty(d.defectQty)})</Button></Form.Item></Col>
            </Row>
          </Form>
        </>
      )}
      {completable && (
        <>
          <Divider titlePlacement="start" plain>완료</Divider>
          <Form form={doneForm} layout="vertical" onFinish={(v) => void run(() => api(`/api/defects/${id}/complete`, { method: 'POST', body: { ...v, rowVersion: d.rowVersion } }), '완료했습니다.')}>
            <Row gutter={12}>
              <Col span={8}><Form.Item name="employeeId" label="완료자" rules={[{ required: true }]}><Select showSearch optionFilterProp="label" options={employees.options} /></Form.Item></Col>
              <Col span={12}><Form.Item name="remark" label="처리 내용"><Input /></Form.Item></Col>
              <Col span={4}><Form.Item label=" "><Button htmlType="submit" loading={busy}>완료</Button></Form.Item></Col>
            </Row>
          </Form>
        </>
      )}
    </EditorWindow>
  )
}
