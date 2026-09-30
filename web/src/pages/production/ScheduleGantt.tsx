import { LockOutlined } from '@ant-design/icons'
import { Tooltip, Typography, theme } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useEffect, useMemo, useRef, type DragEvent } from 'react'
import { dragState, type BacklogDropTarget } from './dragState'
import { hourTicks, insertionTarget, workDayColumns, xOf } from './ganttMath'
import type { BacklogRow, Board, BoardBlock } from './scheduleTypes'
import { isMovable } from './scheduleTypes'


interface Props {
  board: Board
  pxPerHour: number
  canEdit: boolean
  now: Dayjs
  selectedId?: number
  statusColor: (status: string) => string
  priorityColor: (priority: number) => string | undefined
  onSelect: (block: BoardBlock) => void
  onDropBacklog: (row: BacklogRow, equipmentId: number, target: BacklogDropTarget) => void
  onMoveBlock: (block: BoardBlock, equipmentId: number, beforeBlockId: number | null) => void
}

const NAME_WIDTH = 150
const ROW_HEIGHT = 58
const WORK_LANE = 12

export default function ScheduleGantt(props: Props) {
  const { board, pxPerHour, canEdit, now, selectedId, statusColor, priorityColor } = props
  const { token } = theme.useToken()
  const from = useMemo(() => dayjs(board.from), [board.from])
  const to = useMemo(() => dayjs(board.to), [board.to])
  const width = xOf(to, from, pxPerHour)
  const days = useMemo(() => workDayColumns(from, to), [from, to])
  const ticks = useMemo(() => hourTicks(from, to, pxPerHour), [from, to, pxPerHour])
  const holidays = useMemo(() => new Set(board.holidays.map((h) => dayjs(h).format('YYYY-MM-DD'))), [board.holidays])
  const scrollRef = useRef<HTMLDivElement>(null)

  // 범위가 바뀌면 현재 시각(범위 밖이면 처음)이 보이도록 — 현재선 1시간 앞부터
  useEffect(() => {
    if (!scrollRef.current) return
    scrollRef.current.scrollLeft = now.isAfter(from) && now.isBefore(to) ? Math.max(xOf(now, from, pxPerHour) - pxPerHour, 0) : 0
    // eslint-disable-next-line react-hooks/exhaustive-deps -- 현재 시각이 흐를 때마다 스크롤을 옮기지 않는다
  }, [from, to, pxPerHour])

  const handleRowDrop = (e: DragEvent<HTMLDivElement>, equipmentId: number) => {
    e.preventDefault()
    const payload = dragState.current
    dragState.current = null
    if (!payload || !canEdit) return
    const at = from.add(Math.round(((e.clientX - e.currentTarget.getBoundingClientRect().left) / pxPerHour) * 60), 'minute')
    if (payload.kind === 'backlog') {
      props.onDropBacklog(payload.row, equipmentId, { beforeBlockId: insertionTarget(board.blocks, equipmentId, at) })
    } else {
      props.onMoveBlock(payload.block, equipmentId, insertionTarget(board.blocks, equipmentId, at, payload.block.productionScheduleId))
    }
  }

  const handleBlockDrop = (e: DragEvent<HTMLDivElement>, block: BoardBlock) => {
    // 배정 대기를 블록 위에 놓으면 병합/삽입 선택 — 블록 끌기는 행 드롭(위치 기준)으로 처리
    if (dragState.current?.kind !== 'backlog' || !isMovable(block)) return
    e.preventDefault()
    e.stopPropagation()
    const row = dragState.current.row
    dragState.current = null
    if (canEdit) props.onDropBacklog(row, block.equipmentId, { onBlock: block })
  }

  const header = (
    <div style={{ position: 'sticky', top: 0, zIndex: 3, background: token.colorBgContainer, borderBottom: `1px solid ${token.colorBorder}` }}>
      <div style={{ position: 'relative', height: 26 }}>
        {days.map((d) => (
          <div key={d.start.valueOf()} style={{
            position: 'absolute', left: xOf(d.start, from, pxPerHour), width: 24 * pxPerHour, height: 26,
            borderLeft: `1px solid ${token.colorBorder}`, padding: '3px 8px', fontWeight: 600, whiteSpace: 'nowrap', overflow: 'hidden',
            color: holidays.has(d.workDate.format('YYYY-MM-DD')) ? token.colorError : undefined,
          }}>
            {d.workDate.format('MM-DD (ddd)')}
            <Typography.Text type="secondary" style={{ fontWeight: 400, marginLeft: 6, fontSize: 12 }}>
              {d.start.format('HH:mm')} 시작
            </Typography.Text>
          </div>
        ))}
      </div>
      <div style={{ position: 'relative', height: 20, fontSize: 11, color: token.colorTextSecondary }}>
        {ticks.map((t) => (
          <div key={t.valueOf()} style={{ position: 'absolute', left: xOf(t, from, pxPerHour) + 2, top: 2 }}>{t.format('HH')}</div>
        ))}
      </div>
    </div>
  )

  return (
    <div style={{ display: 'flex', border: `1px solid ${token.colorBorderSecondary}`, borderRadius: token.borderRadius, background: token.colorBgContainer }}>
      {/* 설비 이름 열 */}
      <div style={{ width: NAME_WIDTH, flex: 'none', borderRight: `1px solid ${token.colorBorder}` }}>
        <div style={{ height: 47, borderBottom: `1px solid ${token.colorBorder}` }} />
        {board.equipment.map((eq) => (
          <div key={eq.equipmentId} style={{ height: ROW_HEIGHT, padding: '8px 12px', borderBottom: `1px solid ${token.colorBorderSecondary}` }}>
            <div style={{ fontWeight: 600 }}>{eq.equipmentName}</div>
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>{eq.equipmentCode} · {eq.equipmentTypeName}</Typography.Text>
          </div>
        ))}
      </div>

      {/* 시간 축 */}
      <div ref={scrollRef} style={{ overflowX: 'auto', flex: 1 }}>
        <div style={{ width, position: 'relative' }}>
          {header}
          {/* 휴일·눈금·현재선 (전체 행 공통) */}
          <div style={{ position: 'absolute', top: 47, left: 0, width, bottom: 0, pointerEvents: 'none' }}>
            {days.filter((d) => holidays.has(d.workDate.format('YYYY-MM-DD'))).map((d) => (
              <div key={d.start.valueOf()} style={{
                position: 'absolute', left: xOf(d.start, from, pxPerHour), width: 24 * pxPerHour, top: 0, bottom: 0,
                background: token.colorErrorBg, opacity: 0.6,
              }} />
            ))}
            {ticks.map((t) => (
              <div key={t.valueOf()} style={{
                position: 'absolute', left: xOf(t, from, pxPerHour), top: 0, bottom: 0,
                borderLeft: `1px ${days.some((d) => d.start.isSame(t)) ? 'solid' : 'dashed'} ${token.colorBorderSecondary}`,
              }} />
            ))}
            {now.isAfter(from) && now.isBefore(to) && (
              <div style={{ position: 'absolute', left: xOf(now, from, pxPerHour), top: 0, bottom: 0, borderLeft: `2px solid ${token.colorError}`, zIndex: 2 }} />
            )}
          </div>

          {board.equipment.map((eq) => (
            <div key={eq.equipmentId} data-equipment-id={eq.equipmentId}
              onDragOver={(e) => { if (canEdit) e.preventDefault() }}
              onDrop={(e) => handleRowDrop(e, eq.equipmentId)}
              style={{ position: 'relative', height: ROW_HEIGHT, borderBottom: `1px solid ${token.colorBorderSecondary}`, overflow: 'hidden' }}>
              {board.downtimes.filter((d) => d.equipmentId === eq.equipmentId).map((d, i) => (
                <Tooltip key={i} title={`${d.isPlanned ? '계획' : ''} 비가동 ${dayjs(d.startedAt).format('MM-DD HH:mm')} ~ ${dayjs(d.endedAt).format('HH:mm')}`}>
                  <div style={{
                    position: 'absolute', left: xOf(d.startedAt, from, pxPerHour), width: xOf(d.endedAt, from, pxPerHour) - xOf(d.startedAt, from, pxPerHour),
                    top: 0, bottom: 0,
                    background: `repeating-linear-gradient(45deg, ${token.colorFillSecondary}, ${token.colorFillSecondary} 6px, transparent 6px, transparent 12px)`,
                  }} />
                </Tooltip>
              ))}
              {board.works.filter((w) => w.equipmentId === eq.equipmentId && w.actualStartAt).map((w) => {
                const expectedEnd = dayjs(w.actualStartAt).add(w.expectedDurationMin ?? 0, 'minute')
                const end = w.actualEndAt ? dayjs(w.actualEndAt) : now.isAfter(expectedEnd) ? now : expectedEnd
                const left = xOf(w.actualStartAt!, from, pxPerHour)
                return (
                  <Tooltip key={w.productionWorkId} title={`실적 ${w.lotNo} · ${dayjs(w.actualStartAt).format('MM-DD HH:mm')} ~ ${w.actualEndAt ? dayjs(w.actualEndAt).format('MM-DD HH:mm') : '진행 중'}`}>
                    <div style={{
                      position: 'absolute', left, width: Math.max(xOf(end, from, pxPerHour) - left, 3), top: 2, height: WORK_LANE - 4,
                      borderRadius: 3, background: w.status === 'COMPLETED' ? token.colorTextQuaternary : token.colorSuccess,
                    }} />
                  </Tooltip>
                )
              })}
              {board.blocks.filter((b) => b.equipmentId === eq.equipmentId).map((b) => {
                const left = xOf(b.plannedStartAt, from, pxPerHour)
                const blockWidth = Math.max(xOf(b.plannedEndAt, from, pxPerHour) - left, 4)
                const movable = canEdit && isMovable(b)
                const topPriority = Math.max(...b.items.map((i) => i.priority), 0)
                const first = b.items[0]
                return (
                  <Tooltip key={b.productionScheduleId} mouseEnterDelay={0.4} title={
                    <div>
                      <div>{b.plannedLotNo} · {b.unitProcessName}</div>
                      <div>{dayjs(b.plannedStartAt).format('MM-DD HH:mm')} ~ {dayjs(b.plannedEndAt).format('MM-DD HH:mm')} ({b.plannedDurationMin}분)</div>
                      {b.items.map((i) => <div key={i.salesOrderItemId}>{i.orderItemNo} {i.customerName} {i.partName} {i.plannedQty}</div>)}
                    </div>
                  }>
                    <div data-block-id={b.productionScheduleId}
                      draggable={movable}
                      onDragStart={(e) => { dragState.current = { kind: 'block', block: b }; e.dataTransfer.effectAllowed = 'move' }}
                      onDragEnd={() => { dragState.current = null }}
                      onDragOver={(e) => { if (canEdit) e.preventDefault() }}
                      onDrop={(e) => handleBlockDrop(e, b)}
                      onClick={() => props.onSelect(b)}
                      style={{
                        position: 'absolute', left, width: blockWidth, top: WORK_LANE, bottom: 4,
                        background: statusColor(b.status), color: '#fff', borderRadius: 4, padding: '2px 6px',
                        fontSize: 12, lineHeight: '16px', overflow: 'hidden', whiteSpace: 'nowrap', cursor: movable ? 'grab' : 'pointer',
                        borderLeft: `4px solid ${priorityColor(topPriority) ?? 'transparent'}`,
                        outline: b.productionScheduleId === selectedId ? `2px solid ${token.colorText}` : b.isTimeLocked ? '2px dashed #fff' : undefined,
                        outlineOffset: -2, boxShadow: token.boxShadowTertiary,
                      }}>
                      <div>{b.isTimeLocked && <LockOutlined style={{ marginRight: 4 }} />}{b.plannedLotNo}</div>
                      <div style={{ opacity: 0.9 }}>
                        {first ? `${first.partName ?? ''}${b.items.length > 1 ? ` 외 ${b.items.length - 1}` : ''}` : ''} · {b.plannedQty}
                      </div>
                    </div>
                  </Tooltip>
                )
              })}
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}
