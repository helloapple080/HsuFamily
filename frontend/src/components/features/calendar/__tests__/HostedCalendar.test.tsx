import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import api from '@/lib/api'
import { useAuthStore } from '@/stores/authStore'
import HostedCalendar from '../HostedCalendar'

afterEach(() => useAuthStore.getState().logout())

it('家人切換年份可看到農曆對應日期，但不會看到編輯按鈕', async () => {
  vi.spyOn(api, 'get').mockResolvedValue({ data: { success: true, message: '', errors: null, data: [
    { id: '70000000-0000-4000-8000-000000000001', title: '中秋拜拜', lunarMonth: 8, lunarDay: 15, leapMonthPolicy: 'RegularOnly', notes: '' },
  ] } })
  useAuthStore.getState().login({ id: 'family', role: 'User', username: 'family', email: 'family@example.test', displayName: '家人' })
  render(<QueryClientProvider client={new QueryClient()}><HostedCalendar /></QueryClientProvider>)
  const year = screen.getByRole('spinbutton', { name: '國曆年份' })
  await userEvent.clear(year)
  await userEvent.type(year, '2036')
  expect(await screen.findByText('2036-10-04')).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: '新增規則' })).not.toBeInTheDocument()
})

it('下一次拜拜能跨到隔年，不隨年度清單切換', async () => {
  vi.setSystemTime(new Date('2026-12-31T16:30:00Z'))
  try {
    vi.spyOn(api, 'get').mockResolvedValue({ data: { data: [
      { id: '70000000-0000-4000-8000-000000000001', title: '除夕前拜拜', lunarMonth: 12, lunarDay: 15, leapMonthPolicy: 'RegularOnly', notes: '' },
    ] } })
    useAuthStore.getState().login({ id: 'family', role: 'User', username: 'family', email: 'family@example.test', displayName: '家人' })
    render(<QueryClientProvider client={new QueryClient()}><HostedCalendar /></QueryClientProvider>)
    const upcoming = within(await screen.findByRole('region', { name: '下一次拜拜' }))
    expect(upcoming.getByText('除夕前拜拜')).toBeInTheDocument()
    expect(upcoming.getByText('2027-01-22')).toBeInTheDocument()
    const year = screen.getByRole('spinbutton', { name: '國曆年份' })
    await userEvent.clear(year)
    await userEvent.type(year, '2036')
    expect(upcoming.getByText('2027-01-22')).toBeInTheDocument()
  } finally { vi.useRealTimers() }
})
