import { Result } from 'antd'

/** 메뉴(권한)는 있지만 화면이 아직 없는 경우 */
export default function PlaceholderPage({ title }: { title: string }) {
  return <Result status="info" title={title} subTitle="이 화면은 아직 준비 중입니다." />
}
