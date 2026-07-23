---
description: 啟動本地開發環境（後端 + 前端 + MongoDB）
---

# 啟動開發環境

## 前置條件
- .NET 10 SDK 已安裝
- Node.js 22+ 與 pnpm 已安裝
- MongoDB 已啟動

## 步驟

// turbo
1. 確認 .NET 環境
```bash
export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec" && dotnet --version
```

// turbo
2. 啟動後端 API（背景執行）
```bash
cd backend && export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec" && dotnet run --project FamilyPortal.Api
```
> API 會自動建立 MongoDB 索引與預設 Admin 帳號

// turbo
3. 啟動前端（另一個終端）
```bash
cd frontend && pnpm dev
```

4. 驗證
- 後端: 瀏覽 `https://localhost:5001/api/auth/me`（應返回 401）
- 前端: 瀏覽 `http://localhost:3000`
