import { LockOutlined, ReloadOutlined, UnlockOutlined } from '@ant-design/icons'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  App, Button, Card, Checkbox, Col, DatePicker, Descriptions, Drawer, Empty, Input, InputNumber, Modal, Popconfirm,
  Row, Segmented, Select, Space, Table, Tag, Typography,
} from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useState } from 'react'
import { ApiError, api } from '../../api/client'
import { useCan } from '../../auth/useAuth'
import { useClientSettings } from '../../hooks/useClientSettings'
import { useCommonCodes } from '../../hooks/useCommonCodes'
import { useNow } from '../../hooks/useNow'
import { queryKeys } from '../../queryKeys'
import ScheduleGantt from './ScheduleGantt'
import { dragState, type BacklogDropTarget } from './dragState'
import { nextInChain } from './ganttMath'
import type { BacklogRow, Board, BoardBlock } from './scheduleTypes'

const MENU_KEY = 'production.schedule'
const DAY_OPTIONS = [1, 2, 3, 7]
/** 확대 (화면 표시 옵션 — 업무 값 아님) */
const ZOOM_OPTIONS = [{ label: '넓게', value: 15 }, { label: '보통', value: 30 }, { label: '자세히', value: 60 }]

/** 작업시간을 찾지 못해 사용자 입력이 필요한 요청 (구 F_DummyTime) */
type PendingRequest =
  | { kind: 'create'; body: { salesOrderItemId: number; unitProcessId: number; equipmentId: number; beforeBlockId: number | null } }
  | { kind: 'merge'; blockId: number; body: { rowVersion: number; salesOrderItemId: number } }

