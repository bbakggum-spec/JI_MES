import { DeleteOutlined, PlusOutlined } from '@ant-design/icons'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Card, Col, DatePicker, Form, Input, InputNumber, Popconfirm, Radio, Row, Select, Space, Table, Tabs, Tag, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import { ApiError, api } from '../../api/client'
import { useCan } from '../../auth/useAuth'
import AttachmentList, { type Attachment } from '../../components/AttachmentList'
import EditorWindow from '../../components/EditorWindow'
import { useCommonCodes } from '../../hooks/useCommonCodes'
import { useOptions } from '../../hooks/useOptions'
import { queryKeys } from '../../queryKeys'
import type { PageProps } from '../registry'

interface Maintenance {
  maintenanceId: number
  equipmentId: number
  equipmentName: string
  maintenanceType: string
  maintenanceDate: string
  startedAt: string | null
  completedAt: string | null
  workerEmployeeId: number | null
  workerName: string | null
  description: string | null
  result: string | null
  repairPart: string | null
  vendorName: string | null
  cost: number | null
  nextDueDate: string | null
  status: string
  attachmentCount: number
  rowVersion: number
}

interface Due {
  equipmentId: number
  equipmentName: string
  lastMaintenanceDate: string | null
  nextDueDate: string | null
  dueState: 'OVERDUE' | 'DUE_SOON' | null
}

const DATE = 'YYYY-MM-DD'
const DATETIME = 'YYYY-MM-DD HH:mm'
const SEND = 'YYYY-MM-DDTHH:mm:00'
const TYPE = 'MAINTENANCE_TYPE'
const STATUS = 'MAINTENANCE_STATUS'
const won = (n: number | null | undefined) => (n == null ? '' : n.toLocaleString())
const errorText = (e: unknown) => (e instanceof ApiError && e.errors ? Object.values(e.errors).flat().join(' ') : e instanceof Error ? e.message : String(e))

/** 설비 보전 — 구 F_MaintenanceForm (설계 §28.3). 측정기구 교정은 품질 > 측정기구 교정 (별도, 결정 B) */
export default function MaintenancePage({ menuKey }: PageProps) {
  const codes = useCommonCodes()
  const queryClient = useQueryClient()
  const canCreate = useCan(menuKey, 'create')
  const equipment = useOptions('/api/master/equipment/options')
  const [range, setRange] = useState<[Dayjs, Dayjs]>([dayjs().subtract(2, 'month').startOf('month'), dayjs()])
  const [equipmentId, setEquipmentId] = useState<number>()
  const [status, setStatus] = useState<string>()
  const [search, setSearch] = useState('')
  const [editing, setEditing] = useState<number | 'new' | null>(null)
  const params = new URLSearchParams({ from: range[0].format(DATE), to: range[1].format(DATE) })
  if (equipmentId) params.set('equipmentId', String(equipmentId))
  if (status) params.set('status', status)
  if (search) params.set('search', search)
  const list = useQuery({
    queryKey: [...queryKeys.equipment, 'maintenances', params.toString()],
    queryFn: ({ signal }) => api<Maintenance[]>(`/api/maintenances?${params}`, { signal }),
  })
  const due = useQuery({
    queryKey: [...queryKeys.equipment, 'maintenances', 'due'],
    queryFn: ({ signal }) => api<Due[]>('/api/maintenances/due', { signal }),
  })
  const refresh = () => void queryClient.invalidateQueries({ queryKey: [...queryKeys.equipment, 'maintenances'] })
  const typeName = (t: string) => codes.name(TYPE, t)   // 이관한 구 문자열은 그대로 보임
  const rows = list.data ?? []
  const totalCost = rows.filter((r) => r.status !== 'CANCELLED').reduce((s, r) => s + (r.cost ?? 0), 0)
  const alerts = (due.data ?? []).filter((d) => d.dueState)

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 8 }} wrap>
        <Typography.Title level={4} style={{ margin: 0 }}>설비 보전</Typography.Title>
        <Space wrap>
          <DatePicker.RangePicker value={range} allowClear={false} onChange={(v) => v?.[0] && v[1] && setRange([v[0], v[1]])} />
          <Select allowClear showSearch optionFilterProp="label" placeholder="설비 전체" style={{ width: 160 }} value={equipmentId}
            onChange={setEquipmentId} options={equipment.options} />
          <Select allowClear placeholder="상태" style={{ width: 100 }} value={status} onChange={setStatus}
            options={codes.options(STATUS).map((c) => ({ value: c.code, label: c.codeName }))} />
          <Input.Search allowClear placeholder="내용·부위·업체" style={{ width: 180 }} onSearch={(v) => setSearch(v.trim())} />
          {canCreate && <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditing('new')}>등록</Button>}
        </Space>
      </Space>

      {alerts.length > 0 && (
        <Card size="small" title="점검 예정 (지남·임박)" style={{ marginBottom: 12 }}>
          <Space wrap>
            {alerts.map((d) => (
              <Tag key={d.equipmentId} color={d.dueState === 'OVERDUE' ? 'red' : 'orange'} style={{ cursor: 'pointer' }} onClick={() => setEquipmentId(d.equipmentId)}>
                {d.equipmentName} · {dayjs(d.nextDueDate).format(DATE)}
              </Tag>
            ))}
          </Space>
        </Card>
      )}

      <Table<Maintenance> rowKey="maintenanceId" size="small" loading={list.isFetching} dataSource={rows} scroll={{ x: 1200 }}
        pagination={{ defaultPageSize: 50, showSizeChanger: true, showTotal: (n) => `${n}건 · 비용 ${won(totalCost)}원` }}
        onRow={(r) => ({ onClick: () => setEditing(r.maintenanceId), style: { cursor: 'pointer', opacity: r.status === 'CANCELLED' ? 0.5 : 1 } })}
        columns={[
          { title: '보전일', dataIndex: 'maintenanceDate', width: 95, render: (d: string) => dayjs(d).format(DATE) },
          { title: '설비', dataIndex: 'equipmentName', width: 130 },
          { title: '구분', dataIndex: 'maintenanceType', width: 100, render: typeName },
          { title: '상태', dataIndex: 'status', width: 70, render: (s: string) => <Tag color={codes.attr<{ color?: string }>(STATUS, s)?.color}>{codes.name(STATUS, s)}</Tag> },
          { title: '내용', dataIndex: 'description', ellipsis: true },
          { title: '부위', dataIndex: 'repairPart', width: 110, ellipsis: true },
          { title: '업체', dataIndex: 'vendorName', width: 110, ellipsis: true },
          { title: '작업자', dataIndex: 'workerName', width: 80 },
          { title: '비용', dataIndex: 'cost', width: 100, align: 'right', render: won },
          { title: '다음 점검', dataIndex: 'nextDueDate', width: 100, render: (d: string | null) => d && dayjs(d).format(DATE) },
          { title: '첨부', dataIndex: 'attachmentCount', width: 55, align: 'right', render: (n: number) => (n > 0 ? n : null) },
        ]} />

      {editing !== null && (
        <MaintenanceWindow id={editing === 'new' ? null : editing} menuKey={menuKey} defaultEquipmentId={equipmentId}
          onClose={() => setEditing(null)} onSaved={(id) => { refresh(); setEditing(id) }} onDeleted={() => { setEditing(null); refresh() }} />
      )}
    </>
  )
}

