using System.Net;
using System.Net.Http.Json;
using FamilyPortal.Application.DTOs.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FamilyPortal.Security.Tests;

public sealed class ApiSecurityIntegrationTests : IClassFixture<ApiSecurityIntegrationTests.Factory>
{
    private readonly HttpClient _client;

    public ApiSecurityIntegrationTests(Factory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    [Fact]
    public async Task LiveHealthCheck_IsAnonymousAndDoesNotExposeDependencies()
    {
        var response = await _client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutCookie_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

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
