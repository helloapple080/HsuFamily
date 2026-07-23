/// 使用者角色
export type UserRole = 'Admin' | 'User'

/// 使用者資訊（不含敏感資料）
export interface UserInfo {
  id: string
  username: string
  email: string
  displayName: string
  role: UserRole
  linkedMemberId?: string | null
}

/// 登入請求
export interface LoginRequest {
  username: string
  password: string
}

/// 登入回應
export interface LoginResponse {
  expiresAt: string
  user: UserInfo
}

/// 註冊請求
export interface RegisterRequest {
  username: string
  email: string
  password: string
  displayName: string
  role: string
}
