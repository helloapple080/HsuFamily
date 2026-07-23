import axios from 'axios'

/// Axios API Client 實體 — 統一管理 API 請求
const api = axios.create({
  baseURL: process.env.NEXT_PUBLIC_API_URL || 'http://localhost:5001',
  headers: {
    'Content-Type': 'application/json',
  },
  timeout: 15000,
})

/// 請求攔截器 — 自動附加 JWT Token
api.interceptors.request.use(
  (config) => {
    // 在 Client 端從 Zustand store 取得 Token
    if (typeof window !== 'undefined') {
      const authState = JSON.parse(
        localStorage.getItem('auth-storage') || '{}'
      )
      const token = authState?.state?.accessToken
      if (token) {
        config.headers.Authorization = `Bearer ${token}`
      }
    }
    return config
  },
  (error) => Promise.reject(error)
)

/// 回應攔截器 — 處理 401 (Token 過期) 自動重新取得
api.interceptors.response.use(
  (response) => response,
  async (error) => {
    const originalRequest = error.config

    // 避免無限重試
    if (error.response?.status === 401 && !originalRequest._retry) {
      originalRequest._retry = true

      try {
        const authState = JSON.parse(
          localStorage.getItem('auth-storage') || '{}'
        )
        const { accessToken, refreshToken } = authState?.state || {}

        if (accessToken && refreshToken) {
          const response = await axios.post(
            `${api.defaults.baseURL}/api/auth/refresh`,
            { accessToken, refreshToken }
          )

          if (response.data?.success) {
            const newToken = response.data.data.accessToken
            // 更新 localStorage（Zustand persist）
            authState.state.accessToken = response.data.data.accessToken
            authState.state.refreshToken = response.data.data.refreshToken
            localStorage.setItem('auth-storage', JSON.stringify(authState))

            originalRequest.headers.Authorization = `Bearer ${newToken}`
            return api(originalRequest)
          }
        }
      } catch {
        // Refresh 失敗，清除登入狀態
        localStorage.removeItem('auth-storage')
        if (typeof window !== 'undefined') {
          window.location.href = '/login'
        }
      }
    }

    return Promise.reject(error)
  }
)

export default api
