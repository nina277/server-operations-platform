export type Severity = 'Critical' | 'High' | 'Medium' | 'Low'
export type IncidentStatus = 'Open' | 'Acknowledged' | 'Recovering' | 'Resolved' | 'Closed'
export type RiskLevel = 'Low' | 'Medium' | 'High'

export interface AdapterTemplateInput {
  key: string
  label: string
  type: string
  required: boolean
  /** trueなら秘密値。画面では値を再表示しない。 */
  secret: boolean
  description: string
  defaultValue: string | null
}

export interface AdapterTemplate {
  id: string
  name: string
  description: string
  inputs: AdapterTemplateInput[]
  recommendedMonitors: string[]
  initialRules: string[]
  allowedOperations: string[]
  capabilities: string[]
}

export interface Target {
  id: number
  name: string
  templateId: string
  description: string | null
  isEnabled: boolean
  /** 自動復旧の有効/無効。初期値はOFF。 */
  autoRecoveryEnabled: boolean
  /** 操作を許可するコンテナ名。空ならどのコンテナも操作できない。 */
  allowedContainers: string[]
  settings: Record<string, string>
  /** 設定済みの秘密値の種別名のみ。値は返らない。 */
  configuredCredentials: string[]
  createdAt: string
  updatedAt: string
}

export interface CreateTargetRequest {
  name: string
  templateId: string
  description?: string | null
  settings: Record<string, string>
  credentials: Record<string, string>
}

export interface UpdateTargetRequest {
  name: string
  description?: string | null
  isEnabled: boolean
  autoRecoveryEnabled: boolean
  allowedContainers: string[]
  settings: Record<string, string>
  /** 変更する秘密値のみ入れる。省略したものは維持される。 */
  credentials: Record<string, string>
}

export interface ConnectionTestResult {
  success: boolean
  message: string
  latencyMs: number | null
  detail: string | null
}

export interface TargetCapabilities {
  targetId: number
  templateId: string
  capabilities: string[]
  allowedOperations: string[]
  recommendedMonitors: string[]
  initialRules: string[]
}

export interface MetricSnapshot {
  id: number
  collectedAt: string
  kind: string
  status: string
  payloadJson: string | null
  errorMessage: string | null
}

export interface IncidentLog {
  id: number
  collectedAt: string
  source: string
  /** 収集時にマスク済み。 */
  maskedContent: string
  incidentId: number | null
}

export interface Incident {
  id: number
  targetId: number
  title: string
  classification: string
  service: string | null
  severity: Severity
  status: IncidentStatus
  firstOccurredAt: string
  lastOccurredAt: string
  occurrenceCount: number
  resolvedAt: string | null
}

export interface IncidentListQuery {
  status?: IncidentStatus
  severity?: Severity
  targetId?: number
  search?: string
  sort?: string
  page?: number
  pageSize?: number
}

export interface Diagnosis {
  id: number
  incidentId: number
  source: string
  ruleId: number | null
  reusedDiagnosisId: number | null
  classification: string
  severity: Severity
  rationale: string
  recommendedActionId: string | null
  /** 対象の許可操作に含まれるか。falseなら実行できない。 */
  recommendedActionAllowed: boolean
  createdAt: string
}

export interface RediagnoseResult {
  diagnosis: Diagnosis | null
  /** Diagnosed か、診断できなかった理由。 */
  outcome: string
  message: string | null
}

export interface Approval {
  id: number
  incidentId: number
  actionId: string
  targetResource: string | null
  status: string
  decidedByUsername: string | null
  decidedAt: string | null
  expiresAt: string
  isConsumed: boolean
  comment: string | null
}

export interface CreateApprovalRequest {
  actionId: string
  targetResource?: string | null
  approve: boolean
  comment?: string | null
}

export interface RecoveryAction {
  id: number
  incidentId: number
  targetId: number
  actionId: string
  targetResource: string | null
  riskLevel: RiskLevel
  status: string
  approvalId: number | null
  requestedAt: string
  completedAt: string | null
  resultMessage: string | null
  blockedReason: string | null
}

/**
 * 復旧アクションの定義(サーバー側の許可リスト)。
 * 危険度や承認要否を画面側で作り直さず、この定義に従う。
 */
export interface RecoveryActionDefinition {
  actionId: string
  name: string
  riskLevel: RiskLevel
  requiresApproval: boolean
  requiresIdempotencyKey: boolean
  requiresTargetResource: boolean
  description: string
}

export interface CreateRecoveryActionRequest {
  actionId: string
  targetResource?: string | null
  /** Medium操作では承認済みApprovalのIDが必須。 */
  approvalId?: number | null
}

export interface HealthCheck {
  id: number
  targetId: number
  recoveryActionId: number | null
  status: string
  message: string
  latencyMs: number | null
  checkedAt: string
}

export interface DashboardSummary {
  targetCount: number
  enabledTargetCount: number
  activeIncidentsBySeverity: Record<string, number>
  incidentsByStatus: Record<string, number>
  recentIncidents: Incident[]
}

export interface DiagnosticRule {
  id: number
  name: string
  classification: string
  ruleType: string
  conditionJson: string
  severity: Severity
  recommendedActionId: string | null
  priority: number
  isEnabled: boolean
}

export interface RuleTestRequest {
  containerState?: string | null
  containerName?: string | null
  restartCount?: number | null
  memoryUsagePercent?: number | null
  diskUsagePercent?: number | null
  httpSuccess?: boolean | null
  httpStatus?: number | null
  httpLatencyMs?: number | null
  logExcerpt?: string | null
}

export interface RuleTestMatch {
  ruleId: number
  ruleName: string
  classification: string
  severity: Severity
  recommendedActionId: string | null
  rationale: string
}

export interface RuleTestResponse {
  matches: RuleTestMatch[]
}

export interface AppNotification {
  id: number
  severity: Severity
  title: string
  body: string
  incidentId: number | null
  targetId: number | null
  occurrenceCount: number
  firstNotifiedAt: string
  lastNotifiedAt: string
  isRead: boolean
}

export interface DeviceToken {
  id: number
  label: string | null
  isActive: boolean
  createdAt: string
  lastUsedAt: string | null
  revokedAt: string | null
  /** 識別用の末尾数文字のみ。トークン本体は返らない。 */
  tokenSuffix: string
}

export interface AiUsageRecord {
  id: number
  calledAt: string
  result: string
  incidentId: number | null
  inputCharacters: number
  outputTokens: number | null
  latencyMs: number | null
  errorSummary: string | null
}

export interface AiUsageSummary {
  isEnabled: boolean
  provider: string
  model: string
  hourlyUsed: number
  hourlyLimit: number
  dailyUsed: number
  dailyLimit: number
  monthlyUsed: number
  monthlyLimit: number
  maxInputCharacters: number
  maxOutputTokens: number
  recentCalls: AiUsageRecord[]
}

export interface UpdateAiLimitsRequest {
  model?: string | null
  monthlyLimit: number
  dailyLimit: number
  hourlyLimit: number
  maxInputCharacters: number
  maxOutputTokens: number
  timeoutSeconds: number
}
