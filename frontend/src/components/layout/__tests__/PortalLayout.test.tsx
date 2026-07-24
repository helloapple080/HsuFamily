import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import PortalLayout from '@/components/layout/PortalLayout'
import { useAuthStore } from '@/stores/authStore'

vi.mock('next/navigation', () => ({
  usePathname: () => '/calendar',
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
}))

vi.mock('@/hooks/useSignalR', () => ({
  useSignalR: () => ({ on: vi.fn(), reconnect: vi.fn(), status: 'connected' }),
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
})
