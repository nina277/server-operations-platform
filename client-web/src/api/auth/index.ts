import http, { unwrap } from '@/api/http'
import type { ApiResponse } from '@/types/common'
import type {
  ChangePasswordRequest,
  ChangePasswordResult,
  CreateUserRequest,
  CurrentUser,
  LoginRequest,
  ManagedUser,
  MfaSetupResult,
  MfaVerifyResult,
  TokenPair,
  UserRole,
} from '@/types/auth'

export async function login(request: LoginRequest): Promise<TokenPair> {
  return unwrap(await http.post<ApiResponse<TokenPair>>('/api/v1/auth/login', request))
}

export async function logout(refreshToken: string): Promise<void> {
  await http.post('/api/v1/auth/logout', { refreshToken })
}

export async function fetchCurrentUser(): Promise<CurrentUser> {
  return unwrap(await http.get<ApiResponse<CurrentUser>>('/api/v1/me'))
}

export async function setupMfa(): Promise<MfaSetupResult> {
  return unwrap(await http.post<ApiResponse<MfaSetupResult>>('/api/v1/auth/mfa/setup'))
}

export async function verifyMfa(totpCode: string): Promise<MfaVerifyResult> {
  return unwrap(
    await http.post<ApiResponse<MfaVerifyResult>>('/api/v1/auth/mfa/verify', { totpCode }),
  )
}

/**
 * 自分のパスワードを変更する。
 * 変更すると他の端末のセッションが切れる。
 */
export async function changePassword(
  request: ChangePasswordRequest,
): Promise<ChangePasswordResult> {
  return unwrap(await http.put<ApiResponse<ChangePasswordResult>>('/api/v1/me/password', request))
}

// --- 利用者管理(運用管理者 + MFA再認証) ---

export async function fetchUsers(): Promise<ManagedUser[]> {
  return unwrap(await http.get<ApiResponse<ManagedUser[]>>('/api/v1/users'))
}

export async function createUser(request: CreateUserRequest): Promise<ManagedUser> {
  return unwrap(await http.post<ApiResponse<ManagedUser>>('/api/v1/users', request))
}

export async function updateUserRole(id: number, role: UserRole): Promise<ManagedUser> {
  return unwrap(await http.patch<ApiResponse<ManagedUser>>(`/api/v1/users/${id}/role`, { role }))
}

export async function updateUserActive(id: number, isActive: boolean): Promise<ManagedUser> {
  return unwrap(
    await http.patch<ApiResponse<ManagedUser>>(`/api/v1/users/${id}/active`, { isActive }),
  )
}

/**
 * 他人のMFAを解除する。端末を失ったときの回復手段。
 * 対象の全セッションが失効し、操作は監査に残る。
 */
export async function resetUserMfa(id: number): Promise<ManagedUser> {
  return unwrap(await http.post<ApiResponse<ManagedUser>>(`/api/v1/users/${id}/mfa/reset`))
}
