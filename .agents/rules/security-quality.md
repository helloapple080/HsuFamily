---
description: 安全性與品質規範 — 適用於所有程式碼變更
---

# 安全性與品質規範

## 安全性規則（零容忍，違反即拒絕 PR）

### 認證與授權
- 所有 API 端點必須有 `[Authorize]` 或明確標註為公開的文件註解
- Admin 操作必須使用 `[Authorize(Policy = "AdminOnly")]`
- JWT Secret 長度至少 32 字元，禁止出現在版本控制中
- Refresh Token 必須實施 Rotation Pattern

### 密碼安全
- 唯一允許的密碼雜湊演算法：**Argon2id**
- 禁止使用 BCrypt、SHA-256、MD5 或任何非 Argon2id 演算法
- 密碼最低強度：8 字元
- 密碼不得出現在任何 Log 輸出中

### 輸入驗證
- 後端永遠不信任前端輸入（前端驗證僅為 UX 優化）
- 所有字串輸入需 Trim 並限制長度
- MongoDB 查詢禁止直接拼接使用者輸入（使用強型別 Driver API）
- 文件上傳必須驗證 MIME 類型和大小

### XSS / CSRF 防護
- HTTP 回應標頭：`X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`
- 前端禁止 `dangerouslySetInnerHTML`
- API 啟用 CORS 白名單制

## 品質規範

### Pull Request 要求
- 每個 PR 必須附帶變更原因描述
- 新增 API 端點必須附帶 Request/Response 範例
- 所有 CI 檢查必須通過後才可合併

### 日誌記錄
- 使用結構化日誌（`ILogger<T>`）
- 日誌等級使用規範：
  - `Debug` — 開發除錯資訊
  - `Information` — 業務操作記錄（登入、CRUD）
  - `Warning` — 可恢復的異常（登入失敗、Token 過期）
  - `Error` — 系統錯誤
- 敏感資料（密碼、完整 Token）禁止出現在任何日誌等級

### 效能規範
- MongoDB 查詢必須使用索引（慢查詢 > 100ms 需調查）
- API 回應時間 P95 < 500ms
- 前端首屏載入 LCP < 2.5s
- 圖片使用 Next.js `<Image />` 自動最佳化
