import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import ThemeSwitcher from '@/components/theme/ThemeSwitcher'

const setTheme = vi.fn()

vi.mock('next-themes', () => ({
  useTheme: () => ({ theme: 'system', setTheme }),
}))

describe('ThemeSwitcher', () => {
  it('offers light, dark and system themes', async () => {
    const user = userEvent.setup()
    render(<ThemeSwitcher />)

    expect(screen.getByRole('group', { name: '切換顯示主題' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: '淺色' }))

    expect(setTheme).toHaveBeenCalledWith('light')
    expect(screen.getByRole('button', { name: '深色' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: '跟隨系統' })).toBeInTheDocument()
  })
})
