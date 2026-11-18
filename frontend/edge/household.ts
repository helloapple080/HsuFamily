import { z } from 'zod'
import { householdDatesHeader, householdInput, householdLimit, householdNote } from '@/lib/hosted/household-contract'
import { portalResponse, readJson, requireVersion } from './http'

const uuid = z.uuid()
const columns = 'id, category, title, location, details, completed_on AS completedOn, due_on AS dueOn, version, updated_at AS updatedAt'

async function unmatched(db: D1Database, id: string) {
  const current = await db.prepare('SELECT id FROM portal_household_notes WHERE id = ?').bind(id).first()
  return current
    ? portalResponse(409, '備忘已更新，請重新載入後再試', null, ['VERSION_CONFLICT'])
    : portalResponse(404, '找不到備忘')
}

// 僅由已通過 Access、同源及角色檢查的路由呼叫。
export async function handleHousehold(request: Request, rawId: string | undefined, creatorId: string, db: D1Database) {
  const id = rawId?.toLowerCase()
  if (id && !uuid.safeParse(id).success) return portalResponse(400, '備忘識別碼不正確')
  if (new URL(request.url).search) return portalResponse(400, '不支援網址查詢條件')
  const includeDates = request.headers.get(householdDatesHeader) === '1'
  function responseRow(row: unknown) {
    const { completedOn, dueOn, ...legacy } = householdNote.parse(row)
    return includeDates ? { ...legacy, completedOn, dueOn } : legacy
  }
  function reply<T>(status: number, message: string, data: T) {
    const response = portalResponse(status, message, data)
    if (includeDates) response.headers.set(householdDatesHeader, '1')
    return response
  }

  if (request.method === 'GET' && !id) {
    const { results } = await db.prepare(`SELECT ${columns} FROM portal_household_notes ORDER BY updated_at DESC, id DESC LIMIT ?`)
      .bind(householdLimit).all()
    return reply(200, '查詢成功', results.map(responseRow))
  }
  if (request.method === 'DELETE' && id) {
    const version = requireVersion(request)
    if (version === null) return portalResponse(428, '請提供資料版本，重新載入後再試')
    const row = await db.prepare('DELETE FROM portal_household_notes WHERE id = ? AND version = ? RETURNING id')
      .bind(id, version).first()
    return row ? portalResponse(200, '已刪除', true) : unmatched(db, id)
  }
  if ((!id && request.method === 'POST') || (id && request.method === 'PUT')) {
    const version = id ? requireVersion(request) : null
    if (id && version === null) return portalResponse(428, '請提供資料版本，重新載入後再試')
    const body = await readJson(request)
    if (!body.ok) return body.response
    const input = householdInput.safeParse(body.value)
    if (!input.success) return portalResponse(400, '請檢查備忘文字、分類及日期是否正確')
    const { category, title, location, details, completedOn, dueOn } = input.data
    const changeDates = completedOn !== undefined ? 1 : 0
    if (id) {
      const row = await db.prepare(`UPDATE portal_household_notes
        SET category = ?, title = ?, location = ?, details = ?,
            completed_on = CASE WHEN ? THEN ? ELSE completed_on END,
            due_on = CASE WHEN ? THEN ? ELSE due_on END, version = version + 1,
            updated_at = strftime('%Y-%m-%dT%H:%M:%fZ', 'now')
        WHERE id = ? AND version = ? RETURNING ${columns}`)
        .bind(category, title, location, details, changeDates, completedOn ?? null, changeDates, dueOn ?? null, id, version).first()
      return row ? reply(200, '已儲存', responseRow(row)) : unmatched(db, id)
    }
    // 同一個 SQL 敘述檢查上限並新增，避免同時操作超過上限。
    const row = await db.prepare(`INSERT INTO portal_household_notes (id, category, title, location, details, completed_on, due_on, created_by)
      SELECT ?, ?, ?, ?, ?, ?, ?, ? WHERE (SELECT count(*) FROM portal_household_notes) < ? RETURNING ${columns}`)
      .bind(crypto.randomUUID(), category, title, location, details, completedOn ?? null, dueOn ?? null, creatorId, householdLimit).first()
    return row ? reply(201, '已儲存', responseRow(row))
      : portalResponse(409, '備忘已達筆數上限', null, ['HOUSEHOLD_LIMIT'])
  }
  return portalResponse(405, '不支援此操作')
}
