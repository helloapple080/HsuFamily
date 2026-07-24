import { render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import CalendarPage from '@/app/(portal)/calendar/page'
import { useAuthStore } from '@/stores/authStore'

vi.mock('@/hooks/useCalendarEvents', () => ({ useCalendarEvents: () => ({ data: [], isLoading: false }) }))
vi.mock('@/hooks/useSignalR', () => ({ useSignalR: () => ({ on: () => () => undefined }) }))
vi.mock('@tanstack/react-query', () => ({ useQueryClient: () => ({ invalidateQueries: vi.fn() }) }))
vi.mock('@/lib/lunar', () => ({
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
})
