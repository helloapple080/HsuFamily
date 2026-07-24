'use client'

import type { ReactNode } from 'react'
import { useAuthStore } from '@/stores/authStore'

interface AdminOnlyProps {
  children: ReactNode
}

/// 僅在當前使用者具備管理員角色時顯示操作介面。
export default function AdminOnly({ children }: AdminOnlyProps) {
  return useAuthStore((state) => state.isAdmin) ? children : null
}
