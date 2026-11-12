// @vitest-environment node
import { execFile } from 'node:child_process'
import { mkdtemp, readFile, rm, stat, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { promisify } from 'node:util'
import { PGlite } from '@electric-sql/pglite'
import { expect, it } from 'vitest'
import { eventInput } from '../../src/lib/hosted/event-contract'
import { memberInput, itineraryInput } from '../../src/lib/hosted/family-records'
import { parseExport, prepareImport, renderImportSql } from './convert-mongo-export.mjs'

const adminId = '8ab21dd4-b611-4ab1-8786-f07ec705a922'
const memberId = '1234567890abcdef12345678'
const itineraryId = '2234567890abcdef12345678'
const eventId = '3234567890abcdef12345678'
const exec = promisify(execFile)
const dates = { createdAt: { $date: '2025-06-01T09:00:00Z' }, updatedAt: { $date: '2025-07-01T09:00:00Z' } }
const example = {
  members: [{ _id: { $oid: memberId }, name: "測試 O'Neil", title: '表親', birthday: { $date: { $numberLong: '-1577923200000' } },
    lunarBirthday: { Year: { $numberInt: '1919' }, Month: 11, Day: 11, IsLeapMonth: false },
    sortOrder: { $numberInt: '2' }, isActive: false, contactInfo: { LineId: 'demo-only' }, ...dates }],
  itineraries: [{ _id: { $oid: itineraryId }, title: '測試行程', date: { $date: '2026-03-01T00:00:00Z' },
    participants: [{ MemberId: memberId, Name: "測試 O'Neil" }], linkedEventId: { $oid: eventId }, tags: ['測試'], ...dates }],
  events: [{ _id: { $oid: eventId }, title: '年度測試祭拜', eventType: 'Worship', startDate: { $date: '2026-03-03T00:00:00Z' },
    endDate: { $date: '2026-03-04T00:00:00Z' }, isAllDay: true, isLunarDate: true,
    lunarDate: { Year: 2026, Month: 1, Day: 15, IsLeapMonth: false },
    recurrenceRule: { Type: 'LunarYearly', Interval: { $numberLong: '2' }, LeapMonthPolicy: 'Both',
      EffectiveFrom: { $date: '2026-03-01T00:00:00Z' }, EffectiveUntil: { $date: '2036-12-31T00:00:00Z' } },
    worshipDetails: { Deity: '祖先', Offerings: ['茶'], Preparations: [], Notes: '測試備註' }, relatedMemberIds: [memberId], tags: [], ...dates }],
  media: [],
}

it('讀取 NDJSON、JSON 陣列、空 collection 並拒絕意外資料形狀', () => {
  expect(parseExport(example.members.concat(example.members).map((row) => JSON.stringify(row)).join('\n'))).toHaveLength(2)
  expect(parseExport(JSON.stringify(example.members))).toHaveLength(1)
  expect(parseExport('  \n')).toEqual([])
  expect(() => parseExport('[{"example":1},null]')).toThrow('匯出檔包含非文件資料')
  expect(() => parseExport('{"broken":"避免把原始資料印到終端"')).toThrow('匯出檔不是有效 JSON')
})

it('保留跨 collection 關聯、日期和農曆週期，ID 跨次執行固定', () => {
  const plan = prepareImport(example)
  expect(plan.report.planned).toEqual({ members: 1, itineraries: 1, events: 1 })
  expect(plan.report.issues).toEqual([])
  expect(plan.report.warnings).toEqual([])
  const [member, itinerary] = plan.family
  const [event] = plan.events
  expect(memberInput.parse(member.details)).toMatchObject({ birthday: '1920-01-01', lunarBirthday: { month: 11 }, isActive: false })
  expect(itineraryInput.parse(itinerary.details)).toMatchObject({ linkedEventId: event.id, participants: [{ memberId: member.id }] })
  expect(event.details).toMatchObject({
    isLunarDate: true, recurrenceRule: { type: 'LunarYearly', interval: 2, leapMonthPolicy: 'Both', effectiveUntil: '2036-12-31T00:00:00.000Z' },
    relatedMemberIds: [member.id], worshipDetails: { offerings: ['茶'] },
  })
  expect(eventInput.safeParse({ ...event.details, startDate: event.startDate, endDate: event.endDate }).success).toBe(true)
  expect(prepareImport(example).family).toEqual(plan.family)
})

it('不改寫超出新版範圍的週期或標籤，而是列出需要人工核對的來源筆序', () => {
  const source = { ...example, events: [
    { ...example.events[0], endDate: { $date: '2028-03-03T00:00:00Z' } },
    { ...example.events[0], _id: { $oid: '4234567890abcdef12345678' }, tags: ['甲'.repeat(51)] },
  ] }
  expect(prepareImport(source).report.issues).toEqual([
    { collection: 'events', row: 1, code: 'invalid_document' },
    { collection: 'events', row: 2, code: 'invalid_document' },
  ])
})

it('保留舊版行程可用的 8000 字備註', () => {
  const notes = '甲'.repeat(8000)
  const plan = prepareImport({ ...example, itineraries: [{ ...example.itineraries[0], notes }] })
  expect(plan.report.issues).toEqual([])
  expect(plan.family[1].details.notes).toBe(notes)
  expect(prepareImport({ ...example, itineraries: [{ ...example.itineraries[0], notes: notes + '甲' }] }).report.issues)
    .toContainEqual({ collection: 'itineraries', row: 1, code: 'invalid_document' })
})

it('隔離不支援的舊照片，遇到驗證錯誤即使允許部分也不產生 SQL', () => {
  const url = 'https://example.invalid/private-secret'
  const source = { ...example, members: [{ ...example.members[0], avatarUrl: url }], media: [{ storageObjectId: url }] }
  const plan = prepareImport(source)
  expect(plan.report.warnings).toContainEqual({ collection: 'members', row: 1, code: 'avatar_not_imported', count: 1 })
  expect(plan.report.warnings).toContainEqual({ collection: 'media', code: 'media_not_imported', count: 1 })
  expect(JSON.stringify(plan.report)).not.toContain(url)
  expect(() => renderImportSql(plan, { adminId })).toThrow('有待核對項目')
  expect(renderImportSql(plan, { adminId, allowPartial: true })).not.toContain(url)
  const invalid = prepareImport({ ...example, members: [{ ...example.members[0], birthday: { $date: '2025-02-30T00:00:00Z' } }] })
  expect(invalid.report.issues).toContainEqual({ collection: 'members', row: 1, code: 'invalid_document' })
  expect(() => renderImportSql(invalid, { adminId, allowPartial: true })).toThrow('驗證失敗')
})

it('CLI 只顯示非個資報告，產物為 0600，且不覆寫既有檔案', async () => {
  const dir = await mkdtemp(join(tmpdir(), 'family-import-test-'))
  try {
    const program = fileURLToPath(new URL('./convert-mongo-export.mjs', import.meta.url))
    const input = await Promise.all(Object.entries(example).map(async ([key, rows]) => {
      const file = join(dir, `${key}.json`)
      await writeFile(file, JSON.stringify(rows), { mode: 0o600 })
      return [`--${key}`, file]
    }))
    const args = [program, ...input.flat()]
    const preview = await exec(process.execPath, [...args, '--dry-run'])
    expect(preview.stdout).not.toContain("測試 O'Neil")
    const output = join(dir, 'restore.sql')
    const apply = [...args, '--backup-confirmed', '--admin-id', adminId, '--output', output]
    await exec(process.execPath, apply)
    expect((await stat(output)).mode & 0o777).toBe(0o600)
    const before = await readFile(output, 'utf8')
    await expect(exec(process.execPath, apply)).rejects.toMatchObject({ code: 1, stderr: expect.stringContaining('無法寫入新的私人 SQL') })
    expect(await readFile(output, 'utf8')).toBe(before)
  } finally {
    await rm(dir, { recursive: true })
  }
})

it('隔離 PostgreSQL 還原保留內容，非管理員被拒絕且重跑不覆蓋任何資料', async () => {
  const db = new PGlite()
  try {
    await db.exec(`
      create role anon; create role authenticated;
      create schema auth; create table auth.users (id uuid primary key);
      create function auth.uid() returns uuid language sql stable as $$ select null::uuid $$;
      create schema storage; create table storage.buckets (id text, name text, public boolean);
      create table storage.objects (bucket_id text, name text);
    `)
    for (const name of ['202611110001_portal_foundation.sql', '202611120001_family_records.sql', '202611120003_calendar_events.sql']) {
      await db.exec(await readFile(new URL(`../../../supabase/migrations/${name}`, import.meta.url), 'utf8'))
    }
    const plan = prepareImport(example)
    const sql = renderImportSql(plan, { adminId })
    await expect(db.exec(sql)).rejects.toThrow('沒有核准的匯入管理員')
    await db.exec('rollback')
    await db.query('insert into auth.users (id) values ($1)', [adminId])
    await db.query("insert into public.portal_access (user_id, role) values ($1, 'Admin')", [adminId])
    await db.exec(sql)
    const rows = await db.query('select label, civil_date::text as day from public.family_records order by civil_date')
    expect(rows.rows).toEqual([{ label: "測試 O'Neil", day: '1920-01-01' }, { label: '測試行程', day: '2026-03-01' }])
    const events = await db.query('select details, created_by from public.calendar_events')
    expect(events.rows).toMatchObject([{ created_by: adminId, details: { recurrenceRule: { interval: 2 } } }])
    await db.query("update public.family_records set label = '人工已改' where kind = 'members'")
    await expect(db.exec(sql)).rejects.toThrow('duplicate key')
    await db.exec('rollback')
    expect((await db.query("select label from public.family_records where kind = 'members'")).rows).toEqual([{ label: '人工已改' }])
    const staged = prepareImport({
      ...example,
      members: [{ ...example.members[0], _id: { $oid: '5234567890abcdef12345678' }, name: '不得留下的測試成員' }],
      itineraries: [],
    })
    await expect(db.exec(renderImportSql(staged, { adminId, allowPartial: true }))).rejects.toThrow('duplicate key')
    await db.exec('rollback')
    expect((await db.query("select count(*)::int as count from public.family_records")).rows).toEqual([{ count: 2 }])
  } finally {
    await db.close()
  }
})
