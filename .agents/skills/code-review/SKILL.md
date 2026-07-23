---
name: code-review
description: >-
  程式碼審查檢查清單與常見問題模式。用於 PR Review 或自我審查。
  Use when reviewing code changes, creating PRs, or ensuring code quality standards.
---

# 程式碼審查技能

## 審查流程

### 1. 架構合規性檢查
```
✅ Domain 層沒有引用 Application / Infrastructure
✅ Application 層只依賴 Domain
✅ Api 層不直接使用 Infrastructure 實作（透過介面）
✅ 新 Repository 已在 DependencyInjection.cs 註冊
```

### 2. 安全性檢查
```
✅ API 端點有 [Authorize] 或 [AllowAnonymous]
✅ 寫入操作有 [Authorize(Policy = "AdminOnly")]
✅ 沒有在 Log 中輸出完整 Token 或密碼
✅ 使用者輸入在後端有驗證（不只前端驗證）
✅ 沒有 SQL/NoSQL Injection 風險（使用強型別 API）
✅ 沒有使用 dangerouslySetInnerHTML
✅ 密碼相關操作只使用 Argon2id
```

### 3. 效能檢查
```
✅ MongoDB 查詢使用到索引
✅ 大量資料查詢有分頁
✅ 沒有 N+1 查詢問題
✅ 前端沒有不必要的 re-render
✅ 圖片使用 Next.js <Image />
✅ 大型列表使用虛擬化（如果超過 100 項）
```

### 4. 可維護性檢查
```
✅ 命名清晰（英文變數名、繁體中文註解）
✅ 函式不超過 50 行（建議拆分）
✅ 檔案不超過 300 行（建議拆分元件）
✅ 魔術數字已提取為常數
✅ 重複程式碼已提取為共用方法
✅ TypeScript 沒有 any 型別
```

### 5. 測試檢查
```
✅ 新增的 API 有對應的手動測試步驟
✅ 邊界條件已考慮（空值、極端值）
✅ 錯誤路徑有適當處理
```

## 常見問題模式

### ❌ 反模式 1: 同步阻塞
```csharp
// 錯誤
var result = _repo.GetByIdAsync(id).Result;

// 正確
var result = await _repo.GetByIdAsync(id);
```

### ❌ 反模式 2: Controller 中寫業務邏輯
```csharp
// 錯誤：在 Controller 中直接操作資料庫
[HttpPost]
public async Task<IActionResult> Create([FromBody] Request req)
{
    // 大量業務邏輯直接寫在 Controller...
}

// 正確：抽到 Application Service
[HttpPost]
public async Task<IActionResult> Create([FromBody] Request req)
{
    var result = await _service.CreateAsync(req);
    return Ok(ApiResponse<Response>.Ok(result));
}
```

### ❌ 反模式 3: 前端硬編碼 API URL
```typescript
// 錯誤
fetch('http://localhost:5001/api/members')

// 正確
const apiClient = axios.create({
  baseURL: process.env.NEXT_PUBLIC_API_URL
})
```

### ❌ 反模式 4: 直接修改 shadcn/ui 元件
```
// 錯誤：直接編輯 src/components/ui/button.tsx
// 正確：建立 features/ 中的包裝元件
```

## PR 描述模板
```markdown
## 變更類型
- [ ] feat: 新功能
- [ ] fix: 修復
- [ ] refactor: 重構

## 變更內容
簡述此 PR 的目的與變更範圍。

## 測試方式
描述如何驗證此變更。

## 檢查清單
- [ ] 通過自我審查（參考 code-review skill）
- [ ] 更新相關文件
- [ ] 確認無安全性問題
```
