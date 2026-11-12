import { createHash } from 'node:crypto'
import { open, readFile, realpath, stat } from 'node:fs/promises'
import { dirname, isAbsolute, relative, resolve, sep } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { parseArgs } from 'node:util'
import { LunarYear } from 'lunar-javascript'
import { z } from 'zod'

const collections = ['members', 'itineraries', 'events', 'media']
const maxRecurringDurationMs = 366 * 24 * 60 * 60 * 1000
const objectId = z.string().regex(/^[\da-f]{24}$/i).transform((id) => id.toLowerCase())
const adminIdSchema = z.uuid()
const dateOnly = z.iso.date().refine((value) => !value.startsWith('0000'))
const timestamp = z.iso.datetime({ offset: true }).transform((value, ctx) => {
  const date = new Date(value)
  if (!Number.isFinite(date.getTime()) || !dateOnly.safeParse(date.toISOString().slice(0, 10)).success) {
    ctx.addIssue({ code: 'custom', message: '日期超出範圍' })
    return z.NEVER
  }
  return date.toISOString()
})
const civilDate = z.union([dateOnly, timestamp]).transform((value) => value.slice(0, 10))
const eventTimestamp = timestamp.refine((value) => Number(value.slice(0, 4)) >= 1901 && Number(value.slice(0, 4)) <= 2100)
const text = (max) => z.string().max(max).refine((value) => !value.includes('\u0000'))
const label = (max) => text(max).refine((value) => value.trim().length > 0)
const legacyAudit = {
  _id: objectId,
  createdAt: timestamp.nullish(),
  updatedAt: timestamp.nullish(),
}
const lunar = z.strictObject({
  year: z.number().int().min(1).max(9999), month: z.number().int().min(1).max(12),
  day: z.number().int().min(1).max(30), isLeapMonth: z.boolean().default(false),
  displayText: text(100).optional(),
})
const eventLunar = lunar.refine(({ year, month, day, isLeapMonth }) => {
  if (year < 1900 || year > 2100) return false
  return LunarYear.fromYear(year).getMonthsInYear().some((item) => item.getMonth() === (isLeapMonth ? -month : month)
    && day <= item.getDayCount())
})
const memberSource = z.strictObject({
  ...legacyAudit,
  name: label(200), title: label(200), birthday: civilDate,
  avatarUrl: text(4096).nullish(), bio: text(4000).nullish(), bloodType: text(200).nullish(),
  sortOrder: z.number().int().min(-1000000).max(1000000).default(0), isActive: z.boolean().default(true),
  contactInfo: z.strictObject({ phone: text(64).nullish(), lineId: text(100).nullish() }).nullish(),
  lunarBirthday: lunar.nullish(),
})
const itinerarySource = z.strictObject({
  ...legacyAudit,
  title: label(200), date: civilDate, endDate: civilDate.nullish(), location: text(200).nullish(),
  description: text(4000).nullish(), notes: text(8000).nullish(),
  tags: z.array(label(100)).max(30).default([]),
  participants: z.array(z.strictObject({ memberId: objectId, name: label(200) })).max(100).default([]),
  photos: z.array(text(4096)).default([]), linkedEventId: objectId.nullish(), createdBy: objectId.or(z.literal('')).nullish(),
}).refine((value) => !value.endDate || value.endDate >= value.date)
const eventSource = z.strictObject({
  ...legacyAudit,
  title: label(200), description: text(4000).nullish(),
  eventType: z.enum(['General', 'Birthday', 'Festival', 'Worship', 'Anniversary', 'Meeting', 'Other']),
  startDate: eventTimestamp, endDate: eventTimestamp.nullish(), isAllDay: z.boolean().default(true),
  isLunarDate: z.boolean().default(false), lunarDate: eventLunar.nullish(),
  recurrenceRule: z.strictObject({
    type: z.enum(['Yearly', 'Monthly', 'LunarYearly', 'LunarMonthly']),
    interval: z.number().int().min(1).max(120).default(1),
    lunarDays: z.array(z.number().int().min(1).max(30)).min(1).max(30).nullish(),
    leapMonthPolicy: z.enum(['RegularOnly', 'LeapOnly', 'Both']).default('RegularOnly'),
    effectiveFrom: eventTimestamp.nullish(), effectiveUntil: eventTimestamp.nullish(),
  }).nullish(),
  worshipDetails: z.strictObject({
    deity: text(200).nullish(), offerings: z.array(label(200)).max(20).default([]),
    preparations: z.array(label(200)).max(20).default([]), notes: text(8000).nullish(),
  }).nullish(),
  relatedMemberIds: z.array(objectId).max(100).default([]),
  tags: z.array(label(50)).max(20).default([]), color: text(200).nullish(),
  createdBy: objectId.or(z.literal('')).nullish(),
}).refine((event) => {
  if (event.endDate && event.endDate < event.startDate) return false
  if (event.isLunarDate !== Boolean(event.lunarDate)) return false
  if (event.worshipDetails && event.eventType !== 'Worship') return false
  const rule = event.recurrenceRule
  if (!rule) return true
  if (event.endDate && Date.parse(event.endDate) - Date.parse(event.startDate) > maxRecurringDurationMs) return false
  const isLunar = rule.type === 'LunarYearly' || rule.type === 'LunarMonthly'
  return (!isLunar || (event.isLunarDate && event.lunarDate))
    && (isLunar || rule.leapMonthPolicy === 'RegularOnly')
    && (rule.type === 'LunarMonthly' ? Boolean(rule.lunarDays?.length) : !rule.lunarDays)
    && !(rule.effectiveFrom && rule.effectiveUntil && rule.effectiveFrom > rule.effectiveUntil)
})
const schemas = { members: memberSource, itineraries: itinerarySource, events: eventSource }

