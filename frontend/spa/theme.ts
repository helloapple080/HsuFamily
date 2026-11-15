import { useSyncExternalStore } from 'react'

type Theme = 'light' | 'dark' | 'system'

function readTheme(): Theme {
  try {
    const value = localStorage.getItem('theme')
    if (value === 'light' || value === 'dark') return value
  } catch {
    // 儲存功能遭封鎖時仍可跟隨系統。
  }
  return 'system'
}

const preferDark = window.matchMedia('(prefers-color-scheme: dark)')
const themeListeners = new Set<() => void>()
let selectedTheme = readTheme()

function applyTheme() {
  const dark = selectedTheme === 'dark' || (selectedTheme === 'system' && preferDark.matches)
  document.documentElement.classList.toggle('dark', dark)
  document.documentElement.classList.toggle('light', !dark)
}

function notifyTheme() {
  applyTheme()
  themeListeners.forEach((listener) => listener())
}

function updateTheme(theme: string) {
  if (theme !== 'light' && theme !== 'dark' && theme !== 'system') return
  selectedTheme = theme
  try {
    localStorage.setItem('theme', theme)
  } catch {
    // 儲存失敗不影響這個分頁的主題。
  }
  notifyTheme()
}

function subscribeTheme(listener: () => void) {
  themeListeners.add(listener)
  return () => { themeListeners.delete(listener) }
}

// 只以同源 JavaScript 模組設定主題，無需注入 inline script 或 style。
applyTheme()
preferDark.addEventListener('change', applyTheme)
window.addEventListener('storage', (event) => {
  if (event.key === 'theme' || event.key === null) {
    selectedTheme = readTheme()
    notifyTheme()
  }
})

export function useTheme() {
  const theme = useSyncExternalStore(subscribeTheme, () => selectedTheme)
  return { theme, setTheme: updateTheme }
}
