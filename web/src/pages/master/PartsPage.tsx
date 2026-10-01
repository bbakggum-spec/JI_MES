import { MinusCircleOutlined, PlusOutlined } from '@ant-design/icons'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  App, Button, Checkbox, Col, Form, Input, InputNumber, Radio, Row, Select, Space, Switch,
  Table, Tabs, Tag, Typography,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import AttachmentList from '../../components/AttachmentList'
import EditorWindow from '../../components/EditorWindow'
import dayjs from 'dayjs'
import { useState } from 'react'
import { ApiError, api, fieldErrors } from '../../api/client'
import { fetchAllPages } from '../../api/paging'
import { useCan } from '../../auth/useAuth'
import ExportButton from '../../components/ExportButton'
import { useCommonCodes } from '../../hooks/useCommonCodes'
import { useDataVersion } from '../../hooks/useDataVersion'
import { queryKeys } from '../../queryKeys'
import type { PageProps } from '../registry'

interface Part {
  partId: number
  partCode: string
  partName: string
  partNumber: string | null
  specification: string | null
  model: string | null
  material: string | null
  unitWeight: number | null
  unitCode: string | null
  priceBasis: string
  unitPrice: number | null
  drawingNo: string | null
  hardness: string | null
  coreHardness: string | null
  effectiveHardeningDepth: string | null
  grade: string | null
  texture: string | null
  remark: string | null
  isActive: boolean
  customerNames: string | null
  defaultHeatProcessName: string | null
  attachmentCount: number
}

interface PartDetail {
  part: Part
  customers: { customerId: number; customerName: string; customerPartCode: string | null; isCustomerLotRequired: boolean; isPrimary: boolean }[]
  heatProcesses: { heatProcessId: number; heatProcessName: string; isDefault: boolean }[]
  printTemplates: { printTemplateId: number; printTemplateName: string; purposeName: string; customerId: number | null; customerName: string | null; isDefault: boolean }[]
  attachments: { attachmentId: number; attachmentKind: string; fileName: string; contentType: string | null; fileSize: number | null; caption: string | null; createdAt: string }[]
}

interface Option {
  value: number
  label: string
  active: boolean
  group: string | null
}

interface Lookups {
  customers: Option[]
  heatProcesses: Option[]
  templates: Option[]
}

const PAGE_SIZE = 50

