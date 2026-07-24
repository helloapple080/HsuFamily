import axios from 'axios'

/// React Query 唯針對短暫網路錯誤與伺服器錯誤重試。
export function shouldRetryQuery(failureCount: number, error: unknown): boolean {
  if (failureCount >= 2) return false

  if (!axios.isAxiosError(error) || axios.isCancel(error)) return false

  if (!error.response) return true

  return error.response.status >= 500 && error.response.status < 600
}
