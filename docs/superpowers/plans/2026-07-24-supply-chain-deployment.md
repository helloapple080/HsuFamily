# Supply Chain and Deployment Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 消除直接依賴 High 弱點，並建立 Production 強制 HTTPS、Secure Cookie、內網 API 與持續安全掃描的部署基線。

**Architecture:** 保留現有 localhost 開發流程，將 Production 移到獨立 compose 與 Caddy TLS edge。應用程式在 Production 組態不安全時 fail fast，CI 以依賴、NuGet、CodeQL、Trivy 與 SBOM gates 阻止回歸。

**Tech Stack:** Next.js 16.2.11、Axios 1.18.x、.NET SDK 10.0.302／ASP.NET Runtime 10.0.10、Docker Compose、Caddy、GitHub Actions CodeQL v4、Trivy Action 0.36.0。

## Global Constraints

- 不執行 `drop database`、清空 collection、刪除 Docker volume 或資料庫重建。
- 不改變現有 API 路徑、`ApiResponse<T>` envelope 或核心欄位。
- Next.js 與 `eslint-config-next` 固定為 16.2.11；Axios 使用 1.18 系列安全版本。
- .NET SDK 固定 10.0.302；ASP.NET runtime 與 Microsoft packages 對齊 10.0.10。
- Production 的 `SecuritySettings__CookieSecure` 必須為 `true`，公開 origin 必須為 HTTPS。
- 不提交真實密碼、JWT Secret、TLS private key 或正式環境連線字串。
- 每個行為修正先建立會失敗的測試或安全檢核，再修改 production 設定。

---

## File Map

- `frontend/package.json`／`frontend/pnpm-lock.yaml`：安全依賴版本與 audit scripts。
- `backend/global.json`／三個 backend `.csproj`／`backend/Dockerfile`：.NET patch 對齊。
- `docker-compose.yml`：明確的 localhost Development compose。
- `docker-compose.production.yml`：只由 TLS edge 對外的 Production stack。
- `deploy/Caddyfile`：TLS termination、request limits 與 proxy routes。
- `.env.example`：不含 secret 的必要設定契約。
- `backend/FamilyPortal.Api/Configuration/ProductionSecurityOptions.cs`：Production 啟動驗證。
- `backend/FamilyPortal.Security.Tests/ProductionConfigurationTests.cs`：fail-fast 與 header 測試。
- `frontend/src/proxy.ts`：每個動態 request 的 CSP nonce 與 request／response headers。
- `frontend/src/app/layout.tsx`：以 `connection()` 明確採用 nonce 所需的動態 rendering。
- `.github/workflows/ci.yml`：build、test、audit 與 SBOM。
- `.github/workflows/codeql.yml`：C#／JavaScript-TypeScript 靜態分析。
- `.github/dependabot.yml`：每週 NuGet、pnpm、Actions、Docker 更新。

### Task 1: Patch Frontend Direct Dependencies

**Files:**
- Modify: `frontend/package.json`
- Modify: `frontend/pnpm-lock.yaml`

**Interfaces:**
- Consumes: 既有 Next.js App Router 與 Axios API client。
- Produces: `pnpm audit:prod` 與不含已知 High 直接依賴的 frozen lockfile。

- [ ] **Step 1: Record the failing security gate**

Run:

```bash
cd frontend
pnpm audit --prod --audit-level high
```

Expected: non-zero exit，報告包含 `next@16.2.2` 與 `axios@1.14.0` 的 High advisories。

- [ ] **Step 2: Add the exact audit script and patched dependencies**

Apply these `package.json` values:

```json
{
  "scripts": {
    "dev": "next dev",
    "build": "next build",
    "start": "next start",
    "lint": "eslint",
    "audit:prod": "pnpm audit --prod --audit-level high"
  },
  "dependencies": {
    "axios": "^1.18.0",
    "next": "16.2.11"
  },
  "devDependencies": {
    "eslint-config-next": "16.2.11"
  }
}
```

Run:

```bash
cd frontend
pnpm install --lockfile-only
pnpm install --frozen-lockfile
```

Expected: lockfile resolves `next@16.2.11` and Axios 1.18.x without modifying unrelated major versions.

- [ ] **Step 3: Verify audit and compatibility**

Run:

```bash
cd frontend
pnpm audit:prod
pnpm lint
pnpm build
```

Expected: all commands exit 0; security output contains no High direct dependency finding.

