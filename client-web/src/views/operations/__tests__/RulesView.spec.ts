import { describe, it, expect, beforeEach, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { h } from 'vue'
import RulesView from '../RulesView.vue'
import { createTestI18n } from '@/test-utils/i18n'
import { at, lastCall } from '@/test-utils/mock'
import type { DiagnosticRule, RuleTestRequest, RuleTestResponse } from '@/types/operations'
import type { CurrentUser } from '@/types/auth'

const rules: DiagnosticRule[] = [
  {
    id: 1,
    name: 'コンテナ停止',
    classification: 'ContainerStopped',
    ruleType: 'State',
    conditionJson: '{"field":"containerState","equalsAny":["exited"]}',
    severity: 'High',
    recommendedActionId: 'RESTART_ALLOWED_CONTAINER',
    priority: 10,
    isEnabled: true,
  },
  {
    id: 2,
    name: '止めてあるルール',
    classification: 'DiskPressure',
    ruleType: 'Threshold',
    conditionJson: '{"field":"diskUsagePercent","operator":">=","value":90}',
    severity: 'Medium',
    recommendedActionId: null,
    priority: 20,
    isEnabled: false,
  },
]

const admin: CurrentUser = { id: 1, username: 'admin', role: 'OperatorAdmin', mfaEnabled: true }

const testDiagnosticRules = vi.fn<(r: RuleTestRequest) => Promise<RuleTestResponse>>()
const setDiagnosticRuleEnabled =
  vi.fn<(id: number, isEnabled: boolean) => Promise<DiagnosticRule>>()

vi.mock('@/api/operations', () => ({
  fetchDiagnosticRules: () => Promise.resolve(rules),
  testDiagnosticRules: (r: RuleTestRequest) => testDiagnosticRules(r),
  setDiagnosticRuleEnabled: (id: number, isEnabled: boolean) =>
    setDiagnosticRuleEnabled(id, isEnabled),
}))

vi.mock('@/api/auth', () => ({
  login: vi.fn<() => Promise<never>>(),
  logout: vi.fn<() => Promise<void>>(),
  fetchCurrentUser: () => Promise.resolve(admin),
}))

function createTestRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/rules', name: 'rules', component: RulesView },
      { path: '/rules/new', name: 'rule-new', component: { render: () => h('div') } },
      { path: '/rules/:id', name: 'rule-edit', component: { render: () => h('div') } },
    ],
  })
}

async function mountView() {
  const router = createTestRouter()
  await router.push('/rules')
  await router.isReady()

  const wrapper = mount(RulesView, {
    global: { plugins: [createTestI18n(), router] },
  })
  await flushPromises()
  return wrapper
}

describe('RulesView', () => {
  beforeEach(async () => {
    localStorage.setItem('sop.accessToken', 'access-1')
    localStorage.setItem('sop.refreshToken', 'refresh-1')
    vi.clearAllMocks()
    setActivePinia(createPinia())
    testDiagnosticRules.mockResolvedValue({ matches: [] })
    setDiagnosticRuleEnabled.mockResolvedValue(at(rules, 0))

    const { useAuthStore } = await import('@/stores/auth')
    await useAuthStore().loadCurrentUser()
  })

  it('ルールの一覧と有効・無効を表示する', async () => {
    const wrapper = await mountView()
    const text = wrapper.text()

    expect(text).toContain('コンテナ停止')
    expect(text).toContain('止めてあるルール')
    expect(text).toContain('RESTART_ALLOWED_CONTAINER')
  })

  it('数値を入れて試験してもエラーにならない', async () => {
    // type="number" の入力ではVueが値を数値へ変換するため、
    // 文字列だけを想定していると実行時に壊れる
    const wrapper = await mountView()

    await wrapper.get('#test-disk').setValue('95')
    await wrapper.get('#test-restart-count').setValue('3')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(testDiagnosticRules).toHaveBeenCalledTimes(1)
    const request = lastCall(testDiagnosticRules.mock.calls)[0]
    expect(request.diskUsagePercent).toBe(95)
    expect(request.restartCount).toBe(3)
    // 取得失敗の案内が出ていないこと
    expect(wrapper.findAll('[role="alert"]')).toHaveLength(0)
  })

  it('空欄の項目は判定へ渡さない', async () => {
    const wrapper = await mountView()

    await wrapper.get('#test-container-state').setValue('exited')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    const request = lastCall(testDiagnosticRules.mock.calls)[0]
    expect(request.containerState).toBe('exited')
    expect(request.diskUsagePercent).toBeNull()
    expect(request.logExcerpt).toBeNull()
  })

  it('有効・無効を切り替えられる', async () => {
    const wrapper = await mountView()

    // 2件目は無効なので「有効」にする操作が出る
    const toggles = wrapper.findAll('tbody button')
    await at(toggles, 1).trigger('click')
    await flushPromises()

    expect(setDiagnosticRuleEnabled).toHaveBeenCalledWith(2, true)
  })

  it('有効なルールは無効化の操作を出す', async () => {
    const wrapper = await mountView()

    const toggles = wrapper.findAll('tbody button')
    await at(toggles, 0).trigger('click')
    await flushPromises()

    expect(setDiagnosticRuleEnabled).toHaveBeenCalledWith(1, false)
  })
})
