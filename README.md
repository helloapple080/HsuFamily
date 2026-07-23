# 🏠 許氏入口網 — 企業級家庭專屬入口網站 (Hsu Family Portal)

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Next.js 16](https://img.shields.io/badge/Next.js-16-000000?style=flat-square&logo=next.js)](https://nextjs.org/)
[![MongoDB](https://img.shields.io/badge/MongoDB-7.0+-47A248?style=flat-square&logo=mongodb)](https://www.mongodb.com/)
[![TypeScript](https://img.shields.io/badge/TypeScript-5.x-3178C6?style=flat-square&logo=typescript)](https://www.typescriptlang.org/)
[![Tailwind CSS](https://img.shields.io/badge/Tailwind_CSS-4.0-06B6D4?style=flat-square&logo=tailwindcss)](https://tailwindcss.com/)

一個基於 **Clean Architecture** 原則構建的企業級家庭入口網站，整合農曆行事曆、即時同步、RBAC 權限控制，為「許氏家族」量身打造的數位管理平台。

---

## ✨ 核心功能

| 功能 | 說明 | 技術 |
|------|------|------|
| 🔐 **權限與帳號** | Admin/User 雙角色 RBAC，Argon2id 密碼加密 | JWT + SignalR |
| 👨‍👩‍👧‍👦 **家庭成員** | 成員資料管理，支援農曆生日 | MongoDB + REST API |
| 📅 **實時行事曆** | 國農曆雙顯示，即時同步更新 | SignalR + lunar-javascript |
| 🙏 **拜拜提醒** | 初一十五、節日、忌日管理與祭品清單 | 農曆週期規則引擎 |
| 📜 **歷年行程** | 標籤化行程紀錄，游標式分頁 | MongoDB Compound Index |

## 🏗️ 技術架構

```
┌─────────────────────────────────────────────────────┐
│                    Frontend (Next.js 16)             │
│  React 19 · Tailwind CSS v4 · shadcn/ui base-nova   │
│  Zustand · React Query · Framer Motion · SignalR     │
├─────────────────────────────────────────────────────┤
│                    Backend (.NET 10 Web API)          │
│  Clean Architecture · JWT + RBAC · Rate Limiting     │
│  SignalR Hub · Argon2id · RESTful API               │
├─────────────────────────────────────────────────────┤
│                    Database (MongoDB 7.0+)           │
│  4 Collections · Compound Indexes · Schema Validation│
│  Cursor-based Pagination · Text Search              │
└─────────────────────────────────────────────────────┘
```

## 📁 專案結構

```
family-portal/
├── .agents/                    # AI Agent 技能與規則
│   ├── rules/                  # 企業級開發規範
│   ├── skills/                 # Agent 生產力技能
│   └── workflows/              # 自動化工作流程
├── .github/                    # GitHub 配置
│   └── copilot-instructions.md # Copilot AI 指令
├── backend/                    # .NET 10 後端
│   ├── FamilyPortal.Api/       # API 進入點
│   ├── FamilyPortal.Application/ # 業務邏輯介面
│   ├── FamilyPortal.Domain/    # 領域模型
│   └── FamilyPortal.Infrastructure/ # 基礎設施實作
├── frontend/                   # Next.js 16 前端
│   └── src/
│       ├── app/                # App Router 頁面
│       ├── components/         # UI 元件
│       ├── hooks/              # 自訂 Hooks
│       ├── lib/                # 工具函式
│       └── stores/             # Zustand 狀態管理
└── docker-compose.yml          # 一鍵部署配置
```

## 🚀 快速開始

### 前置需求

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 22+](https://nodejs.org/) (搭配 pnpm)
- [MongoDB 7.0+](https://www.mongodb.com/try/download/community)

### 啟動後端

```bash
cd backend
dotnet run --project FamilyPortal.Api
# API 啟動於 https://localhost:5001
# 預設管理員帳號：admin / Admin@123456 (顯示名稱：許浴宸)
```

### 啟動前端

```bash
cd frontend
pnpm install
pnpm dev
# 前端啟動於 http://localhost:3000
```

### Docker 一鍵部署

```bash
docker compose up -d
```

## 📐 架構原則

本專案嚴格遵循以下原則：

- **Clean Architecture** — Domain → Application → Infrastructure → API 依賴方向
- **RESTful API** — 標準 HTTP 動詞與狀態碼
- **Conventional Commits** — `feat:`, `fix:`, `chore:` 標準提交訊息
- **RBAC (Role-Based Access Control)** — Admin 完整權限 / User 唯讀
- **Repository Pattern** — 隔離資料存取，可單元測試
- **SOLID Principles** — 單一職責、依賴反轉

## 📄 授權

MIT License

---

> 🏠 **許氏入口網** — 讓科技守護許氏家族的每一個重要時刻 ｜ Hsu Family Portal
