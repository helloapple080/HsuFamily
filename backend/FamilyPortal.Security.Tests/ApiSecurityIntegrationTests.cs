using System.Net;
using System.Net.Http.Json;
using FamilyPortal.Application.DTOs.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
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
                    ["MongoDbSettings:ConnectionString"] = "mongodb://127.0.0.1:27017",
                    ["MongoDbSettings:DatabaseName"] = "FamilyPortal_Test"
                });
            });
        }
    }
}
