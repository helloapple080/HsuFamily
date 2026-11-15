import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { AxiosError, AxiosHeaders } from 'axios'
import { afterEach, expect, it, vi } from 'vitest'
import { useAuthStore } from '@/stores/authStore'
import PrivatePhotos from '../PrivatePhotos'

const api = vi.hoisted(() => ({ get: vi.fn(), put: vi.fn(), delete: vi.fn() }))
vi.mock('@/lib/api', () => ({ default: api }))
vi.mock('next/image', () => ({ default: ({ alt, src }: { alt: string; src: string }) => <span role="img" aria-label={alt} data-src={src} /> }))
const id = '10000000-0000-4000-8000-000000000001'
const revision = '10000000-0000-4000-8000-000000000002'
const replacementRevision = '10000000-0000-4000-8000-000000000003'

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

it('接受 Worker 的槽位物件且仍使用同源圖片路徑', async () => {
  api.get.mockResolvedValue({ data: { success: true, data: [{ slot: 2, revision }] } })
  renderPhotos()
  await userEvent.click(screen.getByRole('button', { name: '查看照片：家庭旅遊' }))
  expect(await screen.findByRole('img', { name: '家庭旅遊的照片 2' })).toHaveAttribute(
    'data-src', `/api/hosted/photos/itineraries/${id}/2?v=${revision}`)
  expect(api.get).toHaveBeenCalledWith(`/api/hosted/photos/itineraries/${id}`, expect.any(Object))
})

it('以所選照片版本刪除，位置已換圖時會提示並更新照片', async () => {
  const config = { headers: new AxiosHeaders() }
  vi.spyOn(window, 'confirm').mockReturnValue(true)
  api.get.mockResolvedValueOnce({ data: { success: true, data: [{ slot: 2, revision }] } })
    .mockResolvedValueOnce({ data: { success: true, data: [{ slot: 2, revision: replacementRevision }] } })
  api.delete.mockRejectedValueOnce(new AxiosError('Conflict', undefined, config, undefined, {
    data: { success: false, data: null }, status: 409, statusText: 'Conflict', headers: {}, config,
  }))
  renderPhotos(true)
  await userEvent.click(screen.getByRole('button', { name: '查看照片：家庭旅遊' }))
  await userEvent.click(await screen.findByRole('button', { name: '移除照片 2' }))
  expect(api.delete).toHaveBeenCalledWith(`/api/hosted/photos/itineraries/${id}/2`, { headers: { 'If-Match': `"${revision}"` } })
  expect(await screen.findByRole('alert')).toHaveTextContent('照片已由其他人更新')
  await waitFor(() => expect(screen.getByRole('img', { name: '家庭旅遊的照片 2' })).toHaveAttribute(
    'data-src', `/api/hosted/photos/itineraries/${id}/2?v=${replacementRevision}`))
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
