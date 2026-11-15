import { z } from 'zod'
import { dayMs, eventInput, eventQuery } from '@/lib/hosted/event-contract'
import { expandCalendarEvent } from '@/lib/hosted/event-recurrence'
import { itineraryInput, itineraryQuery, memberInput, readFamilyRecord, toRecordValues } from '@/lib/hosted/family-records'
import { worshipInput } from '@/lib/hosted/worship-contract'
import type { UserInfo } from '@/types/auth'
import type { CalendarEvent } from '@/types/event'
import { portalResponse, readJson, requireVersion } from './http'

export type RecordArea = 'members' | 'itineraries' | 'worship' | 'events'
const uuid = z.uuid()
const columns = 'id, version, payload, created_by, created_at, updated_at'
const listLimit = 500
const eventResponseLimit = 1_000
const bodySchemas = { members: memberInput, itineraries: itineraryInput, worship: worshipInput, events: eventInput }
const bodyErrors = {
  members: '請檢查名稱、日期與欄位長度',
  itineraries: '請檢查名稱、日期與欄位長度',
  worship: '請檢查名稱、農曆月日與備註',
  events: '請檢查活動日期、欄位與重複週期',
}
const rowSchema = z.object({
  id: uuid, version: z.number().int().positive(), payload: z.string(),
  created_by: uuid, created_at: z.string(), updated_at: z.string(),
})
const eventCandidate = rowSchema.pick({ id: true }).extend({ is_source: z.union([z.literal(0), z.literal(1)]) })

function decodeRow(row: unknown) {
  const { payload, ...metadata } = rowSchema.parse(row)
  const details: unknown = JSON.parse(payload)
  return { ...metadata, details }
}

function readEvent(row: unknown): CalendarEvent & { version: number } {
  const metadata = decodeRow(row)
  const event = eventInput.parse(metadata.details)
  return {
    ...event, id: metadata.id, version: metadata.version,
    endDate: event.endDate ?? null, createdBy: metadata.created_by,
    createdAt: metadata.created_at, updatedAt: metadata.updated_at,
    isOccurrence: false, sourceRuleId: null,
  }
}

function readRecord(area: RecordArea, row: unknown) {
  if (area === 'events') return readEvent(row)
  const metadata = decodeRow(row)
  return area === 'worship'
    ? { ...worshipInput.parse(metadata.details), id: metadata.id, version: metadata.version }
    : { ...readFamilyRecord(area, metadata), version: metadata.version }
}

function checkedParams(request: Request, allowed: readonly string[]) {
  const params = new URL(request.url).searchParams
  const seen = new Set<string>()
  let valid = true
  params.forEach((value, key) => {
    if (!allowed.includes(key) || seen.has(key) || value.length > 200) valid = false
    seen.add(key)
  })
  return valid ? params : null
}

async function listItineraries(request: Request, db: D1Database) {
  // 舊分頁請求可能附上空白 query；有內容的搜尋一律使用 keyword。
  const params = checkedParams(request, ['query', 'keyword', 'limit', 'pageSize', 'cursorDate', 'cursorId'])
  if (!params) return portalResponse(400, '搜尋或分頁條件不正確')
  const query = itineraryQuery.safeParse({
    keyword: params.get('keyword') ?? undefined, limit: params.get('limit') ?? params.get('pageSize') ?? undefined,
    cursorDate: params.get('cursorDate') ?? undefined, cursorId: params.get('cursorId') ?? undefined,
  })
  if (!query.success) return portalResponse(400, '搜尋或分頁條件不正確')
  const { keyword, limit, cursorDate, cursorId } = query.data
  // 相容 PostgREST 版移除 * 的行為；instr 不把 % 或 _ 視為萬用字元。
  const needle = keyword?.replaceAll('*', '').toLowerCase()
  if (needle === '') return portalResponse(200, '查詢成功', [])
  const conditions: string[] = ["kind = 'itineraries'"]
  const args: (string | number)[] = []
  if (cursorDate && cursorId) {
    conditions.push('(itinerary_date, id) < (?, ?)')
    args.push(cursorDate, cursorId.toLowerCase())
  }
  if (needle) {
    conditions.push('instr(search_text, ?) > 0')
    args.push(needle)
  }
  const { results } = await db.prepare(`SELECT ${columns} FROM portal_records WHERE ${conditions.join(' AND ')}
    ORDER BY itinerary_date DESC, id DESC LIMIT ?`).bind(...args, limit).all()
  return portalResponse(200, '查詢成功', results.map((row) => readRecord('itineraries', row)))
}

