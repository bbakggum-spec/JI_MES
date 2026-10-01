import { useQuery } from '@tanstack/react-query'
import { Badge, Card, Col, Descriptions, Empty, Progress, Row, Space, Statistic, Switch, Table, Tag, Tooltip, Typography } from 'antd'
import { useState } from 'react'
import { api } from '../api/client'
import { useAuth, useCan } from '../auth/useAuth'
import TrendBars from '../components/TrendBars'
import { useClientSettings } from '../hooks/useClientSettings'
import { useCommonCodes } from '../hooks/useCommonCodes'
import { useNow } from '../hooks/useNow'
import { queryKeys } from '../queryKeys'
import { useRealtimeStatus } from '../realtime/realtime'
import { dayjs, workDate, workDayRange } from '../utils/workDate'

interface Health { status: string; database: string }

interface EquipmentPanel {
  now: string
  equipment: {
    equipmentId: number; equipmentName: string; equipmentTypeName: string | null; runningLotNo: string | null; runningUnitProcessName: string | null
    runningStartAt: string | null; runningExpectedMin: number | null; nextLotNo: string | null; nextPlannedStartAt: string | null
    allocatedCount: number; completedCount: number; isDown: boolean
  }[]
}

interface QualityPanel {
  openDefects: { status: string; count: number; qty: number }[]
  inspections: { total: number; completed: number; failed: number; pending: number }
  recent: { id: number; defectDate: string; orderItemNo: string; partName: string | null; lotNo: string | null; defectQty: number; status: string }[]
}

interface SalesPanel {
  intake: { todayCount: number; todayAmount: number; monthCount: number; monthAmount: number; notInput: number } | null
  shipment: { todayCount: number; todayAmount: number; monthCount: number; monthAmount: number; unclosedCount: number; unclosedAmount: number } | null
}

interface TrendPoint { day: string; intakeAmount: number | null; intakeCount: number | null; shipmentAmount: number | null; shipmentCount: number | null; defectCount: number | null; defectQty: number | null }

const won = (n: number) => `${n.toLocaleString()}원`
const manwon = (n: number) => (n >= 10000 ? `${(n / 10000).toLocaleString(undefined, { maximumFractionDigits: 0 })}만` : n.toLocaleString())