function isRecord(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
}

/// 支援 mongoexport 的 JSON 陣列與預設的逐行 JSON，不輸出原始解析例外。
export function parseExport(input) {
  const content = input.trim()
  if (!content) return []
  let documents
  try {
    documents = content.startsWith('[') ? JSON.parse(content)
      : content.split('\n').filter((line) => line.trim()).map((line) => JSON.parse(line))
  } catch {
    throw new Error('匯出檔不是有效 JSON')
  }
  if (!Array.isArray(documents) || !documents.every(isRecord)) throw new Error('匯出檔包含非文件資料')
  return documents
}

function canonicalInteger(value) {
  if (typeof value !== 'string' || !/^-?(?:0|[1-9]\d*)$/.test(value)) throw new Error('整數格式不正確')
  const number = Number(value)
  if (!Number.isSafeInteger(number)) throw new Error('整數超出安全範圍')
  return number
}

function decode(value, depth = 0) {
  if (depth > 12) throw new Error('文件層數超出限制')
  if (Array.isArray(value)) return value.map((entry) => decode(entry, depth + 1))
  if (!isRecord(value)) return value
  const keys = Object.keys(value)
  if (keys.length === 1) {
    if (Object.hasOwn(value, '$oid')) return objectId.parse(value.$oid)
    if (Object.hasOwn(value, '$numberInt')) return canonicalInteger(value.$numberInt)
    if (Object.hasOwn(value, '$numberLong')) return canonicalInteger(value.$numberLong)
    if (Object.hasOwn(value, '$date')) {
      if (typeof value.$date === 'string') return timestamp.parse(value.$date)
      const date = z.strictObject({ $numberLong: z.string() }).parse(value.$date)
      return timestamp.parse(new Date(canonicalInteger(date.$numberLong)).toISOString())
    }
  }
  const result = Object.create(null)
  for (const key of keys) {
    // C# 巢狀欄位預設使用 PascalCase；也接受已有 camelCase 的匯出。
    const normalized = key.charAt(0).toLowerCase() + key.slice(1)
    if (Object.hasOwn(result, normalized)) throw new Error('文件包含衝突欄位')
    result[normalized] = decode(value[key], depth + 1)
  }
  return result
}

function stableId(collection, sourceId) {
  const digest = createHash('sha256').update(`hsu-family-legacy-mongo-v1:${collection}:${sourceId}`).digest()
  // UUIDv8 自訂命名空間：同來源可比對，不能覆寫系統既有資料。
  digest[6] = (digest[6] & 0x0f) | 0x80
  digest[8] = (digest[8] & 0x3f) | 0x80
  const hex = digest.subarray(0, 16).toString('hex')
  return [hex.slice(0, 8), hex.slice(8, 12), hex.slice(12, 16), hex.slice(16, 20), hex.slice(20)].join('-')
}

