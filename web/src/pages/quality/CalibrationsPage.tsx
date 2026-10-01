import { DeleteOutlined, PlusOutlined } from '@ant-design/icons'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, Button, Card, Col, DatePicker, Empty, Form, Input, InputNumber, Popconfirm, Radio, Row, Space, Switch, Table, Tabs, Tag, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import { ApiError, api } from '../../api/client'
import { useCan } from '../../auth/useAuth'
import AttachmentList, { type Attachment } from '../../components/AttachmentList'
import EditorWindow from '../../components/EditorWindow'
import { useCommonCodes } from '../../hooks/useCommonCodes'
import { queryKeys } from '../../queryKeys'
import type { PageProps } from '../registry'

interface Instrument {
  instrumentId: number
  instrumentCode: string
  instrumentName: string
  instrumentType: string | null
  serialNo: string | null
  calibrationCycleDay: number | null
  lastCalibratedDate: string | null
  nextCalibrationDate: string | null
  calibrationCount: number
  isActive: boolean
  dueState: 'OVERDUE' | 'DUE_SOON' | 'NONE' | null
}

interface Calibration {
  instrumentCalibrationId: number
  instrumentId: number
  calibrationDate: string
  result: string
  agencyName: string | null
  certificateNo: string | null
  nextCalibrationDate: string | null
  cost: number | null
  remark: string | null
  attachmentCount: number
}

const DATE = 'YYYY-MM-DD'
const errorText = (e: unknown) => (e instanceof ApiError && e.errors ? Object.values(e.errors).flat().join(' ') : e instanceof Error ? e.message : String(e))
const dueTag = (s: Instrument['dueState']) =>
  s === 'OVERDUE' ? <Tag color="red">지남</Tag> : s === 'DUE_SOON' ? <Tag color="orange">임박</Tag> : s === 'NONE' ? <Tag>미정</Tag> : <Tag color="green">정상</Tag>
const dueOrder = { OVERDUE: 0, DUE_SOON: 1, NONE: 2 } as const

/**
 * 측정기구 교정 — 설비 보전과 별도 관리 (설계 §26.2 B, §28.4).
 * 왼쪽 = 측정기구별 교정 상태 (다음 교정일 지남·임박 먼저), 오른쪽 = 선택한 기구의 교정 이력. 측정기구 자체는 기준정보 > 측정기구.
 */
