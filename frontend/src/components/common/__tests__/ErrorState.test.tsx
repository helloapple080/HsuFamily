import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import ErrorState from '@/components/common/ErrorState'

describe('ErrorState', () => {
  it('announces the problem and offers a recovery action', async () => {
    const retry = vi.fn()
    const user = userEvent.setup()

    render(
      <ErrorState
        title="無法載入行事曆"
        description="請檢查網路連線。"
        onRetry={retry}
      />
    )

    expect(screen.getByRole('alert')).toHaveTextContent('無法載入行事曆')
    expect(screen.getByRole('alert')).toHaveTextContent('請檢查網路連線。')
    await user.click(screen.getByRole('button', { name: '重新嘗試' }))
    expect(retry).toHaveBeenCalledOnce()
  })

  it('omits the recovery action when no retry callback is available', () => {
    render(<ErrorState title="無法載入資料" />)

    expect(screen.queryByRole('button', { name: '重新嘗試' })).not.toBeInTheDocument()
  })
})