- [ ] **Step 4: Commit**

```bash
git add frontend/package.json frontend/pnpm-lock.yaml
git commit -m "chore: patch frontend security dependencies"
```

### Task 2: Align the .NET Security Patch

**Files:**
- Modify: `backend/global.json`
- Modify: `backend/Dockerfile`
- Modify: `backend/FamilyPortal.Api/FamilyPortal.Api.csproj`
- Modify: `backend/FamilyPortal.Infrastructure/FamilyPortal.Infrastructure.csproj`
- Modify: `backend/FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj`

**Interfaces:**
- Consumes: .NET 10 solution and current package references.
- Produces: SDK 10.0.302 build using ASP.NET Runtime 10.0.10 and aligned Microsoft 10.0.10 packages.

- [ ] **Step 1: Verify the stale runtime is detectable**

Run:

```bash
rg -n '10\.0\.9|10\.0\.5' backend/Dockerfile backend --glob '*.csproj'
```

Expected: matches current runtime image and Microsoft package patch versions.

- [ ] **Step 2: Update exact runtime and package versions**

Use these image lines:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS build
FROM mcr.microsoft.com/dotnet/aspnet:10.0.10 AS runtime
```

For Microsoft packages currently on `10.0.5`, set:

```xml
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.10" />
<PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.10" />
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.10" />
```

Keep `backend/global.json` at:

```json
{
  "sdk": {
    "version": "10.0.302",
    "rollForward": "latestPatch"
  }
}
```

- [ ] **Step 3: Verify restore, build, tests and vulnerability report**

Run:

```bash
cd backend
dotnet restore FamilyPortal.slnx
dotnet build FamilyPortal.slnx --no-restore --configuration Release
dotnet test FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj --no-build --configuration Release
dotnet list FamilyPortal.slnx package --vulnerable --include-transitive
```

Expected: build and tests pass; vulnerability report says no vulnerable packages.

- [ ] **Step 4: Commit**

```bash
git add backend/global.json backend/Dockerfile backend/FamilyPortal.Api/FamilyPortal.Api.csproj backend/FamilyPortal.Infrastructure/FamilyPortal.Infrastructure.csproj backend/FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj
git commit -m "chore: align dotnet security patch"
```

### Task 3: Enforce Production Security Configuration

**Files:**
- Create: `backend/FamilyPortal.Api/Configuration/ProductionSecurityOptions.cs`
- Create: `backend/FamilyPortal.Security.Tests/ProductionConfigurationTests.cs`
- Modify: `backend/FamilyPortal.Api/Program.cs`
- Modify: `backend/FamilyPortal.Api/appsettings.json`

**Interfaces:**
- Produces: `ProductionSecurityOptions.Validate(WebApplicationBuilder builder)`; throws `InvalidOperationException` for insecure Production config.

- [ ] **Step 1: Write failing configuration tests**

Create tests with these cases:

```csharp
[Theory]
[InlineData(false, "https://family.example")]
[InlineData(true, "http://family.example")]
[InlineData(true, "")]
public void ValidateProduction_RejectsInsecureSettings(bool secureCookies, string origin)
{
    var values = new Dictionary<string, string?>
    {
        ["SecuritySettings:CookieSecure"] = secureCookies.ToString(),
        ["AllowedOrigins:0"] = origin
    };

    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(values)
        .Build();

    Assert.Throws<InvalidOperationException>(() =>
        ProductionSecurityOptions.Validate(configuration, Environments.Production));
}

[Fact]
public void ValidateProduction_AcceptsSecureHttpsOrigin()
{
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecuritySettings:CookieSecure"] = "true",
            ["AllowedOrigins:0"] = "https://family.example"
        })
        .Build();

    ProductionSecurityOptions.Validate(configuration, Environments.Production);
}
```

- [ ] **Step 2: Run the targeted test and observe RED**

Run:

```bash
cd backend
dotnet test FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj --filter ProductionConfigurationTests
```

Expected: compile failure because `ProductionSecurityOptions` does not exist.

- [ ] **Step 3: Implement the production validator**

Create this public API with XML documentation:

```csharp
namespace FamilyPortal.Api.Configuration;

