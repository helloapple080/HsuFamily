import { describe, expect, it } from 'vitest'
import {
  formatLunarDate,
  getMonthLunarInfo,
  getTaiwanLunarFestivals,
  getTaiwanSolarFestivals,
  solarToLunar,
} from '@/lib/lunar'

describe('Taiwan calendar rules', () => {
  it('does not surface Mainland-only festivals', () => {
    const day = getMonthLunarInfo(2026, 7).find(
      (value) => value.solarDate === '2026-07-01'
    )

    expect(day?.solarFestivals).not.toContain('建党节')
  })

  it('uses the explicit Taiwan solar and lunar festival allowlists', () => {
    expect(getTaiwanSolarFestivals(10, 10)).toEqual(['國慶日'])
    expect(getTaiwanLunarFestivals(8, 15)).toEqual(['中秋節'])
    expect(getTaiwanSolarFestivals(7, 1)).toEqual([])
  })

  it('returns defensive festival arrays', () => {
    const festivals = getTaiwanSolarFestivals(10, 10)
    festivals.push('不應汙染規則')

    expect(getTaiwanSolarFestivals(10, 10)).toEqual(['國慶日'])
  })

  it('formats regular and leap lunar dates', () => {
    expect(formatLunarDate(1, 1, false)).toBe('農曆正月初一')
    expect(formatLunarDate(4, 15, true)).toBe('農曆閏四月十五')
  })

  it('uses exactly one Traditional Chinese leap-month marker for a real leap date', () => {
    const date = solarToLunar(2020, 5, 23)

    expect(date.monthChinese).toBe('四')
    expect(date.dayChinese).toBe('初一')
    expect(date.displayText).toBe('農曆閏四月初一')
  })

  it('uses the project Traditional Chinese name for lunar month twelve', () => {
    const date = solarToLunar(2026, 2, 16)

    expect(date.month).toBe(12)
    expect(date.monthChinese).toBe('臘')
    expect(date.monthChinese).not.toContain('腊')
    expect(date.displayText).toBe('農曆臘月廿九')
  })

  it('uses project-owned Traditional Chinese labels in month data', () => {
    const leapDay = getMonthLunarInfo(2020, 5).find(
      (value) => value.solarDate === '2020-05-23'
    )
    const twelfthMonthDay = getMonthLunarInfo(2026, 2).find(
      (value) => value.solarDate === '2026-02-16'
    )

    expect(leapDay).toMatchObject({
      lunarMonth: '四',
      lunarDay: '初一',
      displayText: '閏四月',
    })
    expect(twelfthMonthDay).toMatchObject({
      lunarMonth: '臘',
      lunarDay: '廿九',
      displayText: '廿九',
    })
  })

  it.each([
    [0, 1],
    [13, 1],
    [1, 0],
    [1, 31],
    [1.5, 1],
  ])('rejects invalid lunar date %s/%s', (month, day) => {
    expect(() => formatLunarDate(month, day, false)).toThrow('農曆日期不合法')
  })
})
