// @vitest-environment node
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { UserInfo } from '@/types/auth'
import { createPortal } from '../router'
import { portalResponse } from '../http'

const origin = 'https://family.example.com'
const config = { PORTAL_ORIGIN: origin, ACCESS_ISSUER: 'https://family.cloudflareaccess.com', ACCESS_AUDIENCE: 'a'.repeat(64) }
const user: UserInfo = { id: 'id', email: 'user@example.com', username: 'user', displayName: '家人', role: 'User' }
const db = Object.create(null) as D1Database
const bucket = Object.create(null) as R2Bucket
const images = Object.create(null) as ImagesBinding
const env = { ...config, DB: db, PHOTOS: bucket, IMAGES: images }
const verify = vi.fn(async () => ({ email: user.email, subject: user.id }) as { email: string; subject: string } | null)
const readUser = vi.fn(async () => user as UserInfo | null)
const records = vi.fn(async () => portalResponse(200, '紀錄', []))
const photos = vi.fn(async () => portalResponse(200, '照片', []))
const route = createPortal({ verify, readUser, records, photos })

const request = (path: string, init?: RequestInit, host = origin) => new Request(`${host}${path}`, init)

beforeEach(() => vi.clearAllMocks())

describe('Worker 權限邊界', () => {
  it('無 Cloudflare 設定、替代主機、偽造身分皆無法讀取資料', async () => {
    expect((await route(request('/api/hosted/auth/me'), {})).status).toBe(503)
    expect((await route(request('/api/hosted/auth/me', {}, 'https://preview.example.com'), env)).status).toBe(403)
    verify.mockResolvedValueOnce(null)
    expect((await route(request('/api/hosted/photos/members/id/1', { headers: { 'Cf-Access-Authenticated-User-Email': user.email } }), env)).status).toBe(401)
    expect(readUser).not.toHaveBeenCalled()
    expect(photos).not.toHaveBeenCalled()
  })

  it('每次都檢查家人資格，撤權後即使權杖有效也不能存取', async () => {
    expect((await route(request('/api/hosted/auth/me'), env)).status).toBe(200)
    readUser.mockResolvedValueOnce(null)
    const revoked = await route(request('/api/hosted/members'), env)
    expect(revoked.status).toBe(403)
    expect(revoked.headers.get('cache-control')).toContain('no-store')
    expect(readUser).toHaveBeenCalledTimes(2)
    expect(records).not.toHaveBeenCalled()
  })

  it('拒絕跨站與一般家人修改；管理員可同源修改', async () => {
    const path = '/api/hosted/itineraries/id'
    const edit = { method: 'PUT', headers: { Origin: origin } }
    expect((await route(request(path, { method: 'PUT', headers: { Origin: 'https://attacker.example' } }), env)).status).toBe(403)
    expect((await route(request(path, edit), env)).status).toBe(403)
    expect(records).not.toHaveBeenCalled()
    const admin = { ...user, role: 'Admin' } as const
    readUser.mockResolvedValueOnce(admin)
    expect((await route(request(path, edit), env)).status).toBe(200)
    expect(records).toHaveBeenCalledWith(expect.any(Request), 'itineraries', 'id', admin, db)
  })

  it('身分通過後才調用照片，沒有原密碼入口，不吞 API 為前端頁面', async () => {
    expect((await route(request('/api/hosted/photos/members/id/2'), env)).status).toBe(200)
    expect(photos).toHaveBeenCalledWith(expect.any(Request), ['members', 'id', '2'], user, db, bucket, images)
    expect((await route(request('/api/hosted/auth/login'), env)).status).toBe(404)
    expect((await route(request('/api'), env)).status).toBe(404)
  })

  it('基礎設施失敗時只回安全錯誤，不洩漏服務資料', async () => {
    readUser.mockRejectedValueOnce(new Error('secret details'))
    const result = await route(request('/api/hosted/worship'), env)
    expect(result.status).toBe(503)
    expect(await result.text()).not.toContain('secret details')
  })
})
