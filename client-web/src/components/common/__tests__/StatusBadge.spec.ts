import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import StatusBadge from '../StatusBadge.vue'

describe('StatusBadge', () => {
  it('色だけでなく文字とアイコンでも状態を示す', () => {
    const wrapper = mount(StatusBadge, { props: { tone: 'critical', label: '緊急' } })

    expect(wrapper.text()).toContain('緊急')
    expect(wrapper.get('.status-badge__icon').text()).not.toBe('')
    expect(wrapper.classes()).toContain('status-badge--critical')
  })

  it('アイコンは支援技術から読み上げない(文字と重複するため)', () => {
    const wrapper = mount(StatusBadge, { props: { tone: 'low', label: '正常' } })

    expect(wrapper.get('.status-badge__icon').attributes('aria-hidden')).toBe('true')
  })

  it('種類ごとに異なるアイコンを使う', () => {
    const tones = ['critical', 'high', 'medium', 'low', 'neutral'] as const
    const icons = tones.map((tone) =>
      mount(StatusBadge, { props: { tone, label: tone } })
        .get('.status-badge__icon')
        .text(),
    )

    expect(new Set(icons).size).toBe(tones.length)
  })
})
