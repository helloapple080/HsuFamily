# 🏠 許氏入口網 — 前端 (Next.js 16) | Hsu Family Portal Frontend

## 技術棧
- **Next.js 16.2** — App Router, React 19, Turbopack
- **TypeScript 5** — 嚴格模式
- **Tailwind CSS v4** — `@theme inline {}` 新語法
- **shadcn/ui** — `base-nova` 風格，Radix UI 基礎
- **Zustand 5** — Client 狀態管理
- **TanStack React Query 5** — Server 狀態與快取
- **React Hook Form 7 + Zod 4** — 表單驗證與全視窗對話框 (Dialog) CRUD
- **Framer Motion 12** — 位移、縮放與極致玻璃擬態 (Glassmorphism 2.0)
- **@microsoft/signalr** — 即時事件推送與自動狀態重新驗證
- **lunar-javascript** — 農曆引擎轉換與節慶判斷
- **next-themes** — 深色/淺色模式切換

## 啟動

```bash
pnpm install
pnpm dev        # 開發模式 (Turbopack)
pnpm build      # 正式建置
pnpm lint       # ESLint 檢查
```

## 目錄結構

```
src/
├── app/                          # App Router 頁面
│   ├── layout.tsx                # 根佈局
│   ├── page.tsx                  # 首頁
│   ├── globals.css               # 全域樣式 + Tailwind v4
│   ├── (auth)/                   # 認證群組路由
│   │   └── login/page.tsx        # 登入頁
│   └── (portal)/                 # 需登入的群組路由
│       ├── layout.tsx            # Portal 佈局（含 Sidebar）
│       ├── calendar/page.tsx     # 行事曆
│       ├── members/page.tsx      # 家庭成員
│       └── itineraries/page.tsx  # 歷年行程
├── components/
│   ├── ui/                       # shadcn/ui 基礎元件（勿修改）
│   ├── layout/                   # 共用佈局（Header, Sidebar）
│   └── features/                 # 功能模組元件
│       ├── calendar/             # 行事曆相關
│       ├── members/              # 家庭成員相關
│       └── auth/                 # 認證相關
├── hooks/                        # 自訂 React Hooks
│   ├── useAuth.ts                # 認證 Hook
│   ├── useSignalR.ts             # SignalR 連線
│   └── useCalendarEvents.ts      # 行事曆事件
├── lib/                          # 工具函式
│   ├── api.ts                    # Axios API Client
│   ├── utils.ts                  # shadcn cn() 工具
│   └── lunar.ts                  # 農曆工具函式
├── stores/                       # Zustand Stores
│   └── authStore.ts              # 認證狀態
└── types/                        # TypeScript 型別
    ├── api.ts                    # API 回應型別
    ├── member.ts                 # 成員型別
    └── event.ts                  # 事件型別
```

## ✨ 互動功能
- **家庭成員管理**: 具備新增、編輯與刪除成員之對話框，支援國農曆生日輸入。
- **行事曆新增**: 整合式的彈窗表單，可快速新增事件、拜拜行程。
- **歷年行程**: 支援快速新增行程圖文與搜尋標籤。
- **即時通知**: 透過 SignalR 達成，當有其他裝置更新資料時，系統會自動在不重新整理的情況下更新畫面。
- **視覺效果**: 採用 Glassmorphism 2.0，具備動態漸層背景與細膩的微交互動效。

## 設計風格
- **整體風格**: 深色玻璃擬態 (Dark Glassmorphism)
- **元件風格**: shadcn/ui `base-nova`（緊湊、資訊密度高）
- **動畫**: 微交互動畫、頁面轉場
- **響應式**: Mobile-first，支援 320px ~ 4K
- **無障礙**: WCAG 2.1 AA 標準

## 行事曆農曆顯示
- 月檢視同時顯示國曆與農曆
- 農曆初一顯示月份名稱
- 節日與拜拜日以特殊顏色標記
- 格式範例：「農曆正月初一」
