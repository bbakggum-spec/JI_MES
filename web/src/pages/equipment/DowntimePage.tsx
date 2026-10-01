import { DeleteOutlined, PlusOutlined } from '@ant-design/icons'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Col, DatePicker, Form, Input, InputNumber, Popconfirm, Radio, Row, Select, Space, Statistic, Table, Tag, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useMemo, useState } from 'react'
import { ApiError, api } from '../../api/client'
import { useCan } from '../../auth/useAuth'
import EditorWindow from '../../components/EditorWindow'
import { useCommonCodes } from '../../hooks/useCommonCodes'
import { useOptions } from '../../hooks/useOptions'
import { queryKeys } from '../../queryKeys'
import type { PageProps } from '../registry'

interface Downtime {
  equipmentDowntimeId: number
  equipmentId: number
  equipmentName: string
  equipmentTypeName: string | null
  downtimeDate: string
  startedAt: string | null
  endedAt: string | null
  durationMin: number | null
  isPlanned: boolean
  reasonCode: string | null
  reporterEmployeeId: number | null
  reporterName: string | null
  remark: string | null
}

const DATE = 'YYYY-MM-DD'
const DATETIME = 'YYYY-MM-DD HH:mm'
const SEND = 'YYYY-MM-DDTHH:mm:00'
const REASON = 'DOWNTIME_REASON'
const errorText = (e: unknown) => (e instanceof ApiError && e.errors ? Object.values(e.errors).flat().join(' ') : e instanceof Error ? e.message : String(e))
/** 분 → "3시간 20분" */
const hm = (min: number) => {
  const m = Math.round(min)
  const h = Math.floor(m / 60)
  return h > 0 ? `${h}시간${m % 60 ? ` ${m % 60}분` : ''}` : `${m}분`
}
const isOpen = (d: Downtime) => d.startedAt !== null && d.endedAt === null && d.durationMin === null
/** 진행 중이면 지금까지 시간 */
const minutesOf = (d: Downtime, now: Dayjs) => d.durationMin ?? (isOpen(d) ? now.diff(dayjs(d.startedAt), 'minute') : 0)

