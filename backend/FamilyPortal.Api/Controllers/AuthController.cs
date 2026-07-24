using System.Security.Claims;
using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs;
using FamilyPortal.Application.DTOs.Auth;
using FamilyPortal.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyPortal.Api.Controllers;

/// <summary>認證、工作階段 Cookie 與帳號管理 HTTP adapter。</summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController(
    IAuthService authService,
    ILogger<AuthController> logger,
    IAntiforgery antiforgery,
    IConfiguration configuration) : ControllerBase
{
    private const string AccessTokenCookieName = "family_access";
    private const string RefreshTokenCookieName = "family_refresh";
    private readonly bool _secureCookies = configuration.GetValue(
        "SecuritySettings:CookieSecure",
        true);

    /// <summary>GET /api/auth/csrf — 建立 antiforgery cookie 並取得 request token。</summary>
    [HttpGet("csrf")]
    [AllowAnonymous]
    public ActionResult<ApiResponse<CsrfTokenResponse>> GetCsrfToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(ApiResponse<CsrfTokenResponse>.Ok(new CsrfTokenResponse
        {
            Token = tokens.RequestToken ?? string.Empty
        }));
    }

    /// <summary>POST /api/auth/login — 使用者登入。</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.LoginAsync(
            request.Username,
            request.Password,
            cancellationToken);
        if (!result.IsSuccess)
        {
            logger.LogWarning(
                "登入失敗，來源 IP：{RemoteIp}",
                HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(ApiResponse<LoginResponse>.Fail(
                result.Message ?? "帳號或密碼錯誤"));
        }

        WriteAuthCookies(result.Value!);
        logger.LogInformation("使用者 {UserId} 登入成功", result.Value!.User.Id);
        return Ok(ApiResponse<LoginResponse>.Ok(ToLoginResponse(result.Value!), "登入成功"));
    }

    /// <summary>POST /api/auth/refresh — 更新 Access Token。</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("refresh")]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> RefreshToken(
        CancellationToken cancellationToken)
    {
        if (!Request.Cookies.TryGetValue(RefreshTokenCookieName, out var rawRefreshToken))
        {
            ClearAuthCookies();
            return Unauthorized(ApiResponse<LoginResponse>.Fail("缺少登入憑證"));
        }

        var result = await authService.RefreshAsync(rawRefreshToken, cancellationToken);
        if (!result.IsSuccess)
        {
            ClearAuthCookies();
            logger.LogWarning(
                "Refresh Token 更新失敗，來源 IP：{RemoteIp}",
                HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(ApiResponse<LoginResponse>.Fail(
                result.Message ?? "Refresh Token 無效或已過期"));
        }

        WriteAuthCookies(result.Value!);
        return Ok(ApiResponse<LoginResponse>.Ok(
            ToLoginResponse(result.Value!),
            "Token 更新成功"));
    }

    /// <summary>POST /api/auth/logout — 撤銷使用者所有 Refresh Token。</summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<ActionResult<ApiResponse>> Logout(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var result = await authService.LogoutAsync(userId, cancellationToken);
        if (!result.IsSuccess)
        {
            return Unauthorized(ApiResponse.Fail(result.Message ?? "未授權"));
        }

        ClearAuthCookies();
        logger.LogInformation("使用者 {UserId} 已登出", userId);
        return Ok(ApiResponse.Ok("登出成功"));
    }

    /// <summary>POST /api/auth/register — 註冊新帳號（僅 Admin）。</summary>
    [HttpPost("register")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ApiResponse<UserInfo>>> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authService.RegisterAsync(request, cancellationToken);
        if (!result.IsSuccess)
        {
            return ToUserActionResult(result);
        }

        logger.LogInformation("Admin 建立了新帳號：{UserId}", result.Value!.Id);
        return CreatedAtAction(
            nameof(GetMe),
            null,
            ApiResponse<UserInfo>.Ok(result.Value, "帳號建立成功"));
    }

    /// <summary>GET /api/auth/me — 取得當前使用者資訊。</summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<UserInfo>>> GetMe(
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(ApiResponse<UserInfo>.Fail("未授權"));
        }

        var result = await authService.GetUserAsync(userId, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<UserInfo>.Ok(result.Value!))
            : ToUserActionResult(result);
    }

    private static LoginResponse ToLoginResponse(AuthSession session) => new()
    {
        ExpiresAt = session.AccessTokenExpiresAt,
        User = session.User
    };

    private ActionResult<ApiResponse<UserInfo>> ToUserActionResult(
        ServiceResult<UserInfo> result) => result.Error switch
    {
        ServiceError.Validation => BadRequest(ApiResponse<UserInfo>.Fail(
            result.Message ?? "驗證失敗")),
        ServiceError.Unauthorized => Unauthorized(ApiResponse<UserInfo>.Fail(
            result.Message ?? "未授權")),
        ServiceError.Forbidden => StatusCode(
            StatusCodes.Status403Forbidden,
            ApiResponse<UserInfo>.Fail(result.Message ?? "禁止存取")),
        ServiceError.NotFound => NotFound(ApiResponse<UserInfo>.Fail(
            result.Message ?? "使用者不存在")),
        ServiceError.Conflict => Conflict(ApiResponse<UserInfo>.Fail(
            result.Message ?? "資料衝突")),
        _ => StatusCode(
            StatusCodes.Status500InternalServerError,
            ApiResponse<UserInfo>.Fail("帳號操作失敗"))
    };

    private void WriteAuthCookies(AuthSession session)
    {
        Response.Cookies.Append(AccessTokenCookieName, session.AccessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = _secureCookies,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = new DateTimeOffset(session.AccessTokenExpiresAt)
        });
        Response.Cookies.Append(RefreshTokenCookieName, session.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = _secureCookies,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            Expires = new DateTimeOffset(session.RefreshTokenExpiresAt)
        });
    }

    private void ClearAuthCookies()
    {
        Response.Cookies.Delete(AccessTokenCookieName, CookieOptionsFor("/"));
        Response.Cookies.Delete(RefreshTokenCookieName, CookieOptionsFor("/api/auth"));
    }

    private CookieOptions CookieOptionsFor(string path) => new()
    {
        HttpOnly = true,
        Secure = _secureCookies,
        SameSite = SameSiteMode.Strict,
        Path = path
    };
}
