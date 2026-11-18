'use client'

import { Monitor, Moon, Sun } from 'lucide-react'
import { useTheme } from 'next-themes'
import { Button } from '@/components/ui/button'

const themes = [
  { value: 'light', label: '淺色', icon: Sun },
  { value: 'dark', label: '深色', icon: Moon },
  { value: 'system', label: '跟隨系統', icon: Monitor },
] as const

/// 讓使用者選擇淺色、深色或跟隨系統顯示主題。
export default function ThemeSwitcher() {
  const { theme, setTheme } = useTheme()

  return (
    <div role="group" aria-label="切換顯示主題" className="grid grid-cols-3 gap-1">
      {themes.map(({ value, label, icon: Icon }) => (
        <Button
          key={value}
          type="button"
          variant={theme === value ? 'default' : 'ghost'}
          className="h-auto min-h-12 min-w-0 flex-col gap-1 px-1 py-1.5 text-xs leading-tight whitespace-normal"
          aria-pressed={theme === value}
          onClick={() => setTheme(value)}
        >
          <Icon aria-hidden="true" />
          <span>{label}</span>
        </Button>
      ))}
    </div>
  )
}
