using System.Net;
using System.Security.Claims;
using FamilyPortal.Api.Configuration;
using FamilyPortal.Api.Controllers;
using FamilyPortal.Application.Common;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace FamilyPortal.Security.Tests;

/// <summary>驗證可重用 API 安全註冊的實際 options 與 endpoint metadata。</summary>
public sealed class ApiSecurityOptionsTests
{
    /// <summary>授權註冊必須提供 authenticated fallback 與 AdminOnly role policy。</summary>
    [Fact]
    public void AuthorizationOptions_ContainFallbackAndAdminPolicy()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFamilyPortalAuthorization();
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

        Assert.Contains(options.FallbackPolicy!.Requirements, requirement =>
            requirement is DenyAnonymousAuthorizationRequirement);
        var admin = options.GetPolicy("AdminOnly");
        var roles = Assert.Single(admin!.Requirements.OfType<RolesAuthorizationRequirement>());
        Assert.Contains("Admin", roles.AllowedRoles);
    }

    /// <summary>MVC 必須全域套用 antiforgery，multipart 預設仍維持 1 MiB。</summary>
    [Fact]
    public void MvcOptions_ContainAntiforgeryAndOneMiBMultipartDefault()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddFamilyPortalMvcSecurity(secureCookies: false);
        using var provider = services.BuildServiceProvider();

        var mvc = provider.GetRequiredService<IOptions<MvcOptions>>().Value;
        Assert.Contains(mvc.Filters, filter => filter is AutoValidateAntiforgeryTokenAttribute);
        var form = provider.GetRequiredService<IOptions<FormOptions>>().Value;
        Assert.Equal(ValidationLimits.MaxRequestBodyBytes, form.MultipartBodyLengthLimit);
    }

    /// <summary>匿名身分簽發的 request token 登入後必須失效，重新簽發者才可驗證。</summary>
    [Fact]
    public async Task AntiforgeryToken_IsBoundToCurrentIdentity()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddFamilyPortalMvcSecurity(secureCookies: false);
        await using var provider = services.BuildServiceProvider();
        var antiforgery = provider.GetRequiredService<IAntiforgery>();

        var anonymousIssue = Context(provider, new ClaimsPrincipal(new ClaimsIdentity()));
        var anonymousTokens = antiforgery.GetAndStoreTokens(anonymousIssue);
        Assert.NotNull(anonymousTokens.CookieToken);

        var authenticated = Principal("admin-1", "Admin");
        var staleValidation = Context(provider, authenticated);
        SetTokens(staleValidation, anonymousTokens.CookieToken!, anonymousTokens.RequestToken!);
        await Assert.ThrowsAsync<AntiforgeryValidationException>(() =>
            antiforgery.ValidateRequestAsync(staleValidation));

        var freshIssue = Context(provider, authenticated);
        freshIssue.Request.Headers.Cookie = $"family_antiforgery={anonymousTokens.CookieToken}";
        var freshTokens = antiforgery.GetAndStoreTokens(freshIssue);
        var freshValidation = Context(provider, authenticated);
        SetTokens(
            freshValidation,
            freshTokens.CookieToken ?? anonymousTokens.CookieToken!,
            freshTokens.RequestToken!);

        await antiforgery.ValidateRequestAsync(freshValidation);
    }

    /// <summary>已登入 token 在 JWT 過期成為匿名後必須失效，匿名重取 token 才可 refresh。</summary>
    [Fact]
    public async Task AntiforgeryToken_AuthenticatedTokenFailsForAnonymousRefresh()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        services.AddFamilyPortalMvcSecurity(secureCookies: false);
        await using var provider = services.BuildServiceProvider();
        var antiforgery = provider.GetRequiredService<IAntiforgery>();
        var authenticated = Principal("admin-1", "Admin");

        var authenticatedIssue = Context(provider, authenticated);
        var authenticatedTokens = antiforgery.GetAndStoreTokens(authenticatedIssue);
        Assert.NotNull(authenticatedTokens.CookieToken);
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        var staleValidation = Context(provider, anonymous);
        SetTokens(
            staleValidation,
            authenticatedTokens.CookieToken!,
            authenticatedTokens.RequestToken!);
        await Assert.ThrowsAsync<AntiforgeryValidationException>(() =>
            antiforgery.ValidateRequestAsync(staleValidation));

        var anonymousIssue = Context(provider, anonymous);
        anonymousIssue.Request.Headers.Cookie =
            $"family_antiforgery={authenticatedTokens.CookieToken}";
        var anonymousTokens = antiforgery.GetAndStoreTokens(anonymousIssue);
        var freshValidation = Context(provider, anonymous);
        SetTokens(
            freshValidation,
            anonymousTokens.CookieToken ?? authenticatedTokens.CookieToken!,
            anonymousTokens.RequestToken!);

        await antiforgery.ValidateRequestAsync(freshValidation);
    }

    /// <summary>全域 limiter 必須依 user ID 分區，並保留每分鐘 100 次上限。</summary>
    [Fact]
    public void GlobalRateLimiter_UsesIndependentAuthenticatedPartitions()
    {
        var services = new ServiceCollection();
        services.AddFamilyPortalRateLimiting();
        using var provider = services.BuildServiceProvider();
        var limiter = provider.GetRequiredService<IOptions<RateLimiterOptions>>().Value.GlobalLimiter;
        Assert.NotNull(limiter);
        var first = RateContext("user-1");
        var second = RateContext("user-2");

        for (var attempt = 0; attempt < 100; attempt++)
        {
            using var lease = limiter.AttemptAcquire(first, 1);
            Assert.True(lease.IsAcquired);
        }

        using var rejected = limiter.AttemptAcquire(first, 1);
        Assert.False(rejected.IsAcquired);
        using var independent = limiter.AttemptAcquire(second, 1);
        Assert.True(independent.IsAcquired);

        var health = RateContext("health-user", "/health/live");
        for (var attempt = 0; attempt < 200; attempt++)
        {
            using var healthLease = limiter.AttemptAcquire(health, 1);
            Assert.True(healthLease.IsAcquired);
        }
    }

    /// <summary>上傳 action 只能帶 media-upload policy，且必須擴張兩個 body limits。</summary>
    [Fact]
    public void UploadAction_MetadataUsesMediaPolicyAndMediaBodyLimits()
    {
        var action = typeof(MediaController).GetMethod(nameof(MediaController.Upload));
        Assert.NotNull(action);
        var limiter = Assert.Single(action.GetCustomAttributes(
            typeof(EnableRateLimitingAttribute),
            inherit: true).Cast<EnableRateLimitingAttribute>());
        Assert.Equal("media-upload", limiter.PolicyName);
        var requestSize = Assert.Single(action.GetCustomAttributes(
            typeof(RequestSizeLimitAttribute),
            inherit: true).Cast<RequestSizeLimitAttribute>());
        Assert.Equal(
            ValidationLimits.MaxMediaRequestBytes,
            ((IRequestSizeLimitMetadata)requestSize).MaxRequestBodySize);
        var form = Assert.Single(action.GetCustomAttributes(
            typeof(RequestFormLimitsAttribute),
            inherit: true).Cast<RequestFormLimitsAttribute>());
        Assert.Equal(
            ValidationLimits.MaxMediaRequestBytes,
            form.MultipartBodyLengthLimit);
    }

    /// <summary>登入與 refresh action 必須保留各自的嚴格 named limiter。</summary>
    [Fact]
    public void AuthActions_RetainNamedRateLimitPolicies()
    {
        AssertPolicy(nameof(AuthController.Login), "login");
        AssertPolicy(nameof(AuthController.RefreshToken), "refresh");
    }

    private static DefaultHttpContext Context(IServiceProvider provider, ClaimsPrincipal principal) => new()
    {
        RequestServices = provider,
        User = principal
    };

    private static void SetTokens(HttpContext context, string cookieToken, string requestToken)
    {
        context.Request.Headers.Cookie = $"family_antiforgery={cookieToken}";
        context.Request.Headers["X-XSRF-TOKEN"] = requestToken;
    }

    private static ClaimsPrincipal Principal(string userId, string role) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Role, role)
            ],
            "Test"));

    private static void AssertPolicy(string methodName, string policyName)
    {
        var action = typeof(AuthController).GetMethod(methodName);
        Assert.NotNull(action);
        var limiter = Assert.Single(action.GetCustomAttributes(
            typeof(EnableRateLimitingAttribute),
            inherit: true).Cast<EnableRateLimitingAttribute>());
        Assert.Equal(policyName, limiter.PolicyName);
    }

    private static DefaultHttpContext RateContext(
        string userId,
        string path = "/api/probe")
    {
        var context = new DefaultHttpContext { User = Principal(userId, "Admin") };
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Path = path;
        return context;
    }
}
