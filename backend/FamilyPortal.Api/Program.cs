using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FamilyPortal.Application.DTOs;
using FamilyPortal.Api.Middleware;
using FamilyPortal.Infrastructure;
using FamilyPortal.Infrastructure.Hubs;
using FamilyPortal.Infrastructure.MongoDB;
using FamilyPortal.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ─────────────────────────────────────────────
// 1. 基礎設施服務註冊 (Infrastructure DI)
// ─────────────────────────────────────────────
builder.Services.AddInfrastructure(builder.Configuration);

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

    // SignalR JWT 配置 — 從 Query String 取得 Token
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;

            if (!string.IsNullOrEmpty(accessToken) &&
                path.StartsWithSegments(CalendarHub.HubUrl))
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
builder.Services.AddControllers()
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
    options.AddFixedWindowLimiter("fixed", limiterOptions =>
    {
        limiterOptions.PermitLimit = 100;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit = 10;
    });

    // 登入端點更嚴格的速率限制（防暴力破解）
    options.AddFixedWindowLimiter("login", limiterOptions =>
    {
        limiterOptions.PermitLimit = 10;
        limiterOptions.Window = TimeSpan.FromMinutes(5);
        limiterOptions.QueueLimit = 2;
    });
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

// 開發環境配置
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// 安全性標頭 Middleware
app.Use(async (context, next) =>
{
    // 防止 XSS
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("Content-Security-Policy", "default-src 'self'");
    await next();
});

app.UseHttpsRedirection();
app.UseCors("FrontendPolicy");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// 路由映射
app.MapControllers();
app.MapHub<CalendarHub>(CalendarHub.HubUrl);
app.MapHealthChecks("/health");

// ─────────────────────────────────────────────
// 應用程式啟動初始化
// ─────────────────────────────────────────────
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
            if (app.Environment.IsDevelopment())
            {
                defaultAdminPassword = "Admin@123456";
            }
            else
            {
                throw new InvalidOperationException("Production 環境必須設定 DefaultAdmin:Password。");
            }
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
        app.Logger.LogInformation("已建立預設管理員帳號：{Username}", defaultAdminUsername);
    }
}

app.Logger.LogInformation("🏠 Family Portal API 啟動成功！");
app.Run();
