import { CloseOutlined, CopyOutlined, LeftOutlined, RightOutlined } from '@ant-design/icons'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Alert, App, Button, Card, Col, DatePicker, Empty, Input, Popconfirm, Row, Space, Tag, theme, Tooltip, Typography } from 'antd'
import dayjs, { type Dayjs } from 'dayjs'
import { useMemo, useState, type DragEvent } from 'react'
import { ApiError, api } from '../../api/client'
import { useCan } from '../../auth/useAuth'
import { queryKeys } from '../../queryKeys'
import type { PageProps } from '../registry'

interface Shift { workShiftId: number; workShiftName: string; startTime: string; endTime: string; isNextDayEnd: boolean }
interface BoardEquipment { equipmentId: number; equipmentName: string; equipmentTypeName: string | null; runningLotNo: string | null }
interface Employee { employeeId: number; employeeName: string; teamName: string | null; departmentName: string | null }
interface Assignment { workerAssignmentId: number; workShiftId: number; equipmentId: number; employeeId: number; employeeName: string; assignmentType: 'PRIMARY' | 'SUPPORT'; remark: string | null }
interface Board { workDate: string; shifts: Shift[]; equipment: BoardEquipment[]; employees: Employee[]; assignments: Assignment[] }

const DATE = 'YYYY-MM-DD'
/** 끌기 데이터 종류 — 사원 카드(새 배치) / 배치 칩(옮기기) */
const DRAG_EMPLOYEE = 'application/x-jimes-employee'
const DRAG_ASSIGNMENT = 'application/x-jimes-assignment'
const hhmm = (t: string) => t.slice(0, 5)
const errorText = (e: unknown) => (e instanceof ApiError && e.errors ? Object.values(e.errors).flat().join(' ') : e instanceof Error ? e.message : String(e))

/**
 * 작업자 주·야 배치 (설계 §26.2 C, §28.5) — 사람이 직접 배치.
 * 왼쪽 작업자를 설비 × 교대 칸에 끌어다 놓는다 (태블릿: 작업자를 누르고 칸을 누름). 칩을 다른 칸으로 끌면 이동,
 * 왼쪽 목록으로 끌어 놓거나 × 로 해제, 두 번 누르면 주·보조 전환. 같은 사람을 같은 교대 여러 설비에 둘 수 있다.
 */
