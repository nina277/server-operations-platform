import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

/**
 * 配色のコントラスト比を測る(WCAG 2.2 AA)。
 *
 * 「AAを目標に選んだ」だけでは守れない。実際に測るまで、
 * 暗い配色の主ボタンは白文字を明るい青に載せていて 2.34:1 しかなかった。
 * 目視では気づけないため、値で固定する。
 *
 * 必要な比率:
 *   4.5:1 通常の文字
 *   3.0:1 操作部品の輪郭(1.4.11 非テキストのコントラスト)。入力欄やボタンの枠が該当する
 */

// jsdom環境では import.meta.url が file: にならないため、プロジェクト直下から辿る
const css = readFileSync(resolve(process.cwd(), 'src/assets/main.css'), 'utf-8')

// :root より前が明るい配色、@media (prefers-color-scheme: dark) 以降が暗い配色。
// 暗い側は上書きなので、明るい側に重ねて解決する
const darkAt = css.indexOf('prefers-color-scheme: dark')

function tokens(source: string): Record<string, string> {
  const found: Record<string, string> = {}
  for (const match of source.matchAll(/(--color-[a-z-]+):\s*(#[0-9a-fA-F]{6})/g)) {
    const name = match[1]
    const value = match[2]
    if (name !== undefined && value !== undefined) {
      found[name] = value
    }
  }
  return found
}

const light = tokens(css.slice(0, darkAt))
const dark = { ...light, ...tokens(css.slice(darkAt)) }

function channel(value: number): number {
  const c = value / 255
  return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4
}

function luminance(hex: string): number {
  return (
    0.2126 * channel(parseInt(hex.slice(1, 3), 16)) +
    0.7152 * channel(parseInt(hex.slice(3, 5), 16)) +
    0.0722 * channel(parseInt(hex.slice(5, 7), 16))
  )
}

function contrast(foreground: string, background: string): number {
  const a = luminance(foreground)
  const b = luminance(background)
  return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05)
}

/** [前景, 背景, 用途, 必要な比率] */
const PAIRS: [string, string, string, number][] = [
  ['--color-text', '--color-bg', '本文 / ページ背景', 4.5],
  ['--color-text', '--color-surface', '本文 / カード', 4.5],
  ['--color-text', '--color-surface-alt', '本文 / 表の縞', 4.5],
  ['--color-text-muted', '--color-bg', '補助文 / ページ背景', 4.5],
  ['--color-text-muted', '--color-surface', '補助文 / カード', 4.5],
  ['--color-text-muted', '--color-surface-alt', '補助文 / 表の縞', 4.5],
  ['--color-accent', '--color-bg', 'リンク / ページ背景', 4.5],
  ['--color-accent', '--color-surface', 'リンク / カード', 4.5],
  // .button--primary
  ['--color-text-inverse', '--color-accent', '主ボタンの文字 / 主ボタン', 4.5],
  ['--color-text-inverse', '--color-accent-hover', '主ボタンの文字 / ホバー時', 4.5],
  // 深刻度の帯
  ['--color-critical', '--color-critical-bg', '重大 / 重大の帯', 4.5],
  ['--color-high', '--color-high-bg', '高 / 高の帯', 4.5],
  ['--color-medium', '--color-medium-bg', '中 / 中の帯', 4.5],
  ['--color-low', '--color-low-bg', '低 / 低の帯', 4.5],
  ['--color-neutral', '--color-neutral-bg', 'その他 / その他の帯', 4.5],
  ['--color-critical', '--color-surface', '重大 / カード', 4.5],
  ['--color-high', '--color-surface', '高 / カード', 4.5],
  ['--color-medium', '--color-surface', '中 / カード', 4.5],
  ['--color-low', '--color-surface', '低 / カード', 4.5],
  // .form-field input / .button の枠。文字ではなく操作部品なので 3:1
  ['--color-border-strong', '--color-surface', '入力欄とボタンの枠 / カード', 3],
  ['--color-border-strong', '--color-bg', '入力欄とボタンの枠 / ページ背景', 3],
]

describe.each([
  ['明るい配色', light],
  ['暗い配色', dark],
])('%s', (_label, palette) => {
  it.each(PAIRS)(
    '%s と %s (%s) が %d:1 以上ある',
    (foreground, background, _use, required) => {
      const fg = palette[foreground]
      const bg = palette[background]
      expect(fg, `${foreground} が定義されていない`).toBeDefined()
      expect(bg, `${background} が定義されていない`).toBeDefined()

      const ratio = contrast(fg!, bg!)
      expect(
        Number(ratio.toFixed(2)),
        `${fg} と ${bg} は ${ratio.toFixed(2)}:1`,
      ).toBeGreaterThanOrEqual(required)
    },
  )
})
