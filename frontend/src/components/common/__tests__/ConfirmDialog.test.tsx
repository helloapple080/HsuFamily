import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import ConfirmDialog from '@/components/common/ConfirmDialog'

describe('ConfirmDialog', () => {
  it('focuses cancel and closes on Escape without confirming', async () => {
    const user = userEvent.setup()
    const onConfirm = vi.fn()
    render(
      <ConfirmDialog
        trigger={<button>刪除成員</button>}
        title="刪除成員？"
        description="這個動作無法復原。"
        onConfirm={onConfirm}
      />
    )

    await user.click(screen.getByRole('button', { name: '刪除成員' }))
    await waitFor(() => expect(screen.getByRole('button', { name: '取消' })).toHaveFocus())
    await user.keyboard('{Escape}')

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(onConfirm).not.toHaveBeenCalled()
  })

  it('awaits confirmation and disables actions while pending', async () => {
    const user = userEvent.setup()
    let resolveConfirmation: (() => void) | undefined
    const onConfirm = vi.fn(() => new Promise<void>((resolve) => { resolveConfirmation = resolve }))
    render(
      <ConfirmDialog
        trigger={<button>刪除行程</button>}
        title="刪除行程？"
        description="這個動作無法復原。"
        onConfirm={onConfirm}
      />
    )

    await user.click(screen.getByRole('button', { name: '刪除行程' }))
    await user.click(screen.getByRole('button', { name: '確認刪除' }))
    expect(onConfirm).toHaveBeenCalledOnce()
    expect(screen.getByRole('button', { name: '刪除中…' })).toBeDisabled()
    resolveConfirmation?.()
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })
})