/** 설비 비가동 — 구 F_DowntimeInput(입력) + F_DowntimeStatus(현황)를 한 화면으로 (설계 §26.2 A, §28.1) */
export default function DowntimePage({ menuKey }: PageProps) {
  const codes = useCommonCodes()
  const queryClient = useQueryClient()
  const canCreate = useCan(menuKey, 'create')
  const canUpdate = useCan(menuKey, 'update')
  const canDelete = useCan(menuKey, 'delete')
  const { message } = App.useApp()
  const equipment = useOptions('/api/master/equipment/options')
  const [range, setRange] = useState<[Dayjs, Dayjs]>([dayjs().startOf('month'), dayjs()])
  const [equipmentId, setEquipmentId] = useState<number>()
  const [planned, setPlanned] = useState<'all' | 'true' | 'false'>('all')
  const [editing, setEditing] = useState<Downtime | 'new' | null>(null)
  const params = new URLSearchParams({ from: range[0].format(DATE), to: range[1].format(DATE) })
  if (equipmentId) params.set('equipmentId', String(equipmentId))
  if (planned !== 'all') params.set('planned', planned)
  const list = useQuery({
    queryKey: [...queryKeys.equipment, 'downtimes', params.toString()],
    queryFn: ({ signal }) => api<Downtime[]>(`/api/downtimes?${params}`, { signal }),
  })
  const rows = useMemo(() => list.data ?? [], [list.data])
  const refresh = () => void queryClient.invalidateQueries({ queryKey: [...queryKeys.equipment, 'downtimes'] })
  const now = dayjs()

  // 현황: 설비별·사유별 합계 (진행 중은 지금까지)
  const summary = useMemo(() => {
    const byEquipment = new Map<number, { key: number; name: string; count: number; planned: number; breakdown: number }>()
    const byReason = new Map<string, { key: string; count: number; minutes: number }>()
    for (const d of rows) {
      const m = minutesOf(d, now)
      const e = byEquipment.get(d.equipmentId) ?? { key: d.equipmentId, name: d.equipmentName, count: 0, planned: 0, breakdown: 0 }
      e.count += 1
      if (d.isPlanned) e.planned += m
      else e.breakdown += m
      byEquipment.set(d.equipmentId, e)
      const rk = d.reasonCode ?? ''
      const r = byReason.get(rk) ?? { key: rk, count: 0, minutes: 0 }
      r.count += 1
      r.minutes += m
      byReason.set(rk, r)
    }
    return {
      equipment: [...byEquipment.values()].sort((a, b) => b.planned + b.breakdown - (a.planned + a.breakdown)),
      reasons: [...byReason.values()].sort((a, b) => b.minutes - a.minutes),
      total: rows.reduce((s, d) => s + minutesOf(d, now), 0),
      open: rows.filter(isOpen).length,
    }
    // now 는 목록을 다시 읽을 때만 바뀌면 충분
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rows])

  const end = async (d: Downtime) => {
    try {
      await api(`/api/downtimes/${d.equipmentDowntimeId}/end`, { method: 'POST', body: {} })
      message.success('비가동을 종료했습니다.')
      refresh()
    } catch (e) { message.error(errorText(e)) }
  }
  const remove = async (d: Downtime) => {
    try {
      await api(`/api/downtimes/${d.equipmentDowntimeId}`, { method: 'DELETE' })
      refresh()
    } catch (e) { message.error(errorText(e)) }
  }
  const kind = (p: boolean) => (p ? <Tag color="blue">계획</Tag> : <Tag color="orange">고장</Tag>)

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 8 }} wrap>
        <Typography.Title level={4} style={{ margin: 0 }}>설비 비가동</Typography.Title>
        <Space wrap>
          <DatePicker.RangePicker value={range} allowClear={false} onChange={(v) => v?.[0] && v[1] && setRange([v[0], v[1]])} />
          <Select allowClear showSearch optionFilterProp="label" placeholder="설비 전체" style={{ width: 160 }} value={equipmentId}
            onChange={setEquipmentId} options={equipment.options} />
          <Radio.Group optionType="button" value={planned} onChange={(e) => setPlanned(e.target.value)}
            options={[{ value: 'all', label: '전체' }, { value: 'false', label: '고장' }, { value: 'true', label: '계획' }]} />
          {canCreate && <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditing('new')}>등록</Button>}
        </Space>
      </Space>

      <Row gutter={[12, 12]} style={{ marginBottom: 12 }}>
        <Col xs={24} xl={6}>
          <Card size="small">
            <Space size="large">
              <Statistic title="건수" value={rows.length} />
              <Statistic title="비가동 시간" value={hm(summary.total)} />
              <Statistic title="진행 중" value={summary.open} valueStyle={summary.open ? { color: '#d4380d' } : undefined} />
            </Space>
          </Card>
        </Col>
        <Col xs={24} md={14} xl={10}>
          <Card size="small" title="설비별">
            <Table size="small" pagination={false} scroll={{ y: 150 }} rowKey="key" dataSource={summary.equipment}
              columns={[
                { title: '설비', dataIndex: 'name' },
                { title: '건수', dataIndex: 'count', width: 60, align: 'right' },
                { title: '고장', dataIndex: 'breakdown', width: 100, align: 'right', render: (m: number) => (m ? hm(m) : '') },
                { title: '계획', dataIndex: 'planned', width: 100, align: 'right', render: (m: number) => (m ? hm(m) : '') },
              ]} />
          </Card>
        </Col>
        <Col xs={24} md={10} xl={8}>
          <Card size="small" title="사유별">
            <Table size="small" pagination={false} scroll={{ y: 150 }} rowKey="key" dataSource={summary.reasons}
              columns={[
                { title: '사유', dataIndex: 'key', render: (k: string) => (k ? codes.name(REASON, k) : <Typography.Text type="secondary">(없음)</Typography.Text>) },
                { title: '건수', dataIndex: 'count', width: 60, align: 'right' },
                { title: '시간', dataIndex: 'minutes', width: 100, align: 'right', render: hm },
              ]} />
          </Card>
        </Col>
      </Row>

      <Table<Downtime> rowKey="equipmentDowntimeId" size="small" loading={list.isFetching} dataSource={rows} scroll={{ x: 1100 }}
        pagination={{ defaultPageSize: 50, showSizeChanger: true, showTotal: (n) => `${n}건` }}
        onRow={(r) => ({ onClick: () => canUpdate && setEditing(r), style: { cursor: canUpdate ? 'pointer' : undefined } })}
        columns={[
          { title: '일자', dataIndex: 'downtimeDate', width: 95, render: (d: string) => dayjs(d).format(DATE) },
          { title: '설비', dataIndex: 'equipmentName', width: 130, render: (n: string, r) => <>{n}{r.equipmentTypeName && <Typography.Text type="secondary"> {r.equipmentTypeName}</Typography.Text>}</> },
          { title: '구분', dataIndex: 'isPlanned', width: 70, render: kind },
          { title: '시작', dataIndex: 'startedAt', width: 135, render: (t: string | null) => t && dayjs(t).format(DATETIME) },
          {
            title: '종료', dataIndex: 'endedAt', width: 170,
            render: (t: string | null, r) => t ? dayjs(t).format(DATETIME) : isOpen(r) ? (
              <Space size={4}>
                <Tag color="red">진행 중</Tag>
                {canUpdate && (
                  <Popconfirm title="지금 시각으로 종료합니다" onConfirm={() => void end(r)}>
                    <Button size="small" onClick={(e) => e.stopPropagation()}>종료</Button>
                  </Popconfirm>
                )}
              </Space>
            ) : null,
          },
          { title: '시간', width: 100, align: 'right', render: (_: unknown, r) => hm(minutesOf(r, now)) },
          { title: '사유', dataIndex: 'reasonCode', width: 110, render: (c: string | null) => c && codes.name(REASON, c) },
          { title: '보고자', dataIndex: 'reporterName', width: 90 },
          { title: '비고', dataIndex: 'remark', ellipsis: true },
          ...(canDelete ? [{
            title: '', width: 50, render: (_: unknown, r: Downtime) => (
              <Popconfirm title="이 비가동 기록을 삭제합니다" onConfirm={() => void remove(r)}>
                <Button size="small" type="text" danger icon={<DeleteOutlined />} aria-label="삭제" onClick={(e) => e.stopPropagation()} />
              </Popconfirm>
            ),
          }] : []),
        ]} />

      {editing !== null && (
        <DowntimeWindow row={editing === 'new' ? null : editing} defaultEquipmentId={equipmentId}
          onClose={() => setEditing(null)} onSaved={() => { setEditing(null); refresh() }} />
      )}
    </>
  )
}

