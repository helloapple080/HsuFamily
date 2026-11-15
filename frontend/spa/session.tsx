import { MutationCache, QueryCache, QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query'
import { isAxiosError } from 'axios'
import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import { z } from 'zod'
import { Button } from '@/components/ui/button'
import api from '@/lib/api'
import { shouldRetryQuery } from '@/lib/queryRetry'
import { useAuthStore } from '@/stores/authStore'
import type { UserInfo } from '@/types/auth'

const sessionKey = ['access-session'] as const
const accessLogoutPath = '/cdn-cgi/access/logout'
const userInfo = z.object({
  id: z.string().min(1),
  username: z.string(),
  email: z.email(),
  displayName: z.string(),
  role: z.enum(['Admin', 'User']),
  linkedMemberId: z.string().nullish(),
})
const sessionResponse = z.object({ success: z.literal(true), data: userInfo })
type Session = { user: UserInfo } | 'unauthenticated' | 'forbidden'

function authorizationStatus(error: unknown): number | undefined {
  return isAxiosError(error) ? error.response?.status : undefined
}

async function loadSession({ signal }: { signal: AbortSignal }): Promise<Session> {
  try {
    const { data } = await api.get<unknown>('/api/hosted/auth/me', { signal, headers: { Accept: 'application/json' } })
    return { user: sessionResponse.parse(data).data }
  } catch (error) {
    const status = authorizationStatus(error)
    if (status === 401) return 'unauthenticated'
    if (status === 403) return 'forbidden'
    throw error
  }
}

function SessionNotice({ state, onRetry }: { state: 'loading' | 'logged-out' | 'unavailable' | 'forbidden' | 'unauthenticated'; onRetry: () => void }) {
  const title = {
    loading: '正在確認登入',
    'logged-out': '正在登出',
    unavailable: '目前無法確認登入',
    forbidden: '尚未核准使用',
    unauthenticated: '請先登入',
  }[state]

  return (
    <main className="grid min-h-screen place-items-center bg-gradient-mesh p-4">
      <section className="glass-panel-strong w-full max-w-md space-y-4 p-6 sm:p-8">
        <p className="text-sm text-muted-foreground">許氏入口網</p>
        <h1 className="text-2xl font-bold">{title}</h1>
        {state === 'loading' || state === 'logged-out' ? (
          <p role="status">請稍候…</p>
        ) : state === 'forbidden' ? (
          <>
            <p>這個帳號目前無法使用入口網。請聯絡家族管理員確認資格，或切換到已核准的電子信箱。</p>
            <div className="flex flex-wrap gap-2">
              <Button variant="outline" className="min-h-11" onClick={onRetry}>重新確認資格</Button>
              <Button className="min-h-11" render={<a href={accessLogoutPath} />}>切換登入帳號</Button>
            </div>
          </>
        ) : (
          <>
            <p>{state === 'unavailable'
              ? '請確認網路連線，或重新開啟本頁，透過 Cloudflare Access 登入。'
              : '請以已核准的電子信箱透過 Cloudflare Access 登入。'}</p>
            <Button className="min-h-11" onClick={() => window.location.reload()}>重新開啟入口網</Button>
          </>
        )}
      </section>
    </main>
  )
}

interface PrivateScopeProps {
  children: ReactNode
  checkedAt: number
  onAuthorizationError: () => void
}

function PrivateScope({ children, checkedAt, onAuthorizationError }: PrivateScopeProps) {
  const [client] = useState(() => {
    const onError = (error: unknown) => {
      const status = authorizationStatus(error)
      if (status === 401 || status === 403) onAuthorizationError()
    }
    return new QueryClient({
      queryCache: new QueryCache({ onError }),
      mutationCache: new MutationCache({ onError }),
      defaultOptions: {
        queries: { staleTime: 60_000, refetchOnWindowFocus: false, retry: shouldRetryQuery },
      },
    })
  })
  const priorCheck = useRef(checkedAt)

  useEffect(() => {
    if (priorCheck.current === checkedAt) return
    priorCheck.current = checkedAt
    void client.refetchQueries({ stale: true, type: 'active' })
  }, [checkedAt, client])

  useLayoutEffect(() => () => {
    client.clear()
  }, [client])

  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

interface SessionGateProps {
  children: ReactNode | ((onLogout: () => void) => ReactNode)
}

/// 資格查詢與私人快取分開，帳號或角色改變時卸載表單和整個舊快取。
export function SessionGate({ children }: SessionGateProps) {
  const [sessionClient] = useState(() => new QueryClient())
  const [loggingOut, setLoggingOut] = useState(false)
  const session = useQuery({
    queryKey: sessionKey,
    queryFn: loadSession,
    enabled: !loggingOut,
    retry: false,
    refetchOnWindowFocus: false,
    networkMode: 'always',
  }, sessionClient)
  const { refetch } = session
  const user = !loggingOut && session.isSuccess && typeof session.data === 'object' ? session.data.user : null
  const activeUser = useAuthStore((state) => state.user)

  useLayoutEffect(() => {
    const auth = useAuthStore.getState()
    if (user) auth.login(user)
    else auth.logout()
    auth.setHydrated(true)
  }, [user, session.dataUpdatedAt])

  useEffect(() => {
    if (loggingOut) return
    const check = () => { void refetch({ cancelRefetch: false }) }
    const onVisibility = () => { if (document.visibilityState === 'visible') check() }
    const hideRestoredPage = (event: PageTransitionEvent) => {
      if (event.persisted) document.documentElement.classList.add('awaiting-portal-reload')
    }
    const reloadRestoredPage = (event: PageTransitionEvent) => {
      if (event.persisted) window.location.reload()
    }
    window.addEventListener('focus', check)
    window.addEventListener('pagehide', hideRestoredPage)
    window.addEventListener('pageshow', reloadRestoredPage)
    document.addEventListener('visibilitychange', onVisibility)
    return () => {
      window.removeEventListener('focus', check)
      window.removeEventListener('pagehide', hideRestoredPage)
      window.removeEventListener('pageshow', reloadRestoredPage)
      document.removeEventListener('visibilitychange', onVisibility)
    }
  }, [loggingOut, refetch])

  const onLogout = () => {
    setLoggingOut(true)
    void sessionClient.cancelQueries({ queryKey: sessionKey })
    useAuthStore.getState().logout()
    window.location.replace(accessLogoutPath)
  }
  const retry = () => { void refetch({ cancelRefetch: false }) }
  if (loggingOut) return <SessionNotice state="logged-out" onRetry={retry} />
  if (session.isError) return <SessionNotice state="unavailable" onRetry={retry} />
  if (!user && typeof session.data === 'string') return <SessionNotice state={session.data} onRetry={retry} />
  if (!user || activeUser?.id !== user.id || activeUser.role !== user.role) return <SessionNotice state="loading" onRetry={retry} />

  return (
    <PrivateScope key={JSON.stringify([user.id, user.role])} checkedAt={session.dataUpdatedAt} onAuthorizationError={retry}>
      {typeof children === 'function' ? children(onLogout) : children}
    </PrivateScope>
  )
}
