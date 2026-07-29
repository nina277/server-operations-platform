import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ConfirmActionDialog from '../ConfirmActionDialog.vue'
import { createTestI18n } from '@/test-utils/i18n'

function mountDialog(props: Record<string, unknown>) {
  return mount(ConfirmActionDialog, {
    props: {
      open: true,
      title: '復旧操作の確認',
      targetName: 'nextcloud-app',
      actionLabel: 'コンテナ再起動',
      risk: 'Low',
      ...props,
    },
    global: { plugins: [createTestI18n()] },
    attachTo: document.body,
  })
}

describe('ConfirmActionDialog', () => {
  it('危険度がLowなら対象名を入力せずに実行できる', async () => {
    const wrapper = mountDialog({ risk: 'Low' })

    const execute = wrapper.get('[data-testid="confirm-execute"]')
    expect(execute.attributes('disabled')).toBeUndefined()

    await execute.trigger('click')
    expect(wrapper.emitted('confirm')).toHaveLength(1)
  })

  it('危険度がHighなら対象名が一致するまで実行できない', async () => {
    const wrapper = mountDialog({ risk: 'High' })

    const execute = wrapper.get('[data-testid="confirm-execute"]')
    expect(execute.attributes('disabled')).toBeDefined()

    await execute.trigger('click')
    expect(wrapper.emitted('confirm')).toBeUndefined()

    await wrapper.get('#confirm-name').setValue('nextcloud-db')
    expect(execute.attributes('disabled')).toBeDefined()

    await wrapper.get('#confirm-name').setValue('nextcloud-app')
    expect(execute.attributes('disabled')).toBeUndefined()

    await execute.trigger('click')
    expect(wrapper.emitted('confirm')).toHaveLength(1)
  })

  it('対象と操作を必ず表示する', () => {
    const wrapper = mountDialog({ risk: 'Medium' })

    expect(wrapper.get('[data-testid="confirm-target"]').text()).toBe('nextcloud-app')
    expect(wrapper.text()).toContain('コンテナ再起動')
  })

  it('Escキーで取り消しを通知する', async () => {
    const wrapper = mountDialog({ risk: 'Low' })

    await wrapper.get('.confirm-backdrop').trigger('keydown', { key: 'Escape' })
    expect(wrapper.emitted('cancel')).toHaveLength(1)
  })

  it('実行中は両方のボタンを押せない', () => {
    const wrapper = mountDialog({ risk: 'Low', busy: true })

    expect(wrapper.get('[data-testid="confirm-execute"]').attributes('disabled')).toBeDefined()
  })

  it('閉じているときは何も描画しない', () => {
    const wrapper = mountDialog({ open: false, risk: 'Low' })

    expect(wrapper.find('[role="dialog"]').exists()).toBe(false)
  })
})
