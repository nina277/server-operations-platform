import http, { unwrap } from '@/api/http'
import type { ApiResponse, PagedResult } from '@/types/common'
import type {
  AdapterTemplate,
  AiUsageSummary,
  AppNotification,
  Approval,
  ConnectionTestResult,
  CreateApprovalRequest,
  CreateMaintenanceWindowRequest,
  CreateRecoveryActionRequest,
  CreateTargetRequest,
  DashboardSummary,
  DeviceToken,
  Diagnosis,
  DiagnosticRule,
  HealthCheck,
  Incident,
  IncidentListQuery,
  IncidentLog,
  IncidentNote,
  IncidentStatus,
  MaintenanceWindow,
  MetricSnapshot,
  OperationsInsights,
  Recurrence,
  RecoveryAction,
  RecoveryActionDefinition,
  RediagnoseResult,
  RuleEditorOptions,
  RuleTestRequest,
  RuleTestResponse,
  SaveDiagnosticRuleRequest,
  Target,
  TargetCapabilities,
  UpdateAiLimitsRequest,
  UpdateTargetRequest,
} from '@/types/operations'

// --- ダッシュボード ---

export async function fetchDashboardSummary(): Promise<DashboardSummary> {
  return unwrap(await http.get<ApiResponse<DashboardSummary>>('/api/v1/dashboard/summary'))
}

// --- 監視対象 ---

export async function fetchTargets(): Promise<Target[]> {
  return unwrap(await http.get<ApiResponse<Target[]>>('/api/v1/targets'))
}

export async function fetchTarget(id: number): Promise<Target> {
  return unwrap(await http.get<ApiResponse<Target>>(`/api/v1/targets/${id}`))
}

export async function fetchTargetCapabilities(id: number): Promise<TargetCapabilities> {
  return unwrap(
    await http.get<ApiResponse<TargetCapabilities>>(`/api/v1/targets/${id}/capabilities`),
  )
}

export async function createTarget(request: CreateTargetRequest): Promise<Target> {
  return unwrap(await http.post<ApiResponse<Target>>('/api/v1/targets', request))
}

export async function updateTarget(id: number, request: UpdateTargetRequest): Promise<Target> {
  return unwrap(await http.put<ApiResponse<Target>>(`/api/v1/targets/${id}`, request))
}

/**
 * 登録済みの対象に対してのみ接続試験を行う。
 * 任意のURLやIPを指定する口は用意しない。
 */
export async function testTargetConnection(id: number): Promise<ConnectionTestResult> {
  return unwrap(
    await http.post<ApiResponse<ConnectionTestResult>>(`/api/v1/targets/${id}/test-connection`),
  )
}

export async function runHealthCheck(id: number): Promise<HealthCheck> {
  return unwrap(await http.post<ApiResponse<HealthCheck>>(`/api/v1/targets/${id}/health-check`))
}

export async function fetchTargetMetrics(id: number, limit = 100): Promise<MetricSnapshot[]> {
  return unwrap(
    await http.get<ApiResponse<MetricSnapshot[]>>(`/api/v1/targets/${id}/metrics`, {
      params: { limit },
    }),
  )
}

export async function fetchTargetLogs(id: number, limit = 50): Promise<IncidentLog[]> {
  return unwrap(
    await http.get<ApiResponse<IncidentLog[]>>(`/api/v1/targets/${id}/logs`, { params: { limit } }),
  )
}

export async function fetchAdapterTemplates(): Promise<AdapterTemplate[]> {
  return unwrap(await http.get<ApiResponse<AdapterTemplate[]>>('/api/v1/adapter-templates'))
}

// --- インシデント ---

export async function searchIncidents(query: IncidentListQuery): Promise<PagedResult<Incident>> {
  return unwrap(
    await http.get<ApiResponse<PagedResult<Incident>>>('/api/v1/incidents', { params: query }),
  )
}

export async function fetchIncident(id: number): Promise<Incident> {
  return unwrap(await http.get<ApiResponse<Incident>>(`/api/v1/incidents/${id}`))
}

export async function updateIncidentStatus(id: number, status: IncidentStatus): Promise<Incident> {
  return unwrap(
    await http.patch<ApiResponse<Incident>>(`/api/v1/incidents/${id}/status`, { status }),
  )
}

