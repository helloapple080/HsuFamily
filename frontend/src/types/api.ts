/// API 統一回應格式
export interface ApiResponse<T> {
  success: boolean
  message: string
  data: T | null
  errors: string[] | null
}

/// 分頁游標
export interface PaginationCursor {
  cursorDate?: string
  cursorId?: string
  pageSize: number
}
