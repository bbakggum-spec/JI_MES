import { Button, Modal, Space } from 'antd'
import type { ReactNode } from 'react'

/** 폭 이름 → px. 화면 폭보다 크면 좌우 여백만 남기고 줄인다 */
const WIDTHS = { default: 560, large: 820 } as const

/**
 * 추가·수정 입력 창 (가운데 뜨는 별도 창). 목록 화면 위에 떠서 입력하고, 바깥을 눌러도 닫히지 않는다 (입력 보호).
 * `extra`(저장 버튼 등)는 아래쪽 버튼 줄에 [닫기] 와 함께 놓인다. 탭 안에 자체 저장 버튼이 있으면 생략.
 */
export default function EditorWindow({ title, size = 'large', extra, onClose, destroyOnHidden, children }: {
  title: ReactNode
  size?: keyof typeof WIDTHS | number
  extra?: ReactNode
  onClose: () => void
  destroyOnHidden?: boolean
  children: ReactNode
}) {
  const width = typeof size === 'number' ? size : WIDTHS[size]
  return (
    <Modal open title={title} onCancel={onClose} centered maskClosable={false} destroyOnHidden={destroyOnHidden}
      width={`min(${width}px, calc(100vw - 32px))`}
      styles={{ body: { maxHeight: 'calc(100vh - 200px)', overflowY: 'auto', overflowX: 'hidden', paddingRight: 4 } }}
      footer={extra ? <Space><Button onClick={onClose}>닫기</Button>{extra}</Space> : null}>
      {children}
    </Modal>
  )
}
