import { z } from 'zod'
import type { ApiResponse } from '@/types/api'
import type { LoginResponse, UserInfo } from '@/types/auth'
import { worshipInput, type WorshipInput, type WorshipRule } from './worship-contract'
import { itineraryInput, itineraryQuery, memberInput, type RecordKind } from './family-records'
import type { FamilyStore } from './family-store'
import { maxPhotoSlots } from './photo-contract'
import type { PhotoStore } from './photo-store'
import { encodePhoto } from './photo-upload'
import { eventInput, eventQuery } from './event-contract'
import type { createEventStore } from './event-store'

export type { WorshipRule } from './worship-contract'

export interface HostedGateway {
  signIn(email: string, password: string): Promise<LoginResponse | null>
  readUser(): Promise<UserInfo | null>
  signOut(): Promise<boolean>
  listRules(): Promise<WorshipRule[]>
  saveRule(input: WorshipInput, id?: string): Promise<WorshipRule | null>
  deleteRule(id: string): Promise<boolean>
  family?: FamilyStore
  photos?: PhotoStore
  events?: ReturnType<typeof createEventStore>
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

async function readJson(request: Request, limit = maxBodyBytes): Promise<{ ok: true; value: unknown } | { ok: false; response: Response }> {
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
      if (size > limit) {
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

async function handleFamily(request: Request, kind: RecordKind, id: string | undefined, user: UserInfo, family: FamilyStore | undefined) {
  const read = request.method === 'GET' && (!id || (kind === 'itineraries' && id === 'search'))
  if (!read && user.role !== 'Admin') return portalResponse(403, '只有管理員可以修改家族資料')
  if (!family) return portalResponse(503, '家族資料服務尚未設定')
  if (read) {
    const params = new URL(request.url).searchParams
    const options = itineraryQuery.safeParse({
      keyword: params.get('keyword') ?? undefined, limit: params.get('limit') ?? params.get('pageSize') ?? undefined,
      cursorDate: params.get('cursorDate') ?? undefined, cursorId: params.get('cursorId') ?? undefined,
    })
    if (!options.success) return portalResponse(400, '搜尋或分頁條件不正確')
    return portalResponse(200, '查詢成功', await family.list(kind, options.data))
  }
  if (id && !ruleId.safeParse(id).success) return portalResponse(400, '資料識別碼不正確')
  if (request.method === 'DELETE' && id) {
    return await family.remove(kind, id) ? portalResponse(200, '已刪除', true) : portalResponse(404, '找不到家族資料')
  }
  if ((request.method === 'POST' && !id) || (request.method === 'PUT' && id)) {
    const body = await readJson(request, 128 * 1024)
    if (!body.ok) return body.response
    const input = (kind === 'members' ? memberInput : itineraryInput).safeParse(body.value)
    if (!input.success) return portalResponse(400, '請檢查名稱、日期與欄位長度')
    const saved = await family.save(kind, input.data, id)
    return saved ? portalResponse(id ? 200 : 201, '已儲存', saved) : portalResponse(404, '找不到家族資料')
  }
  return portalResponse(405, '不支援的操作')
}

async function handlePhotos(request: Request, path: readonly string[], user: UserInfo, photos: PhotoStore | undefined) {
  if (request.method !== 'GET' && user.role !== 'Admin') return portalResponse(403, '只有管理員可以更動照片')
  const [kind, rawId, rawSlot] = path
  const id = rawId?.toLowerCase()
  if ((kind !== 'members' && kind !== 'itineraries' && kind !== 'worship') || !ruleId.safeParse(id).success) {
    return portalResponse(400, '照片所屬紀錄不正確')
  }
  const slot = rawSlot ? Number(rawSlot) : undefined
  if (rawSlot && (!/^[1-5]$/.test(rawSlot) || !slot || slot > maxPhotoSlots(kind))) return portalResponse(400, '照片位置不正確')
  if (!photos) return portalResponse(503, '照片服務尚未設定')
  if (!(await photos.targetExists(kind, id))) return portalResponse(404, '找不到照片所屬紀錄')
  if (request.method === 'GET') {
    if (!slot) return portalResponse(200, '查詢成功', await photos.list(kind, id))
    const photo = await photos.get(kind, id, slot)
    if (!photo) return portalResponse(404, '找不到照片')
    return new Response(photo, { headers: {
      'Content-Type': 'image/webp', 'Cache-Control': 'private, no-store',
      'Cross-Origin-Resource-Policy': 'same-origin', 'X-Content-Type-Options': 'nosniff',
    } })
  }
  if (!slot) return portalResponse(405, '請指定照片位置')
  if (request.method === 'PUT') {
    const result = await encodePhoto(request)
    if (!result.ok) return portalResponse(result.status, result.message)
    if (!await photos.put(kind, id, slot, result.bytes, user.id)) return portalResponse(409, '這個照片位置已被使用')
    // Storage 與應用資料分開提交；補償在上傳期間被刪除的紀錄。
    if (!await photos.targetExists(kind, id)) {
      await photos.remove(kind, id, slot)
      return portalResponse(409, '照片所屬紀錄已刪除')
    }
    return portalResponse(200, '照片已儲存', true)
  }
  if (request.method === 'DELETE') {
    await photos.remove(kind, id, slot)
    return portalResponse(200, '照片已刪除', true)
  }
  return portalResponse(405, '不支援的操作')
}

async function handleEvents(request: Request, id: string | undefined, user: UserInfo, store: HostedGateway['events']) {
  if (request.method !== 'GET' && user.role !== 'Admin') return portalResponse(403, '只有管理員可以修改活動')
  if (!store) return portalResponse(503, '活動資料服務尚未設定')
  if (id && !ruleId.safeParse(id).success) return portalResponse(400, '活動識別碼不正確')
  if (request.method === 'GET') {
    if (id) {
      const event = await store.get(id)
      return event ? portalResponse(200, '查詢成功', event) : portalResponse(404, '找不到活動')
    }
    const params = new URL(request.url).searchParams
    const window = eventQuery.safeParse({ start: params.get('start'), end: params.get('end') })
    if (!window.success) return portalResponse(400, '活動查詢的起迄日期不正確')
    return portalResponse(200, '查詢成功', await store.list(window.data.start, window.data.end))
  }
  if (request.method === 'DELETE' && id) {
    return await store.remove(id) ? portalResponse(200, '已刪除', true) : portalResponse(404, '找不到活動')
  }
  if ((request.method === 'POST' && !id) || (request.method === 'PUT' && id)) {
    const body = await readJson(request, 128 * 1024)
    if (!body.ok) return body.response
    const input = eventInput.safeParse(body.value)
    if (!input.success) return portalResponse(400, '請檢查活動日期、欄位與重複週期')
    const saved = await store.save(input.data, id)
    return saved ? portalResponse(id ? 200 : 201, '已儲存', saved) : portalResponse(404, '找不到活動')
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
  if (area === 'photos' ? segments.length < 3 || segments.length > 4
    : segments.length > 2 || (area !== 'auth' && area !== 'worship' && area !== 'members' && area !== 'itineraries' && area !== 'events')) {
    return portalResponse(404, '找不到功能')
  }
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
    if (area === 'members' || area === 'itineraries') return await handleFamily(request, area, action, user, gateway.family)
    if (area === 'photos') return await handlePhotos(request, segments.slice(1), user, gateway.photos)
    if (area === 'events') return await handleEvents(request, action, user, gateway.events)
    return await handleWorship(request, action, user, gateway)
  } catch (error) {
    if (request.method === 'DELETE' && typeof error === 'object' && error !== null && 'code' in error && error.code === '23503' &&
        'message' in error && error.message === 'photos must be removed first') {
      return portalResponse(409, '請先移除這筆紀錄的照片，再刪除紀錄')
    }
    return portalResponse(503, '服務暫時無法使用，請稍後重試')
  }
}
