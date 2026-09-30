import { useQuery } from '@tanstack/react-query'
import { Button, Col, DatePicker, Form, Input, InputNumber, Row, Table, Tag, Typography } from 'antd'
import type { Dayjs } from 'dayjs'
import { useState } from 'react'
import { api } from '../../api/client'
import type { AuditLog, Page } from '../../api/types'
import { queryKeys } from '../../queryKeys'
import { dayjs } from '../../utils/workDate'

interface Filter {
  tableName?: string
  recordId?: number
  range?: [Dayjs, Dayjs]
}

const actionColors: Record<string, string> = {
  CREATE: 'green', UPDATE: 'blue', DELETE: 'red', STATUS_CHANGE: 'purple', CLOSE: 'orange', REOPEN: 'gold',
}

export default function AuditLogsPage() {
  const [filter, setFilter] = useState<Filter>({})
  const [page, setPage] = useState({ page: 1, pageSize: 50 })

  const params = new URLSearchParams()
  if (filter.tableName) params.set('tableName', filter.tableName.trim())
  if (filter.recordId) params.set('recordId', String(filter.recordId))
  if (filter.range) {
    params.set('from', filter.range[0].startOf('day').format('YYYY-MM-DDTHH:mm:ss'))
    params.set('to', filter.range[1].add(1, 'day').startOf('day').format('YYYY-MM-DDTHH:mm:ss'))
  }
  params.set('page', String(page.page))
  params.set('pageSize', String(page.pageSize))
  const qs = params.toString()

  const { data, isFetching } = useQuery({
    queryKey: [...queryKeys.auditLogs, qs],
    queryFn: ({ signal }) => api<Page<AuditLog>>(`/api/audit-logs?${qs}`, { signal }),
    placeholderData: (prev) => prev,
  })

  return (
    <>
      <Typography.Title level={4} style={{ marginTop: 0 }}>변경 이력</Typography.Title>
      <Form<Filter> layout="inline" style={{ marginBottom: 16, rowGap: 8 }}
        onFinish={(v) => { setFilter(v); setPage((p) => ({ ...p, page: 1 })) }}>
        <Form.Item name="tableName" label="테이블"><Input allowClear placeholder="예: system_setting" /></Form.Item>
        <Form.Item name="recordId" label="ID"><InputNumber min={1} precision={0} /></Form.Item>
        <Form.Item name="range" label="기간"><DatePicker.RangePicker /></Form.Item>
        <Button type="primary" htmlType="submit">조회</Button>
      </Form>
      <Table<AuditLog> rowKey="auditLogId" size="middle" loading={isFetching} scroll={{ x: 1000 }} dataSource={data?.items ?? []}
        pagination={{
          current: page.page, pageSize: page.pageSize, total: data?.total ?? 0, showSizeChanger: true,
          showTotal: (t) => `${t.toLocaleString()}건`,
          onChange: (p, s) => setPage({ page: p, pageSize: s }),
        }}
        expandable={{
          expandedRowRender: (r) => (
            <Row gutter={16}>
              <Col span={12}><Typography.Text strong>변경 전</Typography.Text><JsonBlock json={r.beforeJson} /></Col>
              <Col span={12}><Typography.Text strong>변경 후</Typography.Text><JsonBlock json={r.afterJson} /></Col>
            </Row>
          ),
        }}
        columns={[
          { title: '일시', dataIndex: 'occurredAt', width: 170, render: (v: string) => dayjs(v).format('YYYY-MM-DD HH:mm:ss') },
          { title: '사용자', dataIndex: 'userName', width: 120, render: (v: string | null) => v ?? <Typography.Text type="secondary">시스템</Typography.Text> },
          { title: '동작', dataIndex: 'actionType', width: 130, render: (v: string) => <Tag color={actionColors[v]}>{v}</Tag> },
          { title: '테이블', dataIndex: 'tableName', width: 160 },
          { title: 'ID', dataIndex: 'recordId', width: 80 },
          { title: '사유', dataIndex: 'reason', ellipsis: true },
          { title: 'IP', dataIndex: 'clientIp', width: 130 },
        ]} />
    </>
  )
}

function JsonBlock({ json }: { json: string | null }) {
  if (!json) return <div><Typography.Text type="secondary">-</Typography.Text></div>
  let text = json
  try { text = JSON.stringify(JSON.parse(json), null, 2) } catch { /* 원문 표시 */ }
  return <pre style={{ fontSize: 12, margin: '4px 0 0', whiteSpace: 'pre-wrap', wordBreak: 'break-all' }}>{text}</pre>
}
