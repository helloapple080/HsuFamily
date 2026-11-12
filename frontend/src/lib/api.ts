import axios, { AxiosError, type InternalAxiosRequestConfig } from 'axios'
import { useAuthStore } from '@/stores/authStore'
import { isHostedMode } from '@/lib/hosted/mode'
import type { ApiResponse } from '@/types/api'

interface CsrfTokenResponse {
  token: string
}

interface RetryableRequestConfig extends InternalAxiosRequestConfig {
  _retry?: boolean
}

export const apiBaseUrl = isHostedMode ? '' : (process.env.NEXT_PUBLIC_API_URL ?? '')
const unsafeMethods = new Set(['post', 'put', 'patch', 'delete'])

/// 不含攔截器的 Client，避免取得 CSRF Token 或 refresh 時遞迴。
const sessionClient = axios.create({
  baseURL: apiBaseUrl,
  withCredentials: true,
  headers: { 'Content-Type': 'application/json' },
  timeout: 15000,
})

let csrfToken: string | null = null
let csrfRequest: Promise<string> | null = null
let csrfGeneration = 0
let refreshRequest: Promise<void> | null = null

/// 身分 Cookie 改變時清除 claims-bound CSRF token，並隔離舊的 in-flight request。
export function invalidateCsrfToken(): void {
  csrfGeneration++
  csrfToken = null
  csrfRequest = null
}

async function getCsrfToken(): Promise<string> {
  if (csrfToken) return csrfToken

  if (csrfRequest) return csrfRequest

  const generation = csrfGeneration
  const request = sessionClient
    .get<ApiResponse<CsrfTokenResponse>>('/api/auth/csrf')
    .then(({ data }) => {
      if (!data.success || !data.data?.token) {
        throw new Error('無法取得 CSRF Token')
      }
      if (generation === csrfGeneration) {
        csrfToken = data.data.token
      }
      return data.data.token
    })
    .finally(() => {
      if (csrfRequest === request) {
        csrfRequest = null
      }
    })

  csrfRequest = request
  return request
}

async function refreshSession(): Promise<void> {
  invalidateCsrfToken()
  const token = await getCsrfToken()
  await sessionClient.post('/api/auth/refresh', undefined, {
    headers: { 'X-XSRF-TOKEN': token },
  })
  invalidateCsrfToken()
}

async function refreshSessionAcrossTabs(): Promise<void> {
  if (typeof navigator === 'undefined' || !navigator.locks) {
    await refreshSession()
    return
  }

  await navigator.locks.request('family-portal-session-refresh', async () => {
    // 其他分頁可能已在等待期間完成輪替；先用共享 Cookie 驗證，避免重放舊 Refresh Token。
    try {
      await sessionClient.get('/api/auth/me')
      invalidateCsrfToken()
    } catch (error) {
      if (axios.isAxiosError(error) && error.response?.status === 401) {
        await refreshSession()
        return
      }
      throw error
    }
  })
}

/// Axios API Client — 憑證只透過 HttpOnly Cookie 傳送。
const api = axios.create({
  baseURL: apiBaseUrl,
  withCredentials: true,
  headers: { 'Content-Type': 'application/json' },
  timeout: 15000,
})

api.interceptors.request.use(async (config) => {
  if (isHostedMode) {
    if (config.url && /^\/api\/(?:auth|members|itineraries|events)(?:\/|$)/.test(config.url)) {
      config.url = config.url.replace('/api/', '/api/hosted/')
    }
    return config
  }
  if (unsafeMethods.has(config.method?.toLowerCase() ?? '')) {
    config.headers.set('X-XSRF-TOKEN', await getCsrfToken())
  }
  return config
})

api.interceptors.response.use(
  (response) => {
    if (response.config.url?.startsWith('/api/auth/login') ||
        response.config.url?.startsWith('/api/auth/logout')) {
      invalidateCsrfToken()
    }
    return response
  },
  async (error: AxiosError) => {
    const originalRequest = error.config as RetryableRequestConfig | undefined
    if (isHostedMode) {
      // 登入頁自己處理登入失敗；資料請求 401 代表本機身分已失效。
      if (error.response?.status === 401 && originalRequest?.url?.startsWith('/api/hosted/') &&
          !originalRequest.url.startsWith('/api/hosted/auth/')) {
        useAuthStore.getState().logout()
      }
      return Promise.reject(error)
    }
    const isAuthRequest = originalRequest?.url?.startsWith('/api/auth/login') ||
      originalRequest?.url?.startsWith('/api/auth/refresh') ||
      originalRequest?.url?.startsWith('/api/auth/logout')

    if (originalRequest?.url?.startsWith('/api/auth/logout')) {
      invalidateCsrfToken()
    }

    if (error.response?.status === 401 && originalRequest && !originalRequest._retry && !isAuthRequest) {
      originalRequest._retry = true

      try {
        refreshRequest ??= refreshSessionAcrossTabs().finally(() => {
          refreshRequest = null
        })
        await refreshRequest
        return api(originalRequest)
      } catch {
        invalidateCsrfToken()
        useAuthStore.getState().logout()
        if (typeof window !== 'undefined' && window.location.pathname !== '/login') {
          // 舊版會話失效時刻意完整換頁，丟棄目前頁面的私人記憶體快取。
          // eslint-disable-next-line @next/next/no-location-assign-relative-destination
          window.location.assign('/login')
        }
      }
    }

    return Promise.reject(error)
  }
)

export default api
