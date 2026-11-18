import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, expect, it, vi } from 'vitest'
import api from '@/lib/api'
import { useAuthStore } from '@/stores/authStore'
import HouseholdNotes from '../HouseholdNotes'

const note = {
  id: '70000000-0000-4000-8000-000000000001', category: 'water', title: '水塔開關', location: '三樓',
  details: '備用代號 ABC', version: 3, updatedAt: '2026-11-18T12:00:00.000Z',
}
const list = (data: unknown[]) => ({ data: { data } })

function renderPage(role: 'Admin' | 'User') {
  useAuthStore.getState().login({ id: 'family', role, username: 'family', email: 'family@example.test', displayName: '家人' })
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(<QueryClientProvider client={client}><HouseholdNotes /></QueryClientProvider>)
}

afterEach(() => useAuthStore.getState().logout())

it('家人可依說明或類別搜尋，沒有寫入按鈕', async () => {
  const get = vi.spyOn(api, 'get').mockResolvedValue(list([note]))
  renderPage('User')
  expect(await screen.findByRole('heading', { name: '水塔開關' })).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: '新增備忘' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: '編輯 水塔開關' })).not.toBeInTheDocument()
  await userEvent.type(screen.getByRole('searchbox', { name: '搜尋備忘' }), 'ａｂｃ')
  expect(screen.getByRole('heading', { name: '水塔開關' })).toBeInTheDocument()
  await userEvent.selectOptions(screen.getByRole('combobox', { name: '篩選分類' }), 'equipment')
  expect(screen.getByText('找不到符合的備忘')).toBeInTheDocument()
  expect(get).toHaveBeenCalledTimes(1)
})

it('管理員從空白建立，再編輯與刪除，寫入附紀錄版本', async () => {
  const get = vi.spyOn(api, 'get').mockResolvedValueOnce(list([])).mockResolvedValue(list([note]))
  const post = vi.spyOn(api, 'post').mockResolvedValue({})
  const put = vi.spyOn(api, 'put').mockResolvedValue({})
  const remove = vi.spyOn(api, 'delete').mockResolvedValue({})
  renderPage('Admin')
  expect(await screen.findByText('還沒有家用備忘')).toBeInTheDocument()
  await userEvent.click(screen.getByRole('button', { name: '新增備忘' }))
  await userEvent.type(screen.getByLabelText('標題'), note.title)
  await userEvent.type(screen.getByLabelText('說明'), note.details)
  await userEvent.click(screen.getByRole('button', { name: '儲存' }))
  expect(post).toHaveBeenCalledWith('/api/hosted/household', { category: 'water', title: note.title, location: '', details: note.details })
  await userEvent.click(await screen.findByRole('button', { name: '編輯 水塔開關' }))
  await userEvent.click(screen.getByRole('button', { name: '儲存' }))
  expect(put).toHaveBeenCalledWith(`/api/hosted/household/${note.id}`, expect.objectContaining({ title: note.title }), { headers: { 'If-Match': '"3"' } })
  await userEvent.click(await screen.findByRole('button', { name: '刪除 水塔開關' }))
  get.mockResolvedValue(list([]))
  await userEvent.click(screen.getByRole('button', { name: '確認刪除' }))
  expect(remove).toHaveBeenCalledWith(`/api/hosted/household/${note.id}`, { headers: { 'If-Match': '"3"' } })
  expect(await screen.findByText('還沒有家用備忘')).toBeInTheDocument()
})

it('版本衝突保留草稿，取消後回到原本的編輯按鈕', async () => {
  vi.spyOn(api, 'get').mockResolvedValue(list([note]))
  vi.spyOn(api, 'put').mockRejectedValue({ isAxiosError: true, response: { status: 409, data: { errors: ['VERSION_CONFLICT'] } } })
  renderPage('Admin')
  await userEvent.click(await screen.findByRole('button', { name: '編輯 水塔開關' }))
  const field = screen.getByLabelText('說明')
  await userEvent.clear(field)
  await userEvent.type(field, '還沒有存下的草稿')
  await userEvent.click(screen.getByRole('button', { name: '儲存' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('其他人已更新這筆資料')
  expect(field).toHaveValue('還沒有存下的草稿')
  expect(screen.getByRole('button', { name: '儲存' })).toBeDisabled()
  await userEvent.click(screen.getByRole('button', { name: '取消' }))
  expect(screen.getByRole('button', { name: '編輯 水塔開關' })).toHaveFocus()
})