/// <summary>驗證正式環境必要安全設定。</summary>
public static class ProductionSecurityOptions
{
    /// <summary>正式環境若使用不安全 Cookie 或非 HTTPS origin 時拒絕啟動。</summary>
    public static void Validate(IConfiguration configuration, string environmentName)
    {
        if (!string.Equals(environmentName, Environments.Production, StringComparison.Ordinal)) return;

        if (!configuration.GetValue("SecuritySettings:CookieSecure", true))
            throw new InvalidOperationException("Production 必須啟用 Secure Cookie。");

        var origins = configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length == 0 || origins.Any(origin =>
                !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("Production AllowedOrigins 必須全部使用 HTTPS。");
    }
}
```

Call it immediately after `WebApplication.CreateBuilder(args)`:

```csharp
ProductionSecurityOptions.Validate(builder.Configuration, builder.Environment.EnvironmentName);
```

Set safe defaults in `appsettings.json`:

```json
{
  "SecuritySettings": { "CookieSecure": true },
  "AllowedOrigins": []
}
```

- [ ] **Step 4: Run tests and commit**

Run:

```bash
cd backend
dotnet test FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj --filter ProductionConfigurationTests
```

Expected: all targeted tests pass.

```bash
git add backend/FamilyPortal.Api/Configuration/ProductionSecurityOptions.cs backend/FamilyPortal.Api/Program.cs backend/FamilyPortal.Api/appsettings.json backend/FamilyPortal.Security.Tests/ProductionConfigurationTests.cs
git commit -m "fix: reject insecure production configuration"
```

### Task 4: Split Development and Production Compose

**Files:**
- Modify: `docker-compose.yml`
- Create: `docker-compose.production.yml`
- Create: `deploy/Caddyfile`
- Create: `.env.example`
- Modify: `frontend/src/lib/api.ts`
- Modify: `frontend/src/hooks/useSignalR.ts`

**Interfaces:**
- Produces: localhost development via `docker compose up`; Production via `docker compose -f docker-compose.production.yml up -d` with only 80/443 published.

- [ ] **Step 1: Add a failing static deployment check**

Run before editing:

```bash
rg -n 'ASPNETCORE_ENVIRONMENT: Production|COOKIE_SECURE:-false|5001:5001' docker-compose.yml
```

Expected: all three insecure default patterns are found.

- [ ] **Step 2: Make the default compose explicitly Development**

Use these backend values in `docker-compose.yml`:

```yaml
environment:
  ASPNETCORE_ENVIRONMENT: Development
  ASPNETCORE_URLS: http://+:5001
  SecuritySettings__CookieSecure: "false"
  AllowedOrigins__0: http://localhost:3000
ports:
  - "127.0.0.1:5001:5001"
```

Keep MongoDB volumes and names unchanged. Bind frontend to `127.0.0.1:3000:3000`.

- [ ] **Step 3: Add the Production TLS edge**

Create a Production compose in which only `caddy` publishes ports:

```yaml
services:
  backend:
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ASPNETCORE_URLS: http://+:5001
      SecuritySettings__CookieSecure: "true"
      AllowedOrigins__0: https://${PORTAL_DOMAIN:?PORTAL_DOMAIN is required}
      ProxySettings__KnownProxies__0: ${CADDY_PROXY_IP:?CADDY_PROXY_IP is required}
    expose: ["5001"]
  frontend:
    build:
      args:
        NEXT_PUBLIC_API_URL: https://${API_DOMAIN:?API_DOMAIN is required}
    expose: ["3000"]
  caddy:
    image: caddy:2.10.2-alpine
    ports: ["80:80", "443:443", "443:443/udp"]
    volumes:
      - ./deploy/Caddyfile:/etc/caddy/Caddyfile:ro
      - caddy_data:/data
      - caddy_config:/config
```

Create `deploy/Caddyfile`:

```caddyfile
{$PORTAL_DOMAIN} {
    encode zstd gzip
    reverse_proxy frontend:3000
}

{$API_DOMAIN} {
    request_body {
        max_size 1MB
    }
    reverse_proxy backend:5001
}
```

Create `.env.example` with names only and obvious non-secret examples:

```dotenv
PORTAL_DOMAIN=portal.example.com
API_DOMAIN=api.example.com
CADDY_PROXY_IP=172.20.0.10
MONGO_ROOT_PASSWORD=replace-with-secret-manager-value
MONGO_APP_PASSWORD=replace-with-secret-manager-value
JWT_SECRET_KEY=replace-with-at-least-32-random-characters
ADMIN_PASSWORD=replace-with-one-time-bootstrap-password
```

- [ ] **Step 4: Use a same-origin-safe browser default**

Replace the hardcoded public fallback in both frontend files with:

```ts
export const apiBaseUrl = process.env.NEXT_PUBLIC_API_URL ?? ''
```

SignalR uses:

```ts
.withUrl(`${apiBaseUrl}/hubs/calendar`, { withCredentials: true })
```

Development compose continues passing `http://localhost:5001` explicitly.

- [ ] **Step 5: Verify rendered configuration**

Run:

```bash
docker compose config
docker compose --env-file .env.example -f docker-compose.production.yml config
rg -n 'ports:|5001:5001|CookieSecure.*false' docker-compose.production.yml
```

Expected: both configs render; Production does not publish backend 5001 and contains no false Secure Cookie.

- [ ] **Step 6: Commit**

```bash
git add docker-compose.yml docker-compose.production.yml deploy/Caddyfile .env.example frontend/src/lib/api.ts frontend/src/hooks/useSignalR.ts
git commit -m "fix: secure production deployment defaults"
```

### Task 5: Add HTTP Security Headers and HSTS

**Files:**
- Modify: `backend/FamilyPortal.Api/Program.cs`
- Modify: `frontend/next.config.ts`
- Create: `frontend/src/proxy.ts`
- Modify: `frontend/src/app/layout.tsx`
- Modify: `backend/FamilyPortal.Security.Tests/ApiSecurityIntegrationTests.cs`

**Interfaces:**
- Produces: HSTS in Production, no obsolete XSS header, no Next.js fingerprint, nonce-compatible CSP rollout.

- [ ] **Step 1: Add failing response-header assertions**

Add assertions to the integration test:

```csharp
Assert.False(response.Headers.Contains("X-XSS-Protection"));
Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
```

Add a production environment test asserting `Strict-Transport-Security` exists on HTTPS responses.

- [ ] **Step 2: Run RED**

Run:

```bash
cd backend
dotnet test FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj --filter ApiSecurityIntegrationTests
```

Expected: test fails because `X-XSS-Protection` is currently present or HSTS is absent.

- [ ] **Step 3: Implement headers**

Configure:

```csharp
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
{
    app.UseHsts();
}
```

The custom middleware sets only:

```csharp
context.Response.Headers["X-Content-Type-Options"] = "nosniff";
context.Response.Headers["X-Frame-Options"] = "DENY";
context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
```

Set the frontend option:

```ts
const nextConfig: NextConfig = {
  poweredByHeader: false,
  output: 'standalone',
}
```

Remove the static CSP from `next.config.ts` and create the Next.js 16 `proxy.ts` implementation recommended by the official CSP guide:

```ts
import { NextRequest, NextResponse } from 'next/server'

export function proxy(request: NextRequest) {
  const nonce = Buffer.from(crypto.randomUUID()).toString('base64')
  const isDev = process.env.NODE_ENV === 'development'
  const apiUrl = process.env.NEXT_PUBLIC_API_URL
  const apiOrigin = apiUrl ? new URL(apiUrl).origin : ''
  const websocketOrigin = apiUrl
    ? `${new URL(apiUrl).protocol === 'https:' ? 'wss:' : 'ws:'}//${new URL(apiUrl).host}`
    : ''
  const csp = `
    default-src 'self';
    script-src 'self' 'nonce-${nonce}' 'strict-dynamic'${isDev ? " 'unsafe-eval'" : ''};
    style-src 'self' 'nonce-${nonce}'${isDev ? " 'unsafe-inline'" : ''};
    connect-src 'self' ${apiOrigin} ${websocketOrigin};
    img-src 'self' blob: data:;
    font-src 'self';
    object-src 'none';
    base-uri 'self';
    form-action 'self';
    frame-ancestors 'none';
    upgrade-insecure-requests;
  `.replace(/\s{2,}/g, ' ').trim()