export default function SchedulePage() {
  const canCreate = useCan(MENU_KEY, 'create')
  const canUpdate = useCan(MENU_KEY, 'update')
  const canDelete = useCan(MENU_KEY, 'delete')
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const codes = useCommonCodes()
  const { refreshIntervalMs, boardDays } = useClientSettings()
  const now = useNow(refreshIntervalMs)

  const [fromDate, setFromDate] = useState<Dayjs | null>(null)   // null = 서버 기준 오늘 작업일
  const [days, setDays] = useState<number | null>(null)          // null = 설정 schedule.board_days
  const [equipmentTypeId, setEquipmentTypeId] = useState<number | undefined>()
  const [pxPerHour, setPxPerHour] = useState(30)
  const [search, setSearch] = useState('')
  const [selected, setSelected] = useState<BoardBlock | null>(null)
  const [dropChoice, setDropChoice] = useState<{ row: BacklogRow; block: BoardBlock } | null>(null)
  const [pending, setPending] = useState<{ req: PendingRequest; defaultMin: number } | null>(null)
  const [busy, setBusy] = useState(false)

  const effectiveDays = days ?? boardDays
  const params = new URLSearchParams()
  if (fromDate) params.set('from', fromDate.format('YYYY-MM-DD'))
  if (effectiveDays) params.set('days', String(effectiveDays))
  if (equipmentTypeId) params.set('equipmentTypeId', String(equipmentTypeId))

  const board = useQuery({
    queryKey: [...queryKeys.schedule, 'board', params.toString()],
    queryFn: ({ signal }) => api<Board>(`/api/schedule/board?${params}`, { signal }),
    enabled: effectiveDays !== undefined,
    placeholderData: (prev) => prev,
    refetchInterval: refreshIntervalMs,   // 실시간 알림 보조 (설계 §15.4 H2)
  })
  const backlogParams = new URLSearchParams()
  if (equipmentTypeId) backlogParams.set('equipmentTypeId', String(equipmentTypeId))
  if (search.trim()) backlogParams.set('search', search.trim())
  const backlog = useQuery({
    queryKey: [...queryKeys.schedule, 'backlog', backlogParams.toString()],
    queryFn: ({ signal }) => api<BacklogRow[]>(`/api/schedule/backlog?${backlogParams}`, { signal }),
    placeholderData: (prev) => prev,
  })

  const refresh = () => queryClient.invalidateQueries({ queryKey: queryKeys.schedule })

  /** 모든 조작 공통: 충돌(409)은 새로 고침 안내, 작업시간 없음(422 DURATION_REQUIRED)은 입력창 */
  const run = async (action: () => Promise<unknown>, onDurationRequired?: (defaultMin: number) => void, success?: string) => {
    setBusy(true)
    try {
      await action()
      if (success) message.success(success)
      setSelected(null)
    } catch (e) {
      if (e instanceof ApiError && e.code === 'DURATION_REQUIRED' && onDurationRequired) onDurationRequired(Number(e.details.defaultMin))
      else if (e instanceof ApiError && e.status === 409) message.warning(`${e.message} 최신 계획을 다시 불러왔습니다.`)
      else message.error(e instanceof Error ? e.message : String(e))
    } finally {
      setBusy(false)
      await refresh()
    }
  }

  const submit = (req: PendingRequest, extra: object = {}) =>
    run(
      () => req.kind === 'create'
        ? api('/api/schedule/blocks', { method: 'POST', body: { ...req.body, ...extra } })
        : api(`/api/schedule/blocks/${req.blockId}/items`, { method: 'POST', body: { ...req.body, ...extra } }),
      (defaultMin) => setPending({ req, defaultMin }),
      req.kind === 'create' ? '계획을 배정했습니다.' : '계획에 병합했습니다.',
    )

  const createAt = (row: BacklogRow, equipmentId: number, beforeBlockId: number | null) =>
    submit({ kind: 'create', body: { salesOrderItemId: row.salesOrderItemId, unitProcessId: row.unitProcessId, equipmentId, beforeBlockId } })

  const handleBacklogDrop = (row: BacklogRow, equipmentId: number, target: BacklogDropTarget) => {
    if (!canCreate) return
    if ('onBlock' in target) setDropChoice({ row, block: target.onBlock })
    else void createAt(row, equipmentId, target.beforeBlockId)
  }

  const handleMove = (block: BoardBlock, equipmentId: number, beforeBlockId: number | null) => {
    if (!canUpdate || beforeBlockId === block.productionScheduleId) return
    void run(() => api(`/api/schedule/blocks/${block.productionScheduleId}/move`, {
      method: 'PUT', body: { rowVersion: block.rowVersion, equipmentId, beforeBlockId },
    }))
  }

  const statusColor = (status: string) =>
    codes.attr<{ color?: string }>('SCHEDULE_STATUS', status)?.color ?? '#1677FF'
  const priorityColor = (p: number) => codes.attr<{ color?: string }>('PRIORITY', String(p))?.color

  const data = board.data
  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 12 }} wrap>
        <Typography.Title level={4} style={{ margin: 0 }}>생산계획</Typography.Title>
        <Space wrap>
          <DatePicker allowClear placeholder="오늘 작업일" value={fromDate} onChange={setFromDate} />
          <Segmented options={DAY_OPTIONS.map((d) => ({ label: `${d}일`, value: d }))}
            value={effectiveDays} onChange={(v) => setDays(v as number)} />
          <Select allowClear placeholder="설비유형 전체" style={{ width: 150 }} value={equipmentTypeId} onChange={setEquipmentTypeId}
            options={(data?.equipmentTypes ?? []).map((t) => ({ label: t.equipmentTypeName, value: t.equipmentTypeId }))} />
          <Segmented options={ZOOM_OPTIONS} value={pxPerHour} onChange={(v) => setPxPerHour(v as number)} />
          <Button icon={<ReloadOutlined />} onClick={() => void refresh()} loading={board.isFetching}>새로고침</Button>
        </Space>
      </Space>

      <Row gutter={[12, 12]}>
        <Col xs={24} xxl={6}>
          <Card size="small" title="배정 대기" extra={<Typography.Text type="secondary">끌어서 설비에 놓기</Typography.Text>}
            styles={{ body: { padding: 8 } }}>
            <Input.Search allowClear placeholder="수주번호·거래처·품명" onSearch={setSearch} style={{ marginBottom: 8 }} />
            <Table<BacklogRow> size="small" rowKey={(r) => `${r.salesOrderItemId}-${r.unitProcessId}`}
              loading={backlog.isFetching} dataSource={backlog.data ?? []} pagination={{ pageSize: 12, size: 'small' }}
              scroll={{ x: 480 }}
              onRow={(r) => ({
                draggable: canCreate,
                onDragStart: (e) => { dragState.current = { kind: 'backlog', row: r }; e.dataTransfer.effectAllowed = 'copy' },
                onDragEnd: () => { dragState.current = null },
                style: { cursor: canCreate ? 'grab' : undefined },
              })}
              columns={[
                { title: '수주번호', dataIndex: 'orderItemNo', width: 110 },
                { title: '품명', dataIndex: 'partName', ellipsis: true },
                { title: '공정', dataIndex: 'unitProcessName', width: 64 },
                { title: '잔량', dataIndex: 'remainingQty', width: 64, align: 'right' },
                {
                  title: '우선', dataIndex: 'priority', width: 60,
                  render: (p: number) => <Tag color={priorityColor(p)}>{codes.name('PRIORITY', String(p))}</Tag>,
                },
                { title: '거래처', dataIndex: 'customerName', width: 90, ellipsis: true },
              ]} />
          </Card>
        </Col>
        <Col xs={24} xxl={18}>
          {data && data.equipment.length > 0 ? (
            <ScheduleGantt board={data} pxPerHour={pxPerHour} canEdit={canCreate || canUpdate} now={now}
              selectedId={selected?.productionScheduleId} statusColor={statusColor} priorityColor={priorityColor}
              onSelect={setSelected} onDropBacklog={handleBacklogDrop} onMoveBlock={handleMove} />
          ) : (
            <Card><Empty description={board.isPending ? '불러오는 중…' : '표시할 설비가 없습니다.'} /></Card>
          )}
          <Space style={{ marginTop: 8 }} wrap size="middle">
            {codes.options('SCHEDULE_STATUS').filter((c) => c.code !== 'CANCELLED').map((c) => (
              <Space key={c.code} size={4}><span style={{ display: 'inline-block', width: 12, height: 12, borderRadius: 2, background: statusColor(c.code) }} />{c.codeName}</Space>
            ))}
            <Space size={4}><LockOutlined />고정</Space>
            <Typography.Text type="secondary">위쪽 가는 막대 = 실적 · 빗금 = 비가동 · 붉은 선 = 현재</Typography.Text>
          </Space>
        </Col>
      </Row>

      <BlockDrawer block={selected} onClose={() => setSelected(null)} busy={busy}
        canUpdate={canUpdate} canDelete={canDelete} statusName={(s) => codes.name('SCHEDULE_STATUS', s)}
        sourceName={(s) => codes.name('RUNNING_TIME_SOURCE', s)}
        onLock={(b) => void run(() => api(`/api/schedule/blocks/${b.productionScheduleId}/lock`, {
          method: 'PUT', body: { rowVersion: b.rowVersion, locked: !b.isTimeLocked },
        }), undefined, b.isTimeLocked ? '고정을 해제했습니다.' : '시각을 고정했습니다.')}
        onCancel={(b) => void run(() => api(`/api/schedule/blocks/${b.productionScheduleId}/cancel`, {
          method: 'POST', body: { rowVersion: b.rowVersion },
        }), undefined, '계획을 취소했습니다. 수량은 배정 대기로 돌아갑니다.')} />

      {/* 배정 대기를 계획 블록 위에 놓았을 때 */}
      <Modal open={dropChoice !== null} title="배정 방법" onCancel={() => setDropChoice(null)} footer={null} destroyOnHidden>
        {dropChoice && (() => {
          const { row, block } = dropChoice
          const sameProcess = row.unitProcessId === block.unitProcessId
          const close = () => setDropChoice(null)
          return (
            <>
              <Typography.Paragraph>
                <b>{row.orderItemNo}</b> {row.partName} ({row.unitProcessName}) 를 <b>{block.plannedLotNo}</b> 계획에
              </Typography.Paragraph>
              <Space wrap>
                <Button type="primary" disabled={!sameProcess || !canUpdate} onClick={() => {
                  close()
                  void submit({ kind: 'merge', blockId: block.productionScheduleId, body: { rowVersion: block.rowVersion, salesOrderItemId: row.salesOrderItemId } })
                }}>병합 (한 LOT)</Button>
                <Button onClick={() => { close(); void createAt(row, block.equipmentId, block.productionScheduleId) }}>앞에 삽입</Button>
                <Button onClick={() => { close(); void createAt(row, block.equipmentId, nextInChain(data?.blocks ?? [], block)) }}>뒤에 삽입</Button>
              </Space>
              {!sameProcess && <Typography.Paragraph type="secondary" style={{ marginTop: 8 }}>단위공정이 달라 병합할 수 없습니다.</Typography.Paragraph>}
            </>
          )
        })()}
      </Modal>

      <DurationModal pending={pending?.req ?? null} defaultMin={pending?.defaultMin}
        onClose={() => setPending(null)}
        onSubmit={(req, extra) => { setPending(null); void submit(req, extra) }} />
    </>
  )
}

