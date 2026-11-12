import { LunarYear } from 'lunar-javascript'
import { z } from 'zod'
import { formatLunarDate } from '@/lib/lunar'

export const dayMs = 24 * 60 * 60 * 1000
const supported = (value: string) => Number(value.slice(0, 4)) >= 1901 && Number(value.slice(0, 4)) <= 2100
const timestamp = z.union([z.iso.date(), z.iso.datetime({ offset: true })]).refine(supported)
const dateAt = (value: string, atEnd = false) => `${value.slice(0, 10)}T${atEnd ? '23:59:59.999' : '00:00:00.000'}Z`
const normalize = (value: string, civil = false) => civil || value.length === 10 ? dateAt(value) : new Date(value).toISOString()
const shortText = z.string().trim().min(1).max(200)
const optionalText = (max: number) => z.string().max(max).nullish()
const lunarDate = z.object({
  year: z.number().int().min(1900).max(2100),
  month: z.number().int().min(1).max(12),
  day: z.number().int().min(1).max(30),
  isLeapMonth: z.boolean(),
}).refine((value) => {
  const month = LunarYear.fromYear(value.year).getMonthsInYear().find((item) => item.getMonth() === (value.isLeapMonth ? -value.month : value.month))
  return month !== undefined && value.day <= month.getDayCount()
}).transform((value) => ({ ...value, displayText: formatLunarDate(value.month, value.day, value.isLeapMonth) }))

const recurrence = z.strictObject({
  type: z.enum(['Yearly', 'Monthly', 'LunarYearly', 'LunarMonthly']),
  interval: z.number().int().min(1).max(120),
  lunarDays: z.array(z.number().int().min(1).max(30)).min(1).max(30).nullish(),
  leapMonthPolicy: z.enum(['RegularOnly', 'LeapOnly', 'Both']),
  effectiveFrom: timestamp.nullish(),
  effectiveUntil: timestamp.nullish(),
}).refine((value) => (value.type === 'LunarMonthly') === Boolean(value.lunarDays))
  .refine((value) => !value.effectiveFrom || !value.effectiveUntil || dateAt(value.effectiveFrom) <= dateAt(value.effectiveUntil))
  .transform((value) => ({
    ...value,
    lunarDays: value.lunarDays ? [...new Set(value.lunarDays)].sort((a, b) => a - b) : value.lunarDays,
    effectiveFrom: value.effectiveFrom ? dateAt(value.effectiveFrom) : value.effectiveFrom,
    effectiveUntil: value.effectiveUntil ? dateAt(value.effectiveUntil) : value.effectiveUntil,
  }))

export const eventInput = z.strictObject({
  title: shortText,
  description: optionalText(4000),
  eventType: z.enum(['General', 'Birthday', 'Festival', 'Worship', 'Anniversary', 'Meeting', 'Other']),
  startDate: timestamp,
  endDate: timestamp.nullish(),
  isAllDay: z.boolean(),
  isLunarDate: z.boolean(),
  lunarDate: lunarDate.nullish(),
  recurrenceRule: recurrence.nullish(),
  worshipDetails: z.strictObject({
    deity: optionalText(200), offerings: z.array(shortText).max(20), preparations: z.array(shortText).max(20),
    notes: optionalText(8000),
  }).nullish(),
  relatedMemberIds: z.array(shortText).max(100),
  tags: z.array(z.string().trim().min(1).max(50)).max(20),
  color: optionalText(200),
}).transform((value) => ({
  ...value,
  startDate: normalize(value.startDate, value.isAllDay),
  endDate: value.endDate ? normalize(value.endDate, value.isAllDay) : value.endDate,
})).refine((value) =>
  supported(value.startDate) &&
  (!value.endDate || (supported(value.endDate) && value.startDate <= value.endDate)) &&
  (!value.recurrenceRule || !value.endDate || Date.parse(value.endDate) - Date.parse(value.startDate) <= 366 * dayMs) &&
  value.isLunarDate === Boolean(value.lunarDate) &&
  (!value.recurrenceRule?.type.startsWith('Lunar') || value.isLunarDate) &&
  (!value.worshipDetails || value.eventType === 'Worship'),
)

export type EventInput = z.infer<typeof eventInput>

export const eventQuery = z.strictObject({ start: timestamp, end: timestamp }).transform((value) => ({
  start: normalize(value.start),
  end: value.end.length === 10 ? dateAt(value.end, true) : normalize(value.end),
})).refine(({ start, end }) => supported(start) && supported(end) && start <= end && Date.parse(end) - Date.parse(start) <= 62 * dayMs)
