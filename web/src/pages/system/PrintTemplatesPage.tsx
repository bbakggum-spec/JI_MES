import { DownloadOutlined, FileExcelOutlined, PlusOutlined, PrinterOutlined, UploadOutlined } from '@ant-design/icons'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import {
  Alert, App, Button, Card, Col, Drawer, Empty, Form, Input, InputNumber, Menu, Modal, Radio, Row, Space, Spin, Table,
  Tag, Tooltip, Typography, Upload,
} from 'antd'
import dayjs from 'dayjs'
import { useState } from 'react'
import { ApiError, api, apiFile, fieldErrors, saveFile } from '../../api/client'
import { useCan } from '../../auth/useAuth'
import { queryKeys } from '../../queryKeys'
import { parseList, type PrintPurpose, type PrintTemplate, type PrintTemplateVersion, type UploadResult } from './printTypes'

const MENU_KEY = 'system.print'
const printKey = [...queryKeys.print] as const

/** 출력 양식 관리 (설계 §5.3, §15.2~15.3) — 용도별 EXCEL 양식 등록·버전, FIXED 옵션, 기본 양식, 발행 확인 */
export default function PrintTemplatesPage() {
  const canCreate = useCan(MENU_KEY, 'create')
  const canUpdate = useCan(MENU_KEY, 'update')
  const { message } = App.useApp()
  const queryClient = useQueryClient()
  const [selected, setSelected] = useState<string | null>(null)
  const [creating, setCreating] = useState(false)
  const [uploading, setUploading] = useState<PrintTemplate | null>(null)
  const [editingOptions, setEditingOptions] = useState<PrintTemplate | null>(null)
  const [versionsOf, setVersionsOf] = useState<PrintTemplate | null>(null)
  const [issuing, setIssuing] = useState<PrintTemplate | null>(null)
  const [showFields, setShowFields] = useState(false)

  const purposes = useQuery({
    queryKey: [...printKey, 'purposes'],
    queryFn: ({ signal }) => api<PrintPurpose[]>('/api/print/purposes', { signal }),
  })
  const purpose = purposes.data?.find((p) => p.purposeCode === selected) ?? purposes.data?.[0]
  const templates = useQuery({
    queryKey: [...printKey, 'templates', purpose?.purposeCode],
    queryFn: ({ signal }) => api<PrintTemplate[]>(`/api/print/templates?purposeCode=${purpose!.purposeCode}`, { signal }),
    enabled: purpose !== undefined,
  })
  const refresh = () => queryClient.invalidateQueries({ queryKey: printKey })

  const download = async (path: string) => {
    try {
      saveFile(await apiFile(path))
    } catch (e) {
      message.error(e instanceof Error ? e.message : String(e))
    }
  }

  const setDefault = async (t: PrintTemplate) => {
    try {
      await api(`/api/print/templates/${t.printTemplateId}/default`, { method: 'PUT' })
      message.success(`'${t.printTemplateName}' 을 기본 양식으로 지정했습니다.`)
      await refresh()
    } catch (e) {
      message.error(e instanceof Error ? e.message : String(e))
    }
  }

  return (
    <>
      <Typography.Title level={4} style={{ marginTop: 0 }}>출력 양식</Typography.Title>
      <Row gutter={16}>
        <Col xs={24} md={7} lg={6}>
          <Card size="small" title="출력 용도" styles={{ body: { padding: 0 } }}>
            <Spin spinning={purposes.isPending}>
              <Menu mode="inline" style={{ borderInlineEnd: 0 }} selectedKeys={purpose ? [purpose.purposeCode] : []}
                onClick={(e) => setSelected(e.key)}
                items={(purposes.data ?? []).map((p) => ({
                  key: p.purposeCode,
                  label: (
                    <Space size={4}>
                      {p.purposeName}
                      {!p.dataSourceImplemented && <Tooltip title="데이터 공급원 개발 전 — 발행 불가"><Tag>준비 중</Tag></Tooltip>}
                    </Space>
                  ),
                }))} />
            </Spin>
          </Card>
        </Col>
        <Col xs={24} md={17} lg={18}>
          {purpose && (
            <Card size="small" title={`${purpose.purposeName} (${purpose.purposeCode})`}
              extra={
                <Space wrap>
                  <Button icon={<FileExcelOutlined />} onClick={() => void download(`/api/print/purposes/${purpose.purposeCode}/sample-template`)}>
                    샘플 양식
                  </Button>
                  <Button onClick={() => setShowFields(true)}>치환자 목록</Button>
                  {canCreate && <Button type="primary" icon={<PlusOutlined />} onClick={() => setCreating(true)}>엑셀 양식 추가</Button>}
                </Space>
              }>
              <Typography.Paragraph type="secondary">
                데이터 공급원: {purpose.dataSourceName} · 양식 선택 순서: 품목+업체 → 품목 공통 → 용도 기본 양식
              </Typography.Paragraph>
              <Table<PrintTemplate> rowKey="printTemplateId" size="middle" pagination={false} loading={templates.isFetching}
                dataSource={templates.data ?? []} scroll={{ x: 900 }}
                columns={[
                  {
                    title: '양식', dataIndex: 'printTemplateName',
                    render: (v: string, t) => (
                      <Space wrap>
                        {v}
                        {t.isDefault && <Tag color="blue">용도 기본</Tag>}
                        {t.partLinkCount > 0 && <Tag>품목 {t.partLinkCount}</Tag>}
                      </Space>
                    ),
                  },
                  {
                    title: '방식', dataIndex: 'templateKind', width: 120,
                    render: (k: string, t) => <Tag color={k === 'FIXED' ? 'purple' : 'green'}>{k === 'FIXED' ? `고정 ${t.rendererKey}` : '엑셀'}</Tag>,
                  },
                  { title: '발행', dataIndex: 'outputFormat', width: 70 },
                  {
                    title: '현재 버전', width: 220,
                    render: (_: unknown, t) => t.currentVersionNo
                      ? (
                        <Space wrap size={4}>
                          <span>v{t.currentVersionNo}</span>
                          <Typography.Text type="secondary" style={{ fontSize: 12 }}>{t.uploadedAt && dayjs(t.uploadedAt).format('YYYY-MM-DD HH:mm')}</Typography.Text>
                          {parseList(t.unknownPlaceholdersJson).length > 0 && (
                            <Tooltip title={`사전에 없는 치환자: ${parseList(t.unknownPlaceholdersJson).join(', ')}`}>
                              <Tag color="warning">경고 {parseList(t.unknownPlaceholdersJson).length}</Tag>
                            </Tooltip>
                          )}
                        </Space>
                      )
                      : <Tag color="error">파일 없음</Tag>,
                  },
                  {
                    title: '', width: 330, fixed: 'right' as const,
                    render: (_: unknown, t) => (
                      <Space wrap size={4}>
                        {canUpdate && t.templateKind === 'EXCEL' && <Button size="small" icon={<UploadOutlined />} onClick={() => setUploading(t)}>파일 등록</Button>}
                        {canUpdate && t.templateKind === 'FIXED' && <Button size="small" onClick={() => setEditingOptions(t)}>옵션</Button>}
                        <Button size="small" onClick={() => setVersionsOf(t)}>버전</Button>
                        {canUpdate && !t.isDefault && <Button size="small" onClick={() => void setDefault(t)}>기본 지정</Button>}
                        {purpose.dataSourceImplemented && t.currentVersionNo && (
                          <Button size="small" icon={<PrinterOutlined />} onClick={() => setIssuing(t)}>발행 확인</Button>
                        )}
                      </Space>
                    ),
                  },
                ]} />
            </Card>
          )}
        </Col>
      </Row>

      {creating && purpose && <CreateModal purpose={purpose} onClose={() => setCreating(false)} onDone={() => { setCreating(false); void refresh() }} />}
      {uploading && <UploadModal template={uploading} onClose={() => setUploading(null)} onDone={() => void refresh()} />}
      {editingOptions && <OptionsModal template={editingOptions} onClose={() => setEditingOptions(null)} onDone={() => { setEditingOptions(null); void refresh() }} />}
      {issuing && purpose && <IssueModal purpose={purpose} template={issuing} onClose={() => setIssuing(null)} />}
      <VersionsDrawer template={versionsOf} onClose={() => setVersionsOf(null)} onDownload={(id) => void download(`/api/print/versions/${id}/file`)} />
      <Drawer open={showFields} onClose={() => setShowFields(false)} size="large" title={`치환자 — ${purpose?.purposeName ?? ''}`}>
        <Typography.Paragraph type="secondary">
          셀에 {'{{키}}'} 또는 {'{{별칭}}'}. 반복행은 {'{{#목록}} … {{/목록}}'} 로 감싼 행 안에 {'{{항목}}'}. 이미지는 셀 병합 후 {'{{이미지키}}'}.
          {purpose?.dataSourceCode === 'INSPECTION_TARGET' && ' 구 좌표형 키 {{T1_3_P2}}, {{C1_2_Spec}} 도 사용할 수 있습니다.'}
        </Typography.Paragraph>
        <Table size="small" rowKey="fieldKey" pagination={false} dataSource={purpose?.fields ?? []} columns={[
          { title: '치환자', dataIndex: 'fieldKey', render: (k: string) => <Typography.Text code copyable={{ text: `{{${k}}}` }}>{`{{${k}}}`}</Typography.Text> },
          { title: '별칭', dataIndex: 'fieldAlias' },
          { title: '유형', dataIndex: 'fieldType', width: 80 },
          { title: '설명', dataIndex: 'description' },
        ]} />
      </Drawer>
    </>
  )
}

