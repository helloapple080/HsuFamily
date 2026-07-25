import type { ReactNode } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

const apiMock = vi.hoisted(() => ({
  get: vi.fn(),
  post: vi.fn(),
  put: vi.fn(),
  delete: vi.fn(),
}))

vi.mock('@/lib/api', () => ({ default: apiMock }))

function createWrapper() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } })
  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  }
}

describe('Demo data hooks', () => {
  afterEach(() => {
    vi.unstubAllEnvs()
    vi.resetModules()
    vi.clearAllMocks()
  })

  it('讀取展示成員時不呼叫 API', async () => {
    vi.stubEnv('NEXT_PUBLIC_DEMO_MODE', 'true')
    const { useMembers } = await import('@/hooks/useMembers')
    const { result } = renderHook(() => useMembers(), { wrapper: createWrapper() })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    expect(result.current.data).toHaveLength(4)
    expect(apiMock.get).not.toHaveBeenCalled()
  })

  it('展示模式拒絕寫入且不呼叫 API', async () => {
    vi.stubEnv('NEXT_PUBLIC_DEMO_MODE', 'true')
    const { useDeleteMember } = await import('@/hooks/useMembers')
    const { result } = renderHook(() => useDeleteMember(), { wrapper: createWrapper() })

    await act(async () => {
      await expect(result.current.mutateAsync('demo-member-1')).rejects.toThrow('公開展示模式不會儲存任何變更')
    })

    expect(apiMock.delete).not.toHaveBeenCalled()
  })
})
