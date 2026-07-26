using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyPortal.Api.Configuration;

/// <summary>集中註冊 API 的授權、CSRF、MVC 與速率限制安全設定。</summary>
public static class ApiSecurityServiceCollectionExtensions
{
    /// <summary>註冊 fallback、Admin 與 authenticated 授權政策。</summary>
    public static IServiceCollection AddFamilyPortalAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
            options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
            options.AddPolicy("Authenticated", policy => policy.RequireAuthenticatedUser());
        });
        return services;
    }

    /// <summary>註冊 controller、統一驗證回應與 cookie antiforgery 保護。</summary>
    public static IMvcBuilder AddFamilyPortalMvcSecurity(
        this IServiceCollection services,
        bool secureCookies)
    {
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.Name = "family_antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = secureCookies
                ? CookieSecurePolicy.Always
                : CookieSecurePolicy.SameAsRequest;
        });
        services.Configure<FormOptions>(options =>
            options.MultipartBodyLengthLimit = ValidationLimits.MaxRequestBodyBytes);
        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var errors = context.ModelState
                    .Where(entry => entry.Value?.Errors.Count > 0)
                    .SelectMany(entry => entry.Value!.Errors.Select(error =>
                        string.IsNullOrWhiteSpace(entry.Key)
                            ? error.ErrorMessage
                            : $"{entry.Key}: {error.ErrorMessage}"))
                    .ToList();
                return new BadRequestObjectResult(
                    ApiResponse<object>.Fail("驗證失敗", errors));
            };
        });

        return services.AddControllersWithViews(options =>
            {
                options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(
                    new JsonStringEnumConverter(allowIntegerValues: false));
                options.JsonSerializerOptions.DefaultIgnoreCondition =
                    JsonIgnoreCondition.WhenWritingNull;
            });
    }

    /// <summary>註冊全域 user/IP limiter 與登入、refresh、媒體上傳 named policies。</summary>
    public static IServiceCollection AddFamilyPortalRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (!context.Request.Path.StartsWithSegments("/api") &&
                    !context.Request.Path.StartsWithSegments("/hubs"))
                {
                    return RateLimitPartition.GetNoLimiter("non-api");
                }

                return RateLimitPartition.GetFixedWindowLimiter(
                    FamilyPortalRateLimitPartition.GetPartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 100,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    });
            });
            options.AddPolicy("login", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(5),
                        QueueLimit = 0
                    }));
            options.AddPolicy("refresh", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(5),
                        QueueLimit = 0
                    }));
            options.AddPolicy("media-upload", context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    FamilyPortalRateLimitPartition.GetPartitionKey(context),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });
        return services;
    }
}
