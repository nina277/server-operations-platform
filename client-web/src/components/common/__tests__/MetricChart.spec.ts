import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import MetricChart from '../MetricChart.vue'
import { createTestI18n } from '@/test-utils/i18n'
import type { ChartPoint } from '../MetricChart.vue'

function mountChart(points: ChartPoint[], props: Record<string, unknown> = {}) {
  return mount(MetricChart, {
    props: { title: '応答時間', points, ...props },
    global: { plugins: [createTestI18n()] },
  })
}

describe('MetricChart', () => {
  it('点が無ければグラフを描かない', () => {
    const wrapper = mountChart([])

    expect(wrapper.find('svg').exists()).toBe(false)
    expect(wrapper.text()).toContain('データがありません')
  })

  it('折れ線を描く', () => {
    const wrapper = mountChart([
      { at: '2026-07-10T12:00:00Z', value: 10 },
      { at: '2026-07-10T12:01:00Z', value: 20 },
    ])

    const path = wrapper.get('path').attributes('d') ?? ''
    // 2点なので開始のMと continuation の L が1つずつ
    expect(path.startsWith('M')).toBe(true)
    expect(path).toContain('L')
  })

  it('古い順に並べ替えてから描く', () => {
    // APIは新しい順で返すため、そのままでは時間が右から左へ流れる
    const wrapper = mountChart([
      { at: '2026-07-10T12:05:00Z', value: 50 },
      { at: '2026-07-10T12:00:00Z', value: 10 },
    ])

    const path = wrapper.get('path').attributes('d') ?? ''
    const [first, second] = path.split(' ')
    const firstY = Number(first?.split(',')[1])
    const secondY = Number(second?.split(',')[1])

    // 値が小さいほうが先に来るので、最初の点のほうが下(yが大きい)になる
    expect(firstY).toBeGreaterThan(secondY)
  })

  it('点が1つでも描画が壊れない', () => {
    // 全点が同じ値だと高さ0になり、線が消えたり除算が壊れたりする
    const wrapper = mountChart([{ at: '2026-07-10T12:00:00Z', value: 42 }])

    expect(wrapper.find('svg').exists()).toBe(true)
    expect(wrapper.find('circle').exists()).toBe(true)
  })

  it('すべて同じ値でも描画が壊れない', () => {
    const wrapper = mountChart([
      { at: '2026-07-10T12:00:00Z', value: 5 },
      { at: '2026-07-10T12:01:00Z', value: 5 },
    ])

    const path = wrapper.get('path').attributes('d') ?? ''
    expect(path).not.toContain('NaN')
  })

  it('図として読めない利用者向けに内容を文章でも持たせる', () => {
    // SVGだけでは何が描かれているか伝わらない
    const wrapper = mountChart([
      { at: '2026-07-10T12:00:00Z', value: 10 },
      { at: '2026-07-10T12:01:00Z', value: 30 },
    ])

    const summary = wrapper.get('[data-testid="chart-summary"]').text()
    expect(summary).toContain('2')
    expect(summary).toContain('30')
    expect(wrapper.get('svg').attributes('aria-label')).toContain('応答時間')
  })

  it('単位を値に付ける', () => {
    const wrapper = mountChart([{ at: '2026-07-10T12:00:00Z', value: 120 }], { unit: 'ms' })

    expect(wrapper.get('[data-testid="chart-summary"]').text()).toContain('120 ms')
  })
})
