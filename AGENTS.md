# 許氏入口網 (Hsu Family Portal) — AI Agent 指令

> 本文件適用於所有 AI Coding Agent（Copilot, Claude, Gemini, Cursor 等）。
> 在撰寫或修改本專案程式碼前，請務必遵循以下規範。

## 專案概覽
許氏入口網 (Hsu Family Portal) 是一個為許氏家族打造的企業級家庭入口網站，結合台灣傳統文化（農曆、拜拜）與現代 Web 技術。第一任管理員名稱為「許浴宸」。

## 技術棧（切勿使用其他替代品）
- **後端**: C# .NET 10 Web API (Clean Architecture)
- **前端**: Next.js 16 (App Router) + TypeScript 5 + Tailwind CSS 4 + shadcn/ui (base-nova style)
- **資料庫**: MongoDB 7 (官方 C# Driver 3.7)
- **狀態管理**: Zustand 5
- **資料請求**: TanStack React Query 5
- **表單驗證**: React Hook Form 7 + Zod 4
- **動畫**: Framer Motion 12
- **即時通訊**: SignalR (後端 Hub + 前端 @microsoft/signalr)
- **農曆**: lunar-javascript

## 架構規則（最高優先級）

### 後端 — Clean Architecture 依賴規則
```
Domain → 無任何外部依賴，僅 MongoDB.Bson 標注屬性
Application → 僅依賴 Domain
Infrastructure → 依賴 Domain + Application，實作介面
Api → 依賴 Application + Infrastructure，DI 組裝
```
- **禁止** Api 層直接引用 Infrastructure 內部實作（僅透過介面）
- **禁止** Domain 層引用 Application 或 Infrastructure
- 所有業務邏輯在 Application 層的 Service 中實作
- 資料存取僅透過 Repository 介面

### 前端 — 目錄結構規則
```
src/
├── app/           → App Router 頁面，Server Components 優先
├── components/
│   ├── ui/        → shadcn/ui 基礎元件（勿直接修改）
│   ├── layout/    → 共用佈局元件
│   └── features/  → 功能模組元件（calendar/, members/, etc.）
├── hooks/         → 自訂 React Hooks
├── lib/           → 工具函式、API Client、常數
├── stores/        → Zustand Stores
└── types/         → TypeScript 型別定義
```

## 程式碼風格（必須遵守）

### C# (.NET)
- 使用 `async/await`，禁止 `.Result` 或 `.Wait()`
- Enum 存入 MongoDB 時使用 **字串表示**（非數字）
- 所有 public 方法必須有 XML 文件註解
- Controller 方法回傳統一使用 `ApiResponse<T>` 包裝
- 密碼相關操作只能使用 `Argon2id`，禁止 BCrypt/SHA
- DateTime 一律使用 `DateTime.UtcNow`

### TypeScript (Next.js)
- 嚴格模式 (`strict: true`)
- 使用 `interface` 優先於 `type`（除非需要 Union Type）
- 元件使用 `export default function ComponentName()`
- 禁止 `any` 型別，使用 `unknown` 後驗證
- Server Components 優先，僅在需要互動時標記 `'use client'`
- API 請求使用 React Query，禁止直接在元件中 `fetch`

### Git Commit（Conventional Commits）
```
feat: 新增功能
fix: 修復錯誤
refactor: 重構（不改變行為）
style: 格式調整
docs: 文件更新
chore: 建構/依賴調整
test: 測試相關
perf: 效能優化
```

## 安全規則（零容忍）
- ❌ 禁止在前端暴露 API 密鑰或 JWT Secret
- ❌ 禁止使用 `dangerouslySetInnerHTML`
- ❌ 禁止在 Console/Log 中輸出密碼或 Token 完整值
- ✅ 所有 API 端點必須有 `[Authorize]` 或明確標註為公開
- ✅ Admin CRUD 操作必須使用 `[Authorize(Policy = "AdminOnly")]`
- ✅ 使用者輸入必須在後端驗證（不信任前端驗證）

## 農曆相關（文化敏感）
- 前端農曆轉換使用 `lunar-javascript` 套件
- 農曆日期格式統一：`農曆X月X日`（如：農曆正月初一）
- 閏月顯示格式：`農曆閏X月X日`
- 天干地支年份顯示格式：`甲子年`（供參考，非必要）

## 回應語言
- 程式碼註解：使用**繁體中文**
- 變數/函式名：使用**英文**
- Git Commit Message：使用**英文**
- UI 介面文字：使用**繁體中文**
