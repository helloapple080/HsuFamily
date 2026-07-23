import { create } from 'zustand'
import type { UserInfo } from '@/types/auth'

/// 認證狀態介面
interface AuthState {
  /// 使用者資訊
  user: UserInfo | null
  /// 是否已登入
  isAuthenticated: boolean
  /// 是否為 Admin
  isAdmin: boolean
  /// 是否已向後端確認 Cookie Session
  isHydrated: boolean

  /// 設定登入狀態
  login: (user: UserInfo) => void
  /// 清除登入狀態
  logout: () => void
  /// 標記後端 Session 檢查完成
  setHydrated: (isHydrated: boolean) => void
}

/// 認證 Zustand Store — 僅保存非敏感記憶體狀態，憑證由 HttpOnly Cookie 管理
export const useAuthStore = create<AuthState>((set) => ({
  user: null,
  isAuthenticated: false,
  isAdmin: false,
  isHydrated: false,

  login: (user) =>
    set({
      user,
      isAuthenticated: true,
      isAdmin: user.role === 'Admin',
    }),

  logout: () =>
    set({
      user: null,
      isAuthenticated: false,
      isAdmin: false,
    }),

  setHydrated: (isHydrated) => set({ isHydrated }),
}))