export async function fetchDiagnoses(incidentId: number): Promise<Diagnosis[]> {
  return unwrap(
    await http.get<ApiResponse<Diagnosis[]>>(`/api/v1/incidents/${incidentId}/diagnoses`),
  )
}

/** AIによる再診断。AI無効・上限到達・失敗時は診断を作らず理由が返る。 */
export async function rediagnose(incidentId: number): Promise<RediagnoseResult> {
  return unwrap(
    await http.post<ApiResponse<RediagnoseResult>>(`/api/v1/incidents/${incidentId}/rediagnose`),
  )
}

// --- 復旧 ---

export async function fetchApprovals(incidentId: number): Promise<Approval[]> {
  return unwrap(
    await http.get<ApiResponse<Approval[]>>(`/api/v1/incidents/${incidentId}/approvals`),
  )
}

export async function createApproval(
  incidentId: number,
  request: CreateApprovalRequest,
): Promise<Approval> {
  return unwrap(
    await http.post<ApiResponse<Approval>>(`/api/v1/incidents/${incidentId}/approvals`, request),
  )
}

/** 実行できる操作の一覧。危険度と承認要否はサーバー側の定義に従う。 */
export async function fetchRecoveryActionCatalog(): Promise<RecoveryActionDefinition[]> {
  return unwrap(
    await http.get<ApiResponse<RecoveryActionDefinition[]>>('/api/v1/recovery-action-catalog'),
  )
}

export async function fetchRecoveryActions(incidentId: number): Promise<RecoveryAction[]> {
  return unwrap(
    await http.get<ApiResponse<RecoveryAction[]>>(
      `/api/v1/incidents/${incidentId}/recovery-actions`,
    ),
  )
}

/**
 * 復旧アクションを要求する。
 * 二重送信で二重実行にならないよう、Idempotency-Keyを必ず付ける。
 */
export async function createRecoveryAction(
  incidentId: number,
  request: CreateRecoveryActionRequest,
  idempotencyKey: string,
): Promise<RecoveryAction> {
  return unwrap(
    await http.post<ApiResponse<RecoveryAction>>(
      `/api/v1/incidents/${incidentId}/recovery-actions`,
      request,
      { headers: { 'Idempotency-Key': idempotencyKey } },
    ),
  )
}

// --- 診断ルール ---

export async function fetchDiagnosticRules(): Promise<DiagnosticRule[]> {
  return unwrap(await http.get<ApiResponse<DiagnosticRule[]>>('/api/v1/diagnostic-rules'))
}

export async function fetchDiagnosticRule(id: number): Promise<DiagnosticRule> {
  return unwrap(await http.get<ApiResponse<DiagnosticRule>>(`/api/v1/diagnostic-rules/${id}`))
}

/** ルールを書くときに選べる値。条件の項目や演算子を画面で作り直さない。 */
export async function fetchRuleEditorOptions(): Promise<RuleEditorOptions> {
  return unwrap(
    await http.get<ApiResponse<RuleEditorOptions>>('/api/v1/diagnostic-rules/editor-options'),
  )
}

/** ルールの判定を試す。保存も実行もしない。 */
export async function testDiagnosticRules(request: RuleTestRequest): Promise<RuleTestResponse> {
  return unwrap(
    await http.post<ApiResponse<RuleTestResponse>>('/api/v1/diagnostic-rules/test', request),
  )
}

export async function createDiagnosticRule(
  request: SaveDiagnosticRuleRequest,
): Promise<DiagnosticRule> {
  return unwrap(await http.post<ApiResponse<DiagnosticRule>>('/api/v1/diagnostic-rules', request))
}

export async function updateDiagnosticRule(
  id: number,
  request: SaveDiagnosticRuleRequest,
): Promise<DiagnosticRule> {
  return unwrap(
    await http.put<ApiResponse<DiagnosticRule>>(`/api/v1/diagnostic-rules/${id}`, request),
  )
}

/** ルールを消さずに止める。 */
export async function setDiagnosticRuleEnabled(
  id: number,
  isEnabled: boolean,
): Promise<DiagnosticRule> {
  return unwrap(
    await http.patch<ApiResponse<DiagnosticRule>>(`/api/v1/diagnostic-rules/${id}/enabled`, {
      isEnabled,
    }),
  )
}

