import assert from 'node:assert/strict'
import { readFile, readdir } from 'node:fs/promises'

const assets = new URL('../dist-spa/assets/', import.meta.url)
const files = (await readdir(assets)).filter((name) => name.endsWith('.css'))
assert.ok(files.length, 'Cloudflare SPA 缺少編譯後的 CSS')
const css = (await Promise.all(files.map((name) => readFile(new URL(name, assets), 'utf8')))).join('\n')

// 避免 Tailwind 靜默略過 shadcn/ui 必需的語義類別。
const required = [
  ['bg-primary', 'background-color', 'primary'],
  ['text-primary-foreground', 'color', 'primary-foreground'],
  ['bg-secondary', 'background-color', 'secondary'],
  ['bg-card', 'background-color', 'card'],
  ['bg-popover', 'background-color', 'popover'],
  ['border-border', 'border-color', 'border'],
  ['border-input', 'border-color', 'input'],
  ['text-muted-foreground', 'color', 'muted-foreground'],
]
for (const [name, property, token] of required) {
  const rule = new RegExp(`\\.${name}\\s*(?=[,{])[^{}]*\\{[^}]*${property}:\\s*var\\(--${token}\\)`)
  assert.ok(rule.test(css), `SPA 缺少主題樣式 .${name}`)
}
assert.ok(/:where\(\.dark,\s*\.dark \*\)/.test(css), '深色變體必須跟隨使用者選擇的 class')
console.log('Cloudflare SPA 主題樣式檢查通過。')
