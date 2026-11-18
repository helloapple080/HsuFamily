import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import PortalLayout from '@/components/layout/PortalLayout'
import { useAuthStore } from '@/stores/authStore'

vi.mock('next/navigation', () => ({
  usePathname: () => '/calendar',
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
}))

describe('PortalLayout', () => {
  beforeEach(() => {
    useAuthStore.setState({
      user: { id: '1', username: 'admin', email: 'admin@example.com', displayName: '許浴宸', role: 'Admin' },
      isAuthenticated: true,
      isAdmin: true,
      isHydrated: true,
    })
  })

  it('provides skip navigation, current-page state and a main landmark', () => {
    render(<PortalLayout><p>內容</p></PortalLayout>)
    expect(screen.getByRole('link', { name: '跳到主要內容' })).toHaveAttribute('href', '#main-content')
    expect(screen.getByRole('navigation', { name: '主要導覽' })).toBeVisible()
    expect(screen.getAllByRole('link', { name: /行事曆/ })[0]).toHaveAttribute('aria-current', 'page')
    expect(screen.getByRole('main')).toHaveAttribute('id', 'main-content')
  })

  it('mounts theme controls in desktop and mobile user sections', async () => {
    const user = userEvent.setup()
    render(<PortalLayout><p>內容</p></PortalLayout>)

    await user.click(screen.getByRole('button', { name: '帳號與顯示設定' }))

    expect(screen.getAllByRole('group', { name: '切換顯示主題', hidden: true })).toHaveLength(2)
  })

  it('Cloudflare 私人站的手機導覽不必先打開選單', () => {
    render(<PortalLayout onAccessLogout={vi.fn()}><p>內容</p></PortalLayout>)
    const nav = within(screen.getByRole('navigation', { name: '手機主要導覽' }))
    expect(nav.getByRole('link', { name: '行事曆' })).toHaveAttribute('aria-current', 'page')
    expect(nav.getByRole('link', { name: '家用備忘' })).toHaveAttribute('href', '/household')
    expect(nav.getAllByRole('link')).toHaveLength(4)
  })
})
