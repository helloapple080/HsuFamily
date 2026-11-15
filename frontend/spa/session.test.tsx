import { MutationCache, QueryCache, useQuery, useQueryClient } from '@tanstack/react-query'
import { act, render, screen, waitFor } from '@testing-library/react'
import { AxiosError, AxiosHeaders } from 'axios'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import api from '@/lib/api'
import { useAuthStore } from '@/stores/authStore'
import type { UserInfo } from '@/types/auth'
import { SessionGate } from './session'

const member: UserInfo = {
  id: 'fake-member', username: 'fake', email: 'fake@example.test', displayName: '測試家人', role: 'Admin',
}

function deny(status = 403) {
  const config = { headers: new AxiosHeaders() }
  return new AxiosError('Forbidden', undefined, config, undefined, {
    data: { success: false, data: null }, status, statusText: 'Forbidden', headers: {}, config,
  })
}

function authenticate(user: UserInfo) {
  return { data: { success: true, data: user } }
}

beforeEach(() => useAuthStore.getState().logout())
afterEach(() => useAuthStore.getState().logout())

it('does not mount or query private content for unapproved visitors', async () => {
  vi.spyOn(api, 'get').mockRejectedValue(deny())
  const privateQuery = vi.fn(async () => '私人資料')
  function Child() {
    const data = useQuery({ queryKey: ['private'], queryFn: privateQuery })
    return <p>{data.data}</p>
  }
  render(<SessionGate><Child /></SessionGate>)
  expect(await screen.findByRole('heading', { name: '尚未核准使用' })).toBeVisible()
  expect(privateQuery).not.toHaveBeenCalled()
  expect(screen.getByRole('link', { name: '切換登入帳號' })).toHaveAttribute('href', '/cdn-cgi/access/logout')
})

it('clears the old private cache on role changes and immediately unmounts private content on revocation', async () => {
  const session = vi.spyOn(api, 'get').mockResolvedValue(authenticate(member))
  const caches = new Set<QueryCache>()
  const mutationCaches = new Set<MutationCache>()
  function Child() {
    const isAdmin = useAuthStore((state) => state.isAdmin)
    const client = useQueryClient()
    caches.add(client.getQueryCache())
    mutationCaches.add(client.getMutationCache())
    const data = useQuery({ queryKey: ['private'], queryFn: async () => isAdmin ? '管理員資料' : '家人資料' })
    return <p>{data.data}</p>
  }
  render(<SessionGate><Child /></SessionGate>)
  expect(await screen.findByText('管理員資料')).toBeVisible()
  session.mockResolvedValue(authenticate({ ...member, role: 'User' }))
  act(() => { window.dispatchEvent(new Event('focus')) })
  expect(await screen.findByText('家人資料')).toBeVisible()
  const [oldCache, currentCache] = [...caches]
  expect(oldCache).not.toBe(currentCache)
  expect(oldCache.getAll()).toHaveLength(0)
  expect(mutationCaches.size).toBe(2)
  expect(screen.queryByText('管理員資料')).not.toBeInTheDocument()

  session.mockRejectedValue(deny())
  act(() => { window.dispatchEvent(new Event('focus')) })
  expect(await screen.findByRole('heading', { name: '尚未核准使用' })).toBeVisible()
  expect(screen.queryByText('家人資料')).not.toBeInTheDocument()
  expect(currentCache.getAll()).toHaveLength(0)
  await waitFor(() => expect(useAuthStore.getState().user).toBeNull())
})
