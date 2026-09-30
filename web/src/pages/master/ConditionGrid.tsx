import { ArrowDownOutlined, ArrowLeftOutlined, ArrowRightOutlined, ArrowUpOutlined, DeleteOutlined, PlusOutlined } from '@ant-design/icons'
import { useQueryClient } from '@tanstack/react-query'
import { App, Button, Form, Input, Modal, Popconfirm, Select, Space, Table, Tag, Tooltip, Typography } from 'antd'
import { useState } from 'react'
import { ApiError, api, fieldErrors } from '../../api/client'
import { useCan } from '../../auth/useAuth'
import { useCommonCodes } from '../../hooks/useCommonCodes'
import { queryKeys } from '../../queryKeys'
import { COMMON, cellKey, newStep, useConditionItems, type GridItem, type GridLayout, type GridStep, type GridValues } from './conditionGridModel'

/**
 * 조건 입력표 (구 F_WorkStandardAddForm·F_StandardTemplateAdd 가변 그리드) — 열 = [공통 +] 스텝, 행 = 관리항목.
 * 스텝·관리항목을 표 안에서 추가·이름 변경·삭제·이동한다. 단계 템플릿(값 없음)과 작업표준(값 있음)이 같이 쓴다.
 */

const move = <T,>(list: T[], i: number, d: number) => {
  const next = [...list]
  ;[next[i], next[i + d]] = [next[i + d], next[i]]
  return next
}

export default function ConditionGrid({ layout, onLayoutChange, values, onValuesChange, showCommon = false, editable }: {
  layout: GridLayout
  onLayoutChange?: (layout: GridLayout) => void
  /** 없으면 구성만 편집 (단계 템플릿) */
  values?: GridValues
  onValuesChange?: (values: GridValues) => void
  showCommon?: boolean
  editable: boolean
}) {
  const setLayout = (l: Partial<GridLayout>) => onLayoutChange?.({ ...layout, ...l })
  const hasValues = (pred: (key: string) => boolean) => values !== undefined && Object.entries(values).some(([k, v]) => v.trim() !== '' && pred(k))
  const stepHasValues = (s: GridStep) => hasValues((k) => k.startsWith(`${s.key}:`))
  const itemHasValues = (id: number) => hasValues((k) => k.endsWith(`:${id}`))

  /** 값이 있으면 확인 후 삭제 */
  const confirmDelete = (needConfirm: boolean, title: string, onDelete: () => void, tip: string) => needConfirm
    ? <Popconfirm title={title} okText="삭제" okButtonProps={{ danger: true }} onConfirm={onDelete}>
        <Button size="small" type="text" danger icon={<DeleteOutlined />} aria-label={tip} />
      </Popconfirm>
    : <Tooltip title={tip}><Button size="small" type="text" danger icon={<DeleteOutlined />} onClick={onDelete} aria-label={tip} /></Tooltip>

  const cell = (stepKey: string, item: GridItem) => {
    if (values === undefined) return null
    const k = cellKey(stepKey, item.conditionItemId)
    const value = values[k] ?? ''
    if (!editable || !onValuesChange) return value
    const invalid = item.valueType === 'NUMBER' && value.trim() !== '' && Number.isNaN(Number(value))
    return <Input size="small" value={value} status={invalid ? 'error' : undefined} inputMode={item.valueType === 'NUMBER' ? 'decimal' : undefined}
      onChange={(e) => onValuesChange({ ...values, [k]: e.target.value })} />
  }

  const columns = [
    {
      title: '관리항목', key: 'item', fixed: 'left' as const, width: editable ? 200 : 140,
      render: (_: unknown, item: GridItem, i: number) => (
        <Space size={2} style={{ width: '100%', justifyContent: 'space-between' }}>
          <span>
            {item.conditionItemName}{item.unitCode && <Typography.Text type="secondary"> ({item.unitCode})</Typography.Text>}
            {item.isActive === false && <Tag color="warning" style={{ marginLeft: 4 }}>중지</Tag>}
          </span>
          {editable && (
            <Space size={0}>
              <Button size="small" type="text" icon={<ArrowUpOutlined />} disabled={i === 0} aria-label="위로"
                onClick={() => setLayout({ items: move(layout.items, i, -1) })} />
              <Button size="small" type="text" icon={<ArrowDownOutlined />} disabled={i === layout.items.length - 1} aria-label="아래로"
                onClick={() => setLayout({ items: move(layout.items, i, 1) })} />
              {confirmDelete(itemHasValues(item.conditionItemId), `'${item.conditionItemName}' 행과 그 값을 지웁니다.`,
                () => setLayout({ items: layout.items.filter((_, j) => j !== i) }), '행 삭제')}
            </Space>
          )}
        </Space>
      ),
    },
    ...(showCommon ? [{
      title: <Tooltip title="스텝과 무관한 LOT 공통 조건">공통</Tooltip>, key: COMMON, width: 100,
      render: (_: unknown, item: GridItem) => cell(COMMON, item),
    }] : []),
    ...layout.steps.map((s, i) => ({
      key: s.key, width: editable ? 150 : 100,
      title: editable ? (
        <Space.Compact size="small" style={{ width: '100%' }}>
          <Input size="small" value={s.name} placeholder={`스텝 ${i + 1}`} maxLength={100} status={s.name.trim() === '' ? 'error' : undefined}
            onChange={(e) => setLayout({ steps: layout.steps.map((x, j) => (j === i ? { ...x, name: e.target.value } : x)) })} />
          <Button size="small" icon={<ArrowLeftOutlined />} disabled={i === 0} aria-label="왼쪽으로" onClick={() => setLayout({ steps: move(layout.steps, i, -1) })} />
          <Button size="small" icon={<ArrowRightOutlined />} disabled={i === layout.steps.length - 1} aria-label="오른쪽으로"
            onClick={() => setLayout({ steps: move(layout.steps, i, 1) })} />
          {confirmDelete(stepHasValues(s), `스텝 '${s.name || i + 1}' 열과 그 값을 지웁니다.`,
            () => setLayout({ steps: layout.steps.filter((_, j) => j !== i) }), '스텝 삭제')}
        </Space.Compact>
      ) : s.name,
      render: (_: unknown, item: GridItem) => cell(s.key, item),
    })),
    ...(editable ? [{
      key: '__add', width: 90,
      title: <Button size="small" type="dashed" icon={<PlusOutlined />} onClick={() => setLayout({ steps: [...layout.steps, newStep()] })}>스텝</Button>,
      render: () => null,
    }] : []),
  ]

  return (
    <>
      <Table<GridItem> size="small" pagination={false} bordered rowKey="conditionItemId" dataSource={layout.items} scroll={{ x: 'max-content' }}
        columns={columns} locale={{ emptyText: editable ? '아래에서 관리항목(행)을 추가하세요' : '관리항목 없음' }} />
      {editable && <ItemAdder exclude={layout.items.map((x) => x.conditionItemId)} onAdd={(item) => setLayout({ items: [...layout.items, item] })} />}
    </>
  )
}

