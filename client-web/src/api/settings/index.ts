import http, { unwrap } from '@/api/http'
import type { ApiResponse, PagedResult } from '@/types/common'
import type {
  AuditLog,
  AuditLogFilterOptions,
  AuditLogQuery,
  BackupRun,
  BackupSettings,
  NetworkCidr,
  NotificationSettings,
  ProfileSettings,
  RetentionPreview,
  RetentionSettings,
  SecretStatus,
} from '@/types/settings'
import type { ConnectionTestResult } from '@/types/operations'

// --- 一般設定 ---

export async function fetchProfile(): Promise<ProfileSettings> {
  return unwrap(await http.get<ApiResponse<ProfileSettings>>('/api/v1/settings/profile'))
}

export async function updateProfile(settings: ProfileSettings): Promise<ProfileSettings> {
  return unwrap(await http.put<ApiResponse<ProfileSettings>>('/api/v1/settings/profile', settings))
}

// --- 保持設定 ---

export async function fetchRetention(): Promise<RetentionSettings> {
  return unwrap(await http.get<ApiResponse<RetentionSettings>>('/api/v1/settings/retention'))
}

export async function updateRetention(settings: RetentionSettings): Promise<RetentionSettings> {
  return unwrap(
    await http.put<ApiResponse<RetentionSettings>>('/api/v1/settings/retention', settings),
  )
}

/** 現在の保持設定で削除される件数の見込み。削除は行わない。 */
export async function previewRetention(): Promise<RetentionPreview> {
  return unwrap(await http.get<ApiResponse<RetentionPreview>>('/api/v1/settings/retention/preview'))
}

// --- 通知設定 ---

export async function fetchNotificationSettings(): Promise<NotificationSettings> {
  return unwrap(
    await http.get<ApiResponse<NotificationSettings>>('/api/v1/settings/notification'),
  )
}

export async function updateNotificationSettings(
  settings: NotificationSettings,
): Promise<NotificationSettings> {
  return unwrap(
    await http.put<ApiResponse<NotificationSettings>>('/api/v1/settings/notification', settings),
  )
}

// --- 接続を許可するネットワーク範囲 ---

export async function fetchNetworkCidrs(): Promise<NetworkCidr[]> {
  return unwrap(await http.get<ApiResponse<NetworkCidr[]>>('/api/v1/settings/network-cidrs'))
}

export async function addNetworkCidr(cidr: string, description?: string): Promise<NetworkCidr> {
  return unwrap(
    await http.post<ApiResponse<NetworkCidr>>('/api/v1/settings/network-cidrs', {
      cidr,
      description,
    }),
  )
}

export async function deleteNetworkCidr(id: number): Promise<void> {
  await http.delete(`/api/v1/settings/network-cidrs/${id}`)
}

// --- 秘密値 ---

/** 設定済みかどうかだけを返す。値そのものは返らない。 */
export async function fetchSecretStatus(kind: string): Promise<SecretStatus> {
  return unwrap(
    await http.get<ApiResponse<SecretStatus>>(`/api/v1/settings/secrets/${kind}/status`),
  )
}

export async function updateSecret(kind: string, value: string): Promise<SecretStatus> {
  return unwrap(
    await http.put<ApiResponse<SecretStatus>>(`/api/v1/settings/secrets/${kind}`, { value }),
  )
}

// --- バックアップ ---

export async function fetchBackupSettings(): Promise<BackupSettings> {
  return unwrap(await http.get<ApiResponse<BackupSettings>>('/api/v1/settings/backup-settings'))
}

export async function updateBackupSettings(settings: BackupSettings): Promise<BackupSettings> {
  return unwrap(
    await http.put<ApiResponse<BackupSettings>>('/api/v1/settings/backup-settings', settings),
  )
}

export async function testBackupConnection(): Promise<ConnectionTestResult> {
  return unwrap(
    await http.post<ApiResponse<ConnectionTestResult>>('/api/v1/settings/backup/test-connection'),
  )
}

export async function runBackup(): Promise<BackupRun> {
  return unwrap(await http.post<ApiResponse<BackupRun>>('/api/v1/settings/backup/run'))
}

export async function fetchBackupRuns(): Promise<BackupRun[]> {
  return unwrap(await http.get<ApiResponse<BackupRun[]>>('/api/v1/settings/backup/runs'))
}

// --- 監査ログ ---

export async function searchAuditLogs(query: AuditLogQuery): Promise<PagedResult<AuditLog>> {
  return unwrap(
    await http.get<ApiResponse<PagedResult<AuditLog>>>('/api/v1/audit-logs', { params: query }),
  )
}

export async function fetchAuditLogFilterOptions(): Promise<AuditLogFilterOptions> {
  return unwrap(
    await http.get<ApiResponse<AuditLogFilterOptions>>('/api/v1/audit-logs/filter-options'),
  )
}