export default function WorkerAssignmentPage({ menuKey }: PageProps) {
  const { token } = theme.useToken()
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const canCreate = useCan(menuKey, 'create')
  const canUpdate = useCan(menuKey, 'update')
  const canDelete = useCan(menuKey, 'delete')
  const [date, setDate] = useState<Dayjs>(dayjs())
  const [search, setSearch] = useState('')
  const [picked, setPicked] = useState<number | null>(null)   // 누른 작업자 (태블릿 배치)
  const [over, setOver] = useState<string | null>(null)        // 끌고 있는 칸
  const [busy, setBusy] = useState(false)
  const key = [...queryKeys.equipment, 'assignments', date.format(DATE)]
  const board = useQuery({
    queryKey: key,
    queryFn: ({ signal }) => api<Board>(`/api/worker-assignments/board?workDate=${date.format(DATE)}`, { signal }),
  })
  const data = board.data
  const refresh = () => void queryClient.invalidateQueries({ queryKey: [...queryKeys.equipment, 'assignments'] })

  // 칸별 배치, 작업자별 배치 수 (교대별)
  const cells = useMemo(() => {
    const m = new Map<string, Assignment[]>()
    for (const a of data?.assignments ?? []) {
      const k = `${a.equipmentId}:${a.workShiftId}`
      m.set(k, [...(m.get(k) ?? []), a])
    }
    return m
  }, [data])
  const countOf = (employeeId: number, shiftId: number) =>
    (data?.assignments ?? []).filter((a) => a.employeeId === employeeId && a.workShiftId === shiftId).length
  const employees = (data?.employees ?? []).filter((e) =>
    !search || e.employeeName.includes(search) || (e.teamName ?? '').includes(search) || (e.departmentName ?? '').includes(search))
  const teams = [...new Set(employees.map((e) => e.teamName ?? ''))]

  const run = async (f: () => Promise<unknown>) => {
    setBusy(true)
    try {
      await f()
      refresh()
    } catch (e) {
      message.error(errorText(e))
    } finally {
      setBusy(false)
    }
  }
  const assign = (employeeId: number, equipmentId: number, workShiftId: number) => {
    if (!canCreate) return
    void run(() => api('/api/worker-assignments', { method: 'POST', body: { workDate: date.format(DATE), workShiftId, equipmentId, employeeId } }))
  }
  const move = (a: Assignment, equipmentId: number, workShiftId: number) => {
    if (!canUpdate || (a.equipmentId === equipmentId && a.workShiftId === workShiftId)) return
    void run(() => api(`/api/worker-assignments/${a.workerAssignmentId}`, { method: 'PUT', body: { equipmentId, workShiftId } }))
  }
  const toggle = (a: Assignment) => {
    if (!canUpdate) return
    void run(() => api(`/api/worker-assignments/${a.workerAssignmentId}`, { method: 'PUT', body: { assignmentType: a.assignmentType === 'PRIMARY' ? 'SUPPORT' : 'PRIMARY' } }))
  }
  const remove = (a: Assignment) => {
    if (!canDelete) return
    void run(() => api(`/api/worker-assignments/${a.workerAssignmentId}`, { method: 'DELETE' }))
  }
  const copyPrevious = () => void run(async () => {
    const r = await api<{ copied: number }>('/api/worker-assignments/copy', {
      method: 'POST', body: { fromDate: date.subtract(1, 'day').format(DATE), toDate: date.format(DATE) },
    })
    message.info(r.copied > 0 ? `전날 배치 ${r.copied}건을 복사했습니다.` : '복사할 배치가 없습니다 (이미 같은 배치가 있거나 전날 배치 없음).')
  })

  const assignmentById = (id: number) => data?.assignments.find((a) => a.workerAssignmentId === id)
  const onCellDrop = (e: DragEvent, equipmentId: number, shiftId: number) => {
    e.preventDefault()
    setOver(null)
    const emp = e.dataTransfer.getData(DRAG_EMPLOYEE)
    const asg = e.dataTransfer.getData(DRAG_ASSIGNMENT)
    if (emp) assign(Number(emp), equipmentId, shiftId)
    else if (asg) { const a = assignmentById(Number(asg)); if (a) move(a, equipmentId, shiftId) }
  }
  const onPoolDrop = (e: DragEvent) => {
    e.preventDefault()
    const asg = e.dataTransfer.getData(DRAG_ASSIGNMENT)
    const a = asg ? assignmentById(Number(asg)) : undefined
    if (a) remove(a)
  }

  const chip = (a: Assignment) => (
    <span key={a.workerAssignmentId} draggable={canUpdate || canDelete}
      onDragStart={(e) => { e.dataTransfer.setData(DRAG_ASSIGNMENT, String(a.workerAssignmentId)); e.dataTransfer.effectAllowed = 'move' }}
      onDoubleClick={() => toggle(a)}
      title={canUpdate ? '두 번 누르면 주·보조 전환, 끌어서 이동' : undefined}
      style={{
        display: 'inline-flex', alignItems: 'center', gap: 4, padding: '1px 6px', margin: 2, borderRadius: 4, cursor: canUpdate ? 'grab' : 'default',
        background: a.assignmentType === 'PRIMARY' ? token.colorPrimaryBg : token.colorFillQuaternary,
        border: `1px ${a.assignmentType === 'PRIMARY' ? 'solid' : 'dashed'} ${a.assignmentType === 'PRIMARY' ? token.colorPrimaryBorder : token.colorBorder}`,
        fontSize: 13, userSelect: 'none',
      }}>
      {a.employeeName}{a.assignmentType === 'SUPPORT' && <Typography.Text type="secondary" style={{ fontSize: 11 }}>보조</Typography.Text>}
      {canDelete && <CloseOutlined aria-label={`${a.employeeName} 배치 해제`} style={{ fontSize: 10, cursor: 'pointer', color: token.colorTextTertiary }}
        onClick={(e) => { e.stopPropagation(); remove(a) }} />}
    </span>
  )

  return (
    <>
      <Space style={{ width: '100%', justifyContent: 'space-between', marginBottom: 8 }} wrap>
        <Space>
          <Typography.Title level={4} style={{ margin: 0 }}>작업자 배치</Typography.Title>
          {/* 누른 작업자 안내 — 표 위에 끼우면 줄이 밀려 칸을 잘못 누르게 되므로 제목 옆에 */}
          {picked !== null && (
            <Tag color="processing" closable onClose={() => setPicked(null)}>
              {data?.employees.find((e) => e.employeeId === picked)?.employeeName} — 배치할 칸을 누르세요
            </Tag>
          )}
        </Space>
        <Space wrap>
          <Space.Compact>
            <Button icon={<LeftOutlined />} aria-label="전날" onClick={() => setDate(date.subtract(1, 'day'))} />
            <DatePicker value={date} allowClear={false} onChange={(d) => d && setDate(d)} format={`${DATE} (dd)`} />
            <Button icon={<RightOutlined />} aria-label="다음날" onClick={() => setDate(date.add(1, 'day'))} />
          </Space.Compact>
          <Button onClick={() => setDate(dayjs())}>오늘</Button>
          {canCreate && (
            <Popconfirm title="전날 배치를 이 날로 복사합니다" description="이미 있는 배치는 그대로 두고 없는 칸만 더합니다." onConfirm={copyPrevious}>
              <Button icon={<CopyOutlined />} loading={busy}>전날 배치 복사</Button>
            </Popconfirm>
          )}
        </Space>
      </Space>

      {data && data.shifts.length === 0 && (
        <Alert type="warning" showIcon style={{ marginBottom: 12 }} message="사용 중인 교대가 없습니다."
          description="기준정보 > 교대 에서 주간·야간 교대를 등록하세요." />
      )}

      <Row gutter={[12, 12]}>
        <Col xs={24} lg={6} xxl={5}>
          <Card size="small" title="작업자" styles={{ body: { maxHeight: 'calc(100vh - 260px)', overflowY: 'auto' } }}
            extra={<Typography.Text type="secondary" style={{ fontSize: 12 }}>끌어다 놓기</Typography.Text>}
            onDragOver={(e) => { if (e.dataTransfer.types.includes(DRAG_ASSIGNMENT)) e.preventDefault() }} onDrop={onPoolDrop}>
            <Input.Search allowClear placeholder="이름·조·부서" size="small" style={{ marginBottom: 8 }} onChange={(e) => setSearch(e.target.value.trim())} />
            {employees.length === 0 && <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="사원 기준정보에서 '작업자 배정 대상'을 켜세요." />}
            {teams.map((team) => (
              <div key={team} style={{ marginBottom: 8 }}>
                {teams.length > 1 && <Typography.Text type="secondary" style={{ fontSize: 12 }}>{team || '조 없음'}</Typography.Text>}
                {employees.filter((e) => (e.teamName ?? '') === team).map((e) => {
                  const counts = (data?.shifts ?? []).map((s) => countOf(e.employeeId, s.workShiftId))
                  return (
                    <div key={e.employeeId} draggable={canCreate}
                      onDragStart={(ev) => { ev.dataTransfer.setData(DRAG_EMPLOYEE, String(e.employeeId)); ev.dataTransfer.effectAllowed = 'copy' }}
                      onClick={() => setPicked(picked === e.employeeId ? null : e.employeeId)}
                      style={{
                        display: 'flex', justifyContent: 'space-between', alignItems: 'center', padding: '4px 8px', margin: '2px 0', borderRadius: 6,
                        cursor: canCreate ? 'grab' : 'default', userSelect: 'none',
                        border: `1px solid ${picked === e.employeeId ? token.colorPrimary : token.colorBorderSecondary}`,
                        background: picked === e.employeeId ? token.colorPrimaryBg : token.colorBgContainer,
                      }}>
                      <span>{e.employeeName}{e.departmentName && <Typography.Text type="secondary" style={{ fontSize: 12 }}> {e.departmentName}</Typography.Text>}</span>
                      <Space size={2}>
                        {data?.shifts.map((s, i) => (
                          <Tooltip key={s.workShiftId} title={`${s.workShiftName} 배치 ${counts[i]}곳`}>
                            <Tag style={{ margin: 0, fontSize: 11, lineHeight: '16px', opacity: counts[i] ? 1 : 0.35 }} color={counts[i] ? 'blue' : undefined}>
                              {s.workShiftName.slice(0, 1)}{counts[i] || ''}
                            </Tag>
                          </Tooltip>
                        ))}
                      </Space>
                    </div>
                  )
                })}
              </div>
            ))}
          </Card>
        </Col>
        <Col xs={24} lg={18} xxl={19}>
          <div style={{ overflowX: 'auto', border: `1px solid ${token.colorBorderSecondary}`, borderRadius: token.borderRadiusLG, background: token.colorBgContainer }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', minWidth: 160 + (data?.shifts.length ?? 1) * 220 }}>
              <thead>
                <tr style={{ background: token.colorFillAlter }}>
                  <th style={{ textAlign: 'left', padding: '6px 10px', width: 170, borderBottom: `1px solid ${token.colorBorderSecondary}` }}>설비</th>
                  {data?.shifts.map((s) => (
                    <th key={s.workShiftId} style={{ textAlign: 'left', padding: '6px 10px', borderBottom: `1px solid ${token.colorBorderSecondary}`, borderLeft: `1px solid ${token.colorBorderSecondary}` }}>
                      {s.workShiftName} <Typography.Text type="secondary" style={{ fontWeight: 'normal', fontSize: 12 }}>
                        {hhmm(s.startTime)}~{s.isNextDayEnd ? '익일 ' : ''}{hhmm(s.endTime)}
                      </Typography.Text>
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {data?.equipment.map((eq) => (
                  <tr key={eq.equipmentId}>
                    <td style={{ padding: '6px 10px', borderBottom: `1px solid ${token.colorBorderSecondary}`, verticalAlign: 'top' }}>
                      <div>{eq.equipmentName}</div>
                      <Space size={4} wrap>
                        {eq.equipmentTypeName && <Typography.Text type="secondary" style={{ fontSize: 12 }}>{eq.equipmentTypeName}</Typography.Text>}
                        {eq.runningLotNo && <Tag color="processing" style={{ fontSize: 11 }}>진행 {eq.runningLotNo}</Tag>}
                      </Space>
                    </td>
                    {data.shifts.map((s) => {
                      const k = `${eq.equipmentId}:${s.workShiftId}`
                      const list = cells.get(k) ?? []
                      return (
                        <td key={s.workShiftId}
                          onDragOver={(e) => { if (e.dataTransfer.types.includes(DRAG_EMPLOYEE) || e.dataTransfer.types.includes(DRAG_ASSIGNMENT)) { e.preventDefault(); setOver(k) } }}
                          onDragLeave={() => setOver((o) => (o === k ? null : o))}
                          onDrop={(e) => onCellDrop(e, eq.equipmentId, s.workShiftId)}
                          onClick={() => { if (picked !== null) assign(picked, eq.equipmentId, s.workShiftId) }}
                          style={{
                            padding: 4, minHeight: 40, verticalAlign: 'top', cursor: picked !== null ? 'copy' : 'default',
                            borderBottom: `1px solid ${token.colorBorderSecondary}`, borderLeft: `1px solid ${token.colorBorderSecondary}`,
                            background: over === k ? token.colorPrimaryBgHover : list.length === 0 ? token.colorFillQuaternary : undefined,
                          }}>
                          {list.length === 0 ? <Typography.Text type="secondary" style={{ fontSize: 12, padding: 4 }}>-</Typography.Text> : list.map(chip)}
                        </td>
                      )
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
            {data && data.equipment.length === 0 && <Empty style={{ padding: 24 }} description="사용 중인 설비가 없습니다." />}
          </div>
          <Typography.Paragraph type="secondary" style={{ fontSize: 12, marginTop: 8 }}>
            칩을 다른 칸으로 끌면 이동, 왼쪽 목록으로 끌거나 × 를 누르면 해제, 두 번 누르면 주·보조 전환 (점선 = 보조). 다른 사용자가 바꾸면 바로 반영됩니다.
          </Typography.Paragraph>
        </Col>
      </Row>
    </>
  )
}
