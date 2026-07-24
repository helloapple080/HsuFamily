import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import ConnectionStatus from '@/components/common/ConnectionStatus'

describe('ConnectionStatus', () => {
  it('stays quiet while connected', () => {
    const { container } = render(<ConnectionStatus status="connected" reconnect={vi.fn()} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('announces reconnection without interrupting work', () => {
    render(<ConnectionStatus status="reconnecting" reconnect={vi.fn()} />)
    expect(screen.getByRole('status')).toHaveTextContent('即時更新重新連線中')
  })

  it('offers manual recovery after disconnection', async () => {
    const user = userEvent.setup()
    const reconnect = vi.fn().mockResolvedValue(undefined)
    render(<ConnectionStatus status="disconnected" reconnect={reconnect} />)
    await user.click(screen.getByRole('button', { name: '重新連線' }))
    expect(reconnect).toHaveBeenCalledOnce()
  })
})