export default function CalibrationsPage({ menuKey }: PageProps) {
  const queryClient = useQueryClient()
  const codes = useCommonCodes()
  const canCreate = useCan(menuKey, 'create')
  const [includeInactive, setIncludeInactive] = useState(false)
  const [selected, setSelected] = useState<number | null>(null)
  const [editing, setEditing] = useState<number | 'new' | null>(null)
  const instruments = useQuery({
    queryKey: [...queryKeys.equipment, 'calibrations', 'instruments', includeInactive],
    queryFn: ({ signal }) => api<Instrument[]>(`/api/calibrations/instruments?includeInactive=${includeInactive}`, { signal }),
  })
  const history = useQuery({
    queryKey: [...queryKeys.equipment, 'calibrations', 'history', selected],
    queryFn: ({ signal }) => api<Calibration[]>(`/api/calibrations?instrumentId=${selected}`, { signal }),
    enabled: selected !== null,
  })
  const refresh = () => void queryClient.invalidateQueries({ queryKey: [...queryKeys.equipment, 'calibrations'] })
  const sorted = [...(instruments.data ?? [])].sort((a, b) =>
    (a.dueState ? dueOrder[a.dueState] : 3) - (b.dueState ? dueOrder[b.dueState] : 3) || a.instrumentCode.localeCompare(b.instrumentCode))
  const current = instruments.data?.find((i) => i.instrumentId === selected)
  const overdue = (instruments.data ?? []).filter((i) => i.dueState === 'OVERDUE').length
  const soon = (instruments.data ?? []).filter((i) => i.dueState === 'DUE_SOON').length

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 8 }} wrap>
        <Space>
          <Typography.Title level={4} style={{ margin: 0 }}>측정기구 교정</Typography.Title>
          {overdue > 0 && <Tag color="red">교정 지남 {overdue}</Tag>}
          {soon > 0 && <Tag color="orange">임박 {soon}</Tag>}
        </Space>
        <Space size={4}><Switch size="small" checked={includeInactive} onChange={setIncludeInactive} />사용 중지 기구 포함</Space>
      </Space>
      <Row gutter={[12, 12]}>
        <Col xs={24} xl={13}>
          <Table<Instrument> rowKey="instrumentId" size="small" loading={instruments.isFetching} dataSource={sorted} scroll={{ x: 720 }}
            pagination={{ defaultPageSize: 30, showTotal: (n) => `${n}개` }}
            locale={{ emptyText: <Empty description="기준정보 > 측정기구 에서 기구를 등록하세요." /> }}
            rowClassName={(r) => (r.instrumentId === selected ? 'ant-table-row-selected' : '')}
            onRow={(r) => ({ onClick: () => setSelected(r.instrumentId), style: { cursor: 'pointer', opacity: r.isActive ? 1 : 0.5 } })}
            columns={[
              { title: '상태', dataIndex: 'dueState', width: 70, render: dueTag },
              { title: '코드', dataIndex: 'instrumentCode', width: 100 },
              { title: '이름', dataIndex: 'instrumentName', render: (n: string, r) => <>{n}{r.instrumentType && <Typography.Text type="secondary"> {r.instrumentType}</Typography.Text>}</> },
              { title: '주기', dataIndex: 'calibrationCycleDay', width: 70, align: 'right', render: (d: number | null) => d && `${d}일` },
              { title: '최근 교정', dataIndex: 'lastCalibratedDate', width: 100, render: (d: string | null) => d && dayjs(d).format(DATE) },
              { title: '다음 교정', dataIndex: 'nextCalibrationDate', width: 100, render: (d: string | null) => d && dayjs(d).format(DATE) },
              { title: '이력', dataIndex: 'calibrationCount', width: 55, align: 'right' },
            ]} />
        </Col>
        <Col xs={24} xl={11}>
          <Card size="small" title={current ? `${current.instrumentName} (${current.instrumentCode}) 교정 이력` : '교정 이력'}
            extra={current && canCreate && <Button size="small" type="primary" icon={<PlusOutlined />} onClick={() => setEditing('new')}>교정 등록</Button>}>
            {selected === null ? <Empty description="왼쪽에서 측정기구를 고르세요." image={Empty.PRESENTED_IMAGE_SIMPLE} /> : (
              <Table<Calibration> rowKey="instrumentCalibrationId" size="small" loading={history.isFetching} dataSource={history.data ?? []} pagination={false}
                onRow={(r) => ({ onClick: () => setEditing(r.instrumentCalibrationId), style: { cursor: 'pointer' } })}
                columns={[
                  { title: '교정일', dataIndex: 'calibrationDate', width: 95, render: (d: string) => dayjs(d).format(DATE) },
                  { title: '판정', dataIndex: 'result', width: 75, render: (c: string) => <Tag color={codes.attr<{ color?: string }>('DECISION', c)?.color}>{codes.name('DECISION', c)}</Tag> },
                  { title: '기관', dataIndex: 'agencyName', ellipsis: true },
                  { title: '성적서', dataIndex: 'certificateNo', width: 100, ellipsis: true },
                  { title: '다음', dataIndex: 'nextCalibrationDate', width: 95, render: (d: string | null) => d && dayjs(d).format(DATE) },
                  { title: '첨부', dataIndex: 'attachmentCount', width: 50, align: 'right', render: (n: number) => (n > 0 ? n : null) },
                ]} />
            )}
          </Card>
        </Col>
      </Row>
      {editing !== null && current && (
        <CalibrationWindow id={editing === 'new' ? null : editing} instrument={current} menuKey={menuKey}
          onClose={() => setEditing(null)} onSaved={(id) => { refresh(); setEditing(id) }} onDeleted={() => { setEditing(null); refresh() }} />
      )}
    </>
  )
}

interface FormValues {
  calibrationDate?: Dayjs
  result: string
  agencyName?: string | null
  certificateNo?: string | null
  nextCalibrationDate?: Dayjs | null
  cost?: number | null
  remark?: string | null
}