function BlockDrawer(props: {
  block: BoardBlock | null
  busy: boolean
  canUpdate: boolean
  canDelete: boolean
  statusName: (s: string) => string
  sourceName: (s: string) => string
  onClose: () => void
  onLock: (b: BoardBlock) => void
  onCancel: (b: BoardBlock) => void
}) {
  const b = props.block
  const editable = b && (b.status === 'PLANNED' || b.status === 'CONFIRMED')
  return (
    <Drawer open={b !== null} onClose={props.onClose} title={b?.plannedLotNo ?? '계획'} size="large"
      extra={b && editable && (
        <Space>
          {props.canUpdate && (
            <Button icon={b.isTimeLocked ? <UnlockOutlined /> : <LockOutlined />} loading={props.busy} onClick={() => props.onLock(b)}>
              {b.isTimeLocked ? '고정 해제' : '시각 고정'}
            </Button>
          )}
          {props.canDelete && (
            <Popconfirm title="이 계획을 취소할까요?" description="계획수량이 배정 대기로 돌아갑니다." onConfirm={() => props.onCancel(b)}>
              <Button danger loading={props.busy}>계획 취소</Button>
            </Popconfirm>
          )}
        </Space>
      )}>
      {b && (
        <>
          <Descriptions column={1} size="small" bordered items={[
            { label: '상태', children: props.statusName(b.status) },
            { label: '단위공정', children: b.unitProcessName },
            { label: '작업일 · 순번', children: `${dayjs(b.workDate).format('YYYY-MM-DD')} · ${b.sequenceNo}` },
            { label: '계획 시각', children: `${dayjs(b.plannedStartAt).format('MM-DD HH:mm')} ~ ${dayjs(b.plannedEndAt).format('MM-DD HH:mm')}` },
            { label: '작업시간', children: `${b.plannedDurationMin}분${b.durationSource ? ` (${props.sourceName(b.durationSource)})` : ''}` },
            { label: '계획수량', children: b.plannedQty },
            { label: '시각 고정', children: b.isTimeLocked ? '예' : '아니오' },
          ]} />
          <Typography.Title level={5} style={{ marginTop: 16 }}>담긴 수주</Typography.Title>
          <Table size="small" rowKey="salesOrderItemId" pagination={false} dataSource={b.items} columns={[
            { title: '수주번호', dataIndex: 'orderItemNo' },
            { title: '거래처', dataIndex: 'customerName' },
            { title: '품명', dataIndex: 'partName' },
            { title: '수량', dataIndex: 'plannedQty', align: 'right' },
          ]} />
        </>
      )}
    </Drawer>
  )
}