  const requestHeaders = new Headers(request.headers)
  requestHeaders.set('x-nonce', nonce)
  requestHeaders.set('Content-Security-Policy', csp)
  const response = NextResponse.next({ request: { headers: requestHeaders } })
  response.headers.set('Content-Security-Policy', csp)
  return response
}

export const config = {
  matcher: [{
    source: '/((?!api|_next/static|_next/image|favicon.ico).*)',
    missing: [
      { type: 'header', key: 'next-router-prefetch' },
      { type: 'header', key: 'purpose', value: 'prefetch' },
    ],
  }],
}
```

Because request nonces require dynamic rendering, call `await connection()` in the async root layout before returning the HTML tree:

```tsx
import { connection } from 'next/server'

export default async function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  await connection()
  return <html lang="zh-Hant"><body>{children}</body></html>
}
```

This intentionally trades static optimization for strict per-request CSP; record that performance tradeoff in the deployment manual.

- [ ] **Step 4: Verify and commit**

Run:

```bash
cd backend
dotnet test FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj --filter ApiSecurityIntegrationTests
cd ../frontend
pnpm lint
pnpm build
```

Expected: tests, lint and build pass; built response has no `X-Powered-By`.

```bash
git add backend/FamilyPortal.Api/Program.cs backend/FamilyPortal.Security.Tests/ApiSecurityIntegrationTests.cs frontend/next.config.ts frontend/src/proxy.ts frontend/src/app/layout.tsx
git commit -m "fix: harden http security headers"
```

### Task 6: Add Continuous Supply-Chain Gates

**Files:**
- Modify: `.github/workflows/ci.yml`
- Create: `.github/workflows/codeql.yml`
- Create: `.github/dependabot.yml`

**Interfaces:**
- Produces: PR／push／weekly dependency, CodeQL, filesystem scan and SBOM evidence.

- [ ] **Step 1: Add CI workflow lint check**

Run:

```bash
rg -n 'audit:prod|--vulnerable|trivy-action|sbom' .github/workflows
```

Expected: no complete gate set is found.

- [ ] **Step 2: Extend CI**

Add frontend step:

```yaml
- name: Audit production dependencies
  working-directory: frontend
  run: pnpm audit:prod
