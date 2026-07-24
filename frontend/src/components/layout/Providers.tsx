'use client'

import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ThemeProvider } from 'next-themes'
import { Toaster } from '@/components/ui/sonner'
import { useState } from 'react'
import { useEffect } from 'react'
import api from '@/lib/api'
import { useAuthStore } from '@/stores/authStore'
import type { ApiResponse } from '@/types/api'
import type { UserInfo } from '@/types/auth'
import { shouldRetryQuery } from '@/lib/queryRetry'
import { MotionConfig } from 'framer-motion'

interface ProvidersProps {
  children: React.ReactNode
}

/// 全域 Provider 組合 — React Query + Theme + Toast
export default function Providers({ children }: ProvidersProps) {
  const { login, logout, setHydrated } = useAuthStore()
  /// 在 Client 端建立 QueryClient，避免 SSR 共享
  const [queryClient] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            staleTime: 60 * 1000, // 1 分鐘內資料視為新鮮
            retry: shouldRetryQuery,
            refetchOnWindowFocus: false,
          },
        },
      })
  )

  /// 以 HttpOnly Cookie 還原登入狀態；瀏覽器端不保存任何 Token。
  useEffect(() => {
    let isMounted = true

    api.get<ApiResponse<UserInfo>>('/api/auth/me')
      .then(({ data }) => {
        if (isMounted && data.success && data.data) login(data.data)
      })
      .catch(() => {
        if (isMounted) logout()
      })
      .finally(() => {
        if (isMounted) setHydrated(true)
      })

    return () => {
      isMounted = false
    }
  }, [login, logout, setHydrated])

  return (
    <QueryClientProvider client={queryClient}>
      <ThemeProvider
        attribute="class"
        defaultTheme="dark"
        enableSystem
        disableTransitionOnChange
      >
        <MotionConfig reducedMotion="user">
          {children}
          <Toaster richColors position="top-right" />
        </MotionConfig>
      </ThemeProvider>
    </QueryClientProvider>
  )
}