function CreateModal({ purpose, onClose, onDone }: { purpose: PrintPurpose; onClose: () => void; onDone: () => void }) {
  const [form] = Form.useForm<{ printTemplateName: string; outputFormat: 'PDF' | 'XLSX' }>()
  const { message } = App.useApp()
  const [saving, setSaving] = useState(false)
  const save = async () => {
    const v = await form.validateFields()
    setSaving(true)
    try {
      await api('/api/print/templates', { method: 'POST', body: { purposeCode: purpose.purposeCode, ...v } })
      message.success('양식을 만들었습니다. 파일을 등록하세요.')
      onDone()
    } catch (e) {
      const errors = fieldErrors<{ printTemplateName: string; outputFormat: string }>(e)
      if (errors.length > 0) form.setFields(errors)
      else message.error(e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }
  return (
    <Modal open title={`엑셀 양식 추가 — ${purpose.purposeName}`} onCancel={onClose} onOk={save} confirmLoading={saving} okText="만들기" destroyOnHidden>
      <Form form={form} layout="vertical" initialValues={{ outputFormat: 'PDF' }}>
        <Form.Item name="printTemplateName" label="양식 이름" rules={[{ required: true, max: 100 }]}><Input placeholder="예: 한독기어 성적서" /></Form.Item>
        <Form.Item name="outputFormat" label="발행 형식">
          <Radio.Group options={[{ label: 'PDF (서버 변환)', value: 'PDF' }, { label: '엑셀 그대로', value: 'XLSX' }]} />
        </Form.Item>
      </Form>
    </Modal>
  )
}

function UploadModal({ template, onClose, onDone }: { template: PrintTemplate; onClose: () => void; onDone: () => void }) {
  const { message } = App.useApp()
  const [file, setFile] = useState<File | null>(null)
  const [note, setNote] = useState('')
  const [saving, setSaving] = useState(false)
  const [result, setResult] = useState<UploadResult | null>(null)
  const [error, setError] = useState<string | null>(null)

  const upload = async () => {
    if (!file) return
    setSaving(true)
    setError(null)
    try {
      const form = new FormData()
      form.append('file', file)
      form.append('changeNote', note)
      const r = await api<UploadResult>(`/api/print/templates/${template.printTemplateId}/versions`, { method: 'POST', body: form })
      setResult(r)
      message.success(`버전 ${r.versionNo} 을 등록했습니다.`)
      onDone()
    } catch (e) {
      setError(e instanceof ApiError ? (e.errors ? Object.values(e.errors).flat().join(' ') : e.message) : String(e))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal open title={`양식 파일 등록 — ${template.printTemplateName}`} onCancel={onClose} destroyOnHidden
      footer={result ? <Button type="primary" onClick={onClose}>닫기</Button> : [
        <Button key="c" onClick={onClose}>취소</Button>,
        <Button key="ok" type="primary" disabled={!file} loading={saving} onClick={() => void upload()}>등록 (새 버전)</Button>,
      ]}>
      {result ? (
        <>
          <Alert type="success" showIcon title={`버전 ${result.versionNo} 등록 — 치환자 ${result.placeholders.length}개`} />
          {result.unknown.length > 0 && (
            <Alert style={{ marginTop: 12 }} type="warning" showIcon title="사전에 없는 치환자 (출력물에 그대로 보입니다)"
              description={result.unknown.map((k) => `{{${k}}}`).join(', ')} />
          )}
        </>
      ) : (
        <Space orientation="vertical" style={{ width: '100%' }}>
          <Upload.Dragger accept=".xlsx,.xlsm" maxCount={1} beforeUpload={(f) => { setFile(f); return false }}
            onRemove={() => setFile(null)} fileList={file ? [{ uid: '1', name: file.name, status: 'done' }] : []}>
            <p><UploadOutlined style={{ fontSize: 28 }} /></p>
            <p>엑셀 양식(.xlsx, .xlsm)을 끌어 놓거나 클릭</p>
          </Upload.Dragger>
          <Input placeholder="변경 내용 (버전 이력에 남습니다)" value={note} onChange={(e) => setNote(e.target.value)} maxLength={255} />
          {error && <Alert type="error" showIcon title={error} />}
        </Space>
      )}
    </Modal>
  )
}

function OptionsModal({ template, onClose, onDone }: { template: PrintTemplate; onClose: () => void; onDone: () => void }) {
  const { message } = App.useApp()
  const current = useQuery({
    queryKey: [...printKey, 'versions', template.printTemplateId],
    queryFn: ({ signal }) => api<PrintTemplateVersion[]>(`/api/print/templates/${template.printTemplateId}/versions`, { signal }),
  })
  const initial = current.data?.find((v) => v.isCurrent)?.layoutOptionsJson
  const [text, setText] = useState<string | null>(null)
  const [note, setNote] = useState('')
  const [saving, setSaving] = useState(false)
  const value = text ?? (initial ? JSON.stringify(JSON.parse(initial), null, 2) : '')

  let jsonError: string | null = null
  try { JSON.parse(value || '{}') } catch (e) { jsonError = (e as Error).message }

  const save = async () => {
    setSaving(true)
    try {
      const r = await api<UploadResult>(`/api/print/templates/${template.printTemplateId}/options`, {
        method: 'POST', body: { layoutOptionsJson: value, changeNote: note },
      })
      message.success(`버전 ${r.versionNo} 으로 저장했습니다.`)
      onDone()
    } catch (e) {
      message.error(e instanceof ApiError && e.errors ? Object.values(e.errors).flat().join(' ') : e instanceof Error ? e.message : String(e))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal open width={760} title={`레이아웃 옵션 — ${template.printTemplateName}`} onCancel={onClose} destroyOnHidden
      okText="새 버전으로 저장" onOk={() => void save()} confirmLoading={saving} okButtonProps={{ disabled: !!jsonError || !value }}>
      <Typography.Paragraph type="secondary">
        페이지당 행 수·글꼴(설치된 이름, 대체 순서 목록)·보관용 부수·도장 위치 등 (설계 §15.3.1). 없는 키는 코드 기본값으로 동작합니다.
      </Typography.Paragraph>
      <Spin spinning={current.isPending}>
        <Input.TextArea rows={18} value={value} onChange={(e) => setText(e.target.value)} style={{ fontFamily: 'monospace', fontSize: 12 }} />
      </Spin>
      {jsonError && <Alert style={{ marginTop: 8 }} type="error" showIcon title={`JSON 오류: ${jsonError}`} />}
      <Input style={{ marginTop: 8 }} placeholder="변경 내용" value={note} onChange={(e) => setNote(e.target.value)} maxLength={255} />
    </Modal>
  )
}

function VersionsDrawer({ template, onClose, onDownload }: { template: PrintTemplate | null; onClose: () => void; onDownload: (versionId: number) => void }) {
  const versions = useQuery({
    queryKey: [...printKey, 'versions', template?.printTemplateId],
    queryFn: ({ signal }) => api<PrintTemplateVersion[]>(`/api/print/templates/${template!.printTemplateId}/versions`, { signal }),
    enabled: template !== null,
  })
  return (
    <Drawer open={template !== null} onClose={onClose} size="large" title={`버전 이력 — ${template?.printTemplateName ?? ''}`}>
      {versions.data?.length === 0 && <Empty description="등록된 버전이 없습니다." />}
      <Table<PrintTemplateVersion> size="small" rowKey="printTemplateVersionId" pagination={false} loading={versions.isFetching}
        dataSource={versions.data ?? []} columns={[
          { title: '버전', dataIndex: 'versionNo', width: 70, render: (v: number, r) => <Space>v{v}{r.isCurrent && <Tag color="blue">현재</Tag>}</Space> },
          { title: '등록', dataIndex: 'uploadedAt', width: 150, render: (v: string, r) => <>{dayjs(v).format('YYYY-MM-DD HH:mm')}<br />{r.uploadedByName}</> },
          { title: '변경 내용', dataIndex: 'changeNote' },
          {
            title: '경고', width: 120,
            render: (_: unknown, r) => parseList(r.unknownPlaceholdersJson).map((k) => <Tag key={k} color="warning">{k}</Tag>),
          },
          { title: '', width: 60, render: (_: unknown, r) => <Button size="small" icon={<DownloadOutlined />} onClick={() => onDownload(r.printTemplateVersionId)} /> },
        ]} />
    </Drawer>
  )
}

/** 대상 ID 로 이 양식을 직접 지정해 발행 (업무 화면 발행 버튼은 6단계) */
function IssueModal({ purpose, template, onClose }: { purpose: PrintPurpose; template: PrintTemplate; onClose: () => void }) {
  const { message } = App.useApp()
  const [sourceId, setSourceId] = useState<number | null>(null)
  const [busy, setBusy] = useState(false)
  const issue = async () => {
    if (!sourceId) return
    setBusy(true)
    try {
      const file = await apiFile('/api/print/issue', {
        method: 'POST', body: { purposeCode: purpose.purposeCode, sourceId, printTemplateId: template.printTemplateId },
      })
      if (file.blob.type === 'application/pdf') window.open(URL.createObjectURL(file.blob), '_blank')
      else saveFile(file)
      message.success(`발행했습니다 (이력 #${file.headers.get('X-Print-Log-Id')}).`)
    } catch (e) {
      message.error(e instanceof Error ? e.message : String(e))
    } finally {
      setBusy(false)
    }
  }
  return (
    <Modal open title={`발행 확인 — ${template.printTemplateName}`} onCancel={onClose} okText="발행" onOk={() => void issue()}
      confirmLoading={busy} okButtonProps={{ disabled: !sourceId }} destroyOnHidden>
      <Typography.Paragraph type="secondary">{purpose.dataSourceName} ID 를 입력하면 이 양식으로 발행하고 이력을 남깁니다.</Typography.Paragraph>
      <InputNumber autoFocus min={1} precision={0} style={{ width: 200 }} value={sourceId} onChange={setSourceId} placeholder={`${purpose.dataSourceName} ID`} />
    </Modal>
  )
}