/// 報告只含 collection、筆數及筆序；資料本體僅寫入受保護 SQL。
export function prepareImport(source) {
  const report = {
    source: Object.fromEntries(collections.map((key) => [key, source[key].length])),
    planned: { members: 0, itineraries: 0, events: 0 },
    issues: [], warnings: [],
  }
  const rows = {}
  const ids = {}
  for (const [collection, schema] of Object.entries(schemas)) {
    ids[collection] = new Set()
    rows[collection] = source[collection].flatMap((document, index) => {
      const row = index + 1
      let value
      try {
        value = schema.parse(decode(document))
      } catch {
        report.issues.push({ collection, row, code: 'invalid_document' })
        return []
      }
      if (ids[collection].has(value._id)) {
        report.issues.push({ collection, row, code: 'duplicate_id' })
        return []
      }
      ids[collection].add(value._id)
      return [{ row, value }]
    })
    report.planned[collection] = rows[collection].length
  }
  if (source.media.length) report.warnings.push({ collection: 'media', code: 'media_not_imported', count: source.media.length })
  const warn = (collection, row, code, count = 1) => {
    if (count > 0) report.warnings.push({ collection, row, code, count })
  }
  const missing = (kind, sourceIds) => sourceIds.filter((id) => !ids[kind].has(id)).length
  const created = ({ _id, createdAt, updatedAt }, kind) => ({ id: stableId(kind, _id), createdAt, updatedAt })

  const members = rows.members.map(({ row, value }) => {
    const { _id, createdAt, updatedAt, avatarUrl, ...details } = value
    if (avatarUrl) warn('members', row, 'avatar_not_imported')
    return { ...created({ _id, createdAt, updatedAt }, 'members'), kind: 'members',
      label: details.name, civilDate: details.birthday, searchText: details.name, position: details.sortOrder, details }
  })
  const itineraries = rows.itineraries.map(({ row, value }) => {
    const { _id, createdAt, updatedAt, photos, participants, linkedEventId, ...details } = value
    delete details.createdBy // 舊版帳號與新版管理員無直接對應，且不輸出舊帳號 ID。
    warn('itineraries', row, 'photos_not_imported', photos.length)
    warn('itineraries', row, 'missing_member_links', missing('members', participants.map(({ memberId }) => memberId)))
    if (linkedEventId) warn('itineraries', row, 'missing_event_link', missing('events', [linkedEventId]))
    return { ...created({ _id, createdAt, updatedAt }, 'itineraries'), kind: 'itineraries',
      label: details.title, civilDate: details.date,
      searchText: [details.title, details.location, details.description, ...details.tags].filter(Boolean).join(' '),
      position: 0, details: { ...details, linkedEventId: linkedEventId ? stableId('events', linkedEventId) : linkedEventId,
        participants: participants.map(({ memberId, name }) => ({ memberId: stableId('members', memberId), name })) } }
  })
  const events = rows.events.map(({ row, value }) => {
    const { _id, createdAt, updatedAt, startDate, endDate, relatedMemberIds, ...details } = value
    delete details.createdBy
    warn('events', row, 'missing_member_links', missing('members', relatedMemberIds))
    return { ...created({ _id, createdAt, updatedAt }, 'events'), startDate, endDate,
      details: { ...details, relatedMemberIds: relatedMemberIds.map((id) => stableId('members', id)) } }
  })
  return { report, family: [...members, ...itineraries], events }
}

function textSql(value) {
  return value == null ? 'null' : `convert_from(decode('${Buffer.from(value, 'utf8').toString('hex')}','hex'),'UTF8')`
}

