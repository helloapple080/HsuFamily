import type { ReactNode } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

const signalRMock = vi.hoisted(() => ({ builder: vi.fn() }))

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: signalRMock.builder,
  HubConnectionState: { Disconnected: 'Disconnected' },
  LogLevel: { Warning: 'Warning' },
}))

function Wrapper({ children }: { children: ReactNode }) {
  return <QueryClientProvider client={new QueryClient()}>{children}</QueryClientProvider>
}

describe('Demo SignalR', () => {
  afterEach(() => {
    vi.unstubAllEnvs()
    vi.resetModules()
    vi.clearAllMocks()
  })

  it('回傳展示狀態且不建立網路連線', async () => {
    vi.stubEnv('NEXT_PUBLIC_DEMO_MODE', 'true')
    const { useSignalR } = await import('@/hooks/useSignalR')

    const { result } = renderHook(() => useSignalR(), { wrapper: Wrapper })

    expect(result.current.status).toBe('demo')
    expect(signalRMock.builder).not.toHaveBeenCalled()
  })
})
