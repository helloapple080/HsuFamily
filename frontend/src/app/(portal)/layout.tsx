'use client'

import { useEffect, useState } from 'react'
import { useRouter, usePathname } from 'next/navigation'
import { motion, AnimatePresence } from 'framer-motion'
import { useAuthStore } from '@/stores/authStore'
import Link from 'next/link'
import { Button } from '@/components/ui/button'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { Separator } from '@/components/ui/separator'
import api from '@/lib/api'
import { toast } from 'sonner'
import { Menu } from 'lucide-react'
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet'

/// 側邊欄導覽項目
const navItems = [
  { href: '/calendar', label: '行事曆', icon: '📅', description: '國農曆雙顯示' },
  { href: '/members', label: '家庭成員', icon: '👨‍👩‍👧‍👦', description: '成員資料管理' },
  { href: '/itineraries', label: '歷年行程', icon: '📜', description: '行程紀錄查詢' },
]

export default function PortalLayout({
  children,
}: {
  children: React.ReactNode
}) {
  const router = useRouter()
  const pathname = usePathname()
  const [isMenuOpen, setIsMenuOpen] = useState(false)
  const { user, isAuthenticated, isAdmin, isHydrated, logout } = useAuthStore()

  /// 路由守衛 — 未登入自動跳轉到登入頁
  useEffect(() => {
    if (isHydrated && !isAuthenticated) {
      router.replace('/login')
    }
  }, [isAuthenticated, isHydrated, router])

  const handleLogout = async () => {
    try {
      await api.post('/api/auth/logout')
    } catch {
      // 即使 API 呼叫失敗也要登出
    }
    logout()
    toast.success('已登出')
    router.push('/login')
  }

  if (!isHydrated || !isAuthenticated) {
    return (
      <div className="min-h-screen bg-gradient-mesh flex items-center justify-center" role="status">
        <div className="h-8 w-8 animate-spin rounded-full border-2 border-[var(--primary)] border-t-transparent" />
        <span className="sr-only">正在載入入口網</span>
      </div>
    )
  }

  return (
    <div className="min-h-screen bg-gradient-mesh flex">
      {/* ─── 側邊欄 ─── */}
      <motion.aside
        initial={{ x: -280 }}
        animate={{ x: 0 }}
        transition={{ type: 'spring', stiffness: 200, damping: 25 }}
        className="hidden w-64 fixed inset-y-0 left-0 z-30 flex-col glass-panel-strong border-r border-[var(--glass-border)] rounded-none md:flex"
      >
        {/* Logo */}
        <div className="p-6 flex items-center gap-3">
          <div className="w-10 h-10 rounded-xl bg-gradient-to-br from-[oklch(0.6_0.18_260)] to-[oklch(0.5_0.15_310)] flex items-center justify-center shadow-md">
            <span className="text-lg">🏠</span>
          </div>
          <div>
            <h2 className="font-bold text-sm tracking-tight">許氏入口網</h2>
            <p className="text-xs text-[var(--muted-foreground)]">Hsu Family Portal</p>
          </div>
        </div>

        <Separator className="opacity-30" />

        {/* 導覽連結 */}
        <nav className="flex-1 p-3 space-y-1">
          {navItems.map((item) => {
            const isActive = pathname.startsWith(item.href)
            return (
              <Link key={item.href} href={item.href}>
                <motion.div
                  whileHover={{ x: 4 }}
                  whileTap={{ scale: 0.98 }}
                  className={`flex items-center gap-3 px-3 py-2.5 rounded-lg transition-all duration-200 group ${
                    isActive
                      ? 'bg-[var(--primary)] text-[var(--primary-foreground)] shadow-md'
                      : 'hover:bg-[var(--accent)] text-[var(--foreground)]'
                  }`}
                >
                  <span className="text-lg">{item.icon}</span>
                  <div className="flex-1 min-w-0">
                    <p className="text-sm font-medium truncate">{item.label}</p>
                    <p className={`text-xs truncate ${
                      isActive ? 'text-[var(--primary-foreground)]/70' : 'text-[var(--muted-foreground)]'
                    }`}>
                      {item.description}
                    </p>
                  </div>
                  {isActive && (
                    <motion.div
                      layoutId="nav-indicator"
                      className="w-1.5 h-8 rounded-full bg-[var(--primary-foreground)] opacity-50"
                    />
                  )}
                </motion.div>
              </Link>
            )
          })}
        </nav>

        <Separator className="opacity-30" />

        {/* 使用者資訊 */}
        <div className="p-4 space-y-3">
          <div className="flex items-center gap-3">
            <Avatar className="h-9 w-9 border border-[var(--border)]">
              <AvatarFallback className="text-sm bg-gradient-to-br from-[oklch(0.6_0.18_260)] to-[oklch(0.5_0.15_310)] text-white">
                {user?.displayName?.charAt(0) || 'U'}
              </AvatarFallback>
            </Avatar>
            <div className="flex-1 min-w-0">
              <p className="text-sm font-medium truncate">{user?.displayName}</p>
              <Badge
                variant={isAdmin ? 'default' : 'secondary'}
                className="text-[10px] px-1.5 py-0"
              >
                {isAdmin ? '管理員' : '一般用戶'}
              </Badge>
            </div>
          </div>

          <Button
            variant="ghost"
            size="sm"
            className="w-full justify-start text-[var(--muted-foreground)] hover:text-[var(--destructive)]"
            onClick={handleLogout}
          >
            ↗ 登出
          </Button>
        </div>
      </motion.aside>

      {/* ─── 主內容區 ─── */}
      {/* ─── 行動版頂部導覽 ─── */}
      <header className="fixed inset-x-0 top-0 z-30 flex h-16 items-center justify-between border-b border-[var(--glass-border)] px-4 glass-panel-strong rounded-none md:hidden">
        <Link href="/calendar" className="flex items-center gap-2 font-bold">
          <span aria-hidden="true">🏠</span>
          許氏入口網
        </Link>
        <Button
          variant="ghost"
          size="icon"
          aria-label="開啟導覽選單"
          onClick={() => setIsMenuOpen(true)}
        >
          <Menu />
        </Button>
      </header>

      <Sheet open={isMenuOpen} onOpenChange={setIsMenuOpen}>
        <SheetContent side="left" className="w-[min(20rem,85vw)] glass-panel-strong">
          <SheetHeader>
            <SheetTitle>許氏入口網</SheetTitle>
            <SheetDescription>選擇要前往的功能</SheetDescription>
          </SheetHeader>
          <nav className="flex-1 space-y-2 px-4" aria-label="行動版主要導覽">
            {navItems.map((item) => {
              const isActive = pathname.startsWith(item.href)
              return (
                <Link
                  key={item.href}
                  href={item.href}
                  onClick={() => setIsMenuOpen(false)}
                  aria-current={isActive ? 'page' : undefined}
                  className={`flex items-center gap-3 rounded-lg px-3 py-3 transition-colors ${
                    isActive
                      ? 'bg-[var(--primary)] text-[var(--primary-foreground)]'
                      : 'hover:bg-[var(--accent)]'
                  }`}
                >
                  <span aria-hidden="true" className="text-lg">{item.icon}</span>
                  <span>
                    <span className="block text-sm font-medium">{item.label}</span>
                    <span className={`block text-xs ${isActive ? 'opacity-70' : 'text-[var(--muted-foreground)]'}`}>
                      {item.description}
                    </span>
                  </span>
                </Link>
              )
            })}
          </nav>
          <div className="border-t border-[var(--glass-border)] p-4">
            <p className="mb-3 truncate text-sm">{user?.displayName}</p>
            <Button variant="outline" className="w-full" onClick={handleLogout}>
              登出
            </Button>
          </div>
        </SheetContent>
      </Sheet>

      <main className="flex-1 pt-16 md:ml-64 md:pt-0">
        <AnimatePresence mode="wait">
          <motion.div
            key={pathname}
            initial={{ opacity: 0, y: 20 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -20 }}
            transition={{ duration: 0.3 }}
            className="p-4 sm:p-6 lg:p-8"
          >
            {children}
          </motion.div>
        </AnimatePresence>
      </main>
    </div>
  )
}