async function listEvents(request: Request, db: D1Database) {
  const params = checkedParams(request, ['start', 'end'])
  const range = eventQuery.safeParse({ start: params?.get('start'), end: params?.get('end') })
  if (!range.success) return portalResponse(400, '活動查詢的起迄日期不正確')
  const { start, end } = range.data
  // 來源活動獨立於重複規則的生效區間；跨日重複活動可在查詢範圍之前開始。
  const repeatFloor = new Date(Date.parse(start) - 366 * dayMs).toISOString()
  // 一次 SQL 查詢取得同一版本的來源與候選；各分支獨立限制讀回筆數。
  const { results } = await db.prepare(`WITH source AS (
      SELECT ${columns} FROM portal_records WHERE kind = 'events' AND event_overlap_end >= ?
        AND event_start <= ? LIMIT ?
    ), repeating AS (
      SELECT ${columns} FROM portal_records WHERE kind = 'events' AND repeat_until >= ?
        AND repeat_from <= ? LIMIT ?
    )
    SELECT 1 AS is_source, ${columns} FROM source
    UNION ALL
    SELECT 0 AS is_source, ${columns} FROM repeating`)
    .bind(start, end, listLimit + 1, repeatFloor, end, listLimit + 1).all()
  const overLimit = () => portalResponse(413, '行事曆活動過多，請縮小查詢範圍')
  const sourceRows: unknown[] = []
  const repeating: { id: string; row: unknown }[] = []
  for (const row of results) {
    const candidate = eventCandidate.parse(row)
    if (candidate.is_source) sourceRows.push(row)
    else repeating.push({ id: candidate.id, row })
  }
  if (sourceRows.length > listLimit || repeating.length > listLimit) return overLimit()
  const events = sourceRows.map(readEvent)
  const sourceById = new Map(events.map((event) => [event.id, event]))
  for (const { id, row } of repeating) {
    const event = sourceById.get(id) ?? readEvent(row)
    for (const occurrence of expandCalendarEvent(event, start, end)) {
      if (!occurrence.isOccurrence) continue
      events.push({ ...occurrence, version: event.version })
      if (events.length > eventResponseLimit) return overLimit()
    }
  }
  events.sort((a, b) => a.startDate.localeCompare(b.startDate) || a.id.localeCompare(b.id))
  return portalResponse(200, '查詢成功', events)
}

async function listRecords(request: Request, area: RecordArea, user: UserInfo, db: D1Database) {
  if (area === 'itineraries') return listItineraries(request, db)
  if (area === 'events') return listEvents(request, db)
  if (!checkedParams(request, [])) return portalResponse(400, '查詢條件不正確')
  const query = area === 'members'
    ? `WHERE kind = 'members'${user.role === 'Admin' ? '' : ' AND active = 1'} ORDER BY member_position, id`
    : "WHERE kind = 'worship' ORDER BY created_at, id"
  const { results } = await db.prepare(`SELECT ${columns} FROM portal_records ${query} LIMIT ?`).bind(listLimit).all()
  return portalResponse(200, '查詢成功', results.map((row) => readRecord(area, row)))
}

