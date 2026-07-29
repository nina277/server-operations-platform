import { describe, it, expect, beforeEach, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import AuditLogsView from '../AuditLogsView.vue'
import { createTestI18n } from '@/test-utils/i18n'
import { lastCall } from '@/test-utils/mock'
import type { PagedResult } from '@/types/common'
import type { AuditLog, AuditLogFilterOptions, AuditLogQuery } from '@/types/settings'

const auditLog: AuditLog = {
  id: 1,
  occurredAt: '2026-07-10T12:00:00Z',
  actorUserId: 1,
  actorName: 'admin',
  ipAddress: '192.0.2.10',
  userAgent: 'Mozilla/5.0',
  targetType: 'RecoveryAction',
  targetId: '5',
  action: 'recovery.execute',
  result: 'Success',
  details: 'nextcloud-app を再起動',
  traceId: 'trace-1',
}

const searchAuditLogs = vi.fn<(query: AuditLogQuery) => Promise<PagedResult<AuditLog>>>()
const fetchAuditLogFilterOptions = vi.fn<() => Promise<AuditLogFilterOptions>>()

vi.mock('@/api/settings', () => ({
  searchAuditLogs: (query: AuditLogQuery) => searchAuditLogs(query),
  fetchAuditLogFilterOptions: () => fetchAuditLogFilterOptions(),
}))

async function mountView() {
  const wrapper = mount(AuditLogsView, {
    global: { plugins: [createTestI18n()] },
  })
  await flushPromises()
  return wrapper
}

describe('AuditLogsView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    setActivePinia(createPinia())

    searchAuditLogs.mockResolvedValue({
      items: [auditLog],
      page: 1,
      pageSize: 20,
      totalCount: 1,
      totalPages: 1,
    })
    fetchAuditLogFilterOptions.mockResolvedValue({
      targetTypes: ['RecoveryAction', 'User'],
      actions: ['auth.login', 'recovery.execute'],
      results: ['Success', 'Failure', 'Denied'],
    })
  })

  it('操作者・IP・User-Agent・対象・操作・結果・時刻をすべて表示する', async () => {
    const wrapper = await mountView()
    const text = wrapper.text()

    expect(text).toContain('admin')
    expect(text).toContain('192.0.2.10')
    expect(text).toContain('Mozilla/5.0')
    expect(text).toContain('RecoveryAction')
    expect(text).toContain('recovery.execute')
    expect(text).toContain('成功')
    expect(text).toContain('2026')
  })

  it('初回は絞り込み条件を送らない', async () => {
    await mountView()

    expect(searchAuditLogs).toHaveBeenCalledWith({
      actorName: undefined,
      targetType: undefined,
      action: undefined,
      result: undefined,
      from: undefined,
      to: undefined,
      page: 1,
      pageSize: 20,
    })
  })

  it('入力した条件だけを送る', async () => {
    const wrapper = await mountView()

    await wrapper.get('#audit-actor').setValue('admin')
    await wrapper.get('#audit-result').setValue('Failure')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    const query = lastCall(searchAuditLogs.mock.calls)[0]
    expect(query.actorName).toBe('admin')
    expect(query.result).toBe('Failure')
    expect(query.targetType).toBeUndefined()
    expect(query.action).toBeUndefined()
  })

  it('期間はUTCへ変換して送る', async () => {
    const wrapper = await mountView()

    await wrapper.get('#audit-from').setValue('2026-07-10T00:00')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    const query = lastCall(searchAuditLogs.mock.calls)[0]
    expect(query.from).toBe(new Date('2026-07-10T00:00').toISOString())
  })

  it('絞り込みを変えたら1ページ目から探し直す', async () => {
    searchAuditLogs.mockResolvedValue({
      items: [auditLog],
      page: 2,
      pageSize: 20,
      totalCount: 40,
      totalPages: 2,
    })

    const wrapper = await mountView()

    await wrapper.get('#audit-actor').setValue('admin')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(lastCall(searchAuditLogs.mock.calls)[0].page).toBe(1)
  })

  it('絞り込みの選択肢は記録済みの値から作る', async () => {
    const wrapper = await mountView()

    const options = wrapper.get('#audit-target-type').findAll('option')
    expect(options.map((o) => o.text())).toEqual(['すべての対象種別', 'RecoveryAction', 'User'])
  })
})
