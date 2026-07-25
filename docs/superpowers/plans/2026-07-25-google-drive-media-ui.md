# Google Drive Media UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 以私人 Google Drive 實作媒體儲存，並在成員、拜拜事件與行程提供安全圖片上傳、公開性與顯示 UI。

**Architecture:** `GoogleDriveMediaStorage` 是 `IMediaStorage` 的唯一 Google-specific adapter。前端透過 React Query media hooks 與入口網 API 操作；正式模式讀 API，GitHub Pages Demo 只讀本地虛構媒體。

**Tech Stack:** Google Drive API v3、.NET 10、Next.js 16、React Query 5、React Hook Form 7、Zod 4、Vitest、Playwright。

## Global Constraints

- OAuth secret、refresh token、folder ID 只放 Secret／環境變數。
- Drive 資料夾保持私人，不回傳 Drive File ID 或分享網址。
- 成員頭像預設私人；拜拜與行程每張可選 `Public`／`FamilyOnly` 且預設私人。
- GitHub Pages 不呼叫 Drive 或正式 API。
- 圖片失敗不能阻止文字資料使用，必須顯示替代狀態。

---

## File Map

- `backend/FamilyPortal.Infrastructure/Media/GoogleDriveOptions.cs`：具名設定與啟動驗證。
- `backend/FamilyPortal.Infrastructure/Media/GoogleDriveMediaStorage.cs`：Drive adapter。
- `frontend/src/types/media.ts`：公開 media contract。
- `frontend/src/hooks/useMedia.ts`：React Query operations。
- `frontend/src/components/features/media/MediaUploader.tsx`：上傳與公開性。
- `frontend/src/components/features/media/MediaGallery.tsx`：相簿與 fallback。
- `frontend/src/components/features/media/ProtectedMediaImage.tsx`：認證圖片載入與 object URL 清理。

### Task 1: Google Drive adapter

**Files:**
- Create: `backend/FamilyPortal.Infrastructure/Media/GoogleDriveOptions.cs`
- Create: `backend/FamilyPortal.Infrastructure/Media/GoogleDriveMediaStorage.cs`
- Modify: `backend/FamilyPortal.Infrastructure/FamilyPortal.Infrastructure.csproj`
- Modify: `backend/FamilyPortal.Infrastructure/DependencyInjection.cs`
- Modify: `backend/FamilyPortal.Api/Configuration/ProductionSecurityOptions.cs`
- Modify: `.env.example`
- Test: `backend/FamilyPortal.Security.Tests/ProductionConfigurationTests.cs`
- Test: `backend/FamilyPortal.Security.Tests/ArchitectureDependencyTests.cs`

**Interfaces:**
- Consumes: `IMediaStorage` from media-core plan.
- Produces: Google Drive implementation using folder-scoped upload/download/delete.

- [ ] **Step 1: Write failing configuration and dependency tests**

Assert production startup rejects missing Google credentials when media is enabled, accepts explicitly disabled media, and Application/Domain project references contain no Google package.

- [ ] **Step 2: Run tests and confirm RED**

Run: `dotnet test backend/FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj --filter "ProductionConfiguration|ArchitectureDependency"`  
Expected: FAIL because Drive settings and adapter do not exist.

- [ ] **Step 3: Add Google.Apis.Drive.v3 1.75.0.4210 and adapter**

Add `<PackageReference Include="Google.Apis.Drive.v3" Version="1.75.0.4210" />` only to `FamilyPortal.Infrastructure.csproj`.

```csharp
public sealed class GoogleDriveOptions {
    public bool Enabled { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public string FolderId { get; set; } = string.Empty;
}
```

Use resumable upload, request only required Drive scope, place every file under `FolderId`, stream downloads, delete by object ID, apply cancellation tokens, and translate Google exceptions to storage exceptions handled by Application.

- [ ] **Step 4: Run dependency and supply-chain checks**

Run: `dotnet test backend/FamilyPortal.slnx`  
Run: `dotnet list backend/FamilyPortal.slnx package --vulnerable --include-transitive`  
Expected: tests pass and no High/Critical vulnerable package is reported.

- [ ] **Step 5: Commit**

```bash
git add backend .env.example
git commit -m "feat: store private media in google drive"
```

### Task 2: Frontend media contracts and uploader

**Files:**
- Create: `frontend/src/types/media.ts`
- Create: `frontend/src/hooks/useMedia.ts`
- Create: `frontend/src/components/features/media/MediaUploader.tsx`
- Create: `frontend/src/components/features/media/__tests__/MediaUploader.test.tsx`
- Modify: `frontend/src/lib/api.ts`

**Interfaces:**
- Produces: `MediaAssetSummary`, `MediaVisibility`, `MediaPurpose`.
- Produces hooks: `useUploadMedia`, `useUpdateMediaVisibility`, `useDeleteMedia`.

- [ ] **Step 1: Write failing uploader tests**

Cover JPEG/PNG/WebP accept filter, 10 MB rejection, preview URL creation/revocation, default `FamilyOnly`, clear public warning, upload progress, retryable error and Demo read-only rejection.

- [ ] **Step 2: Run tests and confirm RED**

Run: `cd frontend && pnpm test -- MediaUploader`  
Expected: FAIL because uploader and hooks are missing.

- [ ] **Step 3: Implement multipart mutation and accessible UI**