/** 작업표준·직전 작업·기준시간이 없을 때 작업시간 입력 (구 F_DummyTime → 기준시간 자동 등록) */
function DurationModal(props: {
  pending: PendingRequest | null
  defaultMin?: number
  onClose: () => void
  onSubmit: (req: PendingRequest, extra: object) => void
}) {
  const [minutes, setMinutes] = useState<number | null>(null)
  const [saveAsDefault, setSaveAsDefault] = useState(true)
  const req = props.pending
  return (
    <Modal open={req !== null} title="작업시간 입력" onCancel={props.onClose} destroyOnHidden
      footer={req && [
        <Button key="default" onClick={() => props.onSubmit(req, { useDefaultDuration: true })}>기본값 사용{props.defaultMin ? ` (${props.defaultMin}분)` : ''}</Button>,
        <Button key="ok" type="primary" disabled={!minutes || minutes <= 0}
          onClick={() => props.onSubmit(req, { durationMin: minutes, saveAsDefault })}>배정</Button>,
      ]}>
      <Typography.Paragraph>작업표준·직전 작업·설비 기준시간에서 작업시간을 찾지 못했습니다.</Typography.Paragraph>
      <Space orientation="vertical">
        <InputNumber autoFocus min={1} precision={0} suffix="분" value={minutes} onChange={setMinutes} style={{ width: 200 }} />
        <Checkbox checked={saveAsDefault} onChange={(e) => setSaveAsDefault(e.target.checked)}>이 설비유형·공정의 기준시간으로 등록</Checkbox>
      </Space>
    </Modal>
  )
}
