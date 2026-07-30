export type UserRole = 'Viewer' | 'OperatorAdmin' | 'SystemExecutor'

export interface LoginRequest {
  username: string
  password: string
  /** MFA有効ユーザーでは必須。 */
  totpCode?: string
}

export interface TokenPair {
  accessToken: string
  accessTokenExpiresAt: string
  refreshToken: string
  refreshTokenExpiresAt: string
}

export interface CurrentUser {
  id: number
  username: string
  role: UserRole
  mfaEnabled: boolean
}

export interface MfaSetupResult {
  /** この応答でのみ返る。以後は再表示されない。 */
  secret: string
  otpAuthUri: string
}

export interface MfaVerifyResult {
  mfaEnabled: boolean
  verifiedAt: string
}

export interface ChangePasswordRequest {
  currentPassword: string
  newPassword: string
}

export interface ChangePasswordResult {
  changedAt: string
  /** 他の端末のセッションを切ったか。 */
  otherSessionsRevoked: boolean
}
