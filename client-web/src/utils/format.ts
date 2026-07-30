import type { Severity } from '@/types/operations'

/** StatusBadgeの配色区分。 */
export type Tone = 'critical' | 'high' | 'medium' | 'low' | 'neutral'

/** APIはUTCで返すため、表示は利用者の地域設定に合わせる。 */
export function formatDateTime(value: string | null | undefined, locale: string): string {
  if (!value) {
    return '—'
  }

  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return '—'
  }

  return new Intl.DateTimeFormat(locale, {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).format(date)
}

export function formatBytes(value: number | null | undefined): string {
  if (value === null || value === undefined) {
    return '—'
  }

  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  let size = value
  let unit = 0
  while (size >= 1024 && unit < units.length - 1) {
    size /= 1024
    unit += 1
  }

  return `${unit === 0 ? size : size.toFixed(1)} ${units[unit]}`
}

export function severityTone(severity: Severity | string): Tone {
  switch (severity) {
    case 'Critical':
      return 'critical'
    case 'High':
      return 'high'
    case 'Medium':
      return 'medium'
    case 'Low':
      return 'low'
    default:
      return 'neutral'
  }
}

/** インシデントの状態。未対応ほど強い色にする。 */
export function incidentStatusTone(status: string): Tone {
  switch (status) {
    case 'Open':
      return 'critical'
    case 'Acknowledged':
      return 'high'
    case 'Recovering':
      return 'medium'
    case 'Resolved':
    case 'Closed':
      return 'low'
    default:
      return 'neutral'
  }
}

export function riskTone(risk: string): Tone {
  switch (risk) {
    case 'High':
      return 'critical'
    case 'Medium':
      return 'medium'
    default:
      return 'low'
  }
}

/** 成功・失敗・拒否の区分。 */
export function resultTone(result: string): Tone {
  switch (result) {
    case 'Success':
    case 'Succeeded':
      return 'low'
    case 'Failure':
    case 'Failed':
      return 'critical'
    case 'Denied':
    case 'Blocked':
      return 'high'
    default:
      return 'neutral'
  }
}

/**
 * 入力欄の値を数値へ変換する。空欄はnull(「その値を渡さない」)を意味する。
 *
 * type="number" の入力にv-modelを使うとVueが値を数値へ変換するため、
 * 文字列と数値の両方が渡ってくる。どちらでも扱えるようにしておく。
 */
export function toOptionalNumber(value: string | number | null | undefined): number | null {
  if (value === null || value === undefined || value === '') {
    return null
  }

  if (typeof value === 'number') {
    return Number.isFinite(value) ? value : null
  }

  if (value.trim().length === 0) {
    return null
  }

  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : null
}

/** 入力欄の値を文字列へ変換する。空欄はnull(「その値を渡さない」)を意味する。 */
export function toOptionalText(value: string | number | null | undefined): string | null {
  if (value === null || value === undefined) {
    return null
  }

  const text = String(value)
  return text.trim().length > 0 ? text : null
}

/**
 * 冪等キー。同じ操作を二度押ししても二重に実行されないようにする。
 * crypto.randomUUIDが使えない環境向けの控えも用意する。
 */
export function newIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`
}
