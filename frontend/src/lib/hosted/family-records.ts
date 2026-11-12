import { z } from 'zod'
import { formatLunarDate } from '@/lib/lunar'

export type RecordKind = 'members' | 'itineraries'
const dateOnly = z.iso.date()
const civilDate = z.string().transform((value, ctx) => {
  if (value.length === 10 || z.iso.datetime({ offset: true }).safeParse(value).success) {
    const result = dateOnly.safeParse(value.slice(0, 10))
    if (result.success && !result.data.startsWith('0000')) return result.data
  }
  ctx.addIssue({ code: 'custom', message: '日期不正確' })
  return z.NEVER
})
const optionalText = (max: number) => z.string().trim().max(max).nullish()
const requiredText = (max: number) => z.string().trim().min(1).max(max)

export const memberInput = z.strictObject({
  name: requiredText(200), title: requiredText(200), birthday: civilDate,
  bloodType: optionalText(200), bio: optionalText(4000),
  contactInfo: z.strictObject({ phone: optionalText(64), lineId: optionalText(100) }).nullish(),
  lunarBirthday: z.strictObject({
    year: z.number().int().min(1).max(9999), month: z.number().int().min(1).max(12),
    day: z.number().int().min(1).max(30), isLeapMonth: z.boolean(), displayText: z.string().max(100).optional(),
  }).nullish(),
  sortOrder: z.number().int().min(-1000000).max(1000000), isActive: z.boolean(),
})

export const itineraryInput = z.strictObject({
  title: requiredText(200), date: civilDate, endDate: civilDate.nullish(), location: optionalText(200),
  description: optionalText(4000), notes: optionalText(8000), tags: z.array(requiredText(100)).max(30),
  participants: z.array(z.strictObject({
    memberId: z.union([z.uuid(), z.string().regex(/^[a-f\d]{24}$/i)]), name: requiredText(200),
  })).max(100),
  linkedEventId: z.union([z.uuid(), z.string().regex(/^[a-f\d]{24}$/i)]).nullish(),
  photos: z.array(z.never()).max(0).optional(),
}).refine((value) => !value.endDate || value.endDate >= value.date)

export const itineraryQuery = z.strictObject({
  keyword: requiredText(100).optional(),
  limit: z.coerce.number().int().min(1).max(50).default(20),
  cursorDate: civilDate.optional(), cursorId: z.uuid().optional(),
}).refine((value) => Boolean(value.cursorDate) === Boolean(value.cursorId))

export type MemberInput = z.infer<typeof memberInput>
export type ItineraryInput = z.infer<typeof itineraryInput>
export type RecordInput = MemberInput | ItineraryInput
export type RecordQuery = z.infer<typeof itineraryQuery>

export function toRecordValues(kind: RecordKind, value: RecordInput) {
  const common = 'birthday' in value
    ? { label: value.name, civil_date: value.birthday, position: value.sortOrder, search_text: value.name }
    : { label: value.title, civil_date: value.date, position: 0, search_text: [value.title, value.location, value.description, ...value.tags].filter(Boolean).join(' ') }
  return { kind, ...common, details: value }
}

const familyRow = z.object({
  id: z.uuid(), details: z.unknown(), created_by: z.uuid(), created_at: z.string(), updated_at: z.string(),
})

export function readFamilyRecord(kind: RecordKind, input: unknown) {
  const row = familyRow.parse(input)
  if (kind === 'members') {
    const member = memberInput.parse(row.details)
    const lunar = member.lunarBirthday
    return {
      ...member, id: row.id,
      lunarBirthday: lunar ? { ...lunar, displayText: formatLunarDate(lunar.month, lunar.day, lunar.isLeapMonth) } : lunar,
    }
  }
  const itinerary = itineraryInput.parse(row.details)
  return { ...itinerary, id: row.id, photos: [], createdBy: row.created_by, createdAt: row.created_at, updatedAt: row.updated_at }
}
