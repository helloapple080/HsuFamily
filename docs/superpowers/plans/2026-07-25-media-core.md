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

---

## Task 2 獨立審查修正報告（2026-07-26）

### 架構與安全修正

- Application 新增供應商無關的 `IImageInspector` 與 `ImageInspectionResult`，不參考任何影像套件。
- Infrastructure 以 `SkiaSharp 4.150.1` 的 `SKCodec.GetPixels` 完整解碼靜態 JPEG、PNG 與 WebP；只接受 `SKCodecResult.Success`，並比對前置結構檢查所得的格式與尺寸。APNG 與 animated WebP 一律在前置檢查拒絕，不嘗試只處理第一幀。
- 保留 10 MB 與 8192 像素單邊上限，並將總像素上限收緊為 1,600 萬（RGBA 約 64 MB）；完整解碼前已執行簽章、chunk/marker、尺寸與像素數前置檢查。
- `ProviderName` 於 `StoreAsync` 之前只擷取一次並驗證，避免儲存成功後屬性取值例外造成 orphan object。
- WebP 額外拒絕 VP8X reserved bits、VP8X 非第一 chunk 與所有 animated WebP（animation flag、ANIM 或 ANMF），避免只解碼 frame 0；`CleanupPendingAsync` XML 明載僅限可信任背景工作者或 AdminOnly 入口。

### Decoder 授權選型

