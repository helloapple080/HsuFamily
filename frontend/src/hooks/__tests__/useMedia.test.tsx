import type { ReactNode } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'

const apiMock = vi.hoisted(() => ({ post: vi.fn(), patch: vi.fn(), delete: vi.fn() }))
vi.mock('@/lib/api', () => ({ default: apiMock }))

function createWrapper() {
  const client = new QueryClient({ defaultOptions: { mutations: { retry: false } } })
  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={client}>{children}</QueryClientProvider>
  }
}

describe('media mutations', () => {
  afterEach(() => {
    vi.unstubAllEnvs()
    vi.resetModules()
    vi.clearAllMocks()
  })

  it('sends a multipart upload with the bounded media fields', async () => {
    apiMock.post.mockResolvedValue({ data: { success: true, data: { id: 'media-1' } } })
    const { useUploadMedia } = await import('@/hooks/useMedia')
    const { result } = renderHook(() => useUploadMedia(), { wrapper: createWrapper() })
    const file = new File(['image'], 'family.png', { type: 'image/png' })

    await act(async () => {
      await result.current.mutateAsync({
        file,
        purpose: 'MemberAvatar',
        ownerEntityId: 'owner-1',
        visibility: 'FamilyOnly',
      })
    })

    const [url, body, config] = apiMock.post.mock.calls[0] as [string, FormData, { onUploadProgress: unknown }]
    expect(url).toBe('/api/media')
    expect(body).toBeInstanceOf(FormData)
    expect(body.get('file')).toBe(file)
    expect(body.get('purpose')).toBe('MemberAvatar')
    expect(body.get('ownerEntityId')).toBe('owner-1')
    expect(body.get('visibility')).toBe('FamilyOnly')
    expect(config.onUploadProgress).toBeTypeOf('function')
  })

  it('rejects writes in demo mode without calling the API', async () => {
    vi.stubEnv('NEXT_PUBLIC_DEMO_MODE', 'true')
    const { useUploadMedia } = await import('@/hooks/useMedia')
    const { result } = renderHook(() => useUploadMedia(), { wrapper: createWrapper() })

    await act(async () => {
      await expect(result.current.mutateAsync({
        file: new File(['image'], 'family.png', { type: 'image/png' }),
        purpose: 'MemberAvatar',
        ownerEntityId: 'owner-1',
        visibility: 'FamilyOnly',
      })).rejects.toThrow('公開展示模式不會儲存任何變更')
    })
    expect(apiMock.post).not.toHaveBeenCalled()
  })
})
