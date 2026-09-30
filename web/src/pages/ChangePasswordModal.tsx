import { App, Form, Input, Modal } from 'antd'
import { useState } from 'react'
import { api, fieldErrors } from '../api/client'

interface Values {
  currentPassword: string
  newPassword: string
  confirmPassword: string
}

export default function ChangePasswordModal({ open, onClose }: { open: boolean; onClose: () => void }) {
  const [form] = Form.useForm<Values>()
  const { message } = App.useApp()
  const [saving, setSaving] = useState(false)

  const save = async () => {
    const values = await form.validateFields()
    setSaving(true)
    try {
      await api('/api/auth/change-password', {
        method: 'POST',
        body: { currentPassword: values.currentPassword, newPassword: values.newPassword },
      })
      message.success('비밀번호를 변경했습니다.')
      form.resetFields()
      onClose()
    } catch (e) {
      const errors = fieldErrors<Values>(e)
      if (errors.length > 0) form.setFields(errors)
      else message.error(e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal title="비밀번호 변경" open={open} onOk={save} confirmLoading={saving} okText="변경"
      onCancel={() => { form.resetFields(); onClose() }} destroyOnHidden>
      <Form form={form} layout="vertical" requiredMark={false}>
        <Form.Item name="currentPassword" label="현재 비밀번호" rules={[{ required: true, message: '현재 비밀번호를 입력하세요.' }]}>
          <Input.Password autoComplete="current-password" />
        </Form.Item>
        {/* 최소 길이는 서버 설정(auth.password_min_length)으로 검증 */}
        <Form.Item name="newPassword" label="새 비밀번호" rules={[{ required: true, message: '새 비밀번호를 입력하세요.' }]}>
          <Input.Password autoComplete="new-password" />
        </Form.Item>
        <Form.Item name="confirmPassword" label="새 비밀번호 확인" dependencies={['newPassword']}
          rules={[
            { required: true, message: '새 비밀번호를 한 번 더 입력하세요.' },
            ({ getFieldValue }) => ({
              validator: (_, v) => (!v || v === getFieldValue('newPassword')
                ? Promise.resolve()
                : Promise.reject(new Error('새 비밀번호가 일치하지 않습니다.'))),
            }),
          ]}>
          <Input.Password autoComplete="new-password" />
        </Form.Item>
      </Form>
    </Modal>
  )
}
