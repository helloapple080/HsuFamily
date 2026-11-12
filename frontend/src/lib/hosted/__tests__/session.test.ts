// @vitest-environment node
import { createHmac } from 'node:crypto'
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

  beforeEach(() => {
    vi.resetModules()
    vi.stubEnv('NEXT_PUBLIC_PORTAL_MODE', 'supabase')
    vi.stubEnv('PORTAL_ORIGIN', origin)
    vi.stubEnv('SUPABASE_URL', 'https://auth.example.test')
    vi.stubEnv('SUPABASE_PUBLISHABLE_KEY', 'sb_publishable_testing')
    role = 'Admin'
    logoutStatus = 204
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
      throw new Error('未預期的測試路由')
    }))
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    vi.unstubAllEnvs()
  })

  async function call(path: string, method = 'GET', body?: unknown) {
    const { GET, POST } = await import('@/app/api/hosted/[...path]/route.hosted')
    const handler = method === 'POST' ? POST : GET
    return handler(new Request(`${origin}/api/hosted/${path}`, {
      method,
      headers: { Origin: origin, 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    }), { params: Promise.resolve({ path: path.split('/') }) })
  }

  const login = () => call('auth/login', 'POST', { username: email, password: 'test-only' })

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
})
