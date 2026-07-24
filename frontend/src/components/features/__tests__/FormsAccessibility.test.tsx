import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import LoginPage from '@/app/(auth)/login/page'
import { EventDialog } from '@/components/features/calendar/EventDialog'
import { MemberDialog } from '@/components/features/members/MemberDialog'
import { ItineraryDialog } from '@/components/features/itineraries/ItineraryDialog'

const mutations = vi.hoisted(() => ({
  eventCreate: vi.fn(),
  eventUpdate: vi.fn(),
  memberCreate: vi.fn(),
  memberUpdate: vi.fn(),
  itineraryCreate: vi.fn(),
  itineraryUpdate: vi.fn(),
}))

vi.mock('next/navigation', () => ({ useRouter: () => ({ push: vi.fn() }) }))
vi.mock('@/hooks/useCalendarEvents', () => ({
  useCreateEvent: () => ({ mutateAsync: mutations.eventCreate, isPending: false }),
  useUpdateEvent: () => ({ mutateAsync: mutations.eventUpdate, isPending: false }),
}))
vi.mock('@/hooks/useMembers', () => ({
  useCreateMember: () => ({ mutateAsync: mutations.memberCreate, isPending: false }),
  useUpdateMember: () => ({ mutateAsync: mutations.memberUpdate, isPending: false }),
}))
vi.mock('@/hooks/useItineraries', () => ({
  useCreateItinerary: () => ({ mutateAsync: mutations.itineraryCreate, isPending: false }),
  useUpdateItinerary: () => ({ mutateAsync: mutations.itineraryUpdate, isPending: false }),
}))

describe('accessible bounded forms', () => {
  beforeEach(() => {
    Object.values(mutations).forEach((mutation) => mutation.mockReset().mockResolvedValue(undefined))
  })

  it('adds login autocomplete and backend-aligned input limits', () => {
    render(<LoginPage />)
    expect(screen.getByLabelText('帳號')).toHaveAttribute('autocomplete', 'username')
    expect(screen.getByLabelText('帳號')).toHaveAttribute('maxlength', '254')
    expect(screen.getByLabelText('密碼')).toHaveAttribute('autocomplete', 'current-password')
    expect(screen.getByLabelText('密碼')).toHaveAttribute('maxlength', '128')
  })

  it.each([
    ['event', <EventDialog key="event"><button>新增事件</button></EventDialog>, '標題', 'event-title-error', '請輸入事件標題'],
    ['member', <MemberDialog key="member"><button>新增成員</button></MemberDialog>, '姓名', 'member-name-error', '請輸入姓名'],
    ['itinerary', <ItineraryDialog key="itinerary"><button>新增行程</button></ItineraryDialog>, '標題', 'itinerary-title-error', '請輸入行程標題'],
  ])('associates %s validation errors with their fields', async (_name, component, label, errorId, message) => {
    const user = userEvent.setup()
    render(component)
    await user.click(screen.getByRole('button', { name: /^\u65b0\u589e/ }))
    await user.click(screen.getByRole('button', { name: '儲存' }))

    const field = await screen.findByLabelText(label)
    expect(field).toHaveAttribute('aria-invalid', 'true')
    expect(field).toHaveAttribute('aria-describedby', errorId)
    expect(screen.getByText(message, { selector: '[role="alert"]' })).toBeVisible()
  })

  it.each([
    [<EventDialog key="event-required"><button>新增事件</button></EventDialog>, ['標題', '類型', '日期']],
    [<MemberDialog key="member-required"><button>新增成員</button></MemberDialog>, ['姓名', '稱謂', '國曆生日']],
    [<ItineraryDialog key="itinerary-required"><button>新增行程</button></ItineraryDialog>, ['標題', '日期']],
  ])('shows and programs every required field', async (component, labels) => {
    const user = userEvent.setup()
    render(component)
    await user.click(screen.getByRole('button', { name: /^\u65b0\u589e/ }))
    const dialog = screen.getByRole('dialog')
    expect(within(dialog).getAllByText('必填')).toHaveLength(labels.length)
    labels.forEach((label) => expect(within(dialog).getByLabelText(label)).toBeRequired())
  })

  it('keeps the event dialog open and announces a mutation failure', async () => {
    const user = userEvent.setup()
    mutations.eventCreate.mockRejectedValueOnce(new Error('無法儲存事件'))
    render(<EventDialog><button>新增事件</button></EventDialog>)
    await user.click(screen.getByRole('button', { name: '新增事件' }))
    await user.type(screen.getByLabelText('標題'), '家族聚餐')
    await user.type(screen.getByLabelText('日期'), '2026-07-24')
    await user.click(screen.getByRole('button', { name: '儲存' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('無法儲存事件')
    expect(screen.getByRole('dialog')).toBeVisible()
    await waitFor(() => expect(mutations.eventCreate).toHaveBeenCalledOnce())
  })

  it('keeps the selected local calendar date as the event default', async () => {
    const user = userEvent.setup()
    render(<EventDialog defaultDate={new Date(2026, 6, 1)}><button>新增事件</button></EventDialog>)
    await user.click(screen.getByRole('button', { name: '新增事件' }))
    expect(screen.getByLabelText('日期')).toHaveValue('2026-07-01')
  })
})
