'use client'

import { useEffect, useState } from 'react'
import Link from 'next/link'
import { usePathname, useRouter } from 'next/navigation'
import { CalendarDays, House, LogOut, MapPinned, NotebookText, Settings, ShieldCheck, Users } from 'lucide-react'
import api from '@/lib/api'
import { toast } from 'sonner'
import { useAuthStore } from '@/stores/authStore'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Separator } from '@/components/ui/separator'
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from '@/components/ui/sheet'
import DemoModeBanner from '@/components/common/DemoModeBanner'
import ThemeSwitcher from '@/components/theme/ThemeSwitcher'
import { isDemoMode } from '@/lib/demo/config'
import type { ApiResponse } from '@/types/api'

const navItems = [
  { href: '/calendar', label: '行事曆', icon: CalendarDays, description: '國農曆雙顯示' },
  { href: '/members', label: '家庭成員', icon: Users, description: '成員資料管理' },
  { href: '/itineraries', label: '歷年行程', icon: MapPinned, description: '行程紀錄查詢' },
]
const accessNavItems = [...navItems, { href: '/household', label: '家用備忘', icon: NotebookText, description: '設備位置與店務資訊' }]

interface PortalLayoutProps {
  children: React.ReactNode
  onAccessLogout?: () => void
}

/// 入口網的客戶端導覽外殼，處理認證導向與行動版選單。
export default function PortalLayout({ children, onAccessLogout }: PortalLayoutProps) {
  const router = useRouter()
  const pathname = usePathname()
  const [isMenuOpen, setIsMenuOpen] = useState(false)
  const { user, isAuthenticated, isAdmin, isHydrated, logout } = useAuthStore()

  useEffect(() => {
    if (!onAccessLogout && !isDemoMode && isHydrated && !isAuthenticated) router.replace('/login')
  }, [isAuthenticated, isHydrated, onAccessLogout, router])

  const handleLogout = async () => {
    if (onAccessLogout) {
      onAccessLogout()
      return
    }
    if (isDemoMode) {
      toast.info('公開展示模式不需要登入或登出')
      router.push('/calendar')
      return
    }

    let revoked = true
    try {
      const { data } = await api.post<ApiResponse<boolean>>('/api/hosted/auth/logout')
      revoked = data.data === true
    } catch {
      toast.error('目前無法登出，請稍後再試')
      return
    }
    logout()
    if (revoked) toast.success('已登出')
    else toast.warning('此裝置已登出，但未能確認遠端會話撤銷')
    router.push('/login')
  }

  if (!isHydrated || !isAuthenticated) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-gradient-mesh" role="status">
        <div aria-hidden="true" className="size-8 animate-spin rounded-full border-2 border-[var(--primary)] border-t-transparent" />
        <span className="sr-only">正在載入入口網</span>
      </div>
    )
  }

  const items = onAccessLogout && !isDemoMode ? accessNavItems : navItems
  const navigation = (
    <nav className="flex-1 space-y-1 p-3" aria-label="主要導覽">
      {items.map((item) => {
        const isActive = pathname.startsWith(item.href)
        const Icon = item.icon
        return (
          <Link
            key={item.href}
            href={item.href}
            aria-label={item.label}
            aria-current={isActive ? 'page' : undefined}
            className={`flex min-h-11 items-center gap-3 rounded-lg px-3 py-2 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring ${
              isActive
                ? 'bg-[var(--primary)] text-[var(--primary-foreground)]'
                : 'text-[var(--foreground)] hover:bg-[var(--accent)]'
            }`}
          >
            <Icon aria-hidden="true" className="size-5 shrink-0" />
            <span className="min-w-0 flex-1">
              <span className="block truncate text-sm font-medium">{item.label}</span>
              <span className={`block truncate text-xs ${isActive ? 'opacity-75' : 'text-[var(--muted-foreground)]'}`}>
                {item.description}
              </span>
            </span>
          </Link>
        )
      })}
    </nav>
  )

  return (
    <div className="flex min-h-screen bg-gradient-mesh">
      <a className="skip-link" href="#main-content">跳到主要內容</a>

      <aside className="fixed inset-y-0 left-0 z-30 hidden w-64 flex-col overflow-y-auto overscroll-y-contain border-r border-border bg-card md:flex">
        <div className="flex items-center gap-3 p-6">
          <div className="flex size-10 items-center justify-center rounded-xl bg-[var(--primary)] text-[var(--primary-foreground)] shadow-md">
            <House aria-hidden="true" className="size-5" />
          </div>
          <div>
            <h2 className="text-sm font-bold tracking-tight">許氏入口網</h2>
            <p className="text-xs text-[var(--muted-foreground)]">Hsu Family Portal</p>
          </div>
        </div>
        <Separator />
        {navigation}
        <Separator />
        <div className="shrink-0 space-y-3 p-4">
          <div className="flex items-center gap-3">
            <Avatar className="size-9 border border-[var(--border)]">
              <AvatarFallback className="bg-[var(--primary)] text-sm text-[var(--primary-foreground)]">
                {user?.displayName?.charAt(0) || 'U'}
              </AvatarFallback>
            </Avatar>
            <div className="min-w-0 flex-1">
              <p className="truncate text-sm font-medium">{user?.displayName}</p>
              <Badge variant={isAdmin ? 'default' : 'secondary'} className="gap-1 px-1.5 py-0 text-[10px]">
                {isAdmin && <ShieldCheck aria-hidden="true" className="size-3" />}
                {isAdmin ? '管理員' : '一般用戶'}
              </Badge>
            </div>
          </div>
          <ThemeSwitcher />
          <Button variant="ghost" className="min-h-11 w-full justify-start text-[var(--muted-foreground)] hover:text-[var(--destructive)]" onClick={handleLogout}>
            <LogOut aria-hidden="true" />
            登出
          </Button>
        </div>
      </aside>

      <header className="fixed inset-x-0 top-0 z-30 flex h-[calc(3.5rem+env(safe-area-inset-top))] items-center justify-between border-b border-border bg-card pr-[max(1rem,env(safe-area-inset-right))] pl-[max(1rem,env(safe-area-inset-left))] pt-[env(safe-area-inset-top)] md:hidden">
        <Link href="/calendar" className="flex min-h-11 items-center gap-2 font-bold">
          <House aria-hidden="true" className="size-5" />
          許氏入口網
        </Link>
        <Sheet open={isMenuOpen} onOpenChange={setIsMenuOpen}>
          <SheetTrigger render={<Button variant="ghost" size="icon" className="size-11" aria-label="帳號與顯示設定" />}>
            <Settings aria-hidden="true" />
          </SheetTrigger>
          <SheetContent side="right" className="w-[min(20rem,85vw)] overflow-y-auto overscroll-y-contain bg-card">
            <SheetHeader className="pt-[max(1rem,env(safe-area-inset-top))]">
              <SheetTitle>帳號與顯示設定</SheetTitle>
              <SheetDescription>切換主題或登出入口網。</SheetDescription>
            </SheetHeader>
            <div className="shrink-0 space-y-3 border-t border-border p-4">
              <p className="truncate text-sm">{user?.displayName}</p>
              <ThemeSwitcher />
              <Button variant="outline" className="min-h-11 w-full" onClick={handleLogout}>
                <LogOut aria-hidden="true" />
                登出
              </Button>
            </div>
          </SheetContent>
        </Sheet>
      </header>

      <main id="main-content" tabIndex={-1} className="min-w-0 flex-1 pt-[calc(3.5rem+env(safe-area-inset-top))] pb-[calc(4.5rem+env(safe-area-inset-bottom))] md:ml-64 md:py-0">
        <div className="mx-auto w-full max-w-7xl space-y-4 px-[max(1rem,env(safe-area-inset-left),env(safe-area-inset-right))] py-4 sm:p-6 lg:px-10 lg:py-8">
          {isDemoMode && <DemoModeBanner />}
          {children}
        </div>
      </main>

      <nav data-portal-mobile-nav aria-label="手機主要導覽" className="fixed inset-x-0 bottom-0 z-30 grid auto-cols-fr grid-flow-col gap-1 border-t border-border bg-card pr-[max(0.375rem,env(safe-area-inset-right))] pb-[calc(0.375rem+env(safe-area-inset-bottom))] pl-[max(0.375rem,env(safe-area-inset-left))] pt-1.5 md:hidden">
        {items.map(({ href, label, icon: Icon }) => <Link key={href} href={href}
          aria-current={pathname === href || pathname === `${href}/` || (pathname === '/' && href === '/calendar') ? 'page' : undefined}
          className="flex min-h-12 min-w-0 touch-manipulation flex-col items-center justify-center gap-0.5 rounded-lg px-0.5 text-center text-xs font-medium text-muted-foreground hover:bg-accent hover:text-foreground focus-visible:outline-2 focus-visible:outline-ring aria-[current=page]:bg-primary aria-[current=page]:font-semibold aria-[current=page]:text-primary-foreground">
          <Icon aria-hidden="true" className="size-5 shrink-0" />
          <span>{label}</span>
        </Link>)}
      </nav>
    </div>
  )
}
