---
description: C# 後端開發規範 — 適用於 .NET 10 Clean Architecture 層次
---

# 後端開發規範

## 依賴注入
- 所有服務透過介面注入，禁止直接建立具體類別
- Repository 使用 `Scoped` 生命週期
- MongoDbContext 使用 `Singleton` 生命週期（MongoClient 是執行緒安全的）

## 非同步規則
- 所有 I/O 操作必須使用 `async/await`
- 禁止使用 `.Result`、`.Wait()` 或 `Task.Run()` 包裝同步方法
- 方法命名以 `Async` 結尾（如 `GetByIdAsync`）

## 錯誤處理
- Controller 層捕獲業務邏輯例外，回傳 `ApiResponse.Fail()`
- 不可對 `Exception` 做空 catch（`catch { }` 是禁止的）
- 使用 `ILogger<T>` 記錄錯誤，禁止 `Console.WriteLine`

## MongoDB 規範
- Enum 永遠存為字串（使用 `[BsonRepresentation(BsonType.String)]`）
- DateTime 只使用 UTC（`DateTime.UtcNow`）
- 新增 Collection 時必須在 `MongoDbIndexInitializer` 中建立索引
- 大量查詢必須使用投影（Projection），不查不需要的欄位

## API 設計
- 回傳格式統一使用 `ApiResponse<T>`
- 成功：回傳 `200 OK` 或 `201 Created`
- 失敗：`400 Bad Request`（驗證失敗）、`401 Unauthorized`、`404 Not Found`
- 所有 Controller 必須標記 `[ApiController]` 和 `[Route("api/[controller]")]`
- 寫入操作必須有 `[Authorize(Policy = "AdminOnly")]`
