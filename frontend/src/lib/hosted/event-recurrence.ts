import { LunarYear, Solar, type LunarMonth } from 'lunar-javascript'
import { formatLunarDate } from '@/lib/lunar'
import type { CalendarEvent, LeapMonthPolicy, RecurrenceRule } from '@/types/event'
import type { LunarDate } from '@/types/member'
import { dayMs } from './event-contract'

const yearMonths = new Map<number, LunarMonth[]>()

function lunarMonths(year: number): LunarMonth[] {
  const cached = yearMonths.get(year)
  if (cached) return cached
  const months = LunarYear.fromYear(year).getMonthsInYear()
  yearMonths.set(year, months)
  return months
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
  const days = isMonthly ? rule.lunarDays ?? [] : [anchor.day]
  // 國曆年初可能仍在上一個農曆年；以候選日期再過濾。
  const firstYear = Math.max(anchor.year, earliest.getUTCFullYear() - 1)
  const lastYear = latest.getUTCFullYear()
  const anchorIndex = isMonthly && (firstYear === anchor.year || rule.interval > 1)
    ? lunarMonths(anchor.year).findIndex((month) => month.getMonth() === (anchor.isLeapMonth ? -anchor.month : anchor.month))
    : 0
  let elapsedMonths = -anchorIndex
  // 多月週期只需計算從來源到視窗之間的月份；每月／每年週期不展開無關年份。
  if (isMonthly && rule.interval > 1) {
    for (let year = anchor.year; year < firstYear; year++) elapsedMonths += lunarMonths(year).length
  }
  const firstDay = Math.floor(earliest.getTime() / dayMs)
  const lastDay = Math.floor(latest.getTime() / dayMs)
  for (let year = firstYear; year <= lastYear; year++) {
    if (!isMonthly && (year - anchor.year) % rule.interval !== 0) continue
    const months = lunarMonths(year)
    for (const [index, month] of months.entries()) {
      const signedMonth = month.getMonth()
      if (!monthAllowed(rule.leapMonthPolicy, signedMonth)) continue
      if (isMonthly) {
        if (year === anchor.year && index < anchorIndex) continue
        if (rule.interval > 1 && (elapsedMonths + index) % rule.interval !== 0) continue
      } else if (Math.abs(signedMonth) !== anchor.month) continue

      // 只需要每月首日的國曆日期；不反覆計算候選日的節氣與干支。
      const solar = Solar.fromJulianDay(month.getFirstJulianDay())
      const monthFirstDay = Date.UTC(solar.getYear(), solar.getMonth() - 1, solar.getDay()) / dayMs
      for (const day of days) {
        const solarDay = monthFirstDay + day - 1
        if (day > month.getDayCount() || solarDay < firstDay || solarDay > lastDay) continue
        const date = new Date(solarDay * dayMs).toISOString().slice(0, 10)
        yield {
          date,
          lunarDate: {
            year, month: Math.abs(signedMonth), day, isLeapMonth: signedMonth < 0,
            displayText: formatLunarDate(Math.abs(signedMonth), day, signedMonth < 0),
          },
        }
      }
    }
    elapsedMonths += months.length
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