/** 품목 (설계 §22.4, 구 F_PartForm / F_PartDetailForm) */
export default function PartsPage({ menuKey }: PageProps) {
  const canCreate = useCan(menuKey, 'create')
  const canUpdate = useCan(menuKey, 'update')
  const codes = useCommonCodes()
  const queryClient = useQueryClient()
  const [search, setSearch] = useState('')
  const [customerId, setCustomerId] = useState<number | undefined>()
  const [includeInactive, setIncludeInactive] = useState(false)
  const [page, setPage] = useState(1)
  const [editing, setEditing] = useState<number | 'new' | null>(null)

  const key = [...queryKeys.master, 'part']
  const lookups = useQuery({ queryKey: [...key, 'lookups'], queryFn: ({ signal }) => api<Lookups>('/api/parts/lookups', { signal }) })
  const params = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE), includeInactive: String(includeInactive) })
  if (search) params.set('search', search)
  if (customerId) params.set('customerId', String(customerId))
  const list = useQuery({
    queryKey: [...key, 'list', params.toString()],
    queryFn: ({ signal }) => api<{ items: Part[]; total: number }>(`/api/parts?${params}`, { signal }),
    placeholderData: (prev) => prev,
  })

  // 표·내보내기 공용 열 (내보내기 = 화면 표시 글자 그대로)
  const listColumns: ColumnsType<Part> = [
      { title: '코드', dataIndex: 'partCode', width: 120 },
      { title: '품명', dataIndex: 'partName', ellipsis: true },
      { title: '품번', dataIndex: 'partNumber', width: 120 },
      { title: '규격', dataIndex: 'specification', width: 110, ellipsis: true },
      { title: '거래처', dataIndex: 'customerNames', width: 160, ellipsis: true },
      { title: '기본 공정', dataIndex: 'defaultHeatProcessName', width: 110 },
      {
        title: '단가', width: 130, align: 'right',
        render: (_: unknown, p) => p.unitPrice != null && `${p.unitPrice.toLocaleString()} / ${codes.name('PRICE_BASIS', p.priceBasis)}`,
      },
      { title: '도면번호', dataIndex: 'drawingNo', width: 110 },
      { title: '첨부', dataIndex: 'attachmentCount', width: 60, align: 'right', render: (n: number) => (n > 0 ? n : null) },
    ]

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 8 }} wrap>
        <Typography.Title level={4} style={{ margin: 0 }}>품목</Typography.Title>
        <Space wrap>
          <Select allowClear showSearch optionFilterProp="label" placeholder="거래처 전체" style={{ width: 180 }} value={customerId}
            onChange={(v) => { setCustomerId(v); setPage(1) }}
            options={(lookups.data?.customers ?? []).filter((c) => c.active).map((c) => ({ value: c.value, label: c.label }))} />
          <Input.Search allowClear placeholder="코드·품명·품번·도면번호·고객 품번" style={{ width: 260 }} onSearch={(v) => { setSearch(v.trim()); setPage(1) }} />
          <Space size={4}><Switch size="small" checked={includeInactive} onChange={setIncludeInactive} />사용 중지 포함</Space>
          <ExportButton title="품목" columns={listColumns} fetchRows={() => fetchAllPages((page, pageSize) => {
            const q = new URLSearchParams(params)
            q.set('page', String(page))
            q.set('pageSize', String(pageSize))
            return api<{ items: Part[]; total: number }>(`/api/parts?${q}`)
          })} />
          {canCreate && <Button type="primary" icon={<PlusOutlined />} onClick={() => setEditing('new')}>품목 추가</Button>}
        </Space>
      </Space>
      <Table<Part> rowKey="partId" size="middle" loading={list.isFetching} dataSource={list.data?.items ?? []} scroll={{ x: 1100 }}
        onRow={(p) => ({ onClick: () => setEditing(p.partId), style: { cursor: 'pointer', opacity: p.isActive ? 1 : 0.5 } })}
        pagination={{ current: page, pageSize: PAGE_SIZE, total: list.data?.total ?? 0, showSizeChanger: false, showTotal: (t) => `${t.toLocaleString()}건`, onChange: setPage }}
        columns={listColumns} />
      {editing !== null && (
        <PartWindow partId={editing === 'new' ? null : editing} lookups={lookups.data} canEdit={editing === 'new' ? canCreate : canUpdate}
          onClose={() => setEditing(null)}
          onSaved={(id) => { setEditing(id); void queryClient.invalidateQueries({ queryKey: key }) }} />
      )}
    </>
  )
}

type PartForm = Omit<Part, 'partId' | 'customerNames' | 'defaultHeatProcessName' | 'attachmentCount'> & {
  customers: PartDetail['customers']
  heatProcesses: PartDetail['heatProcesses']
  reason?: string
}

function PartWindow({ partId, lookups, canEdit, onClose, onSaved }: {
  partId: number | null; lookups: Lookups | undefined; canEdit: boolean; onClose: () => void; onSaved: (id: number) => void
}) {
  const isNew = partId === null
  const detail = useQuery({
    queryKey: [...queryKeys.master, 'part', 'detail', partId],
    queryFn: ({ signal }) => api<PartDetail>(`/api/parts/${partId}`, { signal }),
    enabled: !isNew,
  })
  const dataVersion = useDataVersion(detail.data)
  const title = isNew ? '품목 추가' : detail.data ? `${detail.data.part.partName} (${detail.data.part.partCode})` : '품목'

  return (
    <EditorWindow onClose={onClose} size="large" title={title} destroyOnHidden>
      {isNew ? <PartForm lookups={lookups} canEdit={canEdit} onSaved={onSaved} /> : detail.data && (
        <Tabs items={[
          { key: 'info', label: '기본 정보', children: <PartForm key={dataVersion} detail={detail.data} lookups={lookups} canEdit={canEdit} onSaved={() => { void detail.refetch(); onSaved(partId) }} /> },
          { key: 'files', label: `도면·이미지 (${detail.data.attachments.length})`, children: <Attachments partId={partId} detail={detail.data} canEdit={canEdit} onChanged={() => void detail.refetch()} /> },
          { key: 'templates', label: '성적서 양식', children: <TemplateLinks partId={partId} detail={detail.data} lookups={lookups} canEdit={canEdit} onSaved={() => void detail.refetch()} /> },
          { key: 'history', label: '변경 이력', children: <History partId={partId} /> },
        ]} />
      )}
    </EditorWindow>
  )
}

