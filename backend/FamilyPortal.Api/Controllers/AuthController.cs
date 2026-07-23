using FamilyPortal.Application.DTOs;
using FamilyPortal.Application.DTOs.Auth;
using FamilyPortal.Application.Interfaces;
using FamilyPortal.Domain.Entities;
using FamilyPortal.Domain.Enums;
using FamilyPortal.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace FamilyPortal.Api.Controllers;

/// <summary>
/// 認證控制器 — 處理登入、登出、Token 更新與帳號管理
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private const string AccessTokenCookieName = "family_access";
    private const string RefreshTokenCookieName = "family_refresh";
    private readonly IUserRepository _userRepo;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly ILogger<AuthController> _logger;
    private readonly IAntiforgery _antiforgery;
    private readonly JwtSettings _jwtSettings;
    private readonly bool _secureCookies;

    public AuthController(
        IUserRepository userRepo,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        ILogger<AuthController> logger,
        IAntiforgery antiforgery,
        IOptions<JwtSettings> jwtSettings,
        IConfiguration configuration)
    {
        _userRepo = userRepo;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _logger = logger;
        _antiforgery = antiforgery;
        _jwtSettings = jwtSettings.Value;
        _secureCookies = configuration.GetValue("SecuritySettings:CookieSecure", true);
    }

    /// <summary>GET /api/auth/csrf — 建立 antiforgery cookie 並取得 request token。</summary>
    [HttpGet("csrf")]
    [AllowAnonymous]
    public ActionResult<ApiResponse<CsrfTokenResponse>> GetCsrfToken()
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(ApiResponse<CsrfTokenResponse>.Ok(new CsrfTokenResponse
        {
            Token = tokens.RequestToken ?? string.Empty
        }));
    }

    /// <summary>
    /// POST /api/auth/login — 使用者登入
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> Login([FromBody] LoginRequest request)
    {
        // 支援帳號或信箱登入
        var user = await _userRepo.GetByUsernameAsync(request.Username)
                   ?? await _userRepo.GetByEmailAsync(request.Username);

        if (user == null || !user.IsActive)
        {
            _logger.LogWarning("登入失敗：帳號不存在或已停用，來源 IP：{RemoteIp}", HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(ApiResponse<LoginResponse>.Fail("帳號或密碼錯誤"));
        }

        if (!_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("登入失敗：密碼錯誤，來源 IP：{RemoteIp}", HttpContext.Connection.RemoteIpAddress);
            return Unauthorized(ApiResponse<LoginResponse>.Fail("帳號或密碼錯誤"));
        }

        // 生成 Token
        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken();

        // 儲存 Refresh Token
        await _userRepo.ReplaceRefreshTokensAsync(user.Id, [refreshToken.PersistedToken]);
        WriteAuthCookies(accessToken, refreshToken.Token, refreshToken.PersistedToken.ExpiresAt);

        _logger.LogInformation("使用者 {UserId} 登入成功", user.Id);

        var response = new LoginResponse
        {
            ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes),
            User = new UserInfo
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                DisplayName = user.DisplayName,
                Role = user.Role.ToString(),
                LinkedMemberId = user.LinkedMemberId
            }
        };

        return Ok(ApiResponse<LoginResponse>.Ok(response, "登入成功"));
    }

    /// <summary>
    /// POST /api/auth/refresh — 更新 Access Token
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("refresh")]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> RefreshToken()
    {
        if (!Request.Cookies.TryGetValue(RefreshTokenCookieName, out var rawRefreshToken))
        {
            ClearAuthCookies();
            return Unauthorized(ApiResponse<LoginResponse>.Fail("缺少登入憑證"));
        }

        // Access Cookie 過期後瀏覽器會移除，因此以不可逆雜湊查找 Refresh Token 所屬使用者。
        var refreshTokenHash = _tokenService.HashRefreshToken(rawRefreshToken);
        var user = await _userRepo.GetByRefreshTokenHashAsync(refreshTokenHash);
        if (user == null || !user.IsActive)
        {
            return Unauthorized(ApiResponse<LoginResponse>.Fail("使用者不存在或已停用"));
        }

        var currentRefreshToken = user.RefreshTokens
            .SingleOrDefault(token => token.TokenHash == refreshTokenHash);

        if (currentRefreshToken == null)
        {
            ClearAuthCookies();
            return Unauthorized(ApiResponse<LoginResponse>.Fail("Refresh Token 無效或已過期"));
        }

        if (!currentRefreshToken.IsActive)
        {
            if (currentRefreshToken.IsRevoked && currentRefreshToken.ReplacedByTokenHash != null)
            {
                await _userRepo.RevokeTokenFamilyAsync(user.Id, currentRefreshToken.FamilyId);
                _logger.LogWarning("偵測到 Refresh Token 重放並撤銷 token family。使用者 ID: {UserId}", user.Id);
            }

            ClearAuthCookies();
            return Unauthorized(ApiResponse<LoginResponse>.Fail("Refresh Token 無效或已過期"));
        }

        var newAccessToken = _tokenService.GenerateAccessToken(user);
        var newRefreshToken = _tokenService.GenerateRefreshToken(currentRefreshToken.FamilyId);
        var rotated = await _userRepo.RotateRefreshTokenAsync(
            user.Id,
            refreshTokenHash,
            newRefreshToken.PersistedToken);

        if (!rotated)
        {
            await _userRepo.RevokeTokenFamilyAsync(user.Id, currentRefreshToken.FamilyId);
            ClearAuthCookies();
            _logger.LogWarning("Refresh Token 輪替競爭失敗並撤銷 token family。使用者 ID: {UserId}", user.Id);
            return Unauthorized(ApiResponse<LoginResponse>.Fail("登入工作階段已失效"));
        }

        WriteAuthCookies(newAccessToken, newRefreshToken.Token, newRefreshToken.PersistedToken.ExpiresAt);

        var response = new LoginResponse
        {
            ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes),
            User = new UserInfo
            {
                Id = user.Id,
                Username = user.Username,
                Email = user.Email,
                DisplayName = user.DisplayName,
                Role = user.Role.ToString(),
                LinkedMemberId = user.LinkedMemberId
            }
        };

        return Ok(ApiResponse<LoginResponse>.Ok(response, "Token 更新成功"));
    }

    /// <summary>
    /// POST /api/auth/logout — 使用者登出（撤銷 Refresh Token）
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<ActionResult<ApiResponse>> Logout()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
        {
            return Unauthorized(ApiResponse.Fail("未授權"));
        }

        await _userRepo.RevokeAllRefreshTokensAsync(userId);
        ClearAuthCookies();
        _logger.LogInformation("使用者 {UserId} 已登出", userId);

        return Ok(ApiResponse.Ok("登出成功"));
    }

    /// <summary>
    /// POST /api/auth/register — 註冊新帳號（僅 Admin）
    /// </summary>
    [HttpPost("register")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ApiResponse<UserInfo>>> Register([FromBody] RegisterRequest request)
    {
        // 檢查帳號是否已存在
        if (await _userRepo.ExistsAsync(u => u.Username == request.Username))
        {
            return BadRequest(ApiResponse<UserInfo>.Fail("帳號已存在"));
        }

        if (await _userRepo.ExistsAsync(u => u.Email == request.Email))
        {
            return BadRequest(ApiResponse<UserInfo>.Fail("信箱已被使用"));
        }

        // 解析角色
        if (!Enum.TryParse<UserRole>(request.Role, true, out var role))
        {
            return BadRequest(ApiResponse<UserInfo>.Fail("無效的角色"));
        }

        var user = new User
        {
            Username = request.Username,
            Email = request.Email,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            Role = role,
            DisplayName = request.DisplayName,
            IsActive = true
        };

        await _userRepo.CreateAsync(user);
        _logger.LogInformation("Admin 建立了新帳號：{UserId}", user.Id);

        var userInfo = new UserInfo
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Role = user.Role.ToString()
        };

        return CreatedAtAction(nameof(GetMe), null, ApiResponse<UserInfo>.Ok(userInfo, "帳號建立成功"));
    }

    /// <summary>
    /// GET /api/auth/me — 取得當前使用者資訊
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<UserInfo>>> GetMe()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == null)
        {
            return Unauthorized(ApiResponse<UserInfo>.Fail("未授權"));
        }

        var user = await _userRepo.GetByIdAsync(userId);
        if (user == null)
        {
            return NotFound(ApiResponse<UserInfo>.Fail("使用者不存在"));
        }

        var userInfo = new UserInfo
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Role = user.Role.ToString(),
            LinkedMemberId = user.LinkedMemberId
        };

        return Ok(ApiResponse<UserInfo>.Ok(userInfo));
    }

    private void WriteAuthCookies(string accessToken, string refreshToken, DateTime refreshExpiresAt)
    {
        Response.Cookies.Append(AccessTokenCookieName, accessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = _secureCookies,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes)
        });
        Response.Cookies.Append(RefreshTokenCookieName, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = _secureCookies,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            Expires = new DateTimeOffset(refreshExpiresAt, TimeSpan.Zero)
        });
    }

    private void ClearAuthCookies()
    {
        Response.Cookies.Delete(AccessTokenCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = _secureCookies,
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });
        Response.Cookies.Delete(RefreshTokenCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = _secureCookies,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth"
        });
    }
}
