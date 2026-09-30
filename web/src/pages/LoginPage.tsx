import { LockOutlined, UserOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Form, Input, Typography, theme } from 'antd'
import { useState } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router'
import { useAuth } from '../auth/useAuth'

interface LoginForm {
  loginId: string
  password: string
}

export default function LoginPage() {
  const { me, login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const { token } = theme.useToken()
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  const from = (location.state as { from?: string } | null)?.from ?? '/'
  if (me) return <Navigate to={from} replace />

  const submit = async (values: LoginForm) => {
    setSubmitting(true)
    setError(null)
    try {
      await login(values.loginId, values.password)
      navigate(from, { replace: true })
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div style={{
      minHeight: '100vh', display: 'flex', alignItems: 'center', justifyContent: 'center',
      background: token.colorBgLayout, padding: 16,
    }}>
      <Card style={{ width: '100%', maxWidth: 380 }}>
        <div style={{ textAlign: 'center', marginBottom: 24 }}>
          <img src="/favicon.svg" alt="" width={40} height={40} />
          <Typography.Title level={3} style={{ margin: '8px 0 0' }}>JI MES</Typography.Title>
          <Typography.Text type="secondary">열처리 생산관리</Typography.Text>
        </div>
        {error && <Alert type="error" title={error} showIcon style={{ marginBottom: 16 }} />}
        <Form<LoginForm> layout="vertical" onFinish={submit} requiredMark={false} autoComplete="on">
          <Form.Item name="loginId" label="아이디" rules={[{ required: true, message: '아이디를 입력하세요.' }]}>
            <Input prefix={<UserOutlined />} autoFocus autoComplete="username" size="large" />
          </Form.Item>
          <Form.Item name="password" label="비밀번호" rules={[{ required: true, message: '비밀번호를 입력하세요.' }]}>
            <Input.Password prefix={<LockOutlined />} autoComplete="current-password" size="large" />
          </Form.Item>
          <Button type="primary" htmlType="submit" block size="large" loading={submitting}>로그인</Button>
        </Form>
      </Card>
    </div>
  )
}
