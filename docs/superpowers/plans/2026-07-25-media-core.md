# Media Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立不依賴 Google SDK 的媒體領域、權限、驗證、補償與 HTTP API 核心。

**Architecture:** Domain 保存媒體中繼資料；Application 透過 `IMediaRepository` 與 `IMediaStorage` 協調上傳／讀取／軟刪除；Infrastructure 先提供 fake/in-memory 測試 adapter 與 Mongo repository；Api 只處理 multipart、JWT、Admin policy 和回應標頭。

**Tech Stack:** .NET 10、ASP.NET Core、MongoDB 7/Driver 3.7、xUnit。

## Global Constraints

- JPEG、PNG、WebP；單張最多 10 MB。
- 成員頭像每人一張；拜拜事件或行程各最多 30 張。
- `Public` 匿名可讀，`FamilyOnly` 必須登入；所有 mutation 僅 Admin。
- Google Drive ID 與憑證不得進入公開 DTO、Log 或前端。
- MongoDB 不保存圖片二進位；刪除採軟刪除與可重試清理。

---

## File Map

- `backend/FamilyPortal.Domain/Entities/MediaAsset.cs`：持久化媒體中繼資料。
- `backend/FamilyPortal.Domain/Enums/MediaVisibility.cs`、`MediaPurpose.cs`：安全列舉。
- `backend/FamilyPortal.Application/Interfaces/IMediaStorage.cs`：供應商邊界。
- `backend/FamilyPortal.Application/Interfaces/IMediaRepository.cs`：資料邊界。
- `backend/FamilyPortal.Application/Interfaces/IMediaService.cs`：用例邊界。
- `backend/FamilyPortal.Application/Services/MediaService.cs`：權限、限制與補償。
- `backend/FamilyPortal.Api/Controllers/MediaController.cs`：HTTP adapter。

### Task 1: Media domain and repository

**Files:**
- Create: `backend/FamilyPortal.Domain/Entities/MediaAsset.cs`
- Create: `backend/FamilyPortal.Domain/Enums/MediaVisibility.cs`
- Create: `backend/FamilyPortal.Domain/Enums/MediaPurpose.cs`
- Create: `backend/FamilyPortal.Application/Interfaces/IMediaRepository.cs`
- Create: `backend/FamilyPortal.Infrastructure/MongoDB/MediaRepository.cs`
- Modify: `backend/FamilyPortal.Infrastructure/MongoDB/MongoDbIndexInitializer.cs`
- Modify: `backend/FamilyPortal.Infrastructure/DependencyInjection.cs`
- Test: `backend/FamilyPortal.Security.Tests/ArchitectureDependencyTests.cs`

**Interfaces:**
- Produces: lookup by ID, active owner media count, create, soft delete and pending-cleanup queries.

- [ ] **Step 1: Add failing architecture and model serialization tests**

Assert Domain has no Google namespace references and enum fields serialize as strings. Assert the repository interface exposes cancellation-aware methods.

- [ ] **Step 2: Run tests and confirm RED**

Run: `dotnet test backend/FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj --filter Media`  
Expected: FAIL because media types are absent.

- [ ] **Step 3: Implement focused entities and repository**

```csharp
public sealed class MediaAsset : BaseEntity {
    public string StorageProvider { get; set; } = string.Empty;
    public string StorageObjectId { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public MediaVisibility Visibility { get; set; }
    public MediaPurpose Purpose { get; set; }
    public string OwnerEntityId { get; set; } = string.Empty;
    public string UploadedBy { get; set; } = string.Empty;
    public DateTime? DeletedAt { get; set; }
    public bool CleanupPending { get; set; }
}
```

Add indexes for active owner/purpose order and cleanup pending state; register only `IMediaRepository` in DI.

- [ ] **Step 4: Run backend tests**

