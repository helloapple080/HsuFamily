import { isAxiosError } from 'axios'
import type { ApiResponse } from '@/types/api'

export interface RecordVersion {
  id: string
  version?: number
}

export const versionConflictMessage = '其他人已更新這筆資料。請先保留你輸入的內容，重新載入後再確認並編輯。'

export function isVersionConflict(error: unknown): boolean {
  return isAxiosError<ApiResponse<null>>(error) && error.response?.status === 409
    && Array.isArray(error.response.data?.errors) && error.response.data.errors.includes('VERSION_CONFLICT')
}

/// Next.js／Supabase 的舊回應不含版本；只有提供版本的紀錄才使用條件式寫入。
export function versionHeaders(version?: number) {
  return version === undefined ? undefined : { headers: { 'If-Match': `"${version}"` } }
}
