import { describe, it, expect, beforeEach, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'
import { h } from 'vue'
import { AxiosError, AxiosHeaders, type InternalAxiosRequestConfig } from 'axios'
import RuleEditView from '../RuleEditView.vue'
import { createTestI18n } from '@/test-utils/i18n'
import { nthCall } from '@/test-utils/mock'
import type {
  DiagnosticRule,
  RuleEditorOptions,
  SaveDiagnosticRuleRequest,
} from '@/types/operations'

const options: RuleEditorOptions = {
  fields: ['containerState', 'memoryUsagePercent', 'diskUsagePercent', 'logExcerpt'],
  operators: ['>=', '>', '<=', '<', '==', '!='],
  ruleTypes: ['State', 'Threshold', 'Regex'],
  severities: ['Critical', 'High', 'Medium', 'Low'],
  recommendedActionIds: ['RECHECK_HTTP_HEALTH', 'RESTART_ALLOWED_CONTAINER'],
}

const existingRule: DiagnosticRule = {
  id: 4,
  name: 'コンテナ停止',
  classification: 'ContainerStopped',
  ruleType: 'State',
  conditionJson: '{"field":"containerState","equalsAny":["exited","dead"]}',
  severity: 'High',
  recommendedActionId: 'RESTART_ALLOWED_CONTAINER',
  priority: 10,
  isEnabled: true,
}

const createDiagnosticRule = vi.fn<(r: SaveDiagnosticRuleRequest) => Promise<DiagnosticRule>>()
const updateDiagnosticRule =
  vi.fn<(id: number, r: SaveDiagnosticRuleRequest) => Promise<DiagnosticRule>>()

vi.mock('@/api/operations', () => ({
  fetchRuleEditorOptions: () => Promise.resolve(options),
  fetchDiagnosticRule: () => Promise.resolve(existingRule),
  createDiagnosticRule: (r: SaveDiagnosticRuleRequest) => createDiagnosticRule(r),
  updateDiagnosticRule: (id: number, r: SaveDiagnosticRuleRequest) => updateDiagnosticRule(id, r),
  testDiagnosticRules: () => Promise.resolve({ matches: [] }),
}))

function apiError(code: string, message: string): AxiosError {
  const config = { headers: new AxiosHeaders() } as InternalAxiosRequestConfig
  const error = new AxiosError(message, 'ERR_BAD_REQUEST', config)
  error.response = {
    data: { success: false, data: null, error: { code, message }, traceId: null },
    status: 400,
    statusText: 'Bad Request',
    headers: new AxiosHeaders(),
    config,
  }
  return error
}

function createTestRouter(): Router {
  return createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/rules/new', name: 'rule-new', component: RuleEditView },
      { path: '/rules/:id', name: 'rule-edit', component: RuleEditView },
      { path: '/rules', name: 'rules', component: { render: () => h('div', 'rules') } },
    ],
  })
}

async function mountView(path = '/rules/new') {
  const router = createTestRouter()
  await router.push(path)
  await router.isReady()

  const wrapper = mount(RuleEditView, {
    global: { plugins: [createTestI18n(), router] },
  })
  await flushPromises()
  return { wrapper, router }
}