interface FormValues {
  equipmentId?: number
  isPlanned: boolean
  startedAt?: Dayjs
  endedAt?: Dayjs | null
  durationMin?: number | null
  reasonCode?: string | null
  reporterEmployeeId?: number | null
  remark?: string | null
}

function DowntimeWindow({ row, defaultEquipmentId, onClose, onSaved }: {
  row: Downtime | null; defaultEquipmentId?: number; onClose: () => void; onSaved: () => void
}) {
  const codes = useCommonCodes()
  const equipment = useOptions('/api/master/equipment/options')
  const employees = useOptions('/api/master/employee/options')
  const [form] = Form.useForm<FormValues>()
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const isPlanned = Form.useWatch('isPlanned', form)
  const endedAt = Form.useWatch('endedAt', form)

  const save = async (v: FormValues) => {
    setBusy(true)
    setError(null)
    try {
      const body = {
        equipmentId: v.equipmentId, isPlanned: v.isPlanned, startedAt: v.startedAt?.format(SEND), endedAt: v.endedAt ? v.endedAt.format(SEND) : null,
        durationMin: v.endedAt ? null : v.durationMin ?? null, reasonCode: v.reasonCode || null, reporterEmployeeId: v.reporterEmployeeId ?? null,
        remark: v.remark ?? null,
      }
      if (row) await api(`/api/downtimes/${row.equipmentDowntimeId}`, { method: 'PUT', body })
      else await api('/api/downtimes', { method: 'POST', body })
      onSaved()
    } catch (e) {
      setError(errorText(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <EditorWindow title={row ? '비가동 수정' : '비가동 등록'} size="default" onClose={onClose}
      extra={<Button type="primary" loading={busy} onClick={() => form.submit()}>저장</Button>}>
      {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 12 }} />}
      <Form<FormValues> form={form} layout="vertical" onFinish={(v) => void save(v)}
        initialValues={row ? {
          equipmentId: row.equipmentId, isPlanned: row.isPlanned, startedAt: row.startedAt ? dayjs(row.startedAt) : undefined,
          endedAt: row.endedAt ? dayjs(row.endedAt) : null, durationMin: row.endedAt ? null : row.durationMin, reasonCode: row.reasonCode,
          reporterEmployeeId: row.reporterEmployeeId, remark: row.remark,
        } : { equipmentId: defaultEquipmentId, isPlanned: false, startedAt: dayjs().second(0) }}>
        <Row gutter={12}>
          <Col span={14}>
            <Form.Item name="equipmentId" label="설비" rules={[{ required: true, message: '설비를 고르세요.' }]}>
              <Select showSearch optionFilterProp="label" options={equipment.options} />
            </Form.Item>
          </Col>
          <Col span={10}>
            <Form.Item name="isPlanned" label="구분" extra={isPlanned ? '스케줄 계산에서 이 시간을 뺍니다.' : undefined}>
              <Radio.Group optionType="button" options={[{ value: false, label: '고장' }, { value: true, label: '계획' }]} />
            </Form.Item>
          </Col>
          <Col span={12}>
            <Form.Item name="startedAt" label="시작" rules={[{ required: true, message: '시작 시각을 입력하세요.' }]}>
              <DatePicker showTime={{ format: 'HH:mm' }} format={DATETIME} style={{ width: '100%' }} />
            </Form.Item>
          </Col>
          <Col span={12}>
            <Form.Item name="endedAt" label="종료" rules={[{ required: !!isPlanned, message: '계획 비가동은 종료 시각이 필요합니다.' }]}
              extra={!isPlanned && !endedAt ? '비우면 진행 중 — 목록에서 [종료]' : undefined}>
              <DatePicker showTime={{ format: 'HH:mm' }} format={DATETIME} style={{ width: '100%' }} />
            </Form.Item>
          </Col>
          {!endedAt && !isPlanned && (
            <Col span={12}>
              <Form.Item name="durationMin" label="시간(분)" extra="종료 시각을 모를 때만">
                <InputNumber min={0} style={{ width: '100%' }} />
              </Form.Item>
            </Col>
          )}
          <Col span={12}>
            <Form.Item name="reasonCode" label="사유">
              <Select allowClear options={codes.options(REASON).map((c) => ({ value: c.code, label: c.codeName }))}
                notFoundContent="공통코드 '비가동 사유'를 등록하세요" />
            </Form.Item>
          </Col>
          <Col span={12}>
            <Form.Item name="reporterEmployeeId" label="보고자">
              <Select allowClear showSearch optionFilterProp="label" options={employees.options} />
            </Form.Item>
          </Col>
          <Col span={24}>
            <Form.Item name="remark" label="비고" rules={[{ max: 500 }]}>
              <Input.TextArea rows={2} />
            </Form.Item>
          </Col>
        </Row>
      </Form>
    </EditorWindow>
  )
}
