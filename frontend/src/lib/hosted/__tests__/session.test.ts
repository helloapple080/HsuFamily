// @vitest-environment node
import { createHmac } from 'node:crypto'
import sharp from 'sharp'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

const { jar, setCookie } = vi.hoisted(() => ({
  jar: new Map<string, string>(),
  setCookie: vi.fn<(name: string, value: string, options: { maxAge?: number }) => void>(),
}))
vi.mock('next/headers', () => ({
  cookies: async () => ({
    getAll: () => [...jar].map(([name, value]) => ({ name, value })),
    set: setCookie,
  }),
}))

const origin = 'https://family.example.test'
const userId = '70000000-0000-4000-8000-000000000001'
const email = 'family@example.test'
const serialize = (value: unknown) => Buffer.from(JSON.stringify(value)).toString('base64url')

describe('Supabase SDK 與登入 Cookie', () => {
  let role: 'Admin' | 'User' | null
  let bearer: string
  let logoutStatus: number
  let photo: ArrayBuffer | null
  let photoOwnerExists: boolean

  beforeEach(() => {
    vi.resetModules()
    vi.stubEnv('NEXT_PUBLIC_PORTAL_MODE', undefined)
    vi.stubEnv('NEXT_PUBLIC_DEMO_MODE', undefined)
    vi.stubEnv('PORTAL_ORIGIN', origin)
    vi.stubEnv('SUPABASE_URL', 'https://auth.example.test')
    vi.stubEnv('SUPABASE_PUBLISHABLE_KEY', 'sb_publishable_testing')
    vi.stubEnv('PORTAL_PHOTO_UPLOAD_KEY', '41'.repeat(32))
    role = 'Admin'
    logoutStatus = 204
    photo = null
    photoOwnerExists = true
    jar.clear()
    setCookie.mockReset().mockImplementation((name, value, { maxAge }) => {
      if (maxAge === 0) jar.delete(name)
      else jar.set(name, value)
    })
    const now = Math.floor(Date.now() / 1000)
    const claims = serialize({ sub: userId, email, iat: now, exp: now + 3600 })
    const encoded = serialize({ alg: 'HS256', typ: 'JWT' }) + '.' + claims
    bearer = encoded + '.' + createHmac('sha256', 'local-test-only').update(encoded).digest('base64url')
    const user = { id: userId, email, aud: 'authenticated', role: 'authenticated' }
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const request = new Request(input, init)
      const url = new URL(request.url)
      expect(url.origin).toBe('https://auth.example.test')
      expect(init?.cache).toBe('no-store')
      if (url.pathname === '/auth/v1/token') {
        return Response.json({ access_token: bearer, refresh_token: 'test-refresh', expires_in: 3600, token_type: 'bearer', user })
      }
      expect(request.headers.get('Authorization')).toBe('Bearer ' + bearer)
      if (url.pathname === '/auth/v1/user') return Response.json(user)
      if (url.pathname === '/auth/v1/logout') return new Response(null, { status: logoutStatus })
      if (url.pathname === '/rest/v1/portal_access') {
        expect(url.searchParams.get('user_id')).toBe('eq.' + userId)
        return Response.json(role ? [{ role }] : [])
      }
      if (url.pathname === '/rest/v1/family_records') return Response.json(photoOwnerExists ? [{ id: userId }] : [])
      const storage = '/storage/v1/object'
      if (url.pathname === `${storage}/list/family-photos`) {
        return Response.json(photo ? [{ name: '1.webp' }] : [])
      }
      if (url.pathname === `${storage}/family-photos` && request.method === 'DELETE') {
        expect(await request.json()).toEqual({ prefixes: [`members/${userId}/1.webp`] })
        photo = null
        return Response.json([])
      }
      if (url.pathname === `${storage}/family-photos/members/${userId}/1.webp`) {
        if (request.method === 'POST') {
          expect(request.headers.get('x-upsert')).toBe('false')
          expect(request.headers.get('content-type')).toBe('image/webp')
          expect(request.headers.get('x-portal-upload')).toMatch(/^[0-9]{10}\.[0-9a-f]{64}$/)
          if (photo) return Response.json({ statusCode: '409', code: 'ResourceAlreadyExists' }, { status: 409 })
          photo = await request.arrayBuffer()
          return Response.json({ Id: userId, Key: `family-photos/members/${userId}/1.webp` })
        }
        return photo ? new Response(photo, { headers: { 'Content-Type': 'image/webp' } }) : Response.json({ code: 'NoSuchKey' }, { status: 404 })
      }
      throw new Error('未預期的測試路由')
    }))
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.unstubAllEnvs()
  })

  async function call(path: string, method = 'GET', body?: unknown) {
    const { GET, POST, PUT, DELETE } = await import('@/app/api/hosted/[...path]/route.hosted')
    const handler = method === 'POST' ? POST : method === 'PUT' ? PUT : method === 'DELETE' ? DELETE : GET
    return handler(new Request(`${origin}/api/hosted/${path}`, {
      method,
      headers: { Origin: origin, 'Content-Type': body instanceof Blob ? body.type : 'application/json' },
      body: body === undefined || body instanceof Blob ? body : JSON.stringify(body),
    }), { params: Promise.resolve({ path: path.split('/') }) })
  }

  const login = () => call('auth/login', 'POST', { username: email, password: 'test-only' })

  it('展示模式即使有正式環境變數也不開放私人路由', async () => {
    vi.stubEnv('NEXT_PUBLIC_DEMO_MODE', 'true')
    expect((await login()).status).toBe(404)
    expect(jar.size).toBe(0)
  })

  it('預設私人模式缺少雲端設定時拒絕服務，不回退到舊 API', async () => {
    vi.stubEnv('SUPABASE_URL', undefined)
    expect((await login()).status).toBe(503)
  })

  it('登入後用 Cookie 還原身分；角色異動立即生效，登出刪除憑證', async () => {
    const response = await login()
    expect(response.status).toBe(200)
    expect(await response.text()).not.toContain(bearer)
    expect(jar.size).toBeGreaterThan(0)
    expect(setCookie).toHaveBeenCalledWith(expect.stringMatching(/^__Host-family-portal-hosted/), expect.any(String),
      expect.objectContaining({ httpOnly: true, secure: true, sameSite: 'strict', path: '/', domain: undefined }))
    expect(await (await call('auth/me')).json()).toMatchObject({ data: { id: userId, role: 'Admin' } })
    role = 'User'
    expect(await (await call('auth/me')).json()).toMatchObject({ data: { role: 'User' } })
    role = null
    expect((await call('auth/me')).status).toBe(401)
    expect((await call('auth/logout', 'POST')).status).toBe(200)
    expect(jar.size).toBe(0)
    expect((await call('auth/me')).status).toBe(401)
  })

  it('未核准的正確帳密不會收到 Cookie', async () => {
    role = null
    expect((await login()).status).toBe(401)
    expect(setCookie).not.toHaveBeenCalled()
    expect(jar.size).toBe(0)
  })

  it('服務無法撤銷遠端會話時仍會清除這台裝置的 Cookie', async () => {
    expect((await login()).status).toBe(200)
    logoutStatus = 503
    const response = await call('auth/logout', 'POST')
    expect(response.status).toBe(200)
    expect(await response.json()).toMatchObject({ success: true, data: false })
    expect(jar.size).toBe(0)
  })

  it('真實 Storage SDK 只接受管理員新增及移除圖片，家人可讀、撤權後不可讀', async () => {
    expect((await login()).status).toBe(200)
    const bytes = await sharp({ create: { width: 4, height: 4, channels: 3, background: 'red' } }).png().toBuffer()
    const body = new Blob([Uint8Array.from(bytes)], { type: 'image/png' })
    const gallery = `photos/members/${userId}`
    const path = gallery + '/1'
    expect((await call(path, 'PUT', body)).status).toBe(200)
    expect((await call(path, 'PUT', body)).status).toBe(409)
    role = 'User'
    expect(await (await call(gallery)).json()).toMatchObject({ data: [1] })
    const response = await call(path)
    expect(response.status).toBe(200)
    expect(response.headers.get('Content-Type')).toBe('image/webp')
    expect(response.headers.get('Cross-Origin-Resource-Policy')).toBe('same-origin')
    expect(response.headers.get('Cache-Control')).toBe('private, no-store')
    expect((await sharp(Buffer.from(await response.arrayBuffer())).metadata()).format).toBe('webp')
    expect((await call(path, 'DELETE')).status).toBe(403)
    role = null
    expect((await call(path)).status).toBe(401)
    role = 'Admin'
    expect((await call(path, 'DELETE')).status).toBe(200)
    expect((await call(path)).status).toBe(404)
    photoOwnerExists = false
    expect((await call(path, 'PUT', body)).status).toBe(404)
  })
})
