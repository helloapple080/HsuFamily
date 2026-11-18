# Mobile Family Workflows Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans and test-driven development at each checkpoint.

**Goal:** 手機能直接切頁、在不被鍵盤打斷的情況下編輯，並一眼看到接下來的日期。

**Architecture:** 共用 React 外殼負責導覽；行事曆與備忘各自使用相同台北日曆日工具。Worker 維持同源授權，D1 只增加 nullable 日期欄位。

- [x] 在 `frontend/src/components/layout/__tests__/PortalLayout.test.tsx` 驗證手機連結與目前頁面，調整 `PortalLayout.tsx` 和 `frontend/spa/index.html`。
- [x] 在 `frontend/edge/tests/access-lifecycle.test.ts` 驗證合法、非法、清空和舊客戶端更新的備忘日期。在 `frontend/src/components/features/household/__tests__/HouseholdNotes.test.tsx` 驗證日期表單、排序、焦點與 Worker 回退；更新 D1 migration、`edge/household.ts`、共享 contract 和備忘頁。
- [x] 在 `frontend/src/components/features/calendar/__tests__/HostedCalendar.test.tsx` 驗證下一次拜拜跨年且不受篩選影響；沿用 `getWorshipDates`，以 `frontend/src/lib/hosted/civil-date.ts` 取得台北今天。
- [x] 跑 Vitest、TypeScript、lint、Cloudflare dry-run、公開產物及瀏覽器手機／桌面驗收。同步家人、開發者與維運手冊，CI 通過後只部署隔離站。
- [ ] 待真實 iPhone Safari、Android Chrome 鍵盤／日期選擇器驗收，以及第二個經核准的家人帳號；不得以本機合成測試代替。