export function renderImportSql(plan, { adminId, allowPartial = false }) {
  if (plan.report.issues.length) throw new Error('驗證失敗；請修正匯出檔的問題後重試')
  if (!allowPartial && plan.report.warnings.length) throw new Error('有待核對項目；保留來源並審閱報告後才可允許部分轉換')
  const actor = adminIdSchema.safeParse(adminId)
  if (!actor.success) throw new Error('請提供有效的目標管理員 UUID')
  const userId = `${textSql(actor.data)}::uuid`
  const dateTime = (value) => `coalesce(${textSql(value)}::timestamptz, transaction_timestamp())`
  const lines = [
    '-- 私人遷移檔；禁止提交版本控制或提供給公開展示環境。',
    'begin;',
    "set local statement_timeout = '90s';",
    `do $$ begin if not exists (select 1 from public.portal_access where user_id = ${userId} and role = 'Admin') then raise exception '沒有核准的匯入管理員'; end if; end $$;`,
  ]
  for (const row of plan.family) {
    lines.push(`insert into public.family_records (id, kind, civil_date, label, position, search_text, details, created_by, created_at, updated_at) values (${textSql(row.id)}::uuid, ${textSql(row.kind)}, ${textSql(row.civilDate)}::date, ${textSql(row.label)}, ${row.position}, ${textSql(row.searchText)}, ${textSql(JSON.stringify(row.details))}::jsonb, ${userId}, ${dateTime(row.createdAt)}, ${dateTime(row.updatedAt)});`)
  }
  for (const row of plan.events) {
    lines.push(`insert into public.calendar_events (id, starts_at, ends_at, details, created_by, created_at, updated_at) values (${textSql(row.id)}::uuid, ${textSql(row.startDate)}::timestamptz, ${textSql(row.endDate)}::timestamptz, ${textSql(JSON.stringify(row.details))}::jsonb, ${userId}, ${dateTime(row.createdAt)}, ${dateTime(row.updatedAt)});`)
  }
  return [...lines, 'commit;', ''].join('\n')
}

async function main() {
  let values
  try {
    ({ values } = parseArgs({ options: {
      members: { type: 'string' }, itineraries: { type: 'string' }, events: { type: 'string' },
      media: { type: 'string' }, 'admin-id': { type: 'string' }, output: { type: 'string' },
      'backup-confirmed': { type: 'boolean' }, 'allow-partial': { type: 'boolean' }, 'dry-run': { type: 'boolean' },
    } }))
  } catch {
    throw new Error('參數錯誤；請查看離線遷移手冊')
  }
  if (!collections.every((key) => values[key])) throw new Error('請提供四個 collection 的本機匯出檔')
  const paths = collections.map((key) => resolve(values[key]))
  if (new Set(paths).size !== collections.length) throw new Error('請分別提供四個不同的匯出檔')
  if (!values['dry-run'] && (!values.output || !values['backup-confirmed'])) {
    throw new Error('產生 SQL 前請先確認離線加密備份，並指定新的 --output 檔案')
  }
  if (values['dry-run'] && values.output) throw new Error('--dry-run 不得指定 --output')
  const entries = []
  for (const [index, key] of collections.entries()) {
    try {
      const info = await stat(paths[index])
      if (!info.isFile() || info.size > 32 * 1024 * 1024) throw new Error('invalid file')
      entries.push([key, parseExport(await readFile(paths[index], 'utf8'))])
    } catch {
      throw new Error(`無法解析 ${key} 匯出檔；請確認格式與 32 MiB 上限`)
    }
  }
  const plan = prepareImport(Object.fromEntries(entries))
  process.stdout.write(`${JSON.stringify(plan.report, null, 2)}\n`)
  if (values['dry-run']) {
    if (plan.report.issues.length || plan.report.warnings.length) process.exitCode = 2
    return
  }
  const sql = renderImportSql(plan, { adminId: values['admin-id'], allowPartial: values['allow-partial'] })
  const repository = await realpath(fileURLToPath(new URL('../../../', import.meta.url)))
  let outputDir
  try {
    outputDir = await realpath(dirname(resolve(values.output)))
  } catch {
    throw new Error('SQL 輸出目錄不存在')
  }
  const outputRelative = relative(repository, outputDir)
  if (!isAbsolute(outputRelative) && outputRelative !== '..' && !outputRelative.startsWith(`..${sep}`)) {
    throw new Error('私人 SQL 必須輸出到工作區以外')
  }
  try {
    const output = await open(values.output, 'wx', 0o600)
    try { await output.writeFile(sql, 'utf8') } finally { await output.close() }
  } catch {
    throw new Error('無法寫入新的私人 SQL 檔，請確認檔案尚不存在且目錄可寫')
  }
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  await main().catch((error) => {
    process.stderr.write(`${error.message}\n`)
    process.exitCode = 1
  })
}
