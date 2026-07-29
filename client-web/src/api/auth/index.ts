import http, { unwrap } from '@/api/http'
import type { ApiResponse } from '@/types/common'
import type {
  CurrentUser,
  LoginRequest,
  MfaSetupResult,
  MfaVerifyResult,
  TokenPair,
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