interface FormValues {
  equipmentId?: number
  maintenanceType?: string
  maintenanceDate?: Dayjs
  status: string
  startedAt?: Dayjs | null
  completedAt?: Dayjs | null
  workerEmployeeId?: number | null
  description?: string | null
  result?: string | null
  repairPart?: string | null
  vendorName?: string | null
  cost?: number | null
  nextDueDate?: Dayjs | null
}

function MaintenanceWindow({ id, menuKey, defaultEquipmentId, onClose, onSaved, onDeleted }: {
  id: number | null; menuKey: string; defaultEquipmentId?: number; onClose: () => void; onSaved: (id: number) => void; onDeleted: () => void
}) {
  const codes = useCommonCodes()
  const canUpdate = useCan(menuKey, 'update')
  const canDelete = useCan(menuKey, 'delete')
  const equipment = useOptions('/api/master/equipment/options')
  const employees = useOptions('/api/master/employee/options')
  const detail = useQuery({
    queryKey: [...queryKeys.equipment, 'maintenances', 'detail', id],
    queryFn: ({ signal }) => api<{ maintenance: Maintenance; attachments: Attachment[] }>(`/api/maintenances/${id}`, { signal }),
    enabled: id !== null,
  })
  const [form] = Form.useForm<FormValues>()
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  if (id !== null && !detail.data) return null
  const m = detail.data?.maintenance
  const editable = m ? canUpdate : true
  // 이관한 구 문자열 구분도 선택지에 보이게
  const typeOptions = codes.options(TYPE).map((c) => ({ value: c.code, label: c.codeName }))
  if (m && !typeOptions.some((o) => o.value === m.maintenanceType)) typeOptions.push({ value: m.maintenanceType, label: m.maintenanceType })

  const save = async (v: FormValues) => {
    setBusy(true)
    setError(null)
    try {
      const body = {
        rowVersion: m?.rowVersion, equipmentId: v.equipmentId, maintenanceType: v.maintenanceType, maintenanceDate: v.maintenanceDate?.format(DATE),
        status: v.status, startedAt: v.startedAt?.format(SEND) ?? null, completedAt: v.completedAt?.format(SEND) ?? null,
        workerEmployeeId: v.workerEmployeeId ?? null, description: v.description ?? null, result: v.result ?? null, repairPart: v.repairPart ?? null,
        vendorName: v.vendorName ?? null, cost: v.cost ?? null, nextDueDate: v.nextDueDate?.format(DATE) ?? null,
      }
      if (m) {
        await api(`/api/maintenances/${id}`, { method: 'PUT', body })
        await detail.refetch()
        onSaved(id!)
      } else {
        const r = await api<{ id: number }>('/api/maintenances', { method: 'POST', body })
        onSaved(r.id)
      }
    } catch (e) {
      setError(errorText(e))
    } finally {
      setBusy(false)
    }
  }
  const remove = async () => {
    try {
      await api(`/api/maintenances/${id}?rowVersion=${m!.rowVersion}`, { method: 'DELETE' })
      onDeleted()
    } catch (e) { setError(errorText(e)) }
  }

  const fields = (
    <Form<FormValues> form={form} layout="vertical" disabled={!editable} onFinish={(v) => void save(v)} key={m?.rowVersion ?? 'new'}
      initialValues={m ? {
        equipmentId: m.equipmentId, maintenanceType: m.maintenanceType, maintenanceDate: dayjs(m.maintenanceDate), status: m.status,
        startedAt: m.startedAt ? dayjs(m.startedAt) : null, completedAt: m.completedAt ? dayjs(m.completedAt) : null, workerEmployeeId: m.workerEmployeeId,
        description: m.description, result: m.result, repairPart: m.repairPart, vendorName: m.vendorName, cost: m.cost,
        nextDueDate: m.nextDueDate ? dayjs(m.nextDueDate) : null,
      } : { equipmentId: defaultEquipmentId, maintenanceType: typeOptions[0]?.value, maintenanceDate: dayjs(), status: 'OPEN' }}>
      {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 12 }} />}
      <Row gutter={12}>
        <Col span={10}>
          <Form.Item name="equipmentId" label="설비" rules={[{ required: true, message: '설비를 고르세요.' }]}>
            <Select showSearch optionFilterProp="label" options={equipment.options} />
          </Form.Item>
        </Col>
        <Col span={7}>
          <Form.Item name="maintenanceType" label="구분" rules={[{ required: true, message: '구분을 고르세요.' }]}>
            <Select options={typeOptions} />
          </Form.Item>
        </Col>
        <Col span={7}>
          <Form.Item name="maintenanceDate" label="보전일" rules={[{ required: true, message: '보전일을 입력하세요.' }]}>
            <DatePicker style={{ width: '100%' }} />
          </Form.Item>
        </Col>
        <Col span={24}>
          <Form.Item name="status" label="상태">
            <Radio.Group optionType="button" options={codes.options(STATUS).map((c) => ({ value: c.code, label: c.codeName }))} />
          </Form.Item>
        </Col>
        <Col span={8}>
          <Form.Item name="startedAt" label="시작">
            <DatePicker showTime={{ format: 'HH:mm' }} format={DATETIME} style={{ width: '100%' }} />
          </Form.Item>
        </Col>
        <Col span={8}>
          <Form.Item name="completedAt" label="완료" extra="완료로 저장할 때 비어 있으면 오늘은 지금, 지난 날짜는 보전일">
            <DatePicker showTime={{ format: 'HH:mm' }} format={DATETIME} style={{ width: '100%' }} />
          </Form.Item>
        </Col>
        <Col span={8}>
          <Form.Item name="workerEmployeeId" label="작업자">
            <Select allowClear showSearch optionFilterProp="label" options={employees.options} />
          </Form.Item>
        </Col>
        <Col span={8}>
          <Form.Item name="repairPart" label="수리·교체 부위" rules={[{ max: 100 }]}><Input /></Form.Item>
        </Col>
        <Col span={8}>
          <Form.Item name="vendorName" label="외부 업체" rules={[{ max: 100 }]}><Input /></Form.Item>
        </Col>
        <Col span={8}>
          <Form.Item name="cost" label="비용(원)">
            <InputNumber min={0} style={{ width: '100%' }} />
          </Form.Item>
        </Col>
        <Col span={12}>
          <Form.Item name="description" label="내용"><Input.TextArea rows={3} /></Form.Item>
        </Col>
        <Col span={12}>
          <Form.Item name="result" label="결과"><Input.TextArea rows={3} /></Form.Item>
        </Col>
        <Col span={8}>
          <Form.Item name="nextDueDate" label="다음 점검 예정일"><DatePicker style={{ width: '100%' }} /></Form.Item>
        </Col>
      </Row>
    </Form>
  )

  return (
    <EditorWindow title={m ? `설비 보전 — ${m.equipmentName} ${dayjs(m.maintenanceDate).format(DATE)}` : '설비 보전 등록'} onClose={onClose}
      extra={(
        <Space>
          {m && canDelete && (
            <Popconfirm title="이 보전 기록을 삭제합니다 (첨부 포함)" onConfirm={() => void remove()}>
              <Button danger icon={<DeleteOutlined />}>삭제</Button>
            </Popconfirm>
          )}
          {editable && <Button type="primary" loading={busy} onClick={() => form.submit()}>저장</Button>}
        </Space>
      )}>
      {m ? (
        <Tabs items={[
          { key: 'form', label: '내용', children: fields },
          {
            key: 'files', label: `사진·자료 (${detail.data!.attachments.length})`,
            children: <AttachmentList basePath={`/api/maintenances/${id}/attachments`} owner="maintenance" defaultKind="MAINTENANCE_PHOTO"
              attachments={detail.data!.attachments} canEdit={canUpdate} onChanged={() => void detail.refetch()} />,
          },
        ]} />
      ) : fields}
    </EditorWindow>
  )
}
