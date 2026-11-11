import { describe, expect, it } from 'vitest'
import { getWorshipDates } from '../worship'

describe('農曆拜拜規則', () => {
  it('每年使用同一農曆規則而不是複製國曆日期', () => {
    const rule = { lunarMonth: 8, lunarDay: 15, leapMonthPolicy: 'RegularOnly' } as const
    expect(getWorshipDates(rule, 2026)).toEqual(['2026-09-25'])
    expect(getWorshipDates(rule, 2036)).toEqual(['2036-10-04'])
  })

  it('同一國曆年涵蓋前一個農曆年仍未過完的月份', () => {
    expect(getWorshipDates({ lunarMonth: 12, lunarDay: 15, leapMonthPolicy: 'RegularOnly' }, 2026))
      .toEqual(['2026-02-02'])
  })

  it('尊重閏月政策，沒有閏月時不移到一般月份', () => {
    expect(getWorshipDates({ lunarMonth: 6, lunarDay: 15, leapMonthPolicy: 'Both' }, 2025))
      .toEqual(['2025-07-09', '2025-08-08'])
    expect(getWorshipDates({ lunarMonth: 6, lunarDay: 15, leapMonthPolicy: 'LeapOnly' }, 2026))
      .toEqual([])
  })

  it('每個月重複時跳過沒有三十日的小月', () => {
    const dates = getWorshipDates({ lunarMonth: null, lunarDay: 30, leapMonthPolicy: 'RegularOnly' }, 2026)
    expect(dates).toHaveLength(6)
    expect(dates).toContain('2026-03-18')
  })

  it('拒絕不合法的農曆月日與國曆年', () => {
    expect(() => getWorshipDates({ lunarMonth: 13, lunarDay: 15, leapMonthPolicy: 'Both' }, 2026)).toThrow()
    expect(() => getWorshipDates({ lunarMonth: null, lunarDay: 31, leapMonthPolicy: 'Both' }, 2026)).toThrow()
    expect(() => getWorshipDates({ lunarMonth: 8, lunarDay: 15, leapMonthPolicy: 'Both' }, 1800)).toThrow()
  })
})
