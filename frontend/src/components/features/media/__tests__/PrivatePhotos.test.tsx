import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import { useAuthStore } from '@/stores/authStore'
import PrivatePhotos from '../PrivatePhotos'

const api = vi.hoisted(() => ({ get: vi.fn(), put: vi.fn(), delete: vi.fn() }))
vi.mock('@/lib/api', () => ({ default: api }))
vi.mock('next/image', () => ({ default: ({ alt }: { alt: string }) => <span role="img" aria-label={alt} /> }))
const id = '10000000-0000-4000-8000-000000000001'

function renderPhotos(admin = false) {
  useAuthStore.getState().login({ id, email: 'family@example.test', username: 'family', displayName: '家人', role: admin ? 'Admin' : 'User' })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(<QueryClientProvider client={client}><PrivatePhotos kind="itineraries" id={id} title="家庭旅遊" /></QueryClientProvider>)
}

afterEach(() => {
  useAuthStore.getState().logout()
  vi.resetAllMocks()
})

it('家人展開後才載入私人照片，不顯示寫入操作', async () => {
  api.get.mockResolvedValue({ data: { success: true, data: [1] } })
  renderPhotos()
  expect(api.get).not.toHaveBeenCalled()
  await userEvent.click(screen.getByRole('button', { name: '查看照片：家庭旅遊' }))
  expect(await screen.findByRole('img', { name: '家庭旅遊的照片 1' })).toBeVisible()
  expect(screen.queryByLabelText('選擇照片')).not.toBeInTheDocument()
})

it('管理員可用原始圖檔上傳至可用槽位', async () => {
  api.get.mockResolvedValue({ data: { success: true, data: [1] } })
  api.put.mockResolvedValue({ data: { success: true, data: true } })
  renderPhotos(true)
  await userEvent.click(screen.getByRole('button', { name: '查看照片：家庭旅遊' }))
  const file = new File(['test'], 'fixture.png', { type: 'image/png' })
  fireEvent.change(await screen.findByLabelText('選擇照片'), { target: { files: [file] } })
  await waitFor(() => expect(api.put).toHaveBeenCalledWith(`/api/hosted/photos/itineraries/${id}/2`, file, { headers: { 'Content-Type': 'image/png' } }))
  expect(await screen.findByText('照片已上傳')).toBeVisible()
})
