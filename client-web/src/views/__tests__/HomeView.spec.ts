import { describe, it, expect, vi } from 'vitest'
import { flushPromises, mount } from '@vue/test-utils'
import HomeView from '../HomeView.vue'
import type { HealthStatus } from '@/api/health'

vi.mock('@/api/health', () => ({
  fetchApiLiveness: vi.fn<() => Promise<HealthStatus>>().mockResolvedValue('healthy'),
}))

describe('HomeView', () => {
  it('タイトルとAPI状態を表示する', async () => {
    const wrapper = mount(HomeView)
    expect(wrapper.text()).toContain('Server Operations Platform')

    await flushPromises()
    expect(wrapper.get('[data-testid="api-status"]').text()).toContain('正常')
  })
})
