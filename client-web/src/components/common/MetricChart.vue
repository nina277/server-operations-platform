<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { formatDateTime } from '@/utils/format'

export interface ChartPoint {
  /** 収集時刻(ISO文字列)。 */
  at: string
  value: number
}

const props = withDefaults(
  defineProps<{
    title: string
    points: ChartPoint[]
    /** 値に付ける単位。軸の目盛りと読み上げに使う。 */
    unit?: string
    /** 下限を0に固定するか。件数のように0が意味を持つ系列で使う。 */
    zeroBased?: boolean
  }>(),
  { unit: '', zeroBased: true },
)

const { t, locale } = useI18n()

/**
 * 描画は外部ライブラリを使わずSVGで行う。
 * グラフ1つのために依存を増やすと、その分だけ脆弱性検査の対象が広がるため。
 */
const WIDTH = 640
const HEIGHT = 180
const PADDING = { top: 12, right: 12, bottom: 28, left: 48 }

/** 古い順に並べ替える。APIは新しい順で返すため、そのままでは時間が逆に流れる。 */
const ordered = computed(() =>
  [...props.points].sort((a, b) => new Date(a.at).getTime() - new Date(b.at).getTime()),
)

const bounds = computed(() => {
  const values = ordered.value.map((p) => p.value)
  const rawMax = values.length > 0 ? Math.max(...values) : 0
  const rawMin = props.zeroBased ? 0 : values.length > 0 ? Math.min(...values) : 0

  // 全点が同じ値だと高さ0になり線が消えるため、幅を持たせる
  const max = rawMax === rawMin ? rawMin + 1 : rawMax
  return { min: rawMin, max }
})

const plotWidth = WIDTH - PADDING.left - PADDING.right
const plotHeight = HEIGHT - PADDING.top - PADDING.bottom

function x(index: number): number {
  const count = ordered.value.length
  if (count <= 1) {
    return PADDING.left + plotWidth / 2
  }
  return PADDING.left + (plotWidth * index) / (count - 1)
}

function y(value: number): number {
  const { min, max } = bounds.value
  const ratio = (value - min) / (max - min)
  return PADDING.top + plotHeight * (1 - ratio)
}

const linePath = computed(() =>
  ordered.value.map((p, i) => `${i === 0 ? 'M' : 'L'}${x(i)},${y(p.value)}`).join(' '),
)

/** 目盛りは上下と中央の3本にとどめる。細かくすると小さい画面で潰れる。 */
const ticks = computed(() => {
  const { min, max } = bounds.value
  return [max, (max + min) / 2, min].map((value) => ({ value, y: y(value) }))
})

/** 最後の点。ビルド時のlib設定では配列の at() を使えないため添字で取る。 */
const latest = computed(() => {
  const points = ordered.value
  return points.length > 0 ? points[points.length - 1]! : null
})

function formatValue(value: number): string {
  // 小数が続くと読みにくいため、大きい値は整数に丸める
  const rounded = Math.abs(value) >= 10 ? Math.round(value) : Math.round(value * 10) / 10
  return props.unit.length > 0 ? `${rounded} ${props.unit}` : String(rounded)
}

/**
 * 図として読めない利用者向けの説明。
 * SVGだけでは内容が伝わらないため、最新値と範囲を文章でも持たせる。
 */
const summary = computed(() => {
  if (ordered.value.length === 0) {
    return t('common.empty')
  }

  return t('chart.summary', {
    count: ordered.value.length,
    latest: formatValue(latest.value?.value ?? 0),
    min: formatValue(Math.min(...ordered.value.map((p) => p.value))),
    max: formatValue(Math.max(...ordered.value.map((p) => p.value))),
  })
})
</script>

<template>
  <figure class="chart">
    <figcaption class="chart__title">{{ title }}</figcaption>

    <p v-if="ordered.length === 0" class="chart__empty">{{ t('common.empty') }}</p>

    <template v-else>
      <div class="chart__scroll">
        <svg
          :viewBox="`0 0 ${WIDTH} ${HEIGHT}`"
          class="chart__svg"
          role="img"
          :aria-label="`${title}: ${summary}`"
        >
          <!-- 目盛り線と値 -->
          <g class="chart__ticks">
            <template v-for="tick in ticks" :key="tick.value">
              <line
                :x1="PADDING.left"
                :y1="tick.y"
                :x2="WIDTH - PADDING.right"
                :y2="tick.y"
                class="chart__gridline"
              />
              <text :x="PADDING.left - 6" :y="tick.y + 4" text-anchor="end" class="chart__label">
                {{ formatValue(tick.value) }}
              </text>
            </template>
          </g>

          <path :d="linePath" class="chart__line" />

          <!-- 点が1つだけだと線が引けないため、印を出す -->
          <circle
            v-if="ordered.length === 1"
            :cx="x(0)"
            :cy="y(ordered[0]!.value)"
            r="3"
            class="chart__point"
          />

          <text
            :x="PADDING.left"
            :y="HEIGHT - 8"
            text-anchor="start"
            class="chart__label chart__label--time"
          >
            {{ formatDateTime(ordered[0]!.at, locale) }}
          </text>
          <text
            v-if="ordered.length > 1"
            :x="WIDTH - PADDING.right"
            :y="HEIGHT - 8"
            text-anchor="end"
            class="chart__label chart__label--time"
          >
            {{ formatDateTime(latest!.at, locale) }}
          </text>
        </svg>
      </div>

      <p class="chart__summary" data-testid="chart-summary">{{ summary }}</p>
    </template>
  </figure>
</template>

<style scoped>
.chart {
  margin: var(--spacing-md) 0;
}

.chart__title {
  font-weight: 600;
  margin-bottom: var(--spacing-xs);
}

/* 幅の狭い画面ではグラフだけを横に流し、本文は折り返させる */
.chart__scroll {
  overflow-x: auto;
}

.chart__svg {
  display: block;
  width: 100%;
  min-width: 20rem;
  height: auto;
}

.chart__gridline {
  stroke: var(--color-border);
  stroke-width: 1;
}

.chart__line {
  fill: none;
  stroke: var(--color-accent, #2563eb);
  stroke-width: 2;
  stroke-linejoin: round;
  stroke-linecap: round;
}

.chart__point {
  fill: var(--color-accent, #2563eb);
}

.chart__label {
  font-size: 0.75rem;
  fill: var(--color-text-muted);
}

.chart__summary,
.chart__empty {
  font-size: 0.875rem;
  color: var(--color-text-muted);
}
</style>
