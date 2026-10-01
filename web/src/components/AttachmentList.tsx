import { DeleteOutlined, DownloadOutlined, UploadOutlined } from '@ant-design/icons'
import { App, Button, Empty, Image, Input, Popconfirm, Select, Space, Table, Tag, Typography, Upload } from 'antd'
import dayjs from 'dayjs'
import { useState } from 'react'
import { ApiError, api, apiFile, saveFile } from '../api/client'
import { useCommonCodes } from '../hooks/useCommonCodes'

export interface Attachment {
  attachmentId: number
  attachmentKind: string
  fileName: string
  contentType: string | null
  fileSize: number | null
  caption: string | null
  createdAt: string
}

/**
 * 공통 첨부 목록 + 올리기 (attachment — 품목 도면, 보전 사진, 교정 성적서 …).
 * 첨부 종류 = 공통코드 ATTACHMENT_KIND 중 attr.owner 가 이 소유 테이블이거나 owner 가 없는 것 (서버도 같은 규칙으로 검사).
 * @param basePath 예: `/api/parts/12/attachments`
 */
export default function AttachmentList({ basePath, owner, defaultKind, attachments, canEdit, onChanged }: {
  basePath: string
  owner: string
  defaultKind: string
  attachments: Attachment[]
  canEdit: boolean
  onChanged: () => void
}) {
  const codes = useCommonCodes()
  const { message } = App.useApp()
  const [kind, setKind] = useState(defaultKind)
  const [caption, setCaption] = useState('')
  const kinds = codes.options('ATTACHMENT_KIND').filter((c) => !c.attrJson?.includes('"owner"') || c.attrJson.includes(`"owner":"${owner}"`))
  const url = (id: number) => `${basePath}/${id}`

  return (
    <>
      {canEdit && (
        <Space style={{ marginBottom: 12 }} wrap>
          <Select value={kind} onChange={setKind} style={{ width: 130 }} options={kinds.map((k) => ({ value: k.code, label: k.codeName }))} />
          <Input placeholder="설명 (선택)" value={caption} onChange={(e) => setCaption(e.target.value)} style={{ width: 200 }} maxLength={255} />
          <Upload showUploadList={false} customRequest={async ({ file }) => {
            const form = new FormData()
            form.append('file', file as Blob)
            form.append('kind', kind)
            form.append('caption', caption)
            try {
              await api(basePath, { method: 'POST', body: form })
              message.success('첨부했습니다.')
              setCaption('')
              onChanged()
            } catch (e) {
              message.error(e instanceof ApiError && e.errors ? Object.values(e.errors).flat().join(' ') : e instanceof Error ? e.message : String(e))
            }
          }}>
            <Button icon={<UploadOutlined />}>파일 첨부</Button>
          </Upload>
        </Space>
      )}
      {attachments.length === 0 ? <Empty description="첨부가 없습니다." image={Empty.PRESENTED_IMAGE_SIMPLE} /> : (
        <Table size="small" rowKey="attachmentId" pagination={false} dataSource={attachments} columns={[
          {
            title: '', width: 70,
            render: (_: unknown, a) => a.contentType?.startsWith('image/') ? <Image src={url(a.attachmentId)} width={56} alt={a.fileName} /> : null,
          },
          { title: '종류', dataIndex: 'attachmentKind', width: 110, render: (k: string) => <Tag>{codes.name('ATTACHMENT_KIND', k)}</Tag> },
          { title: '파일', dataIndex: 'fileName', render: (n: string, a) => <>{n}{a.caption && <div><Typography.Text type="secondary">{a.caption}</Typography.Text></div>}</> },
          { title: '크기', dataIndex: 'fileSize', width: 90, align: 'right', render: (s: number | null) => s != null && `${Math.ceil(s / 1024).toLocaleString()} KB` },
          { title: '등록', dataIndex: 'createdAt', width: 110, render: (d: string) => dayjs(d).format('YYYY-MM-DD') },
          {
            title: '', width: 90,
            render: (_: unknown, a) => (
              <Space>
                <Button size="small" icon={<DownloadOutlined />} aria-label="내려받기" onClick={async () => saveFile(await apiFile(url(a.attachmentId)))} />
                {canEdit && (
                  <Popconfirm title="첨부를 삭제할까요?" onConfirm={async () => { await api(url(a.attachmentId), { method: 'DELETE' }); onChanged() }}>
                    <Button size="small" danger icon={<DeleteOutlined />} aria-label="삭제" />
                  </Popconfirm>
                )}
              </Space>
            ),
          },
        ]} />
      )}
    </>
  )
}
