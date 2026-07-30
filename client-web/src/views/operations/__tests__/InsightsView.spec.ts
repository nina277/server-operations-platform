import { describe, it, expect, beforeEach, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import InsightsView from '../InsightsView.vue'
import { createTestI18n } from '@/test-utils/i18n'
import { lastCall } from '@/test-utils/mock'
import type { OperationsInsights } from '@/types/operations'

const insights: OperationsInsights = {
  from: '2026-07-01T00:00:00Z',
  to: '2026-07-31T23:59:59Z',
  detectionToNotification: {
    count: 10,
    averageSeconds: 90,
    medianSeconds: 80,
    p95Seconds: 240,
    maxSeconds: 310,
  },
  notifiedWithinTargetRatio: 0.9,
  notificationTargetSeconds: 300,
  recoveryDuration: {
    count: 4,
    averageSeconds: 12,
    medianSeconds: 11,
    p95Seconds: 20,
    maxSeconds: 22,
  },
  autoRecoveryDuration: {
    count: 3,
    averageSeconds: 10,
    medianSeconds: 9,
    p95Seconds: 15,
    maxSeconds: 16,
  },
  incidentsDetected: 12,
  incidentsResolved: 9,
  incidentsBySeverity: { Critical: 2, High: 4, Low: 6 },
  recoveryByStatus: { Succeeded: 4, Blocked: 2 },
  autoRecoveryByStatus: { Succeeded: 3, Failed: 1, Blocked: 5 },
  autoRecoverySuccessRatio: 0.75,
  blockedReasons: { cooldown: 4, circuit_open: 1 },
}

const fetchOperationsInsights = vi.fn<(from: string, to: string) => Promise<OperationsInsights>>()

vi.mock('@/api/operations', () => ({
  fetchOperationsInsights: (from: string, to: string) => fetchOperationsInsights(from, to),
}))

async function mountView() {
  const wrapper = mount(InsightsView, { global: { plugins: [createTestI18n()] } })
  await flushPromises()
  return wrapper
}

describe('InsightsView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())
    fetchOperationsInsights.mockResolvedValue(insights)
  })

  it('基準の秒数以内に通知できた割合を出す', async () => {
    // 成功基準#2をSQLを叩かずに読めることがこの画面の目的
    const wrapper = await mountView()

    expect(wrapper.get('[data-testid="within-target"]').text()).toContain('90%')
    expect(wrapper.get('[data-testid="within-target"]').text()).toContain('300')
  })

  it('自動復旧の成功率を出す', async () => {
    const wrapper = await mountView()

    expect(wrapper.get('[data-testid="auto-success-ratio"]').text()).toContain('75%')
  })

  it('安全機構が止めた理由の内訳を件数の多い順に出す', async () => {
    // 何を何回止めたかは、安全機構が効いていることの直接の証拠になる
    const wrapper = await mountView()

    const rows = wrapper.get('[data-testid="blocked-reasons"]').findAll('tbody tr')
    expect(rows[0]?.text()).toContain('cooldown')
    expect(rows[0]?.text()).toContain('4')
    expect(rows[1]?.text()).toContain('circuit_open')
  })

  it('検知件数と解決件数を出す', async () => {
    const wrapper = await mountView()

    expect(wrapper.get('[data-testid="detected"]').text()).toBe('12')
  })

  it('記録が無い割合は0%ではなく—で出す', async () => {
    // 0%と出すと「一度も間に合っていない」と読めてしまう
    fetchOperationsInsights.mockResolvedValue({
      ...insights,
      notifiedWithinTargetRatio: null,
      autoRecoverySuccessRatio: null,
    })

    const wrapper = await mountView()

    expect(wrapper.get('[data-testid="within-target"]').text()).toContain('—')
    expect(wrapper.get('[data-testid="auto-success-ratio"]').text()).toContain('—')
  })

  it('止めた理由が無ければその旨を出す', async () => {
    fetchOperationsInsights.mockResolvedValue({ ...insights, blockedReasons: {} })

    const wrapper = await mountView()

    expect(wrapper.find('[data-testid="blocked-reasons"]').exists()).toBe(false)
    expect(wrapper.text()).toContain('該当する記録がありません')
  })

  it('期間を指定して集計し直せる', async () => {
    const wrapper = await mountView()

    await wrapper.get('#range-from').setValue('2026-06-01')
    await wrapper.get('#range-to').setValue('2026-06-30')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    const [from, to] = lastCall(fetchOperationsInsights.mock.calls)
    expect(from).toBe('2026-06-01T00:00:00.000Z')
    // 終了日はその日いっぱいを含める
    expect(to).toBe('2026-06-30T23:59:59.000Z')
  })

  it('取得に失敗したら理由を出す', async () => {
    fetchOperationsInsights.mockRejectedValue(new Error('failed'))

    const wrapper = await mountView()

    expect(wrapper.find('[role="alert"]').exists()).toBe(true)
  })
})
