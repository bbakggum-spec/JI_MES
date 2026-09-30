import type { BacklogRow, BoardBlock } from './scheduleTypes'

/** 끌고 있는 대상 — dragover 중에는 dataTransfer 를 읽을 수 없어 모듈 변수로 공유 */
export type DragPayload = { kind: 'backlog'; row: BacklogRow } | { kind: 'block'; block: BoardBlock }
export const dragState: { current: DragPayload | null } = { current: null }

export type BacklogDropTarget = { beforeBlockId: number | null } | { onBlock: BoardBlock }