function photosPreventDeletion(error: unknown): boolean {
  return error instanceof Error && (
    /FOREIGN KEY constraint failed|photos must be removed first/i.test(error.message) ||
    (error.cause instanceof Error && /FOREIGN KEY constraint failed|photos must be removed first/i.test(error.cause.message))
  )
}

async function unmatchedVersion(db: D1Database, area: RecordArea, id: string) {
  const current = await db.prepare('SELECT id FROM portal_records WHERE kind = ? AND id = ?').bind(area, id).first()
  return current
    ? portalResponse(409, '資料已更新，請重新載入後再試', null, ['VERSION_CONFLICT'])
    : portalResponse(404, '找不到資料')
}

export async function handleRecords(
  request: Request, area: RecordArea, rawId: string | undefined, user: UserInfo, db: D1Database,
): Promise<Response> {
  const method = request.method
  const itinerarySearch = method === 'GET' && area === 'itineraries' && rawId === 'search'
  const id = itinerarySearch ? undefined : rawId?.toLowerCase()
  if (user.role !== 'Admin' && (user.role !== 'User' || method !== 'GET')) {
    return portalResponse(403, '只有管理員可以修改家族資料')
  }
  if (id && !uuid.safeParse(id).success) return portalResponse(400, '資料識別碼不正確')
  if (method === 'GET') {
    if (!id) return listRecords(request, area, user, db)
    if (area !== 'events') return portalResponse(405, '不支援的操作')
    if (!checkedParams(request, [])) return portalResponse(400, '查詢條件不正確')
    const row = await db.prepare(`SELECT ${columns} FROM portal_records WHERE kind = 'events' AND id = ?`).bind(id).first()
    return row ? portalResponse(200, '查詢成功', readEvent(row)) : portalResponse(404, '找不到活動')
  }

  if (id && method === 'DELETE') {
    const version = requireVersion(request)
    if (version === null) return portalResponse(428, '請提供資料版本，重新載入後再試')
    try {
      const row = await db.prepare('DELETE FROM portal_records WHERE kind = ? AND id = ? AND version = ? RETURNING id')
        .bind(area, id, version).first()
      return row ? portalResponse(200, '已刪除', true) : unmatchedVersion(db, area, id)
    } catch (error) {
      if (photosPreventDeletion(error)) return portalResponse(409, '請先移除這筆紀錄的照片，再刪除紀錄')
      throw error
    }
  }
  if ((!id && method === 'POST') || (id && method === 'PUT')) {
    const version = id ? requireVersion(request) : null
    if (id && version === null) return portalResponse(428, '請提供資料版本，重新載入後再試')
    const body = await readJson(request, area === 'worship' ? 16 * 1024 : 128 * 1024)
    if (!body.ok) return body.response
    const input = bodySchemas[area].safeParse(body.value)
    if (!input.success) return portalResponse(400, bodyErrors[area])
    const payload = JSON.stringify(input.data)
    const active = 'isActive' in input.data && !input.data.isActive ? 0 : 1
    const searchText = 'participants' in input.data
      ? toRecordValues('itineraries', input.data).search_text.toLowerCase() : null
    const row = id
      ? await db.prepare(`UPDATE portal_records SET payload = ?, active = ?, search_text = ?, version = version + 1,
          updated_at = strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
          WHERE kind = ? AND id = ? AND version = ? RETURNING ${columns}`)
        .bind(payload, active, searchText, area, id, version).first()
      : await db.prepare(`INSERT INTO portal_records (id, kind, active, payload, created_by, search_text)
          VALUES (?, ?, ?, ?, ?, ?) RETURNING ${columns}`)
        .bind(crypto.randomUUID(), area, active, payload, user.id, searchText).first()
    if (row) return portalResponse(id ? 200 : 201, '已儲存', readRecord(area, row))
    if (id) return unmatchedVersion(db, area, id)
    throw new Error('新增紀錄未回傳結果')
  }
  return portalResponse(405, '不支援的操作')
}
