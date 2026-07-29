import { describe, it, expect } from 'vitest'
import {
  formatBytes,
  formatDateTime,
  incidentStatusTone,
  newIdempotencyKey,
  resultTone,
  riskTone,
  severityTone,
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

  it('冪等キーは呼ぶたびに異なる', () => {
    const keys = new Set(Array.from({ length: 20 }, () => newIdempotencyKey()))

    expect(keys.size).toBe(20)
    for (const key of keys) {
      expect(key.length).toBeGreaterThan(8)
    }
  })
})
