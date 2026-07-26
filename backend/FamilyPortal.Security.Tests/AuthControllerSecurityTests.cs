using FamilyPortal.Api.Configuration;
using FamilyPortal.Api.Controllers;
using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs.Auth;
using FamilyPortal.Application.Interfaces;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FamilyPortal.Security.Tests;

/// <summary>驗證認證 controller 的敏感回應標頭。</summary>
public sealed class AuthControllerSecurityTests
{
    /// <summary>CSRF token 回應包含敏感、身分綁定資料，必須禁止快取與 MIME sniffing。</summary>
    [Fact]
    public async Task GetCsrfToken_SetsPrivateNoStoreAndNoSniff()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddFamilyPortalMvcSecurity(secureCookies: false);
        await using var provider = services.BuildServiceProvider();
        var controller = new AuthController(
            new FakeAuthService(),
            NullLogger<AuthController>.Instance,
            provider.GetRequiredService<IAntiforgery>(),
            new ConfigurationBuilder().Build())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = provider }
            }
        };

        var result = controller.GetCsrfToken();

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl);
        Assert.Equal("nosniff", controller.Response.Headers.XContentTypeOptions);
    }

    private sealed class FakeAuthService : IAuthService
    {
        /// <inheritdoc />
        public Task<ServiceResult<AuthSession>> LoginAsync(
            string identifier,
            string password,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<ServiceResult<AuthSession>> RefreshAsync(
            string rawRefreshToken,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<ServiceResult<bool>> LogoutAsync(
            string? userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<ServiceResult<UserInfo>> GetUserAsync(
            string userId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<ServiceResult<UserInfo>> RegisterAsync(
            RegisterRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
