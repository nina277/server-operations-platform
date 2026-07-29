import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import SecretField from '../SecretField.vue'
import { createTestI18n } from '@/test-utils/i18n'

/** 最後に通知されたv-modelの値を取り出す。 */
function lastModelValue(events: unknown[][] | undefined): unknown {
  if (events === undefined || events.length === 0) {
    return undefined
  }
  return events[events.length - 1]
}

function mountField(props: Record<string, unknown>) {
  return mount(SecretField, {
    props: {
      id: 'smtp-password',
      label: 'SMTPパスワード',
      configured: false,
      modelValue: null,
      ...props,
    },
    global: { plugins: [createTestI18n()] },
  })
}

describe('SecretField', () => {
  it('設定済みでも値そのものは表示せず、状態だけを示す', () => {
    const wrapper = mountField({ configured: true })

    expect(wrapper.get('[data-testid="secret-state"]').text()).toBe('設定済み(値は表示されません)')
    // 保存済みの値は受け取らないので、入力欄自体が出ない
    expect(wrapper.find('#smtp-password').exists()).toBe(false)
  })

  it('未設定なら最初から入力欄を出す', () => {
    const wrapper = mountField({ configured: false })

    expect(wrapper.get('[data-testid="secret-state"]').text()).toBe('未設定')
    expect(wrapper.get('#smtp-password').attributes('type')).toBe('password')
  })

  it('変更するを押すと入力できるようになる', async () => {
    const wrapper = mountField({ configured: true })

    await wrapper.get('button').trigger('click')
    await wrapper.get('#smtp-password').setValue('new-secret')

    expect(wrapper.props('modelValue')).toBeNull()
    expect(lastModelValue(wrapper.emitted('update:modelValue'))).toEqual(['new-secret'])
  })

  it('入力を空にすると「変更しない」を意味するnullを通知する', async () => {
    const wrapper = mountField({ configured: false, modelValue: 'typed' })

    await wrapper.get('#smtp-password').setValue('')

    expect(lastModelValue(wrapper.emitted('update:modelValue'))).toEqual([null])
  })

  it('入力値の表示切り替えができる', async () => {
    const wrapper = mountField({ configured: false })
    const toggle = wrapper.get('button')

    expect(wrapper.get('#smtp-password').attributes('type')).toBe('password')
    await toggle.trigger('click')
    expect(wrapper.get('#smtp-password').attributes('type')).toBe('text')
  })

  it('変更しないを選ぶと入力を破棄する', async () => {
    const wrapper = mountField({ configured: true })

    await wrapper.get('button').trigger('click')
    await wrapper.get('#smtp-password').setValue('typed')

    const keep = wrapper.findAll('button').find((b) => b.text() === '変更しない')
    await keep?.trigger('click')

    expect(lastModelValue(wrapper.emitted('update:modelValue'))).toEqual([null])
    expect(wrapper.find('#smtp-password').exists()).toBe(false)
  })
})
