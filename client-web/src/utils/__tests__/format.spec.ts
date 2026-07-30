import { describe, it, expect } from 'vitest'
import {
  formatBytes,
  formatDateTime,
  incidentStatusTone,
  newIdempotencyKey,
  resultTone,
  riskTone,
  severityTone,
  toOptionalNumber,
  toOptionalText,
} from '../format'

describe('表示用の整形', () => {
  it('日時が無いときは0や空文字ではなく「—」を出す', () => {
    expect(formatDateTime(null, 'ja')).toBe('—')
    expect(formatDateTime(undefined, 'ja')).toBe('—')
    expect(formatDateTime('', 'ja')).toBe('—')
    expect(formatDateTime('not-a-date', 'ja')).toBe('—')
  })

  it('日時を地域設定に合わせて整形する', () => {
    const formatted = formatDateTime('2026-07-10T12:34:56Z', 'ja')

    expect(formatted).not.toBe('—')
    expect(formatted).toContain('2026')
  })

  it('容量を単位付きで示し、未取得は「—」にする', () => {
    expect(formatBytes(null)).toBe('—')
    expect(formatBytes(0)).toBe('0 B')
    expect(formatBytes(512)).toBe('512 B')
    expect(formatBytes(1024)).toBe('1.0 KB')
    expect(formatBytes(1024 * 1024 * 3)).toBe('3.0 MB')
  })

  it('深刻度ごとに配色を分ける', () => {
    expect(severityTone('Critical')).toBe('critical')
    expect(severityTone('High')).toBe('high')
    expect(severityTone('Medium')).toBe('medium')
    expect(severityTone('Low')).toBe('low')
    expect(severityTone('Unknown')).toBe('neutral')
  })

  it('未対応のインシデントほど強い配色にする', () => {
    expect(incidentStatusTone('Open')).toBe('critical')
    expect(incidentStatusTone('Acknowledged')).toBe('high')
    expect(incidentStatusTone('Recovering')).toBe('medium')
    expect(incidentStatusTone('Resolved')).toBe('low')
    expect(incidentStatusTone('Closed')).toBe('low')
  })

  it('危険度Highは最も強い配色にする', () => {
    expect(riskTone('High')).toBe('critical')
    expect(riskTone('Medium')).toBe('medium')
    expect(riskTone('Low')).toBe('low')
  })

  it('失敗と拒否を区別して示す', () => {
    expect(resultTone('Success')).toBe('low')
    expect(resultTone('Failure')).toBe('critical')
    expect(resultTone('Denied')).toBe('high')
    expect(resultTone('Blocked')).toBe('high')
    expect(resultTone('Queued')).toBe('neutral')
  })

  it('空欄はnull(値を渡さない)として扱う', () => {
    expect(toOptionalNumber('')).toBeNull()
    expect(toOptionalNumber('   ')).toBeNull()
    expect(toOptionalNumber(null)).toBeNull()
    expect(toOptionalNumber(undefined)).toBeNull()
    expect(toOptionalText('')).toBeNull()
    expect(toOptionalText('   ')).toBeNull()
    expect(toOptionalText(null)).toBeNull()
  })

  it('数値入力欄からは文字列でも数値でも受け取れる', () => {
    // type="number" の入力にv-modelを使うとVueが値を数値へ変換するため、
    // 文字列だけを想定すると実行時に壊れる
    expect(toOptionalNumber('85')).toBe(85)
    expect(toOptionalNumber(85)).toBe(85)
    expect(toOptionalNumber('0')).toBe(0)
    expect(toOptionalNumber(0)).toBe(0)
    expect(toOptionalNumber('-1.5')).toBe(-1.5)
  })

  it('数値にならない入力はnullにする', () => {
    expect(toOptionalNumber('abc')).toBeNull()
    expect(toOptionalNumber(Number.NaN)).toBeNull()
    expect(toOptionalNumber(Number.POSITIVE_INFINITY)).toBeNull()
  })

  it('文字列として渡す値も数値でも受け取れる', () => {
    expect(toOptionalText('exited')).toBe('exited')
    expect(toOptionalText(503)).toBe('503')
    expect(toOptionalText(0)).toBe('0')
  })

  it('冪等キーは呼ぶたびに異なる', () => {
    const keys = new Set(Array.from({ length: 20 }, () => newIdempotencyKey()))

    expect(keys.size).toBe(20)
    for (const key of keys) {
      expect(key.length).toBeGreaterThan(8)
    }
  })
})
