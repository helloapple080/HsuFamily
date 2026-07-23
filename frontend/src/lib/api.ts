import axios, { AxiosError, type InternalAxiosRequestConfig } from 'axios'
import { useAuthStore } from '@/stores/authStore'
import type { ApiResponse } from '@/types/api'

interface CsrfTokenResponse {
  token: string
}

interface RetryableRequestConfig extends InternalAxiosRequestConfig {
  _retry?: boolean
}

const apiBaseUrl = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5001'
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
let refreshRequest: Promise<void> | null = null

async function getCsrfToken(): Promise<string> {
  if (csrfToken) return csrfToken

  csrfRequest ??= sessionClient
    .get<ApiResponse<CsrfTokenResponse>>('/api/auth/csrf')
    .then(({ data }) => {
      if (!data.success || !data.data?.token) {
        throw new Error('無法取得 CSRF Token')
      }
      csrfToken = data.data.token
      return csrfToken
    })
    .finally(() => {
      csrfRequest = null
    })

  return csrfRequest
}

async function refreshSession(): Promise<void> {
  const token = await getCsrfToken()
  await sessionClient.post('/api/auth/refresh', undefined, {
    headers: { 'X-XSRF-TOKEN': token },
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
  if (unsafeMethods.has(config.method?.toLowerCase() ?? '')) {
    config.headers.set('X-XSRF-TOKEN', await getCsrfToken())
  }
  return config
})

api.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const originalRequest = error.config as RetryableRequestConfig | undefined
    const isAuthRequest = originalRequest?.url?.startsWith('/api/auth/login') ||
      originalRequest?.url?.startsWith('/api/auth/refresh')

    if (error.response?.status === 401 && originalRequest && !originalRequest._retry && !isAuthRequest) {
      originalRequest._retry = true

      try {
        refreshRequest ??= refreshSession().finally(() => {
          refreshRequest = null
        })
        await refreshRequest
        return api(originalRequest)
      } catch {
        useAuthStore.getState().logout()
        if (typeof window !== 'undefined' && window.location.pathname !== '/login') {
          window.location.assign('/login')
        }
      }
    }

    return Promise.reject(error)
  }
)

export default api
