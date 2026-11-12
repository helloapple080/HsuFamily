import { z } from 'zod'
import type { ApiResponse } from '@/types/api'
import type { LoginResponse, UserInfo } from '@/types/auth'
import { worshipInput, type WorshipInput, type WorshipRule } from './worship-contract'

export type { WorshipRule } from './worship-contract'

export interface HostedGateway {
  signIn(email: string, password: string): Promise<LoginResponse | null>
  readUser(): Promise<UserInfo | null>
  signOut(): Promise<boolean>
  listRules(): Promise<WorshipRule[]>
  saveRule(input: WorshipInput, id?: string): Promise<WorshipRule | null>
  deleteRule(id: string): Promise<boolean>
}

const loginInput = z.strictObject({
  username: z.string().trim().max(254).pipe(z.email()),
  password: z.string().min(1).max(128),
})
const ruleId = z.uuid()
const maxBodyBytes = 16 * 1024

export function portalResponse<T>(status: number, message: string, data: T | null = null) {
  const body: ApiResponse<T> = { success: status < 400, message, data, errors: null }
  return Response.json(body, { status, headers: { 'Cache-Control': 'private, no-store' } })
}

async function readJson(request: Request): Promise<{ ok: true; value: unknown } | { ok: false; response: Response }> {
  if (request.headers.get('content-type')?.split(';')[0].trim().toLowerCase() !== 'application/json' || !request.body) {
    return { ok: false, response: portalResponse(415, '請使用 JSON 格式') }
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
      if (size > maxBodyBytes) {
        await reader.cancel()
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

async function login(request: Request, gateway: HostedGateway) {
  const body = await readJson(request)
  if (!body.ok) return body.response
  const input = loginInput.safeParse(body.value)
  if (!input.success) return portalResponse(400, '請輸入有效信箱與密碼')
  const session = await gateway.signIn(input.data.username, input.data.password)
  return session ? portalResponse(200, '登入成功', session) : portalResponse(401, '無法登入，請確認帳密與家人資格')
}

async function handleWorship(request: Request, id: string | undefined, user: UserInfo, gateway: HostedGateway) {
  if (request.method === 'GET' && !id) return portalResponse(200, '查詢成功', await gateway.listRules())
  if (user.role !== 'Admin') return portalResponse(403, '只有管理員可以修改拜拜規則')
  if (id && !ruleId.safeParse(id).success) return portalResponse(400, '規則識別碼不正確')
  if (request.method === 'DELETE' && id) {
    return await gateway.deleteRule(id) ? portalResponse(200, '已刪除', true) : portalResponse(404, '找不到拜拜規則')
  }
  if ((request.method === 'POST' && !id) || (request.method === 'PUT' && id)) {
    const body = await readJson(request)
    if (!body.ok) return body.response
    const input = worshipInput.safeParse(body.value)
    if (!input.success) return portalResponse(400, '請檢查名稱、農曆月日與備註')
    const saved = await gateway.saveRule(input.data, id)
    return saved ? portalResponse(id ? 200 : 201, '已儲存', saved) : portalResponse(404, '找不到拜拜規則')
  }
  return portalResponse(405, '不支援的操作')
}

/// HTTP 驗證不信任瀏覽器角色、轉送 Host 或未驗證 Cookie。
export async function handleHostedRequest(
  request: Request, segments: readonly string[], origin: string, getGateway: () => Promise<HostedGateway>,
): Promise<Response> {
  if (request.method !== 'GET' && request.headers.get('origin') !== origin) {
    return portalResponse(403, '不允許跨網站操作')
  }
  const [area, action] = segments
  if (segments.length > 2 || (area !== 'auth' && area !== 'worship')) return portalResponse(404, '找不到功能')
  try {
    const gateway = await getGateway()
    if (area === 'auth' && action === 'login' && request.method === 'POST') return await login(request, gateway)
    if (area === 'auth' && action === 'logout' && request.method === 'POST') {
      const revoked = await gateway.signOut()
      return portalResponse(200, revoked ? '已登出' : '此裝置已登出，但未能確認遠端會話撤銷', revoked)
    }
    const user = await gateway.readUser()
    if (!user) return portalResponse(401, '請使用核准的家人帳號登入')
    if (area === 'auth') {
      return action === 'me' && request.method === 'GET'
        ? portalResponse(200, '查詢成功', user) : portalResponse(404, '找不到功能')
    }
    return await handleWorship(request, action, user, gateway)
  } catch {
    return portalResponse(503, '服務暫時無法使用，請稍後重試')
  }
}