/** 대시보드 (설계 §19.4·§24.3) — 패널마다 해당 업무 메뉴 읽기 권한이 있을 때만 보인다 */
export default function DashboardPage() {
  const { me } = useAuth()
  const realtime = useRealtimeStatus()
  const { dayStartTime, refreshIntervalMs } = useClientSettings()
  const now = useNow(refreshIntervalMs)
  const canWork = useCan('production.work', 'read')
  const canDefect = useCan('quality.defect', 'read')
  const canOrder = useCan('sales.order', 'read')
  const canShipment = useCan('sales.shipment', 'read')

  // 실시간이 끊겨도 설정 주기(schedule.refresh_interval_sec)로 다시 조회 (설계 §15.4 H2)
  const health = useQuery({
    queryKey: queryKeys.health,
    queryFn: ({ signal }) => api<Health>('/api/health', { signal }),
    refetchInterval: refreshIntervalMs,
    retry: false,
  })
  const today = dayStartTime ? workDate(now, dayStartTime) : null
  const range = today && dayStartTime ? workDayRange(today, dayStartTime) : null

  return (
    <>
      <Typography.Title level={4} style={{ marginTop: 0 }}>대시보드</Typography.Title>
      <Row gutter={[16, 16]}>
        <Col xs={24} md={8}>
          <Card title="작업일" size="small">
            {today && range ? (
              <>
                <Typography.Title level={3} style={{ margin: 0 }}>{today.format('YYYY-MM-DD (ddd)')}</Typography.Title>
                <Typography.Text type="secondary">{range[0].format('MM-DD HH:mm')} ~ {range[1].format('MM-DD HH:mm')}</Typography.Text>
              </>
            ) : <Typography.Text type="secondary">불러오는 중…</Typography.Text>}
          </Card>
        </Col>
        <Col xs={24} md={8}>
          <Card title="시스템 상태" size="small">
            <Descriptions column={1} size="small">
              <Descriptions.Item label="API · DB">
                {health.isError ? <Badge status="error" text="연결 안 됨" />
                  : health.data ? <Badge status={health.data.database === 'ok' ? 'success' : 'warning'} text={health.data.database === 'ok' ? '정상' : '점검 필요'} />
                    : <Badge status="processing" text="확인 중" />}
              </Descriptions.Item>
              <Descriptions.Item label="실시간 알림">
                <Badge status={realtime === 'connected' ? 'success' : 'warning'} text={realtime === 'connected' ? '연결됨' : '끊김 (주기 조회)'} />
              </Descriptions.Item>
              <Descriptions.Item label="확인 시각">{health.dataUpdatedAt ? dayjs(health.dataUpdatedAt).format('HH:mm:ss') : '-'}</Descriptions.Item>
            </Descriptions>
          </Card>
        </Col>
        <Col xs={24} md={8}>
          <Card title="내 계정" size="small">
            <Descriptions column={1} size="small">
              <Descriptions.Item label="이름">{me?.userName}</Descriptions.Item>
              <Descriptions.Item label="아이디">{me?.loginId}</Descriptions.Item>
              <Descriptions.Item label="역할">{me?.roles.map((r) => <Tag key={r}>{r}</Tag>)}</Descriptions.Item>
            </Descriptions>
          </Card>
        </Col>
        {(canOrder || canShipment) && <Col xs={24}><SalesCard refreshMs={refreshIntervalMs} /></Col>}
        {canWork && <Col xs={24} xl={16}><EquipmentCard refreshMs={refreshIntervalMs} /></Col>}
        {canDefect && <Col xs={24} xl={8}><QualityCard refreshMs={refreshIntervalMs} /></Col>}
        {(canOrder || canShipment || canDefect) && <Col xs={24}><TrendCard canAmount={canOrder || canShipment} canDefect={canDefect} canOrder={canOrder} canShipment={canShipment} /></Col>}
      </Row>
    </>
  )
}

function SalesCard({ refreshMs }: { refreshMs?: number }) {
  const q = useQuery({ queryKey: [...queryKeys.dashboard, 'sales'], queryFn: ({ signal }) => api<SalesPanel>('/api/dashboard/sales', { signal }), refetchInterval: refreshMs })
  const d = q.data
  return (
    <Card title="영업" size="small" loading={q.isPending}>
      <Row gutter={[16, 8]}>
        {d?.intake && <>
          <Col xs={12} md={8} xl={4}><Statistic styles={{ content: { fontSize: 20 } }} title={`오늘 입고 (${d.intake.todayCount}건)`} value={d.intake.todayAmount} suffix="원" /></Col>
          <Col xs={12} md={8} xl={4}><Statistic styles={{ content: { fontSize: 20 } }} title={`이번 달 입고 (${d.intake.monthCount}건)`} value={d.intake.monthAmount} suffix="원" /></Col>
          <Col xs={12} md={8} xl={4}><Statistic styles={{ content: { fontSize: 20 } }} title="미투입 입고" value={d.intake.notInput} suffix="건" /></Col>
        </>}
        {d?.shipment && <>
          <Col xs={12} md={8} xl={4}><Statistic styles={{ content: { fontSize: 20 } }} title={`오늘 출하 (${d.shipment.todayCount}건)`} value={d.shipment.todayAmount} suffix="원" /></Col>
          <Col xs={12} md={8} xl={4}><Statistic styles={{ content: { fontSize: 20 } }} title={`이번 달 출하 (${d.shipment.monthCount}건)`} value={d.shipment.monthAmount} suffix="원" /></Col>
          <Col xs={12} md={8} xl={4}><Statistic styles={{ content: { fontSize: 20 } }} title={`미마감 전표 (${d.shipment.unclosedCount}건)`} value={d.shipment.unclosedAmount} suffix="원" /></Col>
        </>}
      </Row>
    </Card>
  )
}

