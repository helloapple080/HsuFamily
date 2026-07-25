import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import CalendarPage from '@/app/(portal)/calendar/page'
import { EventDialog } from '@/components/features/calendar/EventDialog'
import { useAuthStore } from '@/stores/authStore'
import type { CalendarEvent } from '@/types/event'

const mutations = vi.hoisted(() => ({ create: vi.fn(), update: vi.fn() }))
const hookState = vi.hoisted(() => ({
  events: [] as CalendarEvent[],
  detail: null as CalendarEvent | null,
  detailLoading: false,
  detailError: null as Error | null,
  range: vi.fn(),
  detailArgs: vi.fn(),
  refetch: vi.fn(),
}))

vi.mock('@/hooks/useCalendarEvents', () => ({
  useCalendarEvents: (start: string, end: string) => {
    hookState.range(start, end)
    return { data: hookState.events, isLoading: false }
  },
  useCalendarEvent: (id: string | null, enabled: boolean) => {
    hookState.detailArgs(id, enabled)
    return {
      data: enabled ? hookState.detail : undefined,
      isLoading: enabled && hookState.detailLoading,
      isError: enabled && hookState.detailError !== null,
      error: hookState.detailError,
      refetch: hookState.refetch,
    }
  },
  useCreateEvent: () => ({ mutateAsync: mutations.create, isPending: false }),
  useUpdateEvent: () => ({ mutateAsync: mutations.update, isPending: false }),
}))
vi.mock('@/hooks/useSignalR', () => ({ useSignalR: () => ({ on: () => () => undefined }) }))
vi.mock('@tanstack/react-query', () => ({ useQueryClient: () => ({ invalidateQueries: vi.fn() }) }))
vi.mock('@/lib/lunar', () => ({
  solarToLunar: () => ({
    year: 2026,
    month: 8,
    day: 15,
    isLeapMonth: false,
    displayText: '農曆八月十五',
  }),
  getMonthLunarInfo: () => [{
    solarDate: '2026-07-01',
    lunarMonth: '五',
    lunarDay: '十七',
    isLeapMonth: false,
    displayText: '十七',
    festivals: [],
    solarFestivals: [],
    jieQi: '',
  }],
}))
describe('CalendarPage', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-07-24T00:00:00Z'))
    useAuthStore.setState({ isAdmin: false })
    mutations.create.mockReset()
    mutations.update.mockReset()
    hookState.events = []
    hookState.detail = null
    hookState.detailLoading = false
    hookState.detailError = null
    hookState.range.mockReset()
    hookState.detailArgs.mockReset()
    hookState.refetch.mockReset()
  })

  afterEach(() => vi.useRealTimers())

  it('uses a full date accessible name and hides create controls from normal users', () => {
    render(<CalendarPage />)
    const dayButton = screen.getByRole('button', { name: '2026年7月1日，農曆五月十七' })
    expect(dayButton).toBeVisible()
    expect(dayButton).toHaveClass('min-w-11')
    const calendarGrid = screen.getByRole('grid')
    expect(calendarGrid).toHaveClass('min-w-[21rem]')
    expect(calendarGrid.parentElement).toHaveClass('overflow-x-auto')
    expect(screen.queryByRole('button', { name: '新增事件' })).not.toBeInTheDocument()
  })

  it('requests an inclusive UTC end-of-day for the final visible week day', () => {
    render(<CalendarPage />)

    expect(hookState.range).toHaveBeenCalledWith(
      '2026-06-28T00:00:00.000Z',
      '2026-08-01T23:59:59.999Z',
    )
    const finalDayTimedEvent = new Date('2026-08-01T21:45:00.000Z')
    const requestedEnd = new Date(hookState.range.mock.calls[0][1])
    expect(requestedEnd.getTime()).toBeGreaterThanOrEqual(finalDayTimedEvent.getTime())
  })

  it.each([
    ['LunarYearly', null],
    ['LunarMonthly', [1, 15]],
  ] as const)('submits explicit %s recurrence controls', async (recurrenceType, lunarDays) => {
    vi.useRealTimers()
    render(<EventDialog><button>新增事件</button></EventDialog>)
    fireEvent.click(screen.getByRole('button', { name: '新增事件' }))
    fireEvent.change(screen.getByLabelText('標題'), { target: { value: '中秋聚會' } })
    fireEvent.change(screen.getByLabelText('日期'), { target: { value: '2026-09-25' } })
    fireEvent.click(screen.getByRole('checkbox', { name: '使用農曆日期' }))
    fireEvent.change(screen.getByLabelText('農曆重複方式'), { target: { value: recurrenceType } })
    if (recurrenceType === 'LunarMonthly') {
      fireEvent.change(screen.getByLabelText('每月農曆日'), { target: { value: '1, 15' } })
    }
    fireEvent.change(screen.getByLabelText('閏月處理'), { target: { value: 'Both' } })
    fireEvent.change(screen.getByLabelText('生效日期'), { target: { value: '2026-01-01' } })
    fireEvent.change(screen.getByLabelText('結束日期'), { target: { value: '2030-12-31' } })
    fireEvent.click(screen.getByRole('button', { name: '儲存' }))

    await waitFor(() => expect(mutations.create).toHaveBeenCalledTimes(1))
    expect(mutations.create).toHaveBeenCalledWith(expect.objectContaining({
      isLunarDate: true,
      lunarDate: expect.objectContaining({ month: 8, day: 15 }),
      recurrenceRule: {
        type: recurrenceType,
        interval: 1,
        lunarDays,
        leapMonthPolicy: 'Both',
        effectiveFrom: '2026-01-01T00:00:00.000Z',
        effectiveUntil: '2030-12-31T00:00:00.000Z',
      },
    }))
  })

  it('preserves existing lunar recurrence values when editing', () => {
    const event: CalendarEvent = {
      id: 'rule-1',
      title: '初一十五祭祖',
      eventType: 'Worship',
      startDate: '2026-02-17T00:00:00.000Z',
      isAllDay: true,
      isLunarDate: true,
      lunarDate: { year: 2026, month: 1, day: 1, isLeapMonth: false, displayText: '農曆正月初一' },
      recurrenceRule: {
        type: 'LunarMonthly',
        interval: 2,
        lunarDays: [1, 15],
        leapMonthPolicy: 'RegularOnly',
        effectiveFrom: '2026-02-17T00:00:00.000Z',
        effectiveUntil: '2028-02-17T00:00:00.000Z',
      },
      relatedMemberIds: [],
      tags: [],
      createdBy: 'user-1',
      createdAt: '2026-01-01T00:00:00.000Z',
      updatedAt: '2026-01-01T00:00:00.000Z',
      isOccurrence: false,
      sourceRuleId: null,
    }

    render(<EventDialog event={event}><button>編輯事件</button></EventDialog>)
    fireEvent.click(screen.getByRole('button', { name: '編輯事件' }))

    expect(screen.getByLabelText('農曆重複方式')).toHaveValue('LunarMonthly')
    expect(screen.getByLabelText('重複間隔')).toHaveValue(2)
    expect(screen.getByLabelText('每月農曆日')).toHaveValue('1, 15')
    expect(screen.getByLabelText('生效日期')).toHaveValue('2026-02-17')
    expect(screen.getByLabelText('結束日期')).toHaveValue('2028-02-17')
  })

  it('loads the persisted source before editing an occurrence and updates the original anchor', async () => {
    vi.useRealTimers()
    const sourceEvent: CalendarEvent = {
      id: 'rule-1',
      title: '中秋聚會來源',
      eventType: 'Festival',
      startDate: '2025-10-06T09:30:00.000Z',
      endDate: '2025-10-06T11:00:00.000Z',
      isAllDay: false,
      isLunarDate: true,
      lunarDate: { year: 2025, month: 8, day: 15, isLeapMonth: false, displayText: '農曆八月十五' },
      recurrenceRule: {
        type: 'LunarYearly',
        interval: 1,
        lunarDays: null,
        leapMonthPolicy: 'RegularOnly',
        effectiveFrom: '2025-10-06T00:00:00.000Z',
        effectiveUntil: null,
      },
      relatedMemberIds: [],
      tags: ['團聚'],
      createdBy: 'user-1',
      createdAt: '2025-01-01T00:00:00.000Z',
      updatedAt: '2025-01-01T00:00:00.000Z',
      isOccurrence: false,
      sourceRuleId: null,
    }
    hookState.detail = sourceEvent
    hookState.events = [{
      ...sourceEvent,
      id: 'rule-1:2026-07-01',
      title: '中秋聚會 occurrence',
      startDate: '2026-07-01T09:30:00.000Z',
      endDate: '2026-07-01T11:00:00.000Z',
      isOccurrence: true,
      sourceRuleId: 'rule-1',
    }]
    useAuthStore.setState({ isAdmin: true })

    render(<CalendarPage />)
    fireEvent.click(screen.getByRole('button', { name: '編輯事件：中秋聚會 occurrence' }))
    expect(await screen.findByDisplayValue('中秋聚會來源')).toBeVisible()
    fireEvent.click(screen.getByRole('button', { name: '儲存' }))

    await waitFor(() => expect(mutations.update).toHaveBeenCalledTimes(1))
    expect(mutations.update).toHaveBeenCalledWith(expect.objectContaining({
      id: 'rule-1',
      data: expect.objectContaining({
        startDate: '2025-10-06T09:30:00.000Z',
        endDate: '2025-10-06T11:00:00.000Z',
      }),
    }))
    expect(hookState.detailArgs).toHaveBeenCalledWith('rule-1', true)
  })

  it('shows occurrence source loading and failure states visibly', () => {
    const occurrence: CalendarEvent = {
      id: 'rule-1:2026-07-01',
      title: '待載入 occurrence',
      eventType: 'Festival',
      startDate: '2026-07-01T00:00:00.000Z',
      isAllDay: true,
      isLunarDate: true,
      relatedMemberIds: [],
      tags: [],
      createdBy: 'user-1',
      createdAt: '2025-01-01T00:00:00.000Z',
      updatedAt: '2025-01-01T00:00:00.000Z',
      isOccurrence: true,
      sourceRuleId: 'rule-1',
    }
    hookState.events = [occurrence]
    hookState.detailLoading = true
    useAuthStore.setState({ isAdmin: true })
    const { rerender } = render(<CalendarPage />)
    fireEvent.click(screen.getByRole('button', { name: '編輯事件：待載入 occurrence' }))
    expect(screen.getByRole('status')).toHaveTextContent('載入來源事件中')

    hookState.detailLoading = false
    hookState.detailError = new Error('來源事件讀取失敗')
    rerender(<CalendarPage />)
    expect(screen.getByRole('alert')).toHaveTextContent('無法載入來源事件')
    expect(screen.getByRole('button', { name: '重新載入' })).toBeVisible()
  })

  it.each([
    ['Yearly', '國曆每年重複'],
    ['Monthly', '國曆每月重複'],
  ] as const)('preserves a Gregorian %s recurrence rule as read-only when editing', async (recurrenceType, recurrenceLabel) => {
    vi.useRealTimers()
    const event: CalendarEvent = {
      id: 'gregorian-rule',
      title: '國曆週年聚會',
      eventType: 'Anniversary',
      startDate: '2025-07-01T08:00:00.000Z',
      endDate: '2025-07-01T09:00:00.000Z',
      isAllDay: false,
      isLunarDate: false,
      recurrenceRule: {
        type: recurrenceType,
        interval: 2,
        lunarDays: null,
        leapMonthPolicy: 'RegularOnly',
        effectiveFrom: '2025-07-01T00:00:00.000Z',
        effectiveUntil: '2035-07-01T00:00:00.000Z',
      },
      relatedMemberIds: [],
      tags: [],
      createdBy: 'user-1',
      createdAt: '2025-01-01T00:00:00.000Z',
      updatedAt: '2025-01-01T00:00:00.000Z',
      isOccurrence: false,
      sourceRuleId: null,
    }

    render(<EventDialog event={event}><button>編輯國曆事件</button></EventDialog>)
    fireEvent.click(screen.getByRole('button', { name: '編輯國曆事件' }))
    expect(screen.getByRole('note')).toHaveTextContent(recurrenceLabel)
    expect(screen.getByRole('checkbox', { name: '使用農曆日期' })).toBeDisabled()
    fireEvent.click(screen.getByRole('button', { name: '儲存' }))

    await waitFor(() => expect(mutations.update).toHaveBeenCalledTimes(1))
    expect(mutations.update).toHaveBeenCalledWith({
      id: 'gregorian-rule',
      data: expect.objectContaining({
        startDate: '2025-07-01T08:00:00.000Z',
        recurrenceRule: event.recurrenceRule,
      }),
    })
  })
})
