import { render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import AdminOnly from '@/components/common/AdminOnly'
import { useAuthStore } from '@/stores/authStore'

describe('AdminOnly', () => {
  afterEach(() => useAuthStore.setState({ isAdmin: false }))

  it('does not render children for a normal user', () => {
    useAuthStore.setState({ isAdmin: false })
    render(<AdminOnly><button>新增</button></AdminOnly>)
    expect(screen.queryByRole('button', { name: '新增' })).not.toBeInTheDocument()
  })

  it('renders children for an administrator', () => {
    useAuthStore.setState({ isAdmin: true })
    render(<AdminOnly><button>新增</button></AdminOnly>)
    expect(screen.getByRole('button', { name: '新增' })).toBeVisible()
  })
})