Run: `dotnet test backend/FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj`  
Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend
git commit -m "feat: add provider-neutral media metadata"
```

### Task 2: Storage contract, validation and compensation

**Files:**
- Create: `backend/FamilyPortal.Application/Models/MediaUpload.cs`
- Create: `backend/FamilyPortal.Application/Models/StoredMediaObject.cs`
- Create: `backend/FamilyPortal.Application/Interfaces/IMediaStorage.cs`
- Create: `backend/FamilyPortal.Application/Interfaces/IMediaService.cs`
- Create: `backend/FamilyPortal.Application/Services/MediaService.cs`
- Create: `backend/FamilyPortal.Application/DTOs/Media/MediaDtos.cs`
- Test: `backend/FamilyPortal.Security.Tests/Application/MediaServiceTests.cs`

**Interfaces:**
- Produces: `StoreAsync(MediaUpload, CancellationToken)`, `OpenReadAsync(string storageObjectId, CancellationToken)`, `DeleteAsync(string storageObjectId, CancellationToken)`.
- Produces: `UploadAsync(UploadMediaRequest request, string userId, CancellationToken)` and permission-aware read/delete methods.

- [ ] **Step 1: Write failing service tests**

Cover valid upload, unsupported signature, file over 10 MB, owner limit, storage success/repository failure compensation, private anonymous denial, public anonymous read, soft deletion and cleanup failure.

- [ ] **Step 2: Run tests and confirm RED**

Run: `dotnet test backend/FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj --filter MediaServiceTests`  
Expected: FAIL because `MediaService` is missing.

- [ ] **Step 3: Implement service with narrow contracts**

```csharp
public sealed record MediaUpload(Stream Content, string FileName, string DeclaredContentType, long Length);
public sealed record StoredMediaObject(string ObjectId, string ContentType, long Length, int Width, int Height);
```

Inspect magic bytes before storage, map only JPEG/PNG/WebP, enforce purpose counts, generate safe names inside storage adapter, and call `DeleteAsync` if metadata persistence fails. Return `ServiceResult<T>` rather than provider exceptions.

- [ ] **Step 4: Run all service tests**

Run: `dotnet test backend/FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj`  
Expected: all tests pass including compensation paths.

- [ ] **Step 5: Commit**

```bash
git add backend/FamilyPortal.Application backend/FamilyPortal.Security.Tests
git commit -m "feat: enforce secure media workflows"
```

### Task 3: Authorized media API

**Files:**
- Create: `backend/FamilyPortal.Api/Controllers/MediaController.cs`
- Modify: `backend/FamilyPortal.Api/Program.cs`
- Modify: `backend/FamilyPortal.Application/Common/ValidationLimits.cs`
- Test: `backend/FamilyPortal.Security.Tests/ApiSecurityIntegrationTests.cs`

**Interfaces:**
- Produces endpoints: `POST /api/media`, `GET /api/media/{id}/content`, `PATCH /api/media/{id}/visibility`, `DELETE /api/media/{id}`.

- [ ] **Step 1: Write failing authorization and header integration tests**

Assert anonymous upload is 401, User upload is 403, Admin upload reaches validation, private content is 401 anonymously, public content is 200 anonymously, and content responses include `X-Content-Type-Options: nosniff` plus visibility-specific cache headers.

- [ ] **Step 2: Run tests and confirm RED**

Run: `dotnet test backend/FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj --filter MediaApi`  
Expected: FAIL with missing routes.

- [ ] **Step 3: Implement thin controller and upload rate policy**

Use `[Authorize(Policy = "AdminOnly")]` on mutation actions and explicit anonymous handling only on the content action, where `IMediaService` decides visibility. Set multipart body limit to 10 MB plus bounded overhead and add a named media upload limiter.

- [ ] **Step 4: Verify API and architecture guards**

Run: `dotnet test backend/FamilyPortal.slnx`  
Expected: all backend, authorization and architecture tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend
git commit -m "feat: expose authorized media endpoints"
```

