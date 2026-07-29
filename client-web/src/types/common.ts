/** 全APIで共通のレスポンス形式。 */
export interface ApiResponse<T> {
  success: boolean
  data: T | null
  error: ApiError | null
  traceId: string | null
}

export interface ApiError {
  code: string
  message: string
}

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}
