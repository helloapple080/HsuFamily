import { AxiosError, AxiosHeaders } from 'axios'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it, vi } from 'vitest'
import ConfirmDialog from '@/components/common/ConfirmDialog'
import { MemberDialog } from '@/components/features/members/MemberDialog'
import api from '@/lib/api'
import type { Member } from '@/types/member'

const member: Member = {
  id: '00000000-0000-4000-8000-000000000001', name: '測試家人', title: '家人',
  birthday: '1990-05-01', sortOrder: 1, isActive: true, version: 1,
}

it('keeps the member draft and submits its original version after a background update', async () => {
  const user = userEvent.setup()
  const client = new QueryClient()
  const config = { headers: new AxiosHeaders() }
  const put = vi.spyOn(api, 'put').mockRejectedValue(new AxiosError('Request failed with status code 409', undefined, config, undefined, {
    data: { success: false, data: null, errors: ['VERSION_CONFLICT'] },
    status: 409, statusText: 'Conflict', headers: {}, config,
  }))
  const layout = (record: Member) => <QueryClientProvider client={client}>
    <MemberDialog member={record}><button>編輯家人</button></MemberDialog>
  </QueryClientProvider>
  const { rerender } = render(layout(member))
  await user.click(screen.getByRole('button', { name: '編輯家人' }))
  const name = screen.getByLabelText('姓名')
  await user.clear(name)
  await user.type(name, '尚未儲存')
  rerender(layout({ ...member, name: '他人的更新', sortOrder: 2, version: 2 }))
  await user.click(screen.getByRole('button', { name: '儲存' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('其他人已更新')
  expect(name).toHaveValue('尚未儲存')
  expect(put).toHaveBeenCalledWith(`/api/hosted/members/${member.id}`,
    expect.objectContaining({ name: '尚未儲存', sortOrder: 1 }), { headers: { 'If-Match': '"1"' } })
  await user.click(screen.getByRole('button', { name: '取消' }))
  await user.click(screen.getByRole('button', { name: '編輯家人' }))
  expect(screen.getByLabelText('姓名')).toHaveValue('他人的更新')
})

it('confirms deletion against the version shown when the dialog was opened', async () => {
  const user = userEvent.setup()
  const confirm = vi.fn()
  const dialog = (recordVersion: number) => <ConfirmDialog recordVersion={recordVersion} onConfirm={confirm}
    trigger={<button>刪除家人</button>} title="刪除家人？" description="此動作不可復原。" />
  const { rerender } = render(dialog(1))
  await user.click(screen.getByRole('button', { name: '刪除家人' }))
  rerender(dialog(2))
  await user.click(screen.getByRole('button', { name: '確認刪除' }))
  expect(confirm).toHaveBeenCalledWith(1)
})
