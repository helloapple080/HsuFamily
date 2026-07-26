using System.Text;
using System.Security.Claims;
using FamilyPortal.Application.Common;
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
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.IdentityModel.Tokens;
using System.Net;

var builder = WebApplication.CreateBuilder(args);
ProductionSecurityOptions.Validate(builder.Configuration, builder.Environment.EnvironmentName);
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = ValidationLimits.MaxRequestBodyBytes);

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
builder.Services.AddScoped<IMemberService, MemberService>();
builder.Services.AddScoped<IItineraryService, ItineraryService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IMediaService, MediaService>();

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
builder.Services.AddFamilyPortalAuthorization();

// ─────────────────────────────────────────────
// 4. Controllers 與 JSON 配置
// ─────────────────────────────────────────────
builder.Services.AddFamilyPortalMvcSecurity(secureCookies);

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
builder.Services.AddFamilyPortalRateLimiting();

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
app.UseForwardedHeaders();
app.UseMiddleware<ExceptionMiddleware>();

// 開發環境配置
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.UseFamilyPortalSecurityHeaders();

app.UseHttpsRedirection();
app.UseCors("FrontendPolicy");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

// 路由映射
app.MapControllers();
app.MapHub<CalendarHub>(CalendarHub.HubUrl)
    .RequireAuthorization();
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
    var startupCancellationToken = app.Lifetime.ApplicationStopping;

    // 建立 MongoDB 索引
    var indexInitializer = scope.ServiceProvider.GetRequiredService<MongoDbIndexInitializer>();
    await indexInitializer.InitializeAsync(startupCancellationToken);

    // 建立預設 Admin 帳號（如果不存在）
    var userRepo = scope.ServiceProvider.GetRequiredService<FamilyPortal.Application.Interfaces.IUserRepository>();
    var passwordHasher = scope.ServiceProvider.GetRequiredService<FamilyPortal.Application.Interfaces.IPasswordHasher>();

    FamilyPortal.Domain.Entities.User? adminCandidate = null;
    var adminBootstrapResult = await DefaultAdminBootstrap.EnsureAsync(
        userRepo,
        () =>
        {
            var defaultAdminUsername = builder.Configuration["DefaultAdmin:Username"] ?? "admin";
            var defaultAdminEmail = builder.Configuration["DefaultAdmin:Email"] ?? "admin@family.portal";
            var defaultAdminDisplayName = builder.Configuration["DefaultAdmin:DisplayName"] ?? "許浴宸";
            var defaultAdminPassword = builder.Configuration["DefaultAdmin:Password"];

            if (string.IsNullOrWhiteSpace(defaultAdminPassword))
            {
                throw new InvalidOperationException(
                    "首次啟動前必須透過環境變數或 User Secrets 設定 DefaultAdmin:Password。");
            }

            adminCandidate = new FamilyPortal.Domain.Entities.User
            {
                Username = defaultAdminUsername,
                Email = defaultAdminEmail,
                PasswordHash = passwordHasher.HashPassword(defaultAdminPassword),
                Role = FamilyPortal.Domain.Enums.UserRole.Admin,
                DisplayName = defaultAdminDisplayName,
                IsActive = true
            };
            return adminCandidate;
        },
        startupCancellationToken);
    if (adminBootstrapResult == DefaultAdminBootstrapResult.Created)
    {
        app.Logger.LogInformation("已建立預設管理員帳號：{UserId}", adminCandidate!.Id);
    }
    else
    {
        app.Logger.LogInformation("預設管理員已存在，略過建立。");
    }
}

app.Logger.LogInformation("🏠 Family Portal API 啟動成功！");
app.Run();

public partial class Program;

/// <summary>建立 API 一般速率限制使用的穩定分區鍵。</summary>
public static class FamilyPortalRateLimitPartition
{
    /// <summary>已驗證使用者使用不可變識別碼，匿名請求則依來源 IP 分區。</summary>
    /// <param name="context">目前 HTTP request context。</param>
    /// <returns>速率限制分區鍵。</returns>
    public static string GetPartitionKey(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";
}

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
