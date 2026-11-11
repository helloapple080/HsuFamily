import { Lunar, LunarYear } from 'lunar-javascript'
import { z } from 'zod'
import type { LeapMonthPolicy } from '@/types/event'

export interface WorshipDateRule {
  lunarMonth: number | null
  lunarDay: number
  leapMonthPolicy: LeapMonthPolicy
}

const worshipDateRule = z.object({
  lunarMonth: z.number().int().min(1).max(12).nullable(),
  lunarDay: z.number().int().min(1).max(30),
  leapMonthPolicy: z.enum(['RegularOnly', 'LeapOnly', 'Both']),
})

/// 回傳指定國曆年內符合規則的日期；遇小月沒有指定日期時跳過。
export function getWorshipDates(input: WorshipDateRule, solarYear: number): string[] {
  const rule = worshipDateRule.parse(input)
  if (!Number.isInteger(solarYear) || solarYear < 1901 || solarYear > 2100) {
    throw new RangeError('國曆年須介於 1901 至 2100')
  }

  const dates: string[] = []
  for (const year of [solarYear - 1, solarYear]) {
    for (const month of LunarYear.fromYear(year).getMonthsInYear()) {
      const signedMonth = month.getMonth()
      if (rule.lunarMonth !== null && Math.abs(signedMonth) !== rule.lunarMonth) continue
      if (signedMonth < 0 && rule.leapMonthPolicy === 'RegularOnly') continue
      if (signedMonth > 0 && rule.leapMonthPolicy === 'LeapOnly') continue
      if (rule.lunarDay > month.getDayCount()) continue

      const solar = Lunar.fromYmd(year, signedMonth, rule.lunarDay).getSolar()
      if (solar.getYear() !== solarYear) continue
      const mm = String(solar.getMonth()).padStart(2, '0')
      const dd = String(solar.getDay()).padStart(2, '0')
      dates.push(`${solarYear}-${mm}-${dd}`)
    }
  }
  return dates.sort()
}