```ts
export interface MediaAssetSummary {
  id: string
  contentUrl: string
  contentType: 'image/jpeg' | 'image/png' | 'image/webp'
  width: number
  height: number
  visibility: 'Public' | 'FamilyOnly'
  purpose: 'MemberAvatar' | 'Worship' | 'Itinerary'
}
```

Use Axios multipart progress through the shared client, Zod validation, labelled file input, keyboard-operable remove action and `URL.revokeObjectURL` cleanup.

- [ ] **Step 4: Run frontend tests and lint**

Run: `cd frontend && pnpm test && pnpm lint`  
Expected: all tests and lint pass.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/types frontend/src/hooks frontend/src/components/features/media frontend/src/lib/api.ts
git commit -m "feat: add secure media upload controls"
```

### Task 3: Member avatar, worship and itinerary galleries

**Files:**
- Create: `frontend/src/components/features/media/ProtectedMediaImage.tsx`
- Create: `frontend/src/components/features/media/MediaGallery.tsx`
- Modify: `frontend/src/components/features/members/MemberDialog.tsx`
- Modify: `frontend/src/app/(portal)/members/page.tsx`
- Modify: `frontend/src/components/features/calendar/EventDialog.tsx`
- Modify: `frontend/src/app/(portal)/calendar/page.tsx`
- Modify: `frontend/src/components/features/itineraries/ItineraryDialog.tsx`
- Modify: `frontend/src/app/(portal)/itineraries/page.tsx`
- Modify: `frontend/src/lib/demo/fixtures.ts`
- Test: `frontend/src/components/features/__tests__/FormsAccessibility.test.tsx`
- Test: `frontend/src/components/features/media/__tests__/MediaGallery.test.tsx`

**Interfaces:**
- Consumes: media hooks and `MediaAssetSummary`.
- Produces: avatar assignment, owner galleries, visibility controls and fallback rendering.

- [ ] **Step 1: Write failing integration tests**

Assert avatar image replaces fallback when available; private content is fetched with credentials into a revocable object URL; broken images show initials/placeholder; event and itinerary galleries cap at 30; only Admin sees mutation controls; Demo fixtures remain local and readonly.

- [ ] **Step 2: Run tests and confirm RED**

Run: `cd frontend && pnpm test -- MediaGallery FormsAccessibility MemberDialog CalendarPage`  
Expected: FAIL because gallery integration is absent.

- [ ] **Step 3: Integrate focused media components**

Keep upload state inside `features/media`; dialogs pass only `purpose` and `ownerEntityId`. Use `<AvatarImage>` for members, responsive thumbnail grids for event/itinerary, lazy loading, explicit alt text, and fallback cards. Do not add raw Drive URLs to entity payloads.

- [ ] **Step 4: Run full frontend and static Demo verification**

Run: `cd frontend && pnpm test && pnpm lint && ./node_modules/.bin/next build && pnpm build:pages && pnpm test:pages`  
Expected: formal build retains Proxy/auth; Pages build is static and contains no Google credential or Drive ID marker.

- [ ] **Step 5: Commit**

```bash
git add frontend
git commit -m "feat: show media across family records"
```

### Task 4: Deployment documentation and end-to-end acceptance

**Files:**
- Modify: `docs/02-系統與架構全覽.md`
- Modify: `docs/03-安全風險與修復台帳.md`
- Modify: `docs/04-部署與維運手冊.md`
- Modify: `docs/05-企業級成熟度與後續路線圖.md`
- Modify: `docs/07-Skills全面自檢報告.md`
- Modify: `README.md`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: all prior tasks.
- Produces: reproducible OAuth setup, rotation, backup, cleanup and acceptance documentation.

- [ ] **Step 1: Add CI gates and deploy-safe examples**

Document one-time owner OAuth authorization, exact environment variable names, least privilege, token rotation, Drive folder backup, orphan cleanup, public visibility warning and provider migration. CI uses fake storage and never requires production credentials.

- [ ] **Step 2: Run complete automated verification**

Run: `dotnet test backend/FamilyPortal.slnx`  
Run: `dotnet list backend/FamilyPortal.slnx package --vulnerable --include-transitive`  
Run: `cd frontend && pnpm test && pnpm lint && ./node_modules/.bin/next build && pnpm build:pages && pnpm test:pages && pnpm audit --prod --audit-level high`  
Expected: all commands exit 0 with no High/Critical finding.

- [ ] **Step 3: Run browser acceptance**

Verify desktop and 390px mobile layouts; all three themes; create a lunar worship rule and inspect its 2036 occurrence; upload private/public samples; confirm anonymous private request is 401 and public request is 200; simulate Drive failure and confirm text remains usable; confirm no console error or horizontal overflow.

- [ ] **Step 4: Synchronize Obsidian and inspect parity**

Run the established `rsync -nrc` dry run, synchronize the repository `docs/` directory to the existing `HsuFamily-Obsidian/許氏入口網/` vault, then repeat `rsync -nrc` and expect no differences.

- [ ] **Step 5: Commit documentation and CI**

```bash
git add README.md docs .github/workflows/ci.yml
git commit -m "docs: operate family media and lunar recurrence"
```

- [ ] **Step 6: Request final review and publish**

Run `git diff --check`, request code review, push `codex/family-media-lunar-theme`, create a PR, wait for all GitHub checks, merge only after green, and verify the public Demo remains HTTP 200.
