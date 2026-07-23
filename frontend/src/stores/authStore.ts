import { create } from 'zustand'
import { persist } from 'zustand/middleware'
import type { UserInfo } from '@/types/auth'

/// 認證狀態介面
interface AuthState {
  /// 使用者資訊
  user: UserInfo | null
  /// JWT Access Token
  accessToken: string | null
  /// Refresh Token
  refreshToken: string | null
  /// 是否已登入
  isAuthenticated: boolean
  /// 是否為 Admin
  isAdmin: boolean
  /// persisted 狀態是否已從 localStorage 還原
  isHydrated: boolean

  /// 設定登入狀態
  login: (user: UserInfo, accessToken: string, refreshToken: string) => void
  /// 清除登入狀態
  logout: () => void
  /// 更新 Token
  updateTokens: (accessToken: string, refreshToken: string) => void
  /// 標記 persisted 狀態已完成還原
  setHydrated: (isHydrated: boolean) => void
}

/// 認證 Zustand Store — 持久化到 localStorage
export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      user: null,
      accessToken: null,
      refreshToken: null,
      isAuthenticated: false,
      isAdmin: false,
      isHydrated: false,

      login: (user, accessToken, refreshToken) =>
        set({
          user,
          accessToken,
          refreshToken,
          isAuthenticated: true,
          isAdmin: user.role === 'Admin',
        }),

      logout: () =>
        set({
          user: null,
          accessToken: null,
          refreshToken: null,
          isAuthenticated: false,
          isAdmin: false,
        }),

      updateTokens: (accessToken, refreshToken) =>
        set({ accessToken, refreshToken }),

      setHydrated: (isHydrated) => set({ isHydrated }),
    }),
    {
      name: 'auth-storage',
      partialize: (state) => ({
        user: state.user,
        accessToken: state.accessToken,
        refreshToken: state.refreshToken,
        isAuthenticated: state.isAuthenticated,
        isAdmin: state.isAdmin,
      }),
      onRehydrateStorage: () => (state) => {
        state?.setHydrated(true)
      },
    }
  )
)