- 未使用 ImageSharp 4.0.0。Six Labors 官方公告說明 v4 對直接套件相依啟用 build-time license key，並使用 Six Labors Split License；本公開 MIT repository 不應提交 license key，也不需要引入額外授權營運作業。參考：[ImageSharp 4.0.0 官方公告](https://sixlabors.com/posts/announcing-imagesharp-400/)。
- 改採明確 MIT 授權的 `SkiaSharp 4.150.1`；官方 NuGet 顯示支援 .NET 10 且套件授權為 MIT。參考：[SkiaSharp 4.150.1](https://www.nuget.org/packages/SkiaSharp/4.150.1)、[SkiaSharp source license](https://github.com/mono/SkiaSharp/blob/main/LICENSE.md)。
- `SkiaSharp.NativeAssets.Linux 4.150.1` 僅於 Infrastructure 引入，供 Linux CI/部署解析 native library；macOS/Windows native assets 由 `SkiaSharp` 對應的相依套件提供。參考：[Linux native assets 4.150.1](https://www.nuget.org/packages/SkiaSharp.NativeAssets.Linux/4.150.1)。
- SkiaSharp 套件已以 exact version pin；NuGet lockfile 與第三方授權 notices 將於後續 supply-chain task 統一完成，不在本修正批次擴張。

### 回歸與驗證

- 新增真實 Infrastructure inspector 測試：真實 JPEG/PNG/WebP、CRC 正確但 zlib IDAT 損毀的 PNG、entropy 截斷的 JPEG。
- Application service 回歸涵蓋 PNG forbidden PLTE/chunk order、WebP reserved bits/chunk order、完整解碼器拒絕、ProviderName 在儲存前失敗不寫入物件。真實 inspector 測試已獨立至 `Infrastructure/SkiaImageInspectorTests.cs`，避免再擴張原 service 測試類別。
- 複審追加：PNG color type 0/4 禁止 PLTE、indexed palette 必填且不得超過 bit depth 項目數、拒絕 APNG `acTL`/`fcTL`/`fdAT`，並允許連續 IDAT 中的空 chunk（累計 payload 仍必須大於零）；VP8X 實際 flag byte reserved mask 與 ICCP/Alpha/EXIF/XMP feature gap/order，animation flag/ANIM/ANMF 一律拒絕；Skia 完整解碼以共用兩個 slot 的 gate 限制並行，且在等待與解碼後重新觀察取消。
- `dotnet build backend/FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj --maxcpucount:1 --no-restore`：成功，0 warnings / 0 errors。
- focused media suite：86/86 passed。
- 排除在當前 TestServer 環境會卡住的 `ApiSecurityIntegrationTests` 後，其餘完整後端 suite：289/289 passed。
- 完整直接 runner 在 discover/start 後長時間無輸出，已以 Ctrl-C 中止（exit 130）。以 `-longRunning 15 -diagnostics` 重跑定位為既有 `ApiSecurityIntegrationTests.ProductionHttpsResponse_IncludesHsts` 持續超過 59 秒；本批次不將完整 suite 記錄為通過，需由主流程處理此環境性卡住後補跑。
- `dotnet list backend/FamilyPortal.Infrastructure/FamilyPortal.Infrastructure.csproj package --vulnerable --include-transitive`：NuGet 未回報任何已知易受攻擊的直接或間接套件。
- 本批次未修改 frontend lockfile 或 workspace 設定。

---

## Task 3 實作報告（2026-07-26）

### API、授權與輸入界線

- 新增 `POST /api/media`、`GET /api/media/{id}/content`、`PATCH /api/media/{id}/visibility`、`DELETE /api/media/{id}`；三個 mutation 均套用 `AdminOnly` 並使用 `ApiResponse`，只有內容讀取明確允許匿名，再由 `IMediaService` 依 `Public`／`FamilyOnly` 決定存取。
- 上傳者只取自 `ClaimTypes.NameIdentifier`。上傳端點同時限制檔案內容為 10 MB、multipart request 為 10 MB + 64 KB bounded overhead，並套用 `media-upload` named fixed-window limiter；分區鍵優先使用 `NameIdentifier`，匿名才退回來源 IP。
- JSON enum converter 禁止整數 token；multipart 與 visibility body 另外只接受 enum 名稱並檢查已定義值，避免數字別名繞過字串 API 契約。
- 內容成功回應加入 `X-Content-Type-Options: nosniff`；`Public` 使用 `public, max-age=0, must-revalidate`，`FamilyOnly` 與所有錯誤使用 `private, no-store`。MVC executor 擁有成功 stream 並於寫出後釋放；fail-closed 分支主動釋放 stream。

### 儲存供應商與持久化

- 未加入 Google storage SDK 或實作。Infrastructure 以 `TryAddScoped` 條件註冊 `UnavailableMediaStorage`；未設定正式 provider 時應用仍可啟動，但所有儲存操作均以供應商中立訊息 fail closed，已存在的正式 provider 不會被 fallback 覆寫。
- 新增可見性更新 repository/service 路徑；MongoDB 使用 active-record filter 原子更新 enum 與 `UpdatedAt = DateTime.UtcNow`，並回傳更新後安全 DTO。

### 測試與限制

- Controller contract tests 覆蓋 AdminOnly／AllowAnonymous metadata、upload rate policy、10 MB 邊界、NameIdentifier、字串 enum、防洩漏錯誤與 public/private headers。
- 純 in-process `IAuthorizationService`／`IPolicyEvaluator` 測試驗證匿名 challenge、一般使用者 forbid、Admin success；`FileStreamResultExecutor` 測試驗證實際 response body 與 executor 完成後 stream dispose，未啟動 TestServer。
- fail-closed adapter 測試涵蓋預設 DI 可解析、三種操作均拒絕，以及既有 provider 不被 fallback 覆寫。
- `dotnet build backend/FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj --maxcpucount:1 --no-restore`：成功，0 warnings / 0 errors。
- focused media/API/storage suite：95/95 passed。
- 排除當前環境已知會卡住的 `ApiSecurityIntegrationTests` 後，其餘完整後端 suite：305/305 passed。曾建立的獨立 TestServer 測試在第一個 request 超過 29 秒後，於總計約 42 秒內中止；已改為上述 bounded in-process pipeline/executor 測試，未留下會卡住的新測試。
- 本批次未修改 frontend lockfile 或 workspace 設定。

---

## Task 3 review hardening 報告（2026-07-26）

### 身分與 CSRF cache

- Frontend 新增 `invalidateCsrfToken()` generation boundary；login/logout 成功、logout 錯誤、refresh 成功與 refresh 失敗清除 claims-bound token 與 deduplicated request。舊的 in-flight CSRF request 只能完成原等待者，不得覆寫新世代 cache。
- Refresh 成功後先 invalidate，再讓 Axios replay 原 mutation，因此 request interceptor 會取得目前身分的新 token；跨分頁 `/api/auth/me` 確認 session 已由其他分頁更新時也會 invalidate。
- Direct Vitest 覆蓋 anonymous token → login → fresh token、refresh → replay fresh token、logout success/error，以及 stale in-flight request race。

### Request limits、rate limiting 與撤銷安全

- Kestrel 與全域 `FormOptions.MultipartBodyLengthLimit` 均恢復為 1 MiB；只有 media upload action 透過 `RequestSizeLimit` 與 `RequestFormLimits` 擴張到 10 MiB + 64 KiB overhead。
- 100/min user-ID/IP limiter 改為 `RateLimiterOptions.GlobalLimiter`，讓 endpoint 的 `media-upload` 10/min policy 疊加而不被 controller convention 覆寫。Global limiter 只涵蓋 `/api` 與 `/hubs`；health 等非 API 路徑使用 no-limiter，維持 liveness 可用性。
- Login 與 refresh 保留各自 named limiter。Public media 改為 `public, max-age=0, must-revalidate`，在尚無 purge 或 versioned URL 前避免撤銷後仍被五分鐘 fresh cache 直接使用。
- API 安全註冊抽至可重用 extension；純 DI/options tests 驗證 fallback/Admin policy、全域 MVC antiforgery filter、1 MiB multipart default、真實 antiforgery identity binding、100/min user partition 與 health unlimited。Action reflection 加 production source contract 驗證 named policies、media body metadata，且 `MapControllers` 不再附加 fixed policy；未建立 WebApplication 或 TestServer。

### Fresh verification

- `dotnet build backend/FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj --maxcpucount:1 --no-restore`：成功，0 warnings / 0 errors。
- Backend focused security/media suite：20/20 passed。
- 排除當前環境已知會卡住的 `ApiSecurityIntegrationTests` 後，其餘 backend suite：311/311 passed。
- Frontend focused CSRF suite：5/5 passed；完整 Vitest：76/76 passed（19 files）；focused 與完整 ESLint 均 exit 0。
- 未修改 frontend lockfile 或 workspace 設定。

---

## Task 3 CSRF refresh rotation 修正（2026-07-26）

- Access JWT 過期後 refresh request 會以匿名身分通過 authentication，因此 `refreshSession()` 現在先 invalidate 舊 authenticated CSRF cache，再取得目前匿名身分 token 送出 refresh；refresh 成功後再次 invalidate，讓 replay mutation 取得新 authenticated token。
- Generation guard 持續隔離 identity change 前的 in-flight CSRF request，舊 request 不得回填任何新世代 cache。
- `/api/auth/csrf` 明確回傳 `Cache-Control: private, no-store` 與 `X-Content-Type-Options: nosniff`，避免身分綁定 token 被瀏覽器或 shared cache 保存。
- 真實 `IAntiforgery` 回歸驗證 authenticated-issued token 在匿名 refresh context 失敗，重新以匿名身分簽發的 token 成功；controller unit 驗證 token response 的不可快取與 nosniff headers。
- Frontend direct Vitest 驗證 mutation 的 pre-refresh token、refresh 的 anonymous token、replay 的 post-refresh token 三段輪替，既有 login/logout 與 stale in-flight race 測試均維持通過。
- Fresh verification：backend build 0 warnings / 0 errors；focused 22/22；排除既知 `ApiSecurityIntegrationTests` 後 313/313；frontend focused 5/5、完整 76/76，focused/full ESLint 與 `tsc --noEmit` 均 exit 0。