function PartForm({ detail, lookups, canEdit, onSaved }: { detail?: PartDetail; lookups: Lookups | undefined; canEdit: boolean; onSaved: (id: number) => void }) {
  const [form] = Form.useForm<PartForm>()
  const { message, modal } = App.useApp()
  const codes = useCommonCodes()
  const [saving, setSaving] = useState(false)
  const initial: Partial<PartForm> = detail
    ? { ...detail.part, customers: detail.customers, heatProcesses: detail.heatProcesses }
    : { priceBasis: 'EA', isActive: true, customers: [], heatProcesses: [] }

  const submit = async (allowDuplicatePartNumber = false) => {
    const v = await form.validateFields()
    const body = { ...v, allowDuplicatePartNumber }
    setSaving(true)
    try {
      if (detail) {
        await api(`/api/parts/${detail.part.partId}`, { method: 'PUT', body })
        message.success('저장했습니다.')
        onSaved(detail.part.partId)
      } else {
        const r = await api<{ partId: number }>('/api/parts', { method: 'POST', body })
        message.success('품목을 등록했습니다. 도면·이미지를 첨부할 수 있습니다.')
        onSaved(r.partId)
      }
    } catch (e) {
      if (e instanceof ApiError && e.code === 'DUPLICATE_PART_NUMBER') {
        modal.confirm({ title: '같은 품번이 있습니다', content: e.message, okText: '그래도 저장', onOk: () => submit(true) })
        return
      }
      const errors = fieldErrors<PartForm>(e)
      if (errors.length > 0) form.setFields(errors)
      else message.error(e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }

  const customerOptions = (lookups?.customers ?? []).map((c) => ({ value: c.value, label: c.label, disabled: !c.active }))
  const processOptions = (lookups?.heatProcesses ?? []).map((c) => ({ value: c.value, label: c.label, disabled: !c.active }))

  return (
    <Form form={form} layout="vertical" initialValues={initial} disabled={!canEdit}>
      <Row gutter={12}>
        <Col span={8}><Form.Item name="partCode" label="품목 코드" rules={[{ required: true, max: 50 }]}><Input /></Form.Item></Col>
        <Col span={16}><Form.Item name="partName" label="품명" rules={[{ required: true, max: 100 }]}><Input /></Form.Item></Col>
        <Col span={8}><Form.Item name="partNumber" label="품번"><Input maxLength={100} /></Form.Item></Col>
        <Col span={8}><Form.Item name="specification" label="규격"><Input maxLength={100} /></Form.Item></Col>
        <Col span={8}><Form.Item name="model" label="기종"><Input maxLength={100} /></Form.Item></Col>
        <Col span={8}><Form.Item name="material" label="재질"><Input maxLength={100} /></Form.Item></Col>
        <Col span={8}><Form.Item name="unitWeight" label="단중"><InputNumber min={0} style={{ width: '100%' }} /></Form.Item></Col>
        <Col span={8}><Form.Item name="drawingNo" label="도면번호"><Input maxLength={100} /></Form.Item></Col>
        <Col span={8}>
          <Form.Item name="priceBasis" label="단가 적용" extra="출하 금액 = 수량·중량·charge × 단가">
            <Radio.Group options={codes.options('PRICE_BASIS').map((c) => ({ value: c.code, label: c.codeName }))} />
          </Form.Item>
        </Col>
        <Col span={8}><Form.Item name="unitPrice" label="단가"><InputNumber min={0} style={{ width: '100%' }} /></Form.Item></Col>
        <Col span={8}><Form.Item name="isActive" label="사용" valuePropName="checked"><Switch /></Form.Item></Col>
      </Row>
      <Typography.Text strong>요구사항 (수주 등록 시 복사)</Typography.Text>
      <Row gutter={12} style={{ marginTop: 8 }}>
        <Col span={8}><Form.Item name="hardness" label="요구경도"><Input maxLength={100} /></Form.Item></Col>
        <Col span={8}><Form.Item name="coreHardness" label="심부경도"><Input maxLength={100} /></Form.Item></Col>
        <Col span={8}><Form.Item name="effectiveHardeningDepth" label="경화층"><Input maxLength={100} /></Form.Item></Col>
        <Col span={8}><Form.Item name="texture" label="조직"><Input maxLength={100} /></Form.Item></Col>
        <Col span={8}><Form.Item name="grade" label="등급"><Input maxLength={50} /></Form.Item></Col>
      </Row>

      <Typography.Text strong>거래처별 품번</Typography.Text>
      <Form.List name="customers">
        {(fields, { add, remove }) => (
          <div style={{ marginTop: 8 }}>
            {fields.map((f) => (
              <Space key={f.key} align="baseline" wrap>
                <Form.Item name={[f.name, 'customerId']} rules={[{ required: true, message: '거래처' }]}>
                  <Select showSearch optionFilterProp="label" placeholder="거래처" style={{ width: 170 }} options={customerOptions} />
                </Form.Item>
                <Form.Item name={[f.name, 'customerPartCode']}><Input placeholder="고객 품번" style={{ width: 140 }} maxLength={100} /></Form.Item>
                <Form.Item name={[f.name, 'isCustomerLotRequired']} valuePropName="checked"><Checkbox>고객 LOT 필수</Checkbox></Form.Item>
                <Form.Item name={[f.name, 'isPrimary']} valuePropName="checked"><Checkbox>주 거래처</Checkbox></Form.Item>
                {canEdit && <MinusCircleOutlined onClick={() => remove(f.name)} />}
              </Space>
            ))}
            {canEdit && <Button type="dashed" icon={<PlusOutlined />} onClick={() => add({ isPrimary: fields.length === 0 })}>거래처 추가</Button>}
          </div>
        )}
      </Form.List>

      <Typography.Text strong style={{ display: 'block', marginTop: 16 }}>적용 공정 (수주 등록 시 기본값)</Typography.Text>
      <Form.List name="heatProcesses">
        {(fields, { add, remove }) => (
          <div style={{ marginTop: 8 }}>
            {fields.map((f) => (
              <Space key={f.key} align="baseline">
                <Form.Item name={[f.name, 'heatProcessId']} rules={[{ required: true, message: '공정' }]}>
                  <Select showSearch optionFilterProp="label" placeholder="공정" style={{ width: 200 }} options={processOptions} />
                </Form.Item>
                <Form.Item name={[f.name, 'isDefault']} valuePropName="checked"><Checkbox>기본</Checkbox></Form.Item>
                {canEdit && <MinusCircleOutlined onClick={() => remove(f.name)} />}
              </Space>
            ))}
            {canEdit && <Button type="dashed" icon={<PlusOutlined />} onClick={() => add({ isDefault: fields.length === 0 })}>공정 추가</Button>}
          </div>
        )}
      </Form.List>

      <Form.Item name="remark" label="비고" style={{ marginTop: 16 }}><Input.TextArea rows={2} /></Form.Item>
      {detail && <Form.Item name="reason" label="변경 사유"><Input maxLength={255} placeholder="변경 이력에 남습니다" /></Form.Item>}
      {canEdit && <Button type="primary" loading={saving} onClick={() => void submit()}>{detail ? '저장' : '등록'}</Button>}
    </Form>
  )
}

function Attachments({ partId, detail, canEdit, onChanged }: { partId: number; detail: PartDetail; canEdit: boolean; onChanged: () => void }) {
  return <AttachmentList basePath={`/api/parts/${partId}/attachments`} owner="part" defaultKind="PART_DRAWING" attachments={detail.attachments}
    canEdit={canEdit} onChanged={onChanged} />
}

function TemplateLinks({ partId, detail, lookups, canEdit, onSaved }: {
  partId: number; detail: PartDetail; lookups: Lookups | undefined; canEdit: boolean; onSaved: () => void
}) {
  const { message } = App.useApp()
  const [form] = Form.useForm<{ links: { printTemplateId: number; customerId: number | null; isDefault: boolean }[] }>()
  const save = async () => {
    const v = await form.validateFields()
    try {
      await api(`/api/parts/${partId}/print-templates`, { method: 'PUT', body: v.links ?? [] })
      message.success('양식 연결을 저장했습니다.')
      onSaved()
    } catch (e) {
      message.error(e instanceof ApiError && e.errors ? Object.values(e.errors).flat().join(' ') : e instanceof Error ? e.message : String(e))
    }
  }
  const customerOptions = detail.customers.map((c) => ({ value: c.customerId, label: c.customerName }))
  return (
    <>
      <Typography.Paragraph type="secondary">
        성적서 발행 시 양식 선택 순서: 이 품목 + 거래처 기본 → 이 품목 공통 기본 → 용도 기본 양식 (시스템 &gt; 출력 양식).
      </Typography.Paragraph>
      <Form form={form} disabled={!canEdit} initialValues={{ links: detail.printTemplates.map((t) => ({ printTemplateId: t.printTemplateId, customerId: t.customerId, isDefault: t.isDefault })) }}>
        <Form.List name="links">
          {(fields, { add, remove }) => (
            <>
              {fields.map((f) => (
                <Space key={f.key} align="baseline" wrap>
                  <Form.Item name={[f.name, 'printTemplateId']} rules={[{ required: true, message: '양식' }]}>
                    <Select style={{ width: 240 }} placeholder="성적서 양식"
                      options={(lookups?.templates ?? []).map((t) => ({ value: t.value, label: `${t.label}`, disabled: !t.active }))} />
                  </Form.Item>
                  <Form.Item name={[f.name, 'customerId']}>
                    <Select allowClear style={{ width: 160 }} placeholder="모든 거래처" options={customerOptions} />
                  </Form.Item>
                  <Form.Item name={[f.name, 'isDefault']} valuePropName="checked"><Checkbox>기본</Checkbox></Form.Item>
                  {canEdit && <MinusCircleOutlined onClick={() => remove(f.name)} />}
                </Space>
              ))}
              {canEdit && (
                <Space>
                  <Button type="dashed" icon={<PlusOutlined />} onClick={() => add({ isDefault: true, customerId: null })}>양식 연결</Button>
                  <Button type="primary" onClick={() => void save()}>저장</Button>
                </Space>
              )}
            </>
          )}
        </Form.List>
      </Form>
    </>
  )
}

function History({ partId }: { partId: number }) {
  const history = useQuery({
    queryKey: [...queryKeys.master, 'part', 'history', partId],
    queryFn: ({ signal }) => api<{ partHistoryId: number; changedAt: string; changedByName: string | null; changeType: string; oldDataJson: string | null; newDataJson: string | null }[]>(`/api/parts/${partId}/history`, { signal }),
  })
  return (
    <Table size="small" rowKey="partHistoryId" pagination={false} loading={history.isFetching} dataSource={history.data ?? []}
      expandable={{
        expandedRowRender: (h) => <ChangedFields before={h.oldDataJson} after={h.newDataJson} />,
      }}
      columns={[
        { title: '일시', dataIndex: 'changedAt', width: 150, render: (d: string) => dayjs(d).format('YYYY-MM-DD HH:mm') },
        { title: '사용자', dataIndex: 'changedByName', width: 120 },
        { title: '구분', dataIndex: 'changeType', width: 90, render: (t: string) => <Tag color={t === 'CREATE' ? 'green' : 'blue'}>{t}</Tag> },
      ]} />
  )
}

/** 바뀐 항목만 전 → 후로 */
function ChangedFields({ before, after }: { before: string | null; after: string | null }) {
  const b = before ? (JSON.parse(before) as Record<string, unknown>) : {}
  const a = after ? (JSON.parse(after) as Record<string, unknown>) : {}
  const keys = [...new Set([...Object.keys(b), ...Object.keys(a)])].filter((k) => JSON.stringify(b[k]) !== JSON.stringify(a[k]))
  if (keys.length === 0) return <Typography.Text type="secondary">바뀐 항목 없음</Typography.Text>
  return (
    <Table size="small" pagination={false} rowKey="k" dataSource={keys.map((k) => ({ k, b: b[k], a: a[k] }))} columns={[
      { title: '항목', dataIndex: 'k', width: 200 },
      { title: '변경 전', dataIndex: 'b', render: (v: unknown) => <Typography.Text type="secondary">{v == null ? '' : JSON.stringify(v)}</Typography.Text> },
      { title: '변경 후', dataIndex: 'a', render: (v: unknown) => (v == null ? '' : JSON.stringify(v)) },
    ]} />
  )
}
