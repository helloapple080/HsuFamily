'use client'

import { useState } from 'react'
import { useRouter } from 'next/navigation'
import { motion } from 'framer-motion'
import { useAuthStore } from '@/stores/authStore'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Card, CardContent, CardHeader } from '@/components/ui/card'
import { toast } from 'sonner'
import api from '@/lib/api'
import type { ApiResponse } from '@/types/api'
import type { LoginResponse } from '@/types/auth'
import { LoaderCircle } from 'lucide-react'

export default function LoginPage() {
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [isLoading, setIsLoading] = useState(false)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const router = useRouter()
  const { login } = useAuthStore()

  const handleLogin = async (e: React.FormEvent) => {
    e.preventDefault()
    setIsLoading(true)
    setErrorMessage(null)

    try {
      const { data } = await api.post<ApiResponse<LoginResponse>>(
        '/api/auth/login',
        { username, password }
      )

      if (data.success && data.data) {
        login(data.data.user)
        toast.success('登入成功', { description: `歡迎回來，${data.data.user.displayName}` })
        router.push('/calendar')
      } else {
        setErrorMessage(data.message || '登入失敗，請檢查帳號與密碼。')
        toast.error('登入失敗', { description: data.message })
      }
    } catch {
      setErrorMessage('帳號或密碼錯誤')
      toast.error('登入失敗', { description: '帳號或密碼錯誤' })
    } finally {
      setIsLoading(false)
    }
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-gradient-mesh p-4">
      {/* 裝飾性光暈 */}
      <div className="fixed inset-0 overflow-hidden pointer-events-none">
        <div className="absolute top-1/4 left-1/4 w-96 h-96 rounded-full bg-[oklch(0.5_0.15_260/0.08)] blur-3xl animate-float" />
        <div className="absolute bottom-1/3 right-1/4 w-72 h-72 rounded-full bg-[oklch(0.5_0.12_310/0.06)] blur-3xl animate-float" style={{ animationDelay: '1.5s' }} />
      </div>

      <motion.div
        initial={{ opacity: 0, y: 40, scale: 0.95 }}
        animate={{ opacity: 1, y: 0, scale: 1 }}
        transition={{ duration: 0.6, ease: 'easeOut' }}
        className="relative z-10 w-full max-w-md"
      >
        <Card className="glass-panel-strong border-0 shadow-2xl">
          <CardHeader className="text-center space-y-4 pb-2">
            {/* Logo 區域 */}
            <motion.div
              initial={{ scale: 0 }}
              animate={{ scale: 1 }}
              transition={{ type: 'spring', stiffness: 200, delay: 0.2 }}
              className="mx-auto w-20 h-20 rounded-2xl bg-gradient-to-br from-[oklch(0.6_0.18_260)] to-[oklch(0.5_0.15_310)] flex items-center justify-center shadow-lg animate-glow-pulse"
            >
              <span className="text-3xl">🏠</span>
            </motion.div>

            <motion.div
              initial={{ opacity: 0, y: 10 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: 0.3 }}
            >
              <h1 className="text-2xl font-bold tracking-tight">許氏入口網</h1>
              <p className="text-sm text-[var(--muted-foreground)] mt-1">
                Hsu Family Portal
              </p>
            </motion.div>
          </CardHeader>

          <CardContent className="pt-6">
            <form onSubmit={handleLogin} className="space-y-5">
              <motion.div
                initial={{ opacity: 0, x: -20 }}
                animate={{ opacity: 1, x: 0 }}
                transition={{ delay: 0.4 }}
                className="space-y-2"
              >
                <Label htmlFor="username" className="text-sm font-medium">
                  帳號
                </Label>
                <Input
                  id="username"
                  type="text"
                  autoComplete="username"
                  spellCheck={false}
                  maxLength={254}
                  aria-invalid={Boolean(errorMessage)}
                  aria-describedby={errorMessage ? 'login-error' : undefined}
                  placeholder="輸入帳號或信箱"
                  value={username}
                  onChange={(e) => setUsername(e.target.value)}
                  required
                  className="h-11 bg-[var(--secondary)] border-[var(--border)] focus:ring-2 focus:ring-[var(--primary)] transition-[border-color,box-shadow,background-color]"
                />
              </motion.div>

              <motion.div
                initial={{ opacity: 0, x: -20 }}
                animate={{ opacity: 1, x: 0 }}
                transition={{ delay: 0.5 }}
                className="space-y-2"
              >
                <Label htmlFor="password" className="text-sm font-medium">
                  密碼
                </Label>
                <Input
                  id="password"
                  type="password"
                  autoComplete="current-password"
                  maxLength={128}
                  aria-invalid={Boolean(errorMessage)}
                  aria-describedby={errorMessage ? 'login-error' : undefined}
                  placeholder="輸入密碼"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  required
                  className="h-11 bg-[var(--secondary)] border-[var(--border)] focus:ring-2 focus:ring-[var(--primary)] transition-[border-color,box-shadow,background-color]"
                />
              </motion.div>

              {errorMessage && (
                <p id="login-error" role="alert" className="text-sm text-destructive">
                  {errorMessage}
                </p>
              )}

              <motion.div
                initial={{ opacity: 0, y: 10 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: 0.6 }}
              >
                <Button
                  type="submit"
                  className="w-full h-11 bg-gradient-to-r from-[oklch(0.55_0.18_260)] to-[oklch(0.5_0.15_280)] hover:from-[oklch(0.6_0.18_260)] hover:to-[oklch(0.55_0.15_280)] text-white font-medium shadow-lg transition-[transform,box-shadow,background-color] duration-200 hover:shadow-xl hover:-translate-y-0.5"
                  disabled={isLoading}
                >
                  {isLoading ? (
                    <span className="flex items-center gap-2">
                      <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
                      登入中…
                    </span>
                  ) : (
                    '登入'
                  )}
                </Button>
              </motion.div>
            </form>

            <motion.p
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              transition={{ delay: 0.8 }}
              className="text-center text-xs text-[var(--muted-foreground)] mt-6"
            >
              請使用管理員提供的帳號密碼登入
            </motion.p>
          </CardContent>
        </Card>
      </motion.div>
    </div>
  )
}
