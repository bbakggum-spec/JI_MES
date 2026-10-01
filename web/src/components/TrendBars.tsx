import { Space, theme, Typography } from 'antd'
import { useMemo, useState } from 'react'

/**
 * 일별 막대 그래프 (SVG) — 한 축(같은 단위)만. 단위가 다른 값은 차트를 나눈다 (이중 축 금지).
 * 계열 색은 dataviz 기준 팔레트의 고정 순서(1 파랑, 2 주황, 3 청록 — 인접 쌍 색각 검증), 라이트·다크 각각의 단계.
 * 표시: 얇은 막대 + 위쪽 4px 둥근 끝, 막대 사이 2px 간격, 옅은 격자 3줄, 날 단위 마우스 올림 = 값 풍선.
 */
export interface TrendSeries {
  key: string
  name: string
  /** 팔레트 순서 (1부터) */
  slot: 1 | 2 | 3
}

export interface TrendPoint {
  key: string
  label: string
  values: Record<string, number | null | undefined>
}

const SLOTS = {
  light: { 1: '#2a78d6', 2: '#eb6834', 3: '#1baf7a' },
  dark: { 1: '#3987e5', 2: '#d95926', 3: '#199e70' },
} as const

const HEIGHT = 160
const PAD = { top: 8, right: 8, bottom: 22, left: 56 }

function isDark(color: string) {
  const m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})/i.exec(color)
  if (!m) return false
  const [r, g, b] = [m[1], m[2], m[3]].map((h) => parseInt(h, 16) / 255)
  return 0.2126 * r + 0.7152 * g + 0.0722 * b < 0.5
}

/** 축 눈금: 0 과 위쪽 끝을 보기 좋은 수로 */
function niceMax(v: number) {
  if (v <= 0) return 1
  const p = 10 ** Math.floor(Math.log10(v))
  const n = v / p
  return (n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10) * p
}

export default function TrendBars({ points, series, format, compact }: {
  points: TrendPoint[]
  series: TrendSeries[]
  format: (n: number) => string
  /** 축 눈금용 짧은 표기 (예: 만 원) */
  compact?: (n: number) => string
}) {
  const { token } = theme.useToken()
  const palette = isDark(token.colorBgContainer) ? SLOTS.dark : SLOTS.light
  const [hover, setHover] = useState<number | null>(null)
  const [width, setWidth] = useState(600)
  const max = useMemo(() => niceMax(Math.max(0, ...points.flatMap((p) => series.map((s) => p.values[s.key] ?? 0)))), [points, series])

  const plotW = Math.max(width - PAD.left - PAD.right, 10)
  const plotH = HEIGHT - PAD.top - PAD.bottom
  const band = plotW / Math.max(points.length, 1)
  const gap = 2
  const barW = Math.max(Math.min((band * 0.7 - gap * (series.length - 1)) / series.length, 18), 2)
  const groupW = barW * series.length + gap * (series.length - 1)
  const y = (v: number) => PAD.top + plotH - (v / max) * plotH
  const labelEvery = Math.ceil(points.length / Math.max(Math.floor(plotW / 48), 1))
  // 표기가 같아지는 눈금(정수 건수의 0.5 등)은 하나만
  const ticks = [0, max / 2, max].filter((v, i, all) => all.findIndex((x) => (compact ?? format)(x) === (compact ?? format)(v)) === i)

  return (
    <div style={{ position: 'relative' }} ref={(el) => { if (el && el.clientWidth !== width) setWidth(el.clientWidth) }}>
      {series.length > 1 && (
        <Space size="middle" style={{ marginBottom: 4 }}>
          {series.map((s) => (
            <Space key={s.key} size={4}>
              <span style={{ display: 'inline-block', width: 10, height: 10, borderRadius: 2, background: palette[s.slot] }} />
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>{s.name}</Typography.Text>
            </Space>
          ))}
        </Space>
      )}
      <svg width={width} height={HEIGHT} role="img" aria-label={series.map((s) => s.name).join(', ')} style={{ display: 'block' }}
        onMouseLeave={() => setHover(null)}>
        {ticks.map((t) => (
          <g key={t}>
            <line x1={PAD.left} x2={width - PAD.right} y1={y(t)} y2={y(t)} stroke={token.colorBorderSecondary} strokeWidth={1} />
            <text x={PAD.left - 6} y={y(t)} dy="0.32em" textAnchor="end" fontSize={11} fill={token.colorTextTertiary}>{(compact ?? format)(t)}</text>
          </g>
        ))}
        {points.map((p, i) => {
          const x0 = PAD.left + band * i + (band - groupW) / 2
          return (
            <g key={p.key}>
              {hover === i && <rect x={PAD.left + band * i} y={PAD.top} width={band} height={plotH} fill={token.colorFillQuaternary} />}
              {series.map((s, j) => {
                const v = p.values[s.key] ?? 0
                if (v <= 0) return null
                const h = Math.max(plotH * (v / max), 1)
                const r = Math.min(4, barW / 2, h)
                const x = x0 + j * (barW + gap)
                const top = PAD.top + plotH - h
                // 위쪽만 둥글게 (기준선 쪽은 각지게)
                return <path key={s.key} fill={palette[s.slot]}
                  d={`M${x},${top + h} V${top + r} Q${x},${top} ${x + r},${top} H${x + barW - r} Q${x + barW},${top} ${x + barW},${top + r} V${top + h} Z`} />
              })}
              {i % labelEvery === 0 && (
                <text x={PAD.left + band * i + band / 2} y={HEIGHT - 6} textAnchor="middle" fontSize={11} fill={token.colorTextTertiary}>{p.label}</text>
              )}
              {/* 날짜 칸 전체가 마우스 영역 (막대보다 넓게) */}
              <rect x={PAD.left + band * i} y={PAD.top} width={band} height={plotH} fill="transparent" onMouseEnter={() => setHover(i)} />
            </g>
          )
        })}
        <line x1={PAD.left} x2={width - PAD.right} y1={y(0)} y2={y(0)} stroke={token.colorBorder} strokeWidth={1} />
      </svg>
      {hover !== null && points[hover] && (
        <div style={{
          position: 'absolute', top: 24, left: Math.min(PAD.left + band * hover + band / 2 + 8, width - 170), pointerEvents: 'none',
          background: token.colorBgElevated, boxShadow: token.boxShadowSecondary, borderRadius: 6, padding: '6px 10px', fontSize: 12, minWidth: 140,
        }}>
          <div style={{ fontWeight: 600, marginBottom: 2, color: token.colorText }}>{points[hover].label}</div>
          {series.map((s) => (
            <div key={s.key} style={{ display: 'flex', justifyContent: 'space-between', gap: 12, color: token.colorText }}>
              <span><span style={{ display: 'inline-block', width: 8, height: 8, borderRadius: 2, background: palette[s.slot], marginRight: 4 }} />{s.name}</span>
              <span>{format(points[hover].values[s.key] ?? 0)}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