function EquipmentCard({ refreshMs }: { refreshMs?: number }) {
  const q = useQuery({ queryKey: [...queryKeys.dashboard, 'equipment'], queryFn: ({ signal }) => api<EquipmentPanel>('/api/dashboard/equipment', { signal }), refetchInterval: refreshMs })
  const now = q.data ? dayjs(q.data.now) : dayjs()
  return (
    <Card title="설비 가동" size="small" loading={q.isPending}>
      <Row gutter={[8, 8]}>
        {(q.data?.equipment ?? []).map((e) => {
          const elapsed = e.runningStartAt ? now.diff(dayjs(e.runningStartAt), 'minute') : 0
          const pct = e.runningExpectedMin ? Math.min(100, Math.round((elapsed / e.runningExpectedMin) * 100)) : 0
          const late = e.runningExpectedMin != null && elapsed > e.runningExpectedMin
          return (
            <Col key={e.equipmentId} xs={24} sm={12} lg={8}>
              <Card size="small" styles={{ body: { padding: 10 } }}>
                <Space style={{ width: '100%', justifyContent: 'space-between' }}>
                  <Typography.Text strong>{e.equipmentName}</Typography.Text>
                  {e.isDown ? <Badge status="error" text="비가동" /> : e.runningLotNo ? <Badge status="processing" text="가동" /> : <Badge status="default" text="대기" />}
                </Space>
                {e.runningLotNo ? (
                  <>
                    <div><Typography.Text>{e.runningLotNo}</Typography.Text> <Typography.Text type="secondary">{e.runningUnitProcessName}</Typography.Text></div>
                    <Tooltip title={`${dayjs(e.runningStartAt).format('HH:mm')} 시작 · ${elapsed}분 경과${e.runningExpectedMin ? ` / 예상 ${e.runningExpectedMin}분` : ''}`}>
                      <Progress percent={pct} size="small" status={late ? 'exception' : 'active'} format={() => (late ? '지연' : `${pct}%`)} />
                    </Tooltip>
                  </>
                ) : <Typography.Text type="secondary" style={{ display: 'block', margin: '4px 0' }}>진행 중 LOT 없음</Typography.Text>}
                <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                  다음 {e.nextLotNo ?? '-'}{e.nextPlannedStartAt ? ` (${dayjs(e.nextPlannedStartAt).format('HH:mm')})` : ''} · 배정 {e.allocatedCount} · 완료 {e.completedCount}
                </Typography.Text>
              </Card>
            </Col>
          )
        })}
      </Row>
    </Card>
  )
}

function QualityCard({ refreshMs }: { refreshMs?: number }) {
  const codes = useCommonCodes()
  const q = useQuery({ queryKey: [...queryKeys.dashboard, 'quality'], queryFn: ({ signal }) => api<QualityPanel>('/api/dashboard/quality', { signal }), refetchInterval: refreshMs })
  const d = q.data
  return (
    <Card title="품질" size="small" loading={q.isPending}>
      {d && <>
        <Row gutter={8}>
          {['OPEN', 'DECIDED', 'REWORKING'].map((s) => {
            const x = d.openDefects.find((o) => o.status === s)
            return <Col span={8} key={s}><Statistic title={codes.name('DEFECT_STATUS', s)} value={x?.count ?? 0} suffix="건" /></Col>
          })}
        </Row>
        <Typography.Text type="secondary">
          오늘 검사 {d.inspections.total}건 · 확정 {d.inspections.completed} · 불합격 <Typography.Text type={d.inspections.failed > 0 ? 'danger' : 'secondary'}>{d.inspections.failed}</Typography.Text> · 미확정 전체 {d.inspections.pending}
        </Typography.Text>
        <div style={{ marginTop: 8 }}>
          {d.recent.length === 0 && <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="미처리 부적합 없음" />}
          {d.recent.map((r) => (
            <div key={r.id} style={{ padding: '4px 0' }}>
              <Space size={4} wrap>
                <Tag color={codes.attr<{ color?: string }>('DEFECT_STATUS', r.status)?.color}>{codes.name('DEFECT_STATUS', r.status)}</Tag>
                <Typography.Text>{r.partName}</Typography.Text>
                <Typography.Text type="secondary" style={{ fontSize: 12 }}>{r.lotNo ?? r.orderItemNo} · {r.defectQty.toLocaleString()} · {dayjs(r.defectDate).format('MM-DD')}</Typography.Text>
              </Space>
            </div>
          ))}
        </div>
      </>}
    </Card>
  )
}

