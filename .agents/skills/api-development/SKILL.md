---
name: api-development
description: >-
  建立或修改 .NET 10 Web API 端點。包含 Controller、DTO、Repository 介面與實作的完整開發流程。
  Use when creating a new API endpoint, modifying existing ones, or adding CRUD operations.
---

# API 開發技能

## 新增 API 端點的標準流程

### Step 1: 定義 Domain 實體（如果是新 Collection）
檔案位置: `backend/FamilyPortal.Domain/Entities/`

```csharp
// 繼承 BaseEntity，取得 Id, CreatedAt, UpdatedAt
[BsonIgnoreExtraElements]
public class NewEntity : BaseEntity
{
    [BsonElement("fieldName")]
    public string FieldName { get; set; } = string.Empty;
}
```

### Step 2: 定義 Repository 介面
檔案位置: `backend/FamilyPortal.Application/Interfaces/`

```csharp
public interface INewEntityRepository : IRepository<NewEntity>
{
    // 特殊查詢方法
    Task<List<NewEntity>> GetByCustomFilterAsync(string filter);
}
```

### Step 3: 定義 DTOs
檔案位置: `backend/FamilyPortal.Application/DTOs/{Feature}/`

```csharp
// Response DTO（不包含敏感資訊）
public class NewEntityResponse { ... }

// Request DTO（含 DataAnnotation 驗證）
public class UpsertNewEntityRequest
{
    [Required(ErrorMessage = "欄位為必填")]
    public string Field { get; set; } = string.Empty;
}
```

### Step 4: 實作 Repository
檔案位置: `backend/FamilyPortal.Infrastructure/MongoDB/`

```csharp
public class NewEntityRepository : MongoRepository<NewEntity>, INewEntityRepository
{
    public NewEntityRepository(MongoDbContext context) : base(context, "CollectionName") { }
}
```

### Step 5: 建立 Controller
檔案位置: `backend/FamilyPortal.Api/Controllers/`

```csharp
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NewEntitiesController : ControllerBase
{
    // GET  → 所有已登入使用者可存取
    // POST/PUT/DELETE → [Authorize(Policy = "AdminOnly")]
    // 回傳一律使用 ApiResponse<T>.Ok() 或 ApiResponse.Fail()
}
```

### Step 6: 註冊 DI
檔案: `backend/FamilyPortal.Infrastructure/DependencyInjection.cs`

```csharp
services.AddScoped<INewEntityRepository, NewEntityRepository>();
```

### Step 7: 建立索引
檔案: `backend/FamilyPortal.Infrastructure/MongoDB/MongoDbIndexInitializer.cs`

在 `InitializeAsync()` 中新增索引建立方法。

## 檢查清單
- [ ] Entity 繼承 `BaseEntity`
- [ ] Enum 使用 `[BsonRepresentation(BsonType.String)]`
- [ ] Repository 介面在 Application 層
- [ ] Repository 實作在 Infrastructure 層
- [ ] Controller 有 `[Authorize]` 或 `[Authorize(Policy = "AdminOnly")]`
- [ ] DTO 有 DataAnnotation 驗證
- [ ] 回傳使用 `ApiResponse<T>` 包裝
- [ ] DI 已註冊
- [ ] 索引已建立
- [ ] 方法有 XML 文件註解
