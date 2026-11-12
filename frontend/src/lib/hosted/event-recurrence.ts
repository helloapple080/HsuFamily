import { Lunar, LunarYear, type LunarMonth } from 'lunar-javascript'
import { formatLunarDate } from '@/lib/lunar'
import type { CalendarEvent, LeapMonthPolicy, RecurrenceRule } from '@/types/event'
import type { LunarDate } from '@/types/member'
import { dayMs } from './event-contract'

const minLunarYear = 1900
const yearMonths = new Map<number, LunarMonth[]>()
const yearStartMonths = [0]

function lunarMonths(year: number): LunarMonth[] {
  const cached = yearMonths.get(year)
  if (cached) return cached
  const months = LunarYear.fromYear(year).getMonthsInYear()
  yearMonths.set(year, months)
  return months
}

function lunarYearStartMonth(year: number): number {
  const index = year - minLunarYear
  while (yearStartMonths.length <= index) {
    const previous = yearStartMonths.length - 1
    yearStartMonths.push(yearStartMonths[previous] + lunarMonths(minLunarYear + previous).length)
  }
  return yearStartMonths[index]
}

function monthAllowed(policy: LeapMonthPolicy, signedMonth: number) {
  return policy === 'Both' || (policy === 'LeapOnly' ? signedMonth < 0 : signedMonth > 0)
}

function* solarCandidates(anchor: Date, rule: RecurrenceRule, earliest: Date, latest: Date): Generator<string> {
  const anchorMonth = anchor.getUTCFullYear() * 12 + anchor.getUTCMonth()
  const day = anchor.getUTCDate()
  const period = rule.type === 'Yearly' ? 12 * rule.interval : rule.interval
  const firstMonth = earliest.getUTCFullYear() * 12 + earliest.getUTCMonth()
  const lastMonth = latest.getUTCFullYear() * 12 + latest.getUTCMonth()
  for (let monthIndex = anchorMonth + Math.max(0, Math.ceil((firstMonth - anchorMonth) / period)) * period;
    monthIndex <= lastMonth; monthIndex += period) {
    const month = monthIndex % 12
    const date = new Date(Date.UTC(Math.floor(monthIndex / 12), month, day))
    if (date.getUTCMonth() === month) yield date.toISOString().slice(0, 10)
  }
}

function* lunarCandidates(anchor: LunarDate, rule: RecurrenceRule, earliest: Date, latest: Date): Generator<{ date: string; lunarDate: LunarDate }> {
  const isMonthly = rule.type === 'LunarMonthly'
  const anchorMonth = lunarYearStartMonth(anchor.year) + lunarMonths(anchor.year).findIndex(
    (month) => month.getMonth() === (anchor.isLeapMonth ? -anchor.month : anchor.month),
  )
  const days = isMonthly ? rule.lunarDays ?? [] : [anchor.day]
  // 國曆年初可能仍在上一個農曆年；以候選日期再過濾。
  const firstYear = Math.max(anchor.year, earliest.getUTCFullYear() - 1)
  const lastYear = latest.getUTCFullYear()
  for (let year = firstYear; year <= lastYear; year++) {
    if (!isMonthly && (year - anchor.year) % rule.interval !== 0) continue
    for (const [index, month] of lunarMonths(year).entries()) {
      const signedMonth = month.getMonth()
      if (!monthAllowed(rule.leapMonthPolicy, signedMonth)) continue
      if (isMonthly) {
        const delta = lunarYearStartMonth(year) + index - anchorMonth
        if (delta < 0 || delta % rule.interval !== 0) continue
      } else if (Math.abs(signedMonth) !== anchor.month) continue
      for (const day of days) {
        if (day > month.getDayCount()) continue
        const solar = Lunar.fromYmd(year, signedMonth, day).getSolar()
        const date = new Date(Date.UTC(solar.getYear(), solar.getMonth() - 1, solar.getDay())).toISOString().slice(0, 10)
        yield {
          date,
          lunarDate: {
            year, month: Math.abs(signedMonth), day, isLeapMonth: signedMonth < 0,
            displayText: formatLunarDate(Math.abs(signedMonth), day, signedMonth < 0),
          },
        }
      }
    }
  }
}

export function expandCalendarEvent(event: CalendarEvent, start: string, end: string): CalendarEvent[] {
  const first = Date.parse(start)
  const last = Date.parse(end)
  const sourceStart = Date.parse(event.startDate)
  const sourceEnd = event.endDate ? Date.parse(event.endDate) : sourceStart
  const duration = sourceEnd - sourceStart
  const result = sourceStart <= last && sourceEnd >= first ? [event] : []
  const rule = event.recurrenceRule
  if (!rule) return result
  const isLunar = rule.type === 'LunarYearly' || rule.type === 'LunarMonthly'
  const effectiveFrom = rule.effectiveFrom?.slice(0, 10)
  const effectiveUntil = rule.effectiveUntil?.slice(0, 10)
  // 農曆規則以來源的農曆年／月為週期起點，並不以來源日期為生效日。
  const earliest = new Date(Math.max(isLunar ? -Infinity : sourceStart, first - duration,
    effectiveFrom ? Date.parse(effectiveFrom) : -Infinity))
  const latest = new Date(Math.min(last, effectiveUntil ? Date.parse(effectiveUntil) + dayMs - 1 : Infinity))
  if (earliest > latest) return result
  const add = (date: string, lunarDate: LunarDate | null | undefined) => {
    const occurrenceStart = Date.parse(date + event.startDate.slice(10))
    if (occurrenceStart === sourceStart || (!isLunar && occurrenceStart < sourceStart) ||
      occurrenceStart > latest.getTime() || occurrenceStart + duration < first ||
      (effectiveFrom && date < effectiveFrom)) return
    result.push({
      ...event,
      id: `${event.id}:${date}`, sourceRuleId: event.id, isOccurrence: true,
      startDate: new Date(occurrenceStart).toISOString(),
      endDate: event.endDate ? new Date(occurrenceStart + duration).toISOString() : null,
      lunarDate,
    })
  }
  if (isLunar) {
    if (event.lunarDate) for (const occurrence of lunarCandidates(event.lunarDate, rule, earliest, latest)) add(occurrence.date, occurrence.lunarDate)
  } else {
    for (const date of solarCandidates(new Date(sourceStart), rule, earliest, latest)) add(date, event.lunarDate)
  }
  return result.sort((a, b) => a.startDate.localeCompare(b.startDate))
}
