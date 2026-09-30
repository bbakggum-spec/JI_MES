import { describe, expect, it } from 'vitest'
import { COMMON, cellKey, newStep, toConditions, toValues, type GridLayout } from './conditionGridModel'

const item = (id: number) => ({ conditionItemId: id, conditionItemName: `항목${id}`, unitCode: null, valueType: 'NUMBER' })

describe('조건 입력표 변환', () => {
  it('스텝 순서를 바꾸면 값이 따라가고, 저장 시 스텝 번호는 현재 열 순서', () => {
    const a = newStep('승온')
    const b = newStep('침탄')
    const layout: GridLayout = { steps: [b, a], items: [item(1), item(2)] }   // 순서 바꿈
    const values = { [cellKey(a.key, 1)]: '850', [cellKey(b.key, 1)]: ' 920 ', [cellKey(COMMON, 2)]: '메모', [cellKey(b.key, 2)]: '' }
    expect(toConditions(layout, values)).toEqual([
      { stepNo: 1, conditionItemId: 1, conditionValue: '920' },
      { stepNo: 2, conditionItemId: 1, conditionValue: '850' },
      { stepNo: null, conditionItemId: 2, conditionValue: '메모' },
    ])
  })

  it('지운 스텝·행의 값은 저장하지 않는다', () => {
    const a = newStep('승온')
    const gone = newStep('강온')
    const values = { [cellKey(a.key, 1)]: '1', [cellKey(gone.key, 1)]: '2', [cellKey(a.key, 9)]: '3' }
    expect(toConditions({ steps: [a], items: [item(1)] }, values)).toEqual([{ stepNo: 1, conditionItemId: 1, conditionValue: '1' }])
  })

  it('불러오기 → 저장이 같은 조건을 돌려준다', () => {
    const steps = [newStep('승온'), newStep('침탄')]
    const conditions = [{ stepNo: 2, conditionItemId: 1, conditionValue: '920' }, { stepNo: null, conditionItemId: 1, conditionValue: '공통' }]
    const values = toValues(steps, conditions)
    expect(toConditions({ steps, items: [item(1)] }, values)).toEqual([conditions[1], conditions[0]])
  })
})
