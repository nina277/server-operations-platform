export type AuditResultValue = 'Success' | 'Failure' | 'Denied'

export interface ProfileSettings {
  systemName: string
  language: 'ja' | 'en'
}

export interface RetentionSettings {
  profile: 'compact' | 'standard' | 'long-term' | 'custom'
  metricsDays: number
  logsDays: number
  incidentsDays: number
  auditDays: number
}

export interface RetentionPreview {
  metricSnapshots: number
  incidentLogs: number
  incidents: number
  auditLogs: number
  notifications: number
  healthChecks: number
  total: number
}

export interface NetworkCidr {
  id: number
  cidr: string
  description: string | null
  createdAt: string
}

/** 秘密値の状態。値そのものは決して返らない。 */
export interface SecretStatus {
  kind: string
  isConfigured: boolean
  updatedAt: string | null
}

export interface BackupRun {
  id: number
  status: string
  startedAt: string
  completedAt: string | null
  /** 保存先のオブジェクトキー。接続情報や資格情報は含まない。 */
  objectKey: string | null
  sizeBytes: number | null
  message: string | null
}

export interface AuditLog {
  id: number
  occurredAt: string
  actorUserId: number | null
  actorName: string | null
  ipAddress: string
  userAgent: string
  targetType: string
  targetId: string | null
  action: string
  result: AuditResultValue
  details: string | null
  traceId: string | null
}

export interface AuditLogQuery {
  actorName?: string
  targetType?: string
  action?: string
  result?: AuditResultValue
  from?: string
  to?: string
  page?: number
  pageSize?: number
}

export interface AuditLogFilterOptions {
  targetTypes: string[]
  actions: string[]
  results: string[]
}
