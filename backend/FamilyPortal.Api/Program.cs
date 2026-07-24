using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FamilyPortal.Application.DTOs;
using FamilyPortal.Application.Interfaces;
using FamilyPortal.Application.Services;
using FamilyPortal.Api.Configuration;
using FamilyPortal.Api.Middleware;
using FamilyPortal.Infrastructure;
using FamilyPortal.Infrastructure.Hubs;
using FamilyPortal.Infrastructure.MongoDB;
using FamilyPortal.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Tokens;
using System.Net;

var builder = WebApplication.CreateBuilder(args);
ProductionSecurityOptions.Validate(builder.Configuration, builder.Environment.EnvironmentName);

var secureCookies = builder.Configuration.GetValue("SecuritySettings:CookieSecure", true);
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("FamilyPortal");
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    foreach (var value in builder.Configuration.GetSection("ProxySettings:KnownProxies").Get<string[]>() ?? [])
    {
        if (!IPAddress.TryParse(value, out var address))
        {
            throw new InvalidOperationException($"ProxySettings:KnownProxies 包含無效 IP：{value}");
        }
        options.KnownProxies.Add(address);
    }
});

// ─────────────────────────────────────────────
// 1. 基礎設施服務註冊 (Infrastructure DI)
// ─────────────────────────────────────────────
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<IEventService, EventService>();

// ─────────────────────────────────────────────
// 2. JWT 認證配置
// ─────────────────────────────────────────────
var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>()!;
if (string.IsNullOrWhiteSpace(jwtSettings.SecretKey) || jwtSettings.SecretKey.Length < 32)
{
    throw new InvalidOperationException("JwtSettings:SecretKey 必須透過安全設定提供，且長度至少 32 字元。");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings.Issuer,
        ValidAudience = jwtSettings.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
        ClockSkew = TimeSpan.Zero // 精確控制 Token 過期時間
    };

    // API 與 SignalR 統一從 HttpOnly Cookie 取得 JWT，避免 Token 出現在 URL／Log。
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var path = context.HttpContext.Request.Path;

            if (context.Request.Cookies.TryGetValue("family_access", out var accessToken) &&
                (path.StartsWithSegments(CalendarHub.HubUrl) ||
                 path.StartsWithSegments("/api")))
            {
                context.Token = accessToken;
            }

            return Task.CompletedTask;
        }
    };
});

// ─────────────────────────────────────────────
// 3. RBAC 授權策略
// ─────────────────────────────────────────────
builder.Services.AddAuthorization(options =>
{
    // 未明確標註 [AllowAnonymous] 的端點一律需要登入。
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    // Admin 策略 — 需要 Admin 角色
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("Admin"));

    // Authenticated 策略 — 僅需登入
    options.AddPolicy("Authenticated", policy =>
        policy.RequireAuthenticatedUser());
});

// ─────────────────────────────────────────────
// 4. Controllers 與 JSON 配置
// ─────────────────────────────────────────────
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "family_antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddControllers(options =>
    {
        // Cookie 認證的所有非 GET Controller 請求都必須通過 CSRF 驗證。
        options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    })
    .AddJsonOptions(options =>
    {
        // Enum 序列化為字串（非數字）
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        // 忽略 null 值屬性
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

// 統一 DataAnnotations / ModelState 驗證錯誤格式
builder.Services.Configure<ApiBehaviorOptions>(options =>
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

// ─────────────────────────────────────────────
// 5. SignalR 配置
// ─────────────────────────────────────────────
builder.Services.AddSignalR();

// ─────────────────────────────────────────────
// 6. CORS 配置 — 允許前端跨域請求
// ─────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy.WithOrigins(
                builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()
                ?? new[] { "http://localhost:3000" })
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials(); // SignalR 需要
    });
});

// ─────────────────────────────────────────────
// 7. OpenAPI / Swagger
// ─────────────────────────────────────────────
builder.Services.AddOpenApi();