describe('RuleEditView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())
    createDiagnosticRule.mockResolvedValue(existingRule)
    updateDiagnosticRule.mockResolvedValue(existingRule)
  })

  it('推奨アクションの候補はサーバーの許可リストだけを出す', async () => {
    const { wrapper } = await mountView()

    const values = wrapper
      .get('#rule-action')
      .findAll('option')
      .map((o) => o.attributes('value'))

    // 空(操作を推奨しない) + 許可リストの2件のみ
    expect(values).toEqual(['', 'RECHECK_HTTP_HEALTH', 'RESTART_ALLOWED_CONTAINER'])
  })

  it('条件の項目はサーバーが許可したものだけを出す', async () => {
    const { wrapper } = await mountView()

    const values = wrapper
      .get('#condition-field')
      .findAll('option')
      .map((o) => o.attributes('value'))

    expect(values).toEqual(options.fields)
  })

  it('状態ルールの条件を入力から組み立てる', async () => {
    const { wrapper } = await mountView()

    await wrapper.get('#condition-field').setValue('containerState')
    await wrapper.get('#condition-equals').setValue('exited\n\n  dead  \n')

    expect(wrapper.get('[data-testid="condition-preview"]').text()).toBe(
      '{"field":"containerState","equalsAny":["exited","dead"]}',
    )
  })

  it('しきい値ルールでは比較と値の欄に切り替わる', async () => {
    const { wrapper } = await mountView()

    await wrapper.get('#rule-type').setValue('Threshold')
    await wrapper.get('#condition-field').setValue('memoryUsagePercent')
    await wrapper.get('#condition-operator').setValue('>=')
    await wrapper.get('#condition-value').setValue('90')

    expect(wrapper.find('#condition-equals').exists()).toBe(false)
    expect(wrapper.get('[data-testid="condition-preview"]').text()).toBe(
      '{"field":"memoryUsagePercent","operator":">=","value":90}',
    )
  })

  it('正規表現ルールではパターンの欄に切り替わる', async () => {
    const { wrapper } = await mountView()

    await wrapper.get('#rule-type').setValue('Regex')
    await wrapper.get('#condition-field').setValue('logExcerpt')
    await wrapper.get('#condition-pattern').setValue('(?i)out of memory')

    expect(wrapper.find('#condition-operator').exists()).toBe(false)
    expect(wrapper.get('[data-testid="condition-preview"]').text()).toBe(
      '{"field":"logExcerpt","pattern":"(?i)out of memory"}',
    )
  })

  it('必須項目が空なら保存できない', async () => {
    const { wrapper } = await mountView()

    const submit = wrapper.findAll('button').find((b) => b.text() === '保存')
    expect(submit?.attributes('disabled')).toBeDefined()

    await wrapper.get('#rule-name').setValue('新しいルール')
    await wrapper.get('#rule-classification').setValue('DiskPressure')

    expect(
      wrapper
        .findAll('button')
        .find((b) => b.text() === '保存')
        ?.attributes('disabled'),
    ).toBeUndefined()
  })

  it('新規作成では入力内容をそのまま送る', async () => {
    const { wrapper } = await mountView()

    await wrapper.get('#rule-name').setValue('ディスク逼迫')
    await wrapper.get('#rule-classification').setValue('DiskPressure')
    await wrapper.get('#rule-type').setValue('Threshold')
    await wrapper.get('#condition-field').setValue('diskUsagePercent')
    await wrapper.get('#condition-operator').setValue('>=')
    await wrapper.get('#condition-value').setValue('90')
    await wrapper.get('#rule-severity').setValue('Medium')
    await wrapper.get('#rule-priority').setValue('20')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    const request = nthCall(createDiagnosticRule.mock.calls, 0)[0]
    expect(request.name).toBe('ディスク逼迫')
    expect(request.classification).toBe('DiskPressure')
    expect(request.ruleType).toBe('Threshold')
    expect(request.conditionJson).toBe('{"field":"diskUsagePercent","operator":">=","value":90}')
    expect(request.severity).toBe('Medium')
    expect(request.priority).toBe(20)
  })

  it('推奨アクションを選ばなければnullを送る', async () => {
    const { wrapper } = await mountView()

    await wrapper.get('#rule-name').setValue('通知だけ')
    await wrapper.get('#rule-classification').setValue('DiskPressure')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(nthCall(createDiagnosticRule.mock.calls, 0)[0].recommendedActionId).toBeNull()
  })

  it('保存できたら一覧へ戻る', async () => {
    const { wrapper, router } = await mountView()

    await wrapper.get('#rule-name').setValue('新しいルール')
    await wrapper.get('#rule-classification').setValue('DiskPressure')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(router.currentRoute.value.name).toBe('rules')
  })

  it('既存のルールを開くと条件がフォームへ戻る', async () => {
    const { wrapper } = await mountView('/rules/4')

    expect((wrapper.get('#rule-name').element as HTMLInputElement).value).toBe('コンテナ停止')
    expect((wrapper.get('#condition-equals').element as HTMLTextAreaElement).value).toBe(
      'exited\ndead',
    )
    expect(wrapper.get('[data-testid="condition-preview"]').text()).toBe(
      '{"field":"containerState","equalsAny":["exited","dead"]}',
    )
  })

  it('編集では更新APIを呼ぶ', async () => {
    const { wrapper } = await mountView('/rules/4')

    await wrapper.get('#rule-priority').setValue('5')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(createDiagnosticRule).not.toHaveBeenCalled()
    const [id, request] = nthCall(updateDiagnosticRule.mock.calls, 0)
    expect(id).toBe(4)
    expect(request.priority).toBe(5)
  })

  it('サーバーが条件を拒否したらその理由を出し、一覧へ移らない', async () => {
    createDiagnosticRule.mockRejectedValue(
      apiError('invalid_condition', 'パターンの評価に時間がかかりすぎます。'),
    )

    const { wrapper, router } = await mountView()

    await wrapper.get('#rule-name').setValue('重いルール')
    await wrapper.get('#rule-classification').setValue('UnknownLog')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.get('[data-testid="save-error"]').text()).toContain('時間がかかりすぎます')
    expect(router.currentRoute.value.name).toBe('rule-new')
  })

  it('ルールが自動復旧の入口であることを画面で伝える', async () => {
    const { wrapper } = await mountView()

    expect(wrapper.text()).toContain('自動復旧の入口')
  })
})
