// @vitest-environment node

import { readFileSync } from 'node:fs'
import { DatabaseSync, type SQLInputValue } from 'node:sqlite'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { z } from 'zod'
import type { ApiResponse } from '@/types/api'
import type { UserInfo } from '@/types/auth'
import { handleRecords, type RecordArea } from '../records'

const migration = new URL('../migrations/0001_portal.sql', import.meta.url)
const photoMigration = new URL('../migrations/0002_photos.sql', import.meta.url)
const fixtureRow = z.looseObject({ id: z.uuid(), version: z.number().int().positive() })
const admin: UserInfo = {
  id: '930e8ea2-f061-41de-8299-8b20ff74f86a', username: 'admin', email: 'admin@example.test',
  displayName: '管理員', role: 'Admin',
}
const reader: UserInfo = { ...admin, role: 'User' }
const member = { name: '家人', title: '稱謂', birthday: '2026-07-18T23:00:00-07:00', sortOrder: 3, isActive: true }
const worship = { title: '初一', lunarMonth: null, lunarDay: 1, leapMonthPolicy: 'Both', notes: '' }
const itinerary = { title: '臺北 100%_', date: '2026-07-18', tags: ['假資料'], participants: [] }
const event = {
  title: '家人聚餐', eventType: 'General', startDate: '2026-05-12T10:00:00+08:00',
  endDate: '2026-05-12T12:30:00+08:00', isAllDay: false, isLunarDate: false,
  relatedMemberIds: [], tags: [],
}

let db: DatabaseSync
beforeEach(() => {
  db = new DatabaseSync(':memory:')
})
afterEach(() => db.close())

function migrate() {
  db.exec(readFileSync(migration, 'utf8'))
  db.prepare('INSERT INTO portal_access (id, email, role) VALUES (?, ?, ?)').run(admin.id, admin.email, admin.role)
}

function sqliteBinding(): D1Database {
  return {
    prepare(sql: string) {
      const statement = db.prepare(sql)
      function bound(parameters: SQLInputValue[]) {
        return {
          bind: (...values: SQLInputValue[]) => bound(values),
          first: async () => statement.get(...parameters) ?? null,
          all: async () => ({ results: statement.all(...parameters) }),
          run: async () => ({ meta: statement.run(...parameters) }),
        }
      }
      return bound([])
    },
  } as unknown as D1Database
}

async function call(area: RecordArea, suffix = '', options: { method?: string; body?: unknown; match?: string; user?: UserInfo } = {}) {
  const request = new Request(`https://portal.example.test/api/hosted/${area}${suffix}`, {
    method: options.method ?? 'GET',
    headers: {
      ...(options.body !== undefined ? { 'Content-Type': 'application/json' } : {}),
      ...(options.match !== undefined ? { 'If-Match': options.match } : {}),
    },
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
  })
  const id = new URL(request.url).pathname.split('/')[4]
  const response = await handleRecords(request, area, id, options.user ?? admin, sqliteBinding())
  const { data, errors } = await response.json() as ApiResponse<unknown>
  return { response, data, errors }
}

describe('D1 初始資料表', () => {
  it('拒絕非正規信箱、不合法角色、非物件內容和無效資料版本', () => {
    migrate()
    const otherId = 'a7690682-b274-45a2-8532-950b8ee15d0e'
    const access = db.prepare('INSERT INTO portal_access (id, email, role) VALUES (?, ?, ?)')
    expect(() => access.run(otherId, 'Upper@example.test', 'User')).toThrow(/CHECK/)
    expect(() => access.run(otherId, 'other@example.test', 'Owner')).toThrow(/CHECK/)
    expect(() => access.run(otherId, admin.email, 'User')).toThrow(/UNIQUE/)
    const record = db.prepare('INSERT INTO portal_records (id, kind, payload, created_by, version) VALUES (?, ?, ?, ?, ?)')
    expect(() => record.run(otherId, 'worship', '[]', admin.id, 1)).toThrow(/CHECK/)
    expect(() => record.run(otherId, 'worship', JSON.stringify(worship), admin.id, 0)).toThrow(/CHECK/)
    expect(() => record.run(otherId, 'worship', JSON.stringify(worship), otherId, 1)).toThrow(/FOREIGN KEY/)
  })

  it('不允許成員內容與可見性欄位不一致', () => {
    migrate()
    const record = db.prepare('INSERT INTO portal_records (id, kind, active, payload, created_by) VALUES (?, ?, ?, ?, ?)')
    const id = 'a7690682-b274-45a2-8532-950b8ee15d0e'
    expect(() => record.run(id, 'members', 0, JSON.stringify({ ...member, birthday: '2026-07-18' }), admin.id)).toThrow(/CHECK/)
    expect(() => record.run(id, 'worship', 0, JSON.stringify(worship), admin.id)).toThrow(/CHECK/)
  })

  it('依日期與識別碼的行程分頁可以直接使用索引', () => {
    migrate()
    const plan = db.prepare(`EXPLAIN QUERY PLAN SELECT id, payload FROM portal_records
      WHERE kind = 'itineraries' AND (itinerary_date, id) < (?, ?)
      ORDER BY itinerary_date DESC, id DESC LIMIT ?`).all(itinerary.date, admin.id, 20)
    expect(plan.map(({ detail }) => detail).join(' ')).toContain('portal_itineraries_cursor')
    expect(plan.map(({ detail }) => detail).join(' ')).not.toContain('TEMP B-TREE')
  })
})

