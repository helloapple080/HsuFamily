---
name: nextjs-component
description: >-
  建立 Next.js 16 前端元件。涵蓋 Server/Client Components、shadcn/ui 整合、Tailwind v4 樣式。
  Use when creating new UI components, pages, or layouts for the Family Portal frontend.
---

# Next.js 16 元件開發技能

## ⚠️ 重要：先閱讀 Next.js 16 變更
Next.js 16 有重大 API 變更。開發前請先閱讀：
```
node_modules/next/dist/docs/01-app/
```

## Server Component（預設）
檔案位置: `frontend/src/app/{route}/page.tsx`

```tsx
// Server Component 預設不需要任何指令
import type { Metadata } from 'next'

export const metadata: Metadata = {
  title: '頁面標題 | Family Portal',
  description: '頁面描述',
}

export default async function PageName() {
  // 可直接在這裡 fetch 資料（Server-side）
  return (
    <main className="container mx-auto px-4 py-8">
      {/* 內容 */}
    </main>
  )
}
```

## Client Component（需要互動時）
檔案位置: `frontend/src/components/features/{feature}/`

```tsx
'use client'

import { useState } from 'react'
import { motion } from 'framer-motion'
import { Button } from '@/components/ui/button'

interface ComponentNameProps {
  // Props 使用 interface 定義
  title: string
  onAction?: () => void
}

export default function ComponentName({ title, onAction }: ComponentNameProps) {
  const [state, setState] = useState(false)

  return (
    <motion.div
      initial={{ opacity: 0, y: 20 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.3 }}
    >
      <Button onClick={onAction}>{title}</Button>
    </motion.div>
  )
}
```

## shadcn/ui 使用規範
- 配置: `components.json` → style: `base-nova`
- 新增元件: `npx shadcn@latest add <component-name>`
- 元件目錄: `src/components/ui/`（不直接修改這些檔案）
- 擴展方式: 在 `features/` 中建立包裝元件

## Tailwind CSS v4 注意事項
```css
/* v4 語法 — 使用 @theme inline 取代 tailwind.config.ts */
@import "tailwindcss";

@theme inline {
  --color-primary: oklch(0.7 0.15 200);
  --color-background: var(--background);
}
```

## Framer Motion 動畫模版

### 頁面進場
```tsx
<motion.div
  initial={{ opacity: 0, y: 30 }}
  animate={{ opacity: 1, y: 0 }}
  transition={{ duration: 0.5, ease: "easeOut" }}
>
```

### 列表項目交錯動畫
```tsx
<motion.div
  initial={{ opacity: 0, x: -20 }}
  animate={{ opacity: 1, x: 0 }}
  transition={{ delay: index * 0.1 }}
>
```

### Hover 互動效果
```tsx
<motion.div
  whileHover={{ scale: 1.02, y: -4 }}
  whileTap={{ scale: 0.98 }}
  transition={{ type: "spring", stiffness: 300 }}
>
```

## 檢查清單
- [ ] Server Component 除非需要交互
- [ ] Props 使用 `interface` 定義
- [ ] 使用 shadcn/ui 基礎元件
- [ ] 顏色使用 CSS Variables
- [ ] 動畫使用 Framer Motion
- [ ] 語義化 HTML + aria-label
- [ ] 響應式設計（mobile-first）
- [ ] UI 文字使用繁體中文
