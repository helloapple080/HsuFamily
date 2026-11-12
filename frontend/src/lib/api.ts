import axios, { type AxiosError } from 'axios'
import { useAuthStore } from '@/stores/authStore'

/// 所有請求使用同源入口；會話只透過 HttpOnly Cookie 傳送。
const api = axios.create({
  withCredentials: true,
  headers: { 'Content-Type': 'application/json' },
  timeout: 15000,
})

api.interceptors.response.use(undefined, (error: AxiosError) => {
  const url = error.config?.url
  // 登入錯誤由頁面處理；受保護的資料請求 401 則清除本機身分。
  if (error.response?.status === 401 && url?.startsWith('/api/hosted/') && !url.startsWith('/api/hosted/auth/')) {
    useAuthStore.getState().logout()
  }
  return Promise.reject(error)
})

export default api
