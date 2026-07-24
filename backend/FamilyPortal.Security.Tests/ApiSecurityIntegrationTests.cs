using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using FamilyPortal.Application.DTOs.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace FamilyPortal.Security.Tests;

/// <summary>驗證 API request pipeline 的安全邊界。</summary>
public sealed class ApiSecurityIntegrationTests : IClassFixture<ApiSecurityIntegrationTests.Factory>
{
    private readonly HttpClient _client;

    /// <summary>建立 API 安全整合測試。</summary>
    /// <param name="factory">Testing 環境的 API 測試主機。</param>
    public ApiSecurityIntegrationTests(Factory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    /// <summary>存活檢查應允許匿名存取且不探測外部相依服務。</summary>
    [Fact]
    public async Task LiveHealthCheck_IsAnonymousAndDoesNotExposeDependencies()
    {
        var response = await _client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>未帶登入 Cookie 時，受保護端點應回傳未授權。</summary>
    [Fact]
    public async Task ProtectedEndpoint_WithoutCookie_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>匿名寫入端點未帶 CSRF Token 時應拒絕請求。</summary>
    [Fact]
    public async Task UnsafeAnonymousEndpoint_WithoutCsrfToken_IsRejected()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Username = "someone",
            Password = "ValidPassword!123"
        }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>API 回應應送出現代安全標頭，且不得送出淘汰的 XSS 標頭或頁面 CSP。</summary>
    [Fact]
    public async Task ResponseHeaders_UseHardenedApiPolicy()
    {
        var response = await _client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.False(response.Headers.Contains("X-XSS-Protection"));
        Assert.False(response.Headers.Contains("Content-Security-Policy"));
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("camera=(), microphone=(), geolocation=()", response.Headers.GetValues("Permissions-Policy").Single());
        Assert.Equal("same-origin", response.Headers.GetValues("Cross-Origin-Opener-Policy").Single());
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    /// <summary>Production 的 HTTPS 回應應送出 HSTS。</summary>
    [Fact]
    public async Task ProductionHttpsResponse_IncludesHsts()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production
        });
        builder.Configuration["AllowedHosts"] = "family.example";
        builder.WebHost.UseTestServer();

        await using var app = builder.Build();
        app.UseFamilyPortalSecurityHeaders();
        app.MapGet("/", () => Results.Ok());
        await app.StartAsync(TestContext.Current.CancellationToken);

        using var client = app.GetTestClient();
        client.BaseAddress = new Uri("https://family.example");
        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Strict-Transport-Security"));
    }

    /// <summary>Development 與 Testing 回應不應送出 HSTS。</summary>
    /// <param name="environmentName">要驗證的非正式環境名稱。</param>
    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public async Task NonProductionHttpsResponse_DoesNotIncludeHsts(string environmentName)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environmentName
        });
        builder.Configuration["AllowedHosts"] = "family.example";
        builder.WebHost.UseTestServer();

        await using var app = builder.Build();
        app.UseFamilyPortalSecurityHeaders();
        app.MapGet("/", () => Results.Ok());
        await app.StartAsync(TestContext.Current.CancellationToken);

        using var client = app.GetTestClient();
        client.BaseAddress = new Uri("https://family.example");
        var response = await client.GetAsync("/", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    /// <summary>同一來源 IP 的不同已驗證使用者應使用彼此獨立的速率限制配額。</summary>
    [Fact]
    public async Task AuthenticatedUsersBehindSameIp_HaveIndependentRateLimitPartitions()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });
        builder.WebHost.UseTestServer();
        builder.Services
            .AddAuthentication(HeaderUserAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, HeaderUserAuthenticationHandler>(
                HeaderUserAuthenticationHandler.SchemeName,
                _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("probe", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    FamilyPortalRateLimitPartition.GetPartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 1,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });

        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseRateLimiter();
        app.UseAuthorization();
        app.MapGet("/rate-limit-probe", () => Results.Ok())
            .RequireAuthorization()
            .RequireRateLimiting("probe");
        await app.StartAsync(TestContext.Current.CancellationToken);

        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add(HeaderUserAuthenticationHandler.UserHeaderName, "user-1");
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync("/rate-limit-probe", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            (await client.GetAsync("/rate-limit-probe", TestContext.Current.CancellationToken)).StatusCode);

        client.DefaultRequestHeaders.Remove(HeaderUserAuthenticationHandler.UserHeaderName);
        client.DefaultRequestHeaders.Add(HeaderUserAuthenticationHandler.UserHeaderName, "user-2");
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync("/rate-limit-probe", TestContext.Current.CancellationToken)).StatusCode);
    }

    /// <summary>建立 Testing 環境的完整 API 測試主機。</summary>
    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["JwtSettings:SecretKey"] = "Integration-Test-Secret-Key-Must-Be-At-Least-32-Characters!",
                    ["SecuritySettings:CookieSecure"] = "false",
                    ["MongoDbSettings:ConnectionString"] = "mongodb://127.0.0.1:27017/?serverSelectionTimeoutMS=200&connectTimeoutMS=200",
                    ["MongoDbSettings:DatabaseName"] = "FamilyPortal_Test"
                });
            });
        }
    }

    private sealed class HeaderUserAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "HeaderUser";
        public const string UserHeaderName = "X-Test-User";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(UserHeaderName, out var value) ||
                string.IsNullOrWhiteSpace(value))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, value.ToString())],
                SchemeName);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }
}
