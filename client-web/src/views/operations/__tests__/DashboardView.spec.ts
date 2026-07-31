import { describe, it, expect, beforeEach, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { h } from 'vue'
import DashboardView from '../DashboardView.vue'
import { createTestI18n } from '@/test-utils/i18n'
import type { DashboardSummary } from '@/types/operations'

const summary: DashboardSummary = {
  targetCount: 3,
  enabledTargetCount: 3,
  activeIncidentsBySeverity: {},
  incidentsByStatus: {},
  recentIncidents: [],
  unreachedTargets: [],
}

const fetchDashboardSummary = vi.fn<() => Promise<DashboardSummary>>()

vi.mock('@/api/operations', () => ({
  fetchDashboardSummary: () => fetchDashboardSummary(),
}))

function createTestRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'dashboard', component: DashboardView },
      { path: '/targets/:id', name: 'target-detail', component: { render: () => h('div') } },
      { path: '/incidents/:id', name: 'incident-detail', component: { render: () => h('div') } },
    ],
  })
}

async function mountView() {
  const router = createTestRouter()
  router.push('/')
  await router.isReady()

  const wrapper = mount(DashboardView, {
    global: { plugins: [createTestI18n(), router] },
  })
  await flushPromises()
  return wrapper
}

describe('DashboardView の自己監視', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())
    fetchDashboardSummary.mockResolvedValue({ ...summary })
  })

  it('収集が届いていればその知らせを出さない', async () => {
    const wrapper = await mountView()

    expect(wrapper.find('[data-testid="unreached-targets"]').exists()).toBe(false)
  })

  it('収集が途絶えている対象を知らせる', async () => {
    // 「インシデント0件」と「監視が死んでいる」を区別できることが要点
    fetchDashboardSummary.mockResolvedValue({
      ...summary,
      unreachedTargets: [
        {
          targetId: 1,
          targetName: 'docker1',
          reach: 'Stale',
          lastCollectedAt: '2026-07-10T10:00:00Z',
          expectedIntervalSeconds: 60,
          staleForSeconds: 7200,
        },
      ],
    })

    const wrapper = await mountView()

    const alert = wrapper.get('[data-testid="unreached-targets"]')
    expect(alert.text()).toContain('docker1')
    expect(alert.text()).toContain('収集が途絶えています')
  })

  it('インシデントが0件でも異常が無いとは限らないと伝える', async () => {
    // これを書かないと、利用者は0件を見て安心してしまう
    fetchDashboardSummary.mockResolvedValue({
      ...summary,
      unreachedTargets: [
        {
          targetId: 1,
          targetName: 'docker1',
          reach: 'Stale',
          lastCollectedAt: '2026-07-10T10:00:00Z',
          expectedIntervalSeconds: 60,
          staleForSeconds: 7200,
        },
      ],
    })

    const wrapper = await mountView()

    expect(wrapper.get('[data-testid="unreached-targets"]').text()).toContain(
      '異常が無いとは限りません',
    )
  })

  it('一度も収集されていない対象は途絶と区別して出す', async () => {
    fetchDashboardSummary.mockResolvedValue({
      ...summary,
      unreachedTargets: [
        {
          targetId: 2,
          targetName: 'new-target',
          reach: 'NeverCollected',
          lastCollectedAt: null,
          expectedIntervalSeconds: 60,
          staleForSeconds: null,
        },
      ],
    })

    const wrapper = await mountView()

    const alert = wrapper.get('[data-testid="unreached-targets"]')
    expect(alert.text()).toContain('一度も収集されていません')
    expect(alert.text()).not.toContain('収集が途絶えています')
  })

  it('経過時間を読みやすい単位で出す', async () => {
    fetchDashboardSummary.mockResolvedValue({
      ...summary,
      unreachedTargets: [
        {
          targetId: 1,
          targetName: 'docker1',
          reach: 'Stale',
          lastCollectedAt: '2026-07-10T10:00:00Z',
          expectedIntervalSeconds: 60,
          staleForSeconds: 7200,
        },
      ],
    })

    const wrapper = await mountView()

    // 7200秒をそのまま出しても頭に入らない
    expect(wrapper.get('[data-testid="unreached-targets"]').text()).toContain('2時間前')
  })

  it('対象の詳細へたどれるようにする', async () => {
    fetchDashboardSummary.mockResolvedValue({
      ...summary,
      unreachedTargets: [
        {
          targetId: 5,
          targetName: 'docker1',
          reach: 'Stale',
          lastCollectedAt: '2026-07-10T10:00:00Z',
          expectedIntervalSeconds: 60,
          staleForSeconds: 7200,
        },
      ],
    })

    const wrapper = await mountView()

    const link = wrapper.get('[data-testid="unreached-targets"]').get('a')
    expect(link.attributes('href')).toBe('/targets/5')
  })
})