// --- 通知 ---

export async function searchNotifications(
  isRead: boolean | undefined,
  page: number,
  pageSize: number,
): Promise<PagedResult<AppNotification>> {
  return unwrap(
    await http.get<ApiResponse<PagedResult<AppNotification>>>('/api/v1/notifications', {
      params: { isRead, page, pageSize },
    }),
  )
}

export async function fetchUnreadCount(): Promise<number> {
  return unwrap(await http.get<ApiResponse<number>>('/api/v1/notifications/unread-count'))
}

export async function markNotificationRead(id: number): Promise<AppNotification> {
  return unwrap(await http.patch<ApiResponse<AppNotification>>(`/api/v1/notifications/${id}/read`))
}

export async function fetchDeviceTokens(): Promise<DeviceToken[]> {
  return unwrap(await http.get<ApiResponse<DeviceToken[]>>('/api/v1/notifications/device-tokens'))
}

export async function registerDeviceToken(token: string, label?: string): Promise<DeviceToken> {
  return unwrap(
    await http.post<ApiResponse<DeviceToken>>('/api/v1/notifications/device-tokens', {
      token,
      label,
    }),
  )
}

export async function revokeDeviceToken(id: number): Promise<void> {
  await http.delete(`/api/v1/notifications/device-tokens/${id}`)
}

// --- AI利用状況 ---

export async function fetchAiUsage(): Promise<AiUsageSummary> {
  return unwrap(await http.get<ApiResponse<AiUsageSummary>>('/api/v1/ai-usage/summary'))
}

export async function updateAiLimits(request: UpdateAiLimitsRequest): Promise<AiUsageSummary> {
  return unwrap(await http.put<ApiResponse<AiUsageSummary>>('/api/v1/ai-usage/limits', request))
}

export async function updateAiEnabled(isEnabled: boolean): Promise<AiUsageSummary> {
  return unwrap(
    await http.patch<ApiResponse<AiUsageSummary>>('/api/v1/ai-usage/enabled', { isEnabled }),
  )
}

// --- 運用実績サマリ ---

/**
 * 期間を指定して運用実績を集計する。
 * 日時はUTCのISO文字列で渡す(サーバー側の保存もUTCのため)。
 */
export async function fetchOperationsInsights(
  from: string,
  to: string,
): Promise<OperationsInsights> {
  return unwrap(
    await http.get<ApiResponse<OperationsInsights>>('/api/v1/insights/operations', {
      params: { from, to },
    }),
  )
}

// --- 障害の再発 ---

export async function fetchRecurrence(incidentId: number): Promise<Recurrence> {
  return unwrap(
    await http.get<ApiResponse<Recurrence>>(`/api/v1/incidents/${incidentId}/recurrence`),
  )
}

// --- インシデントの対応メモ ---

export async function fetchIncidentNotes(incidentId: number): Promise<IncidentNote[]> {
  return unwrap(
    await http.get<ApiResponse<IncidentNote[]>>(`/api/v1/incidents/${incidentId}/notes`),
  )
}

export async function addIncidentNote(incidentId: number, body: string): Promise<IncidentNote> {
  return unwrap(
    await http.post<ApiResponse<IncidentNote>>(`/api/v1/incidents/${incidentId}/notes`, { body }),
  )
}

// --- メンテナンス期間 ---

export async function fetchMaintenanceWindows(): Promise<MaintenanceWindow[]> {
  return unwrap(await http.get<ApiResponse<MaintenanceWindow[]>>('/api/v1/maintenance-windows'))
}

export async function createMaintenanceWindow(
  request: CreateMaintenanceWindowRequest,
): Promise<MaintenanceWindow> {
  return unwrap(
    await http.post<ApiResponse<MaintenanceWindow>>('/api/v1/maintenance-windows', request),
  )
}

export async function cancelMaintenanceWindow(id: number): Promise<MaintenanceWindow> {
  return unwrap(
    await http.post<ApiResponse<MaintenanceWindow>>(`/api/v1/maintenance-windows/${id}/cancel`),
  )
}
