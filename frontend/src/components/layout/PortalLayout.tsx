'use client'

import { useEffect, useState } from 'react'
import Link from 'next/link'
import { usePathname, useRouter } from 'next/navigation'
import { CalendarDays, House, LogOut, MapPinned, Menu, ShieldCheck, Users } from 'lucide-react'
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
} from '@/components/ui/sheet'

const navItems = [
  { href: '/calendar', label: '行事曆', icon: CalendarDays, description: '國農曆雙顯示' },
  { href: '/members', label: '家庭成員', icon: Users, description: '成員資料管理' },
  { href: '/itineraries', label: '歷年行程', icon: MapPinned, description: '行程紀錄查詢' },
]

interface PortalLayoutProps {
  children: React.ReactNode
}

/// 入口網的客戶端導覽外殼，處理認證導向與行動版選單。
export default function PortalLayout({ children }: PortalLayoutProps) {
  const router = useRouter()
  const pathname = usePathname()
  const [isMenuOpen, setIsMenuOpen] = useState(false)
  const { user, isAuthenticated, isAdmin, isHydrated, logout } = useAuthStore()

  useEffect(() => {
    if (isHydrated && !isAuthenticated) router.replace('/login')
  }, [isAuthenticated, isHydrated, router])

  const handleLogout = async () => {
    try {
      await api.post('/api/auth/logout')
    } catch {
      // 後端失敗不應阻止清除本機的非敏感登入狀態。
    }
    logout()
    toast.success('已登出')
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

  const navigation = (mobile = false) => (
    <nav className={mobile ? 'flex-1 space-y-2 px-4' : 'flex-1 space-y-1 p-3'} aria-label={mobile ? '行動版主要導覽' : '主要導覽'}>
      {navItems.map((item) => {
        const isActive = pathname.startsWith(item.href)
        const Icon = item.icon
        return (
          <Link
            key={item.href}
            href={item.href}
            aria-label={item.label}
            aria-current={isActive ? 'page' : undefined}
            onClick={mobile ? () => setIsMenuOpen(false) : undefined}
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

      <aside className="fixed inset-y-0 left-0 z-30 hidden w-64 flex-col rounded-none border-r border-[var(--glass-border)] glass-panel-strong md:flex">
        <div className="flex items-center gap-3 p-6">
          <div className="flex size-10 items-center justify-center rounded-xl bg-[var(--primary)] text-[var(--primary-foreground)] shadow-md">
            <House aria-hidden="true" className="size-5" />
          </div>
          <div>
            <h2 className="text-sm font-bold tracking-tight">許氏入口網</h2>
            <p className="text-xs text-[var(--muted-foreground)]">Hsu Family Portal</p>
          </div>
        </div>
        <Separator className="opacity-30" />
        {navigation()}
        <Separator className="opacity-30" />
        <div className="space-y-3 p-4">
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
          <Button variant="ghost" className="min-h-11 w-full justify-start text-[var(--muted-foreground)] hover:text-[var(--destructive)]" onClick={handleLogout}>
            <LogOut aria-hidden="true" />
            登出
          </Button>
        </div>
      </aside>

      <header className="fixed inset-x-0 top-0 z-30 flex h-16 items-center justify-between rounded-none border-b border-[var(--glass-border)] px-4 glass-panel-strong md:hidden">
        <Link href="/calendar" className="flex min-h-11 items-center gap-2 font-bold">
          <House aria-hidden="true" className="size-5" />
          許氏入口網
        </Link>
        <Button variant="ghost" size="icon" className="size-11" aria-label="開啟導覽選單" onClick={() => setIsMenuOpen(true)}>
          <Menu aria-hidden="true" />
        </Button>
      </header>

      <Sheet open={isMenuOpen} onOpenChange={setIsMenuOpen}>
        <SheetContent side="left" className="w-[min(20rem,85vw)] glass-panel-strong">
          <SheetHeader>
            <SheetTitle>許氏入口網</SheetTitle>
            <SheetDescription>選擇要前往的功能</SheetDescription>
          </SheetHeader>
          {navigation(true)}
          <div className="border-t border-[var(--glass-border)] p-4">
            <p className="mb-3 truncate text-sm">{user?.displayName}</p>
            <Button variant="outline" className="min-h-11 w-full" onClick={handleLogout}>
              <LogOut aria-hidden="true" />
              登出
            </Button>
          </div>
        </SheetContent>
      </Sheet>

      <main id="main-content" tabIndex={-1} className="min-w-0 flex-1 pt-16 md:ml-64 md:pt-0">
        <div className="p-4 sm:p-6 lg:p-8">{children}</div>
      </main>
    </div>
  )
}
