// @vitest-environment node
import { describe, expect, it } from 'vitest'
import { handleHostedRequest, type HostedGateway, type WorshipRule } from '../portal'
import type { UserInfo } from '@/types/auth'

const origin = 'https://family.example.test'
const admin: UserInfo = { id: 'admin', username: 'admin', email: 'admin@example.test', displayName: '管理員', role: 'Admin' }
const member: UserInfo = { id: 'member', username: 'member', email: 'member@example.test', displayName: '家人', role: 'User' }
const id = '70000000-0000-4000-8000-000000000001'
const payload = { title: '中秋拜拜', lunarMonth: 8, lunarDay: 15, leapMonthPolicy: 'RegularOnly', notes: '' }

function preview() {
  let user: UserInfo | null = null
  const rules = new Map<string, WorshipRule>()
  const gateway: HostedGateway = {
    signIn: async (email) => {
      user = email === admin.email ? admin : email === member.email ? member : null
      return user ? { user, expiresAt: '2027-01-01T00:00:00.000Z' } : null
    },
    readUser: async () => user,
    signOut: async () => { user = null; return true },
    listRules: async () => [...rules.values()],
    saveRule: async (rule, existingId) => {
      if (existingId && !rules.has(existingId)) return null
      const saved = { ...rule, id }
      rules.set(id, saved)
      return saved
    },
    deleteRule: async (ruleId) => rules.delete(ruleId),
  }
  const call = (path: string, method = 'GET', data?: unknown, requestOrigin = origin) =>
    handleHostedRequest(new Request(`${origin}/api/hosted/${path}`, {
      method,
      headers: { Origin: requestOrigin, 'Content-Type': 'application/json' },
      body: data === undefined ? undefined : JSON.stringify(data),
    }), path.split('/'), origin, async () => gateway)
  const login = (username: string) => call('auth/login', 'POST', { username, password: 'test-password' })
  return { call, login }
}

describe('精簡版 HTTP 流程', () => {
  it('拒絕未登入及未核准帳號，且禁止共享快取', async () => {
    const { call, login } = preview()
    const response = await call('worship')
    expect(response.status).toBe(401)
    expect(response.headers.get('Cache-Control')).toBe('private, no-store')
    expect((await login('outsider@example.test')).status).toBe(401)
  })

  it('拒絕跨來源與缺少來源的寫入', async () => {
    const { call } = preview()
    expect((await call('auth/login', 'POST', { username: admin.email, password: 'test-password' }, 'https://outside.test')).status).toBe(403)
    expect((await call('auth/logout', 'POST', undefined, '')).status).toBe(403)
  })

  it('普通家人無法建立、修改或刪除規則', async () => {
    const { call, login } = preview()
    expect((await login(member.email)).status).toBe(200)
    expect((await call('worship', 'POST', payload)).status).toBe(403)
    expect((await call(`worship/${id}`, 'PUT', payload)).status).toBe(403)
    expect((await call(`worship/${id}`, 'DELETE')).status).toBe(403)
  })

  it('家庭成員與行程端點會在呼叫儲存層前檢查身分與寫入權限', async () => {
    const { call, login } = preview()
    expect((await call('members')).status).toBe(401)
    await login(member.email)
    expect((await call('members', 'POST', {})).status).toBe(403)
    expect((await call(`itineraries/${id}`, 'DELETE')).status).toBe(403)
  })

  it('未登入不能讀取照片，普通家人與跨來源不能更動照片', async () => {
    const { call, login } = preview()
    expect((await call(`photos/members/${id}/1`)).status).toBe(401)
    await login(member.email)
    expect((await call(`photos/members/${id}/1`, 'PUT', {})).status).toBe(403)
    expect((await call(`photos/members/${id}/1`, 'DELETE', undefined, 'https://outside.test')).status).toBe(403)
  })

  it('活動月曆讀取需登入，活動寫入需管理員', async () => {
    const { call, login } = preview()
    expect((await call('events')).status).toBe(401)
    await login(member.email)
    expect((await call('events', 'POST', {})).status).toBe(403)
    expect((await call(`events/${id}`, 'DELETE')).status).toBe(403)
  })

  it('管理員能建立規則，家人重新登入後能讀取，登出後再次拒絕', async () => {
    const { call, login } = preview()
    expect((await login(admin.email)).status).toBe(200)
    expect((await call('worship', 'POST', payload)).status).toBe(201)
    expect((await call(`worship/${id}`, 'PUT', { ...payload, notes: '準備水果' })).status).toBe(200)
    expect((await call('auth/logout', 'POST')).status).toBe(200)
    expect((await call('auth/me')).status).toBe(401)
    expect((await login(member.email)).status).toBe(200)
    expect(await (await call('worship')).json()).toMatchObject({
      data: [{ id, title: '中秋拜拜', lunarMonth: 8, lunarDay: 15, notes: '準備水果' }],
    })
  })

  it('拒絕錯誤日期、額外角色欄位與過大輸入', async () => {
    const { call, login } = preview()
    await login(admin.email)
    expect((await call('worship', 'POST', { ...payload, lunarDay: 31 })).status).toBe(400)
    expect((await call('worship', 'POST', { ...payload, role: 'Admin' })).status).toBe(400)
    expect((await call('worship', 'POST', { ...payload, notes: 'a'.repeat(17000) })).status).toBe(413)
  })
})
