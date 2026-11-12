// @vitest-environment node
import { describe, expect, it } from 'vitest'
import type { CalendarEvent, RecurrenceRule } from '@/types/event'
import { solarToLunar } from '@/lib/lunar'
import { eventInput, eventQuery } from '../event-contract'
import { expandCalendarEvent } from '../event-recurrence'

const id = '10000000-0000-4000-8000-000000000002'
const base = {
  title: '家族聚會', eventType: 'General', startDate: '2026-01-31T00:00:00.000Z', isAllDay: true,
  isLunarDate: false, relatedMemberIds: [], tags: [],
} as const

function template(recurrenceRule: RecurrenceRule, changes: Partial<CalendarEvent> = {}): CalendarEvent {
  return { ...base, relatedMemberIds: [], tags: [], id, createdBy: id, createdAt: base.startDate, updatedAt: base.startDate,
    isOccurrence: false, sourceRuleId: null, recurrenceRule, ...changes }
}

describe('精簡版事件驗證', () => {
  it('沿用月曆民用日期，不因輸入的時區偏移而換日', () => {
    expect(eventInput.parse({ ...base, startDate: '2026-01-31T00:00:00+08:00' }).startDate).toBe(base.startDate)
    expect(eventInput.parse({ ...base, isAllDay: false, startDate: '2026-01-31T00:00:00+08:00' }).startDate)
      .toBe('2026-01-30T16:00:00.000Z')
  })

  it('接受月曆表單產生的農曆資訊，但拒絕日期顛倒、不完整的週期和過大的查詢', () => {
    expect(eventInput.safeParse({ ...base, isLunarDate: true, lunarDate: solarToLunar(2026, 1, 31) }).success).toBe(true)
    expect(eventInput.safeParse({ ...base, endDate: '2026-01-01' }).success).toBe(false)
    expect(eventInput.safeParse({ ...base, recurrenceRule: { type: 'LunarMonthly', interval: 1, leapMonthPolicy: 'Both' } }).success).toBe(false)
    expect(eventQuery.safeParse({ start: '2026-01-01', end: '2026-04-30' }).success).toBe(false)
    expect(eventQuery.safeParse({ start: '2026-02-30', end: '2026-03-01' }).success).toBe(false)
  })

  it('保留原版允許的 8000 字祭祀備註，但不接受超過原上限的祭品清單', () => {
    const worshipDetails = { notes: '祭'.repeat(8000), offerings: ['水果'], preparations: [] }
    expect(eventInput.safeParse({ ...base, eventType: 'Worship', worshipDetails }).success).toBe(true)
    expect(eventInput.safeParse({
      ...base, eventType: 'Worship', worshipDetails: { ...worshipDetails, offerings: Array.from({ length: 21 }, () => '水果') },
    }).success).toBe(false)
  })
})

describe('普通行事曆事件展開', () => {
  it('保留國曆每月事件的週期，略過不存在的日期，不重複顯示來源', () => {
    const rule = template({ type: 'Monthly', interval: 1, leapMonthPolicy: 'RegularOnly' })
    const events = expandCalendarEvent(rule, '2026-01-30T00:00:00.000Z', '2026-03-31T23:59:59.999Z')
    expect(events.map((event) => event.startDate.slice(0, 10))).toEqual(['2026-01-31', '2026-03-31'])
    expect(events[1]).toMatchObject({ isOccurrence: true, sourceRuleId: id })
  })

  it('國曆每年事件保留跨日長度、實際時間和生效日期', () => {
    const rule = template({ type: 'Yearly', interval: 2, leapMonthPolicy: 'RegularOnly', effectiveUntil: '2028-01-31T00:00:00.000Z' }, {
      isAllDay: false, startDate: '2026-01-31T18:00:00.000Z', endDate: '2026-02-01T01:00:00.000Z',
    })
    const events = expandCalendarEvent(rule, '2028-02-01T00:00:00.000Z', '2028-02-01T23:59:59.999Z')
    expect(events).toMatchObject([{ startDate: '2028-01-31T18:00:00.000Z', endDate: '2028-02-01T01:00:00.000Z' }])
    expect(expandCalendarEvent(rule, '2030-01-31T00:00:00.000Z', '2030-02-01T23:59:59.999Z')).toEqual([])
  })

  it('農曆每年規則只在指定閏月發生，並保留來源供編輯', () => {
    const rule = template({ type: 'LunarYearly', interval: 1, leapMonthPolicy: 'LeapOnly' }, {
      isLunarDate: true, startDate: '2024-07-06T00:00:00.000Z', lunarDate: solarToLunar(2024, 7, 6),
    })
    const events = expandCalendarEvent(rule, '2025-07-01T00:00:00.000Z', '2025-08-25T23:59:59.999Z')
    expect(events).toMatchObject([{ startDate: '2025-07-25T00:00:00.000Z', sourceRuleId: id, lunarDate: { isLeapMonth: true, displayText: '農曆閏六月初一' } }])
  })

  it('農曆每月以實際月份（含閏月）計算間隔，沒有三十就跳過', () => {
    const rule = template({ type: 'LunarMonthly', interval: 2, lunarDays: [1, 30], leapMonthPolicy: 'Both' }, {
      isLunarDate: true, startDate: '2025-05-27T00:00:00.000Z', lunarDate: solarToLunar(2025, 5, 27),
    })
    const events = expandCalendarEvent(rule, '2025-07-01T00:00:00.000Z', '2025-08-24T23:59:59.999Z')
    expect(events.map((event) => event.startDate.slice(0, 10))).toEqual(['2025-07-25'])
  })

  it('沿用既有農曆規則：來源月內可顯示早於原始日期但已生效的場次', () => {
    const rule = template({ type: 'LunarMonthly', interval: 1, lunarDays: [1, 15], leapMonthPolicy: 'RegularOnly',
      effectiveFrom: '2026-03-01T00:00:00.000Z' }, {
      isLunarDate: true, startDate: '2026-03-05T00:00:00.000Z', lunarDate: solarToLunar(2026, 3, 5),
    })
    const events = expandCalendarEvent(rule, '2026-02-15T00:00:00.000Z', '2026-03-04T23:59:59.999Z')
    expect(events).toMatchObject([{ startDate: '2026-03-03T00:00:00.000Z', isOccurrence: true }])
  })
})