```

Add backend step:

```yaml
- name: Audit NuGet dependencies
  working-directory: backend
  run: |
    dotnet list FamilyPortal.slnx package --vulnerable --include-transitive 2>&1 | tee vulnerable-packages.txt
    if grep -qE '^\s*>\s' vulnerable-packages.txt; then exit 1; fi
```

Add safe Trivy 0.36.0 filesystem scanning and SBOM artifact:

```yaml
- name: Scan repository
  uses: aquasecurity/trivy-action@v0.36.0
  with:
    scan-type: fs
    scan-ref: .
    severity: HIGH,CRITICAL
    exit-code: '1'

- name: Generate SBOM
  uses: aquasecurity/trivy-action@v0.36.0
  with:
    scan-type: fs
    scan-ref: .
    format: cyclonedx
    output: sbom.cdx.json
```

Upload `sbom.cdx.json` using `actions/upload-artifact@v4` with 30-day retention.

- [ ] **Step 3: Add CodeQL v4**

Create a workflow with:

```yaml
name: CodeQL
on:
  push:
    branches: [main]
  pull_request:
    branches: [main]
  schedule:
    - cron: '17 18 * * 1'
permissions:
  contents: read
  security-events: write
jobs:
  analyze:
    runs-on: ubuntu-latest
    strategy:
      matrix:
        language: ['csharp', 'javascript-typescript']
    steps:
      - uses: actions/checkout@v5
      - uses: github/codeql-action/init@v4
        with:
          languages: ${{ matrix.language }}
      - uses: github/codeql-action/autobuild@v4
      - uses: github/codeql-action/analyze@v4
```

- [ ] **Step 4: Add Dependabot coverage**

Configure weekly updates for `npm` at `/frontend`, `nuget` at `/backend`, `docker` at `/backend` and `/frontend`, and `github-actions` at `/`; limit open PRs to 5 per ecosystem.

- [ ] **Step 5: Verify YAML and commit**

Run:

```bash
ruby -e 'require "yaml"; Dir[".github/**/*.yml"].each { |f| YAML.load_file(f); puts f }'
```

Expected: every workflow path prints and process exits 0.

```bash
git add .github/workflows/ci.yml .github/workflows/codeql.yml .github/dependabot.yml
git commit -m "ci: add security and supply chain gates"
```

## Plan Acceptance

Run:

```bash
cd frontend && pnpm audit:prod && pnpm lint && pnpm build
cd ../backend && dotnet build FamilyPortal.slnx --configuration Release && dotnet test FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj --configuration Release
docker compose config
docker compose --env-file ../.env.example -f ../docker-compose.production.yml config
```

Expected: all available commands pass; GitHub Actions provides evidence for .NET／Docker checks unavailable locally; no database command is executed.