/** 관리항목 추가: 조건 항목에서 검색, 없으면 이 자리에서 새로 등록 */
function ItemAdder({ exclude, onAdd }: { exclude: number[]; onAdd: (item: GridItem) => void }) {
  const items = useConditionItems()
  const canCreate = useCan('master.condition_item', 'create')
  const [creating, setCreating] = useState(false)
  return (
    <Space style={{ marginTop: 8, display: 'flex' }} wrap>
      <Select<number> style={{ width: 260 }} size="small" showSearch optionFilterProp="label" value={null}
        placeholder={<><PlusOutlined /> 관리항목(행) 추가 — 이름 검색</>}
        options={(items.data ?? []).filter((c) => c.isActive && !exclude.includes(c.conditionItemId))
          .map((c) => ({ value: c.conditionItemId, label: c.unitCode ? `${c.conditionItemName} (${c.unitCode})` : c.conditionItemName }))}
        onChange={(id) => { const c = items.data?.find((x) => x.conditionItemId === id); if (c) onAdd(c) }} />
      {canCreate && <Button size="small" onClick={() => setCreating(true)}>목록에 없는 항목 새로 등록</Button>}
      {creating && <NewItemModal onClose={() => setCreating(false)} onCreated={(item) => { setCreating(false); onAdd(item) }} />}
    </Space>
  )
}

interface NewItemForm { code: string; name: string; unitCode?: string; valueType: string }

function NewItemModal({ onClose, onCreated }: { onClose: () => void; onCreated: (item: GridItem) => void }) {
  const { message } = App.useApp()
  const [form] = Form.useForm<NewItemForm>()
  const codes = useCommonCodes()
  const queryClient = useQueryClient()
  const [saving, setSaving] = useState(false)
  const save = async () => {
    const v = await form.validateFields()
    setSaving(true)
    try {
      const r = await api<{ id: number }>('/api/master/condition_item', {
        method: 'POST',
        body: { condition_item_code: v.code, condition_item_name: v.name, unit_code: v.unitCode || null, value_type: v.valueType },
      })
      message.success(`조건 항목 '${v.name}' 을 등록했습니다.`)
      void queryClient.invalidateQueries({ queryKey: queryKeys.master })
      onCreated({ conditionItemId: r.id, conditionItemName: v.name, unitCode: v.unitCode || null, valueType: v.valueType, isActive: true })
    } catch (e) {
      // 범용 기준정보 API 는 컬럼 이름으로 오류를 준다
      const byName: Record<string, keyof NewItemForm> = { condition_item_code: 'code', condition_item_name: 'name', unit_code: 'unitCode', value_type: 'valueType' }
      const errors = fieldErrors(e).filter((f) => byName[f.name]).map((f) => ({ name: byName[f.name], errors: f.errors }))
      if (errors.length > 0) form.setFields(errors)
      else message.error(e instanceof ApiError && e.errors ? Object.values(e.errors).flat().join(' ') : e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }
  return (
    <Modal open title="조건 항목 새로 등록" onCancel={onClose} onOk={() => void save()} okText="등록" confirmLoading={saving} destroyOnHidden>
      <Typography.Paragraph type="secondary">기준정보 → 조건 항목에 등록되고, 입력표에 행으로 추가됩니다.</Typography.Paragraph>
      <Form form={form} layout="vertical" initialValues={{ valueType: 'NUMBER' }}>
        <Form.Item name="name" label="이름" rules={[{ required: true, max: 100 }]}><Input placeholder="예: 온도, 시간, CP" autoFocus /></Form.Item>
        <Form.Item name="code" label="코드" rules={[{ required: true, max: 50 }]}><Input placeholder="예: TEMP" /></Form.Item>
        <Space>
          <Form.Item name="unitCode" label="단위" rules={[{ max: 20 }]}><Input style={{ width: 120 }} placeholder="예: ℃, min, %" /></Form.Item>
          <Form.Item name="valueType" label="값 형식" rules={[{ required: true }]}>
            <Select style={{ width: 140 }} options={codes.options('CONDITION_VALUE_TYPE').map((c) => ({ value: c.code, label: c.codeName }))} />
          </Form.Item>
        </Space>
      </Form>
    </Modal>
  )
}
