import { createServerClient, type CookieOptions } from '@supabase/ssr'
import { cookies } from 'next/headers'
import { z } from 'zod'
import type { UserInfo } from '@/types/auth'
import type { HostedGateway } from './portal'
import { worshipRule } from './worship-contract'

const row = z.strictObject({
  id: z.uuid(),
  title: z.string(),
  lunar_month: z.number().nullable(),
  lunar_day: z.number(),
  leap_month_policy: z.string(),
  notes: z.string(),
})
const fields = 'id,title,lunar_month,lunar_day,leap_month_policy,notes'
const roleSchema = z.enum(['Admin', 'User'])

interface PortalConfig {
  origin: string
  supabaseUrl: string
  publishableKey: string
}

function validOrigin(value: string | undefined) {
  if (!value) return null
  try {
    const url = new URL(value)
    const loopback = ['localhost', '127.0.0.1', '[::1]'].includes(url.hostname)
    return url.origin === value && (url.protocol === 'https:' || (loopback && url.protocol === 'http:' && process.env.NODE_ENV !== 'production'))
      ? value : null
  } catch {
    return null
  }
}

export function getHostedConfig(): PortalConfig | null {
  const origin = validOrigin(process.env.PORTAL_ORIGIN)
  const supabaseUrl = validOrigin(process.env.SUPABASE_URL)
  const publishableKey = process.env.SUPABASE_PUBLISHABLE_KEY
  if (!origin || !supabaseUrl || !publishableKey?.startsWith('sb_publishable_')) return null
  return { origin, supabaseUrl, publishableKey }
}

function fromRow(value: unknown) {
  const rule = row.parse(value)
  return worshipRule.parse({
    id: rule.id, title: rule.title, lunarMonth: rule.lunar_month, lunarDay: rule.lunar_day,
    leapMonthPolicy: rule.leap_month_policy, notes: rule.notes,
  })
}

/// 每個 HTTP 請求獨立建立 client，以使用者權限查詢資料，不能繞過 RLS。
export async function createHostedGateway(config: PortalConfig) {
  const jar = await cookies()
  const secure = config.origin.startsWith('https:')
  // HTTPS 以 __Host- 限定目前主機，避免其他子網域植入同名 Cookie。
  const cookieName = secure ? '__Host-family-portal-hosted' : 'family-portal-hosted'
  const cookieScope = { path: secure ? '/' : '/api/hosted', httpOnly: true, secure, sameSite: 'strict' } as const
  const pending = new Map<string, { name: string; value: string; options: CookieOptions }>()
  const client = createServerClient(config.supabaseUrl, config.publishableKey, {
    cookieOptions: { name: cookieName, ...cookieScope },
    cookies: {
      getAll: () => {
        const current = new Map(jar.getAll().map(({ name, value }) => [name, value]))
        for (const { name, value, options } of pending.values()) {
          if (options.maxAge === 0) current.delete(name)
          else current.set(name, value)
        }
        return [...current].map(([name, value]) => ({ name, value }))
      },
      setAll: (values) => {
        for (const cookie of values) pending.set(cookie.name, cookie)
      },
    },
    global: {
      fetch: (input, init) => {
        const timeout = AbortSignal.timeout(8_000)
        const signal = init?.signal ? AbortSignal.any([init.signal, timeout]) : timeout
        return fetch(input, { ...init, signal, cache: 'no-store' })
      },
    },
  })

  async function readUser(): Promise<UserInfo | null> {
    const { data, error } = await client.auth.getClaims()
    if (error?.status && (error.status === 429 || error.status >= 500)) throw error
    const claims = data?.claims
    if (!claims?.sub || typeof claims.email !== 'string') return null
    const result = await client.from('portal_access').select('role').eq('user_id', claims.sub).maybeSingle()
    if (result.error) throw result.error
    const role = roleSchema.safeParse(result.data?.role)
    return role.success ? {
      id: claims.sub, username: claims.email, email: claims.email,
      displayName: claims.email.split('@')[0], role: role.data,
    } : null
  }

  const gateway: HostedGateway = {
    readUser,
    async signIn(email, password) {
      const { data, error } = await client.auth.signInWithPassword({ email, password })
      if (error?.status && (error.status === 429 || error.status >= 500)) throw error
      if (error || !data.session) return null
      const user = await readUser()
      if (!user) return null
      return { user, expiresAt: new Date((data.session.expires_at ?? 0) * 1000).toISOString() }
    },
    async signOut() {
      try {
        const { error } = await client.auth.signOut({ scope: 'local' })
        return !error || error.name === 'AuthSessionMissingError'
      } catch {
        return false
      } finally {
        // 遠端故障時也必須清除共用裝置上的會話。
        for (const { name } of jar.getAll()) {
          if (name === cookieName || name.startsWith(cookieName + '.')) {
            pending.set(name, { name, value: '', options: { maxAge: 0 } })
          }
        }
      }
    },
    async listRules() {
      const { data, error } = await client.from('worship_rules').select(fields).order('created_at').order('id')
      if (error) throw error
      return data.map(fromRow)
    },
    async saveRule(input, id) {
      const values = {
        title: input.title, lunar_month: input.lunarMonth, lunar_day: input.lunarDay,
        leap_month_policy: input.leapMonthPolicy, notes: input.notes,
      }
      const query = client.from('worship_rules')
      const result = await (id ? query.update(values).eq('id', id) : query.insert(values)).select(fields).maybeSingle()
      if (result.error) throw result.error
      return result.data ? fromRow(result.data) : null
    },
    async deleteRule(id) {
      const result = await client.from('worship_rules').delete().eq('id', id).select('id').maybeSingle()
      if (result.error) throw result.error
      return result.data !== null
    },
  }

  return {
    gateway,
    commitCookies() {
      for (const { name, value, options } of pending.values()) {
        jar.set(name, value, { ...options, domain: undefined, ...cookieScope })
      }
    },
  }
}
