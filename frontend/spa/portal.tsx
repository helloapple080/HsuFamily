import { Component, lazy, Suspense, useLayoutEffect, type ReactNode } from 'react'
import { MotionConfig } from 'framer-motion'
import { toast } from 'sonner'
import PortalLayout from '@/components/layout/PortalLayout'
import ErrorState from '@/components/common/ErrorState'
import { Toaster } from '@/components/ui/sonner'
import Loading from '@/app/(portal)/loading'
import Link, { getPortalRoute, usePathname } from './navigation'

const routeComponents = {
  '/calendar': lazy(() => import('@/app/(portal)/calendar/page')),
  '/members': lazy(() => import('@/app/(portal)/members/page')),
  '/itineraries': lazy(() => import('@/app/(portal)/itineraries/page')),
}

class RouteErrorBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false }

  static getDerivedStateFromError() {
    return { failed: true }
  }

  render() {
    return this.state.failed
      ? <ErrorState title="無法載入頁面" description="請重新整理後再試。" onRetry={() => window.location.reload()} />
      : this.props.children
  }
}

function PortalRoutes() {
  const pathname = usePathname()
  const route = getPortalRoute(pathname)
  if (!route) return <section className="space-y-4">
    <h1 className="text-2xl font-bold">找不到這個頁面</h1>
    <Link href="/calendar" className="inline-block min-h-11 py-3 underline">返回行事曆</Link>
  </section>
  const Route = routeComponents[route]
  return <RouteErrorBoundary key={route}><Suspense fallback={<Loading />}><Route /></Suspense></RouteErrorBoundary>
}

export default function Portal({ onLogout }: { onLogout: () => void }) {
  useLayoutEffect(() => () => { toast.dismiss() }, [])
  return (
    <MotionConfig reducedMotion="user">
      <PortalLayout onAccessLogout={onLogout}>
        <PortalRoutes />
        <Toaster richColors position="top-right" />
      </PortalLayout>
    </MotionConfig>
  )
}
