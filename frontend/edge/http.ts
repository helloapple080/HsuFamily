import type { ApiResponse } from '@/types/api'

export function portalResponse<T>(status: number, message: string, data: T | null = null, errors: string[] | null = null): Response {
  const body: ApiResponse<T> = { success: status < 400, message, data, errors }
  return Response.json(body, { status, headers: {
    'Cache-Control': 'private, no-store',
    'Cross-Origin-Resource-Policy': 'same-origin',
    'X-Content-Type-Options': 'nosniff',
    'X-Frame-Options': 'DENY',
  } })
}

export function requireVersion(request: Request): number | null {
  const value = request.headers.get('If-Match')
  if (!value || !/^"[1-9][0-9]{0,15}"$/.test(value)) return null
  const version = Number(value.slice(1, -1))
  return Number.isSafeInteger(version) ? version : null
}

export async function readJson(request: Request, limit = 16 * 1024): Promise<{ ok: true; value: unknown } | { ok: false; response: Response }> {
  if (request.headers.get('Content-Type')?.split(';')[0].trim().toLowerCase() !== 'application/json' || !request.body) {
    return { ok: false, response: portalResponse(415, '請使用 JSON 格式') }
  }
  if (Number(request.headers.get('Content-Length')) > limit) {
    return { ok: false, response: portalResponse(413, '請求超過大小限制') }
  }

  const reader = request.body.getReader()
  const decoder = new TextDecoder('utf-8', { fatal: true })
  let size = 0
  let text = ''
  try {
    while (true) {
      const { done, value } = await reader.read()
      if (done) break
      size += value.byteLength
      if (size > limit) {
        await reader.cancel().catch(() => undefined)
        return { ok: false, response: portalResponse(413, '請求超過大小限制') }
      }
      text += decoder.decode(value, { stream: true })
    }
    return { ok: true, value: JSON.parse(text + decoder.decode()) }
  } catch {
    return { ok: false, response: portalResponse(400, '請求格式不正確') }
  } finally {
    reader.releaseLock()
  }
}