// ─────────────────────────────────────────────
// 8. Rate Limiting (速率限制)
// ─────────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("fixed", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.Identity?.Name ??
                          context.Connection.RemoteIpAddress?.ToString() ??
                          "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // 登入端點更嚴格的速率限制（防暴力破解）
    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0
            }));

    options.AddPolicy("refresh", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0
            }));
});

// ─────────────────────────────────────────────
// 9. Health Checks
// ─────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddMongoDb(
        clientFactory: sp => new MongoDB.Driver.MongoClient(builder.Configuration["MongoDbSettings:ConnectionString"]!),
        name: "mongodb",
        timeout: TimeSpan.FromSeconds(3)
    );

var app = builder.Build();

// ─────────────────────────────────────────────
// Middleware Pipeline 順序（順序很重要！）
// ─────────────────────────────────────────────
app.UseMiddleware<ExceptionMiddleware>();
app.UseForwardedHeaders();

// 開發環境配置
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseFamilyPortalSecurityHeaders();

app.UseHttpsRedirection();
app.UseCors("FrontendPolicy");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// 路由映射
app.MapControllers().RequireRateLimiting("fixed");
app.MapHub<CalendarHub>(CalendarHub.HubUrl)
    .RequireAuthorization()
    .RequireRateLimiting("fixed");
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
}).AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();

// ─────────────────────────────────────────────
// 應用程式啟動初始化
// ─────────────────────────────────────────────
if (!app.Environment.IsEnvironment("Testing"))
using (var scope = app.Services.CreateScope())
{
    // 建立 MongoDB 索引
    var indexInitializer = scope.ServiceProvider.GetRequiredService<MongoDbIndexInitializer>();
    await indexInitializer.InitializeAsync();

    // 建立預設 Admin 帳號（如果不存在）
    var userRepo = scope.ServiceProvider.GetRequiredService<FamilyPortal.Application.Interfaces.IUserRepository>();
    var passwordHasher = scope.ServiceProvider.GetRequiredService<FamilyPortal.Application.Interfaces.IPasswordHasher>();

    var adminExists = await userRepo.ExistsAsync(u => u.Role == FamilyPortal.Domain.Enums.UserRole.Admin);
    if (!adminExists)
    {
        var defaultAdminUsername = builder.Configuration["DefaultAdmin:Username"] ?? "admin";
        var defaultAdminEmail = builder.Configuration["DefaultAdmin:Email"] ?? "admin@family.portal";
        var defaultAdminDisplayName = builder.Configuration["DefaultAdmin:DisplayName"] ?? "許浴宸";
        var defaultAdminPassword = builder.Configuration["DefaultAdmin:Password"];

        if (string.IsNullOrWhiteSpace(defaultAdminPassword))
        {
            throw new InvalidOperationException("首次啟動前必須透過環境變數或 User Secrets 設定 DefaultAdmin:Password。");
        }

        var admin = new FamilyPortal.Domain.Entities.User
        {
            Username = defaultAdminUsername,
            Email = defaultAdminEmail,
            PasswordHash = passwordHasher.HashPassword(defaultAdminPassword),
            Role = FamilyPortal.Domain.Enums.UserRole.Admin,
            DisplayName = defaultAdminDisplayName,
            IsActive = true
        };
        await userRepo.CreateAsync(admin);
        app.Logger.LogInformation("已建立預設管理員帳號：{UserId}", admin.Id);
    }
}

app.Logger.LogInformation("🏠 Family Portal API 啟動成功！");
app.Run();

public partial class Program;

/// <summary>設定 Family Portal API 的 HTTP 安全標頭。</summary>
public static class SecurityHeadersExtensions
{
    /// <summary>在非 Development、非 Testing 環境啟用 HSTS，並加入 API 安全標頭。</summary>
    /// <param name="app">要設定的 Web 應用程式。</param>
    /// <returns>相同的 Web 應用程式，供 middleware pipeline 串接。</returns>
    public static WebApplication UseFamilyPortalSecurityHeaders(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing"))
        {
            app.UseHsts();
        }

        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
            await next();
        });

        return app;
    }
}
