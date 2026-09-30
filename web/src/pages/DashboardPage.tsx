import { useQuery } from '@tanstack/react-query'
import { Badge, Card, Col, Descriptions, Empty, Row, Tag, Typography } from 'antd'
import { useEffect, useState } from 'react'
import { api } from '../api/client'
import { useAuth } from '../auth/useAuth'
import { useClientSettings } from '../hooks/useClientSettings'
import { queryKeys } from '../queryKeys'
import { useRealtimeStatus } from '../realtime/realtime'
import { dayjs, workDate, workDayRange } from '../utils/workDate'

interface Health {
  status: string
  database: string
}

/** 7단계(조회·대시보드)에서 채울 영역 — 지금은 자리만 잡는다 */
const upcomingPanels = [
  { title: 'LOT 진행 현황', description: '설비별 배정 · 투입 · 완료' },
  { title: '설비 가동', description: '가동 · 비가동 · 보전' },
  { title: '검사 · 부적합', description: '판정 대기 · 부적합 처리' },
  { title: '출하 · 마감', description: '출하 예정 · 미마감 전표' },
]

export default function DashboardPage() {
  const { me } = useAuth()
  const realtime = useRealtimeStatus()
  const { dayStartTime, refreshIntervalMs } = useClientSettings()
  const now = useNow(refreshIntervalMs)

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
                <Typography.Text type="secondary">
                  {range[0].format('MM-DD HH:mm')} ~ {range[1].format('MM-DD HH:mm')}
                </Typography.Text>
              </>
            ) : <Typography.Text type="secondary">불러오는 중…</Typography.Text>}
          </Card>
        </Col>
        <Col xs={24} md={8}>
          <Card title="시스템 상태" size="small">
            <Descriptions column={1} size="small">
              <Descriptions.Item label="API · DB">
                {health.isError
                  ? <Badge status="error" text="연결 안 됨" />
                  : health.data
                    ? <Badge status={health.data.database === 'ok' ? 'success' : 'warning'} text={health.data.database === 'ok' ? '정상' : '점검 필요'} />
                    : <Badge status="processing" text="확인 중" />}
              </Descriptions.Item>
              <Descriptions.Item label="실시간 알림">
                <Badge status={realtime === 'connected' ? 'success' : 'warning'} text={realtime === 'connected' ? '연결됨' : '끊김 (주기 조회)'} />
              </Descriptions.Item>
              <Descriptions.Item label="확인 시각">
                {health.dataUpdatedAt ? dayjs(health.dataUpdatedAt).format('HH:mm:ss') : '-'}
              </Descriptions.Item>
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
        {upcomingPanels.map((p) => (
          <Col key={p.title} xs={24} md={12} xl={6}>
            <Card title={p.title} size="small">
              <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={`${p.description} — 준비 중`} />
            </Card>
          </Col>
        ))}
      </Row>
    </>
  )
}

/** 작업일 표시가 자정·작업일 시작 시각을 넘기면 바뀌도록 주기적으로 현재 시각 갱신 */
function useNow(intervalMs: number | undefined) {
  const [now, setNow] = useState(() => dayjs())
  useEffect(() => {
    if (!intervalMs) return
    const id = window.setInterval(() => setNow(dayjs()), intervalMs)
    return () => window.clearInterval(id)
  }, [intervalMs])
  return now
}