function CalibrationWindow({ id, instrument, menuKey, onClose, onSaved, onDeleted }: {
  id: number | null; instrument: Instrument; menuKey: string; onClose: () => void; onSaved: (id: number) => void; onDeleted: () => void
}) {
  const codes = useCommonCodes()
  const canUpdate = useCan(menuKey, 'update')
  const canDelete = useCan(menuKey, 'delete')
  const detail = useQuery({
    queryKey: [...queryKeys.equipment, 'calibrations', 'detail', id],
    queryFn: ({ signal }) => api<{ calibration: Calibration; attachments: Attachment[] }>(`/api/calibrations/${id}`, { signal }),
    enabled: id !== null,
  })
  const [form] = Form.useForm<FormValues>()
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const calibrationDate = Form.useWatch('calibrationDate', form)
  if (id !== null && !detail.data) return null
  const c = detail.data?.calibration
  const editable = c ? canUpdate : true
  // 판정: 검사 판정 공통코드 (항목 전용 '해당없음' 제외)
  const results = codes.options('DECISION').filter((o) => !o.attrJson?.includes('itemOnly'))

  const save = async (v: FormValues) => {
    setBusy(true)
    setError(null)
    try {
      const body = {
        instrumentId: instrument.instrumentId, calibrationDate: v.calibrationDate?.format(DATE), result: v.result, agencyName: v.agencyName ?? null,
        certificateNo: v.certificateNo ?? null, nextCalibrationDate: v.nextCalibrationDate?.format(DATE) ?? null, cost: v.cost ?? null, remark: v.remark ?? null,
      }
      if (c) {
        await api(`/api/calibrations/${id}`, { method: 'PUT', body })
        await detail.refetch()
        onSaved(id!)
      } else {
        const r = await api<{ id: number }>('/api/calibrations', { method: 'POST', body })
        onSaved(r.id)
      }
    } catch (e) {
      setError(errorText(e))
    } finally {
      setBusy(false)
    }
  }

  const cycleHint = instrument.calibrationCycleDay && calibrationDate
    ? `비우면 ${calibrationDate.add(instrument.calibrationCycleDay, 'day').format(DATE)} (교정 주기 ${instrument.calibrationCycleDay}일)`
    : '비우면 교정 주기로 계산 (주기 없으면 미정)'

  const fields = (
    <Form<FormValues> form={form} layout="vertical" disabled={!editable} onFinish={(v) => void save(v)}
      initialValues={c ? {
        calibrationDate: dayjs(c.calibrationDate), result: c.result, agencyName: c.agencyName, certificateNo: c.certificateNo,
        nextCalibrationDate: c.nextCalibrationDate ? dayjs(c.nextCalibrationDate) : null, cost: c.cost, remark: c.remark,
      } : { calibrationDate: dayjs(), result: results[0]?.code }}>
      {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 12 }} />}
      <Row gutter={12}>
        <Col span={12}>
          <Form.Item name="calibrationDate" label="교정일" rules={[{ required: true, message: '교정일을 입력하세요.' }]}>
            <DatePicker style={{ width: '100%' }} />
          </Form.Item>
        </Col>
        <Col span={12}>
          <Form.Item name="result" label="판정" rules={[{ required: true }]}>
            <Radio.Group optionType="button" options={results.map((o) => ({ value: o.code, label: o.codeName }))} />
          </Form.Item>
        </Col>
        <Col span={12}>
          <Form.Item name="agencyName" label="교정 기관" rules={[{ max: 100 }]}><Input /></Form.Item>
        </Col>
        <Col span={12}>
          <Form.Item name="certificateNo" label="성적서 번호" rules={[{ max: 100 }]}><Input /></Form.Item>
        </Col>
        <Col span={12}>
          <Form.Item name="nextCalibrationDate" label="다음 교정일" extra={cycleHint}><DatePicker style={{ width: '100%' }} /></Form.Item>
        </Col>
        <Col span={12}>
          <Form.Item name="cost" label="비용(원)"><InputNumber min={0} style={{ width: '100%' }} /></Form.Item>
        </Col>
        <Col span={24}>
          <Form.Item name="remark" label="비고" rules={[{ max: 500 }]}><Input.TextArea rows={2} /></Form.Item>
        </Col>
      </Row>
    </Form>
  )

  return (
    <EditorWindow size="default" title={`${instrument.instrumentName} 교정 ${c ? dayjs(c.calibrationDate).format(DATE) : '등록'}`} onClose={onClose}
      extra={(
        <Space>
          {c && canDelete && (
            <Popconfirm title="이 교정 기록을 삭제합니다 (첨부 포함)" description="측정기구의 최근·다음 교정일이 이전 교정으로 돌아갑니다."
              onConfirm={() => void (async () => {
                try { await api(`/api/calibrations/${id}`, { method: 'DELETE' }); onDeleted() } catch (e) { setError(errorText(e)) }
              })()}>
              <Button danger icon={<DeleteOutlined />}>삭제</Button>
            </Popconfirm>
          )}
          {editable && <Button type="primary" loading={busy} onClick={() => form.submit()}>저장</Button>}
        </Space>
      )}>
      {c ? (
        <Tabs items={[
          { key: 'form', label: '내용', children: fields },
          {
            key: 'files', label: `성적서 파일 (${detail.data!.attachments.length})`,
            children: <AttachmentList basePath={`/api/calibrations/${id}/attachments`} owner="instrument_calibration" defaultKind="CALIBRATION_CERT"
              attachments={detail.data!.attachments} canEdit={canUpdate} onChanged={() => void detail.refetch()} />,
          },
        ]} />
      ) : fields}
    </EditorWindow>
  )
}
