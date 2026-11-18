# Household Notes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to verify each test checkpoint.

**Goal:** 在現有 Cloudflare 私人入口加入可查找、可還原的家用備忘。

**Architecture:** React Query 的頁面只呼叫同源 API；Worker 先驗證身分、角色與來源，再交備忘處理器讀寫獨立的 D1 資料表。Next.js 回退及公開展示不接此路由。

**Tech Stack:** React、Vite、TypeScript、Zod、Vitest、Cloudflare Workers 與 D1。

## Global Constraints

不加付費服務或套件；不放合成或真實資料到 Git、文件或已部署的家用備忘。修改與刪除用版本；正式站與白名單不變。

## Tasks

- [x] **API 與資料表：** 在 `frontend/edge/tests/access-lifecycle.test.ts` 先驗證 `/api/hosted/household` 會拒絕跨站和普通家人寫入，並用管理員執行空清單 → 新增 `201` → 修改 `200` → 舊版本刪除 `409` → 正確版本刪除 `200`。以 `node_modules/.bin/vitest run edge/tests/access-lifecycle.test.ts` 看紅綠；實作在 `edge/migrations/0003_household_notes.sql`、`edge/household.ts`、`edge/router.ts`、`src/lib/hosted/household-contract.ts`。
- [x] **頁面與角色：** 在 `frontend/src/components/features/household/__tests__/HouseholdNotes.test.tsx` 驗證空清單與唯讀、篩選、表單衝突保留輸入；建立 `HouseholdNotes.tsx`、編輯器、`useHouseholdNotes.ts`、SPA 導覽；共用外殼只在 Cloudflare 顯示選項。檢查 320px、390px 與桌面，深淺色沿用本機主題。
- [x] **發布與文件：** 更新 `docs/guides` 的家人、開發者、隔離站及 CI/CD 手冊，執行 `ruby docs/guides/render.rb --check`、`git diff --check`，同步 Obsidian。通過完整 CI，再對隔離 D1 作備份／還原、加法遷移、部署及匿名／管理員檢查。實際結果見 [Cloudflare 隔離站紀錄](../../guides/cloudflare-staging.md)。
