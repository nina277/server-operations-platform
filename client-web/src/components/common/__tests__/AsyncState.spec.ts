import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import AsyncState from '../AsyncState.vue'
import { createTestI18n } from '@/test-utils/i18n'

function mountState(props: Record<string, unknown>) {
  return mount(AsyncState, {
    props: { loading: false, ...props },
    slots: { default: '<p data-testid="content">本文</p>' },
    global: { plugins: [createTestI18n()] },
  })
}

describe('AsyncState', () => {
  it('読み込み中は本文を出さない', () => {
    const wrapper = mountState({ loading: true })

    expect(wrapper.text()).toContain('読み込み中')
    expect(wrapper.find('[data-testid="content"]').exists()).toBe(false)
  })

  it('権限不足はエラーより優先して専用の案内を出す', () => {
    const wrapper = mountState({ forbidden: true, error: '取得に失敗しました' })

    expect(wrapper.text()).toContain('この操作を行う権限がありません')
  })

  it('取得失敗では原因と再試行を示す', async () => {
    const wrapper = mountState({ error: '接続できませんでした' })

    expect(wrapper.get('[role="alert"]').text()).toContain('接続できませんでした')
    await wrapper.get('.async-state__retry').trigger('click')
    expect(wrapper.emitted('retry')).toHaveLength(1)
  })

  it('空のときは指定した文言を出す', () => {
    const wrapper = mountState({ empty: true, emptyMessage: '対象がありません' })

    expect(wrapper.text()).toContain('対象がありません')
  })

  it('正常時のみ本文を出す', () => {
    const wrapper = mountState({})

    expect(wrapper.get('[data-testid="content"]').text()).toBe('本文')
  })
})
