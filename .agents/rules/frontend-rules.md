---
description: Next.js 16 前端開發規範 — 適用於 App Router + TypeScript + Tailwind v4
---

# 前端開發規範

## Next.js 16 特有規則
- 預設使用 Server Components，僅在需要交互時加 `'use client'`
- 使用 `use cache` 指令進行顯式快取控制（取代舊版 ISR）
- 路由使用 App Router（`app/` 目錄），不使用 Pages Router
- 閱讀 `node_modules/next/dist/docs/` 中的最新文件再開始開發

## TypeScript 嚴格模式
- `strict: true` 強制開啟
- 禁止 `any` 型別，使用 `unknown` 後進行型別收窄
- 使用 `interface` 定義物件型別，`type` 僅用於 Union/Intersection
- 所有 API 回應必須定義完整介面
- 元件 Props 使用 `interface ComponentNameProps {}`

## 元件與目錄
```
components/
├── ui/          → shadcn/ui 原始元件（不修改）
├── layout/      → Header, Sidebar, Footer, Container
└── features/    → 功能模組（calendar/, members/, worship/）
    └── calendar/
        ├── CalendarGrid.tsx
        ├── EventCard.tsx
        └── LunarDateBadge.tsx
```

## 狀態管理分層
- **Server State** → React Query（API 資料快取）
- **Client State** → Zustand（UI 狀態、使用者偏好）
- **Form State** → React Hook Form + Zod（表單驗證）
- **URL State** → Next.js searchParams（篩選、分頁）

## 樣式規範
- 使用 Tailwind CSS v4 語法（`@theme inline {}` 取代 `tailwind.config.ts`）
- 顏色使用 CSS Variables（`var(--primary)`），不硬編碼 HEX
- 動畫使用 Framer Motion（`motion.div`），不寫原生 CSS animation
- 深色模式使用 `next-themes` + CSS Variables 雙主題
- 元素應有足夠的 `aria-label` 與語義化 HTML

## 安全規範
- API Base URL 使用環境變數 `NEXT_PUBLIC_API_URL`
- JWT Token 存於 httpOnly cookie 或 Zustand（不用 localStorage）
- 禁止 `dangerouslySetInnerHTML`
- 所有使用者輸入在後端也要驗證
