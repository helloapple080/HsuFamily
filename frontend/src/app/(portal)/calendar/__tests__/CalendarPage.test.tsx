import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import CalendarPage from '@/app/(portal)/calendar/page'
import { EventDialog } from '@/components/features/calendar/EventDialog'
import { useAuthStore } from '@/stores/authStore'
import type { CalendarEvent } from '@/types/event'

const mutations = vi.hoisted(() => ({ create: vi.fn(), update: vi.fn() }))

vi.mock('@/hooks/useCalendarEvents', () => ({
  useCalendarEvents: () => ({ data: [], isLoading: false }),
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
    }

    render(<EventDialog event={event}><button>編輯事件</button></EventDialog>)
    fireEvent.click(screen.getByRole('button', { name: '編輯事件' }))

    expect(screen.getByLabelText('農曆重複方式')).toHaveValue('LunarMonthly')
    expect(screen.getByLabelText('重複間隔')).toHaveValue(2)
    expect(screen.getByLabelText('每月農曆日')).toHaveValue('1, 15')
    expect(screen.getByLabelText('生效日期')).toHaveValue('2026-02-17')
    expect(screen.getByLabelText('結束日期')).toHaveValue('2028-02-17')
  })
})
