# Supabase Portal Foundation Implementation Plan

> **For agentic workers:** Execute each step with a test checkpoint in the isolated worktree.

**Goal:** 建立可驗證的資料層和農曆規則底層，原版維持不變。

**Architecture:** PostgreSQL RLS 為最終授權邊界；Next.js 的純函式負責日期換算。正式帳號、部署和資料遷移不在本階段寫入。

**Tech Stack:** Next.js 16、TypeScript、Supabase PostgreSQL、lunar-javascript、PGlite/Vitest。

## 交付檢查點

- [x] 在 `frontend/src/lib/hosted/__tests__/access.test.ts` 驗證匿名、未核准帳號、一般成員、管理員及撤權。
- [x] 在 `supabase/migrations/202611110001_portal_foundation.sql` 實作角色、規則與私人儲存政策。
- [x] 在 `frontend/src/lib/hosted/__tests__/worship.test.ts` 確認跨年、閏月及農曆缺日行為，再於 `frontend/src/lib/hosted/worship.ts` 實作。
- [x] 記錄本機驗證、雲端驗收及後續功能接線邊界。