describe('D1 家族資料 API', () => {
  it('僅對管理員列出停用成員，並保留生日的民用日期', async () => {
    migrate()
    const inactive = await call('members', '', { method: 'POST', body: { ...member, name: '停用', isActive: false, sortOrder: 0 } })
    const active = await call('members', '', { method: 'POST', body: member })
    expect(active.response.status).toBe(201)
    expect(active.response.headers.get('Cache-Control')).toBe('private, no-store')
    expect(active.data).toMatchObject({ birthday: '2026-07-18', version: 1 })
    expect(fixtureRow.array().parse((await call('members')).data).map((row) => row.id))
      .toEqual([fixtureRow.parse(inactive.data).id, fixtureRow.parse(active.data).id])
    expect((await call('members', '', { user: reader })).data).toEqual([active.data])
    expect((await call('members', '', { method: 'POST', body: member, user: reader })).response.status).toBe(403)
  })

  it('行程搜尋依字面比對並使用日期與識別碼組成的游標', async () => {
    migrate()
    const first = await call('itineraries', '', { method: 'POST', body: { ...itinerary, title: '臺北 100%_ Aa' } })
    const second = await call('itineraries', '', { method: 'POST', body: { ...itinerary, title: '臺北 100%_ bb' } })
    await call('itineraries', '', { method: 'POST', body: { ...itinerary, title: '臺北 100 XY' } })
    const ordered = fixtureRow.array().parse([first.data, second.data]).sort((a, b) => b.id.localeCompare(a.id))
    const page = await call('itineraries', '/search?keyword=%25_&pageSize=1')
    expect(page.data).toEqual([ordered[0]])
    expect(fixtureRow.array().parse(page.data)[0]).toMatchObject({ photos: [], createdBy: admin.id, version: 1 })
    expect((await call('itineraries', `/search?keyword=%25_&limit=1&cursorDate=2026-07-18&cursorId=${ordered[0].id}`)).data).toEqual([ordered[1]])
    expect((await call('itineraries', '/search?keyword=AA')).data).toEqual([first.data])
    expect((await call('itineraries', '/search?cursorDate=2026-07-18')).response.status).toBe(400)
    expect((await call('itineraries', '/search?limit=1&limit=2')).response.status).toBe(400)
  })

  it('每個修改和刪除以版本為前提，兩個使用同版本的更新僅能成功一次', async () => {
    migrate()
    const data = fixtureRow.parse((await call('worship', '', { method: 'POST', body: worship })).data)
    const path = `/${data.id}`
    expect(data.version).toBe(1)
    expect((await call('worship', path, { method: 'PUT', body: worship })).response.status).toBe(428)
    expect((await call('worship', path, { method: 'DELETE' })).response.status).toBe(428)
    expect((await call('worship', path, { method: 'DELETE', match: 'W/"1"' })).response.status).toBe(428)
    const attempts = await Promise.all(['測試一', '測試二'].map((title) =>
      call('worship', path, { method: 'PUT', body: { ...worship, title }, match: '"1"' })))
    expect(attempts.map(({ response }) => response.status).sort()).toEqual([200, 409])
    expect((await call('worship')).data).toEqual([attempts.find(({ response }) => response.ok)?.data])
    expect(attempts.find(({ response }) => response.ok)?.data).toMatchObject({ version: 2 })
    const stale = await call('worship', path, { method: 'DELETE', match: '"1"' })
    expect(stale.response.status).toBe(409)
    expect(stale.errors).toEqual(['VERSION_CONFLICT'])
    expect((await call('worship', path, { method: 'DELETE', match: '"2"' })).data).toBe(true)
    expect((await call('worship')).data).toEqual([])
  })

  it('拒絕不合法日期、讀取範圍與識別碼', async () => {
    migrate()
    expect((await call('itineraries', '', { method: 'POST', body: { ...itinerary, endDate: '2026-07-17' } })).response.status).toBe(400)
    expect((await call('events', '?start=2026-02-29&end=2026-03-01')).response.status).toBe(400)
    expect((await call('events', '?start=2026-01-01&end=2026-06-01')).response.status).toBe(400)
    expect((await call('events', '/not-a-uuid')).response.status).toBe(400)
  })

  it('On DELETE RESTRICT 讓含照片的紀錄保留，且回應不同於版本衝突', async () => {
    migrate()
    db.exec(readFileSync(photoMigration, 'utf8'))
    const { id } = fixtureRow.parse((await call('worship', '', { method: 'POST', body: worship })).data)
    db.prepare(`INSERT INTO portal_photos (kind, record_id, slot, object_key, state, created_by)
      VALUES ('worship', ?, 1, ?, 'ready', ?)`).run(id, `photos/${crypto.randomUUID()}.webp`, admin.id)
    const denied = await call('worship', `/${id}`, { method: 'DELETE', match: '"1"' })
    expect(denied.response.status).toBe(409)
    expect(denied.errors).toBeNull()
    expect((await call('worship')).data).toMatchObject([{ id, version: 1 }])
    db.prepare('DELETE FROM portal_photos WHERE record_id = ?').run(id)
    expect((await call('worship', `/${id}`, { method: 'DELETE', match: '"1"' })).data).toBe(true)
  })

  it('活動列表展開時區正規化後的定時活動並回傳來源版本', async () => {
    migrate()
    const data = fixtureRow.parse((await call('events', '', {
      method: 'POST', body: { ...event, recurrenceRule: { type: 'Monthly', interval: 1, leapMonthPolicy: 'RegularOnly' } },
    })).data)
    expect(data).toMatchObject({ startDate: '2026-05-12T02:00:00.000Z', sourceRuleId: null, version: 1 })
    expect((await call('events', `/${data.id}`)).data).toEqual(data)
    expect((await call('events', '?start=2026-06-12&end=2026-06-12')).data).toEqual([
      { ...data, id: `${data.id}:2026-06-12`, sourceRuleId: data.id, isOccurrence: true,
        startDate: '2026-06-12T02:00:00.000Z', endDate: '2026-06-12T04:30:00.000Z' },
    ])
  })

  it('農曆規則跨年維持有效日期、閏月和缺日語意', async () => {
    migrate()
    const data = fixtureRow.parse((await call('events', '', {
      method: 'POST',
      body: { ...event, startDate: '2025-06-25', endDate: null, isAllDay: true, isLunarDate: true,
        lunarDate: { year: 2025, month: 6, day: 1, isLeapMonth: false },
        recurrenceRule: { type: 'LunarYearly', interval: 1, leapMonthPolicy: 'Both', effectiveUntil: '2036-12-31' } },
    })).data)
    const leap = await call('events', '?start=2025-07-01&end=2025-08-01')
    expect(leap.data).toMatchObject([{ startDate: '2025-07-25T00:00:00.000Z', sourceRuleId: data.id,
      lunarDate: { displayText: '農曆閏六月初一' }, version: 1 }])
    const matching = (year: number, isLeapMonth: boolean) => ({ sourceRuleId: data.id, version: 1, lunarDate: { year, day: 1, isLeapMonth } })
    expect((await call('events', '?start=2026-06-21&end=2026-08-21')).data).toMatchObject([matching(2026, false)])
    expect((await call('events', '?start=2036-06-21&end=2036-08-21')).data).toMatchObject([matching(2036, false), matching(2036, true)])
    expect((await call('events', '?start=2037-06-21&end=2037-08-21')).data).toEqual([])
  })
})