function TrendCard({ canAmount, canDefect, canOrder, canShipment }: { canAmount: boolean; canDefect: boolean; canOrder: boolean; canShipment: boolean }) {
  const [asTable, setAsTable] = useState(false)
  const q = useQuery({ queryKey: [...queryKeys.dashboard, 'trend'], queryFn: ({ signal }) => api<TrendPoint[]>('/api/dashboard/trend', { signal }) })
  const rows = q.data ?? []
  const points = rows.map((p) => ({
    key: p.day, label: dayjs(p.day).format('MM-DD'),
    values: { intake: p.intakeAmount, shipment: p.shipmentAmount, defect: p.defectCount },
  }))
  const amountSeries = [
    ...(canOrder ? [{ key: 'intake', name: '입고 금액', slot: 1 as const }] : []),
    ...(canShipment ? [{ key: 'shipment', name: '출하 금액', slot: 2 as const }] : []),
  ]
  return (
    <Card title={`일별 추이 (최근 ${rows.length}일)`} size="small" loading={q.isPending}
      extra={<Space size={4}><Switch size="small" checked={asTable} onChange={setAsTable} />표로 보기</Space>}>
      {asTable ? (
        <Table size="small" rowKey="day" pagination={false} dataSource={[...rows].reverse()} scroll={{ y: 300 }} columns={[
          { title: '일자', dataIndex: 'day', render: (d: string) => dayjs(d).format('YYYY-MM-DD (ddd)') },
          ...(canOrder ? [{ title: '입고 금액', dataIndex: 'intakeAmount', align: 'right' as const, render: (n: number | null) => n != null && won(n) },
            { title: '입고 건수', dataIndex: 'intakeCount', align: 'right' as const }] : []),
          ...(canShipment ? [{ title: '출하 금액', dataIndex: 'shipmentAmount', align: 'right' as const, render: (n: number | null) => n != null && won(n) },
            { title: '출하 건수', dataIndex: 'shipmentCount', align: 'right' as const }] : []),
          ...(canDefect ? [{ title: '부적합 건수', dataIndex: 'defectCount', align: 'right' as const }, { title: '부적합 수량', dataIndex: 'defectQty', align: 'right' as const }] : []),
        ]} />
      ) : (
        <Row gutter={16}>
          {canAmount && amountSeries.length > 0 && (
            <Col xs={24} xl={canDefect ? 16 : 24}>
              <Typography.Text strong>{amountSeries.length === 1 ? amountSeries[0].name : '입고 · 출하 금액'} (원)</Typography.Text>
              <TrendBars points={points} series={amountSeries} format={won} compact={manwon} />
            </Col>
          )}
          {canDefect && (
            <Col xs={24} xl={canAmount ? 8 : 24}>
              <Typography.Text strong>부적합 건수</Typography.Text>
              <TrendBars points={points} series={[{ key: 'defect', name: '부적합 건수', slot: 3 }]} format={(n) => `${n}건`} compact={(n) => String(Math.round(n))} />
            </Col>
          )}
        </Row>
      )}
    </Card>
  )
}
