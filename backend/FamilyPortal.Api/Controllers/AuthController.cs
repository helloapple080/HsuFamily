using FamilyPortal.Application.DTOs;
using FamilyPortal.Application.DTOs.Auth;
using FamilyPortal.Application.Interfaces;
using FamilyPortal.Domain.Entities;
using FamilyPortal.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyPortal.Api.Controllers;

/// <summary>
/// 認證控制器 — 處理登入、登出、Token 更新與帳號管理
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IUserRepository _userRepo;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IUserRepository userRepo,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        ILogger<AuthController> logger)
    {
        _userRepo = userRepo;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/auth/login — 使用者登入
    /// </summary>
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<ApiResponse<LoginResponse>>> Login([FromBody] LoginRequest request)
    {
        // 支援帳號或信箱登入
        var user = await _userRepo.GetByUsernameAsync(request.Username)
                   ?? await _userRepo.GetByEmailAsync(request.Username);

        if (user == null || !user.IsActive)
        {
            _logger.LogWarning("登入失敗：帳號 {Username} 不存在或已停用", request.Username);
            return Unauthorized(ApiResponse<LoginResponse>.Fail("帳號或密碼錯誤"));
        }

        if (!_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("登入失敗：帳號 {Username} 密碼錯誤", request.Username);
            return Unauthorized(ApiResponse<LoginResponse>.Fail("帳號或密碼錯誤"));
        }

        // 生成 Token
        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = _tokenService.GenerateRefreshToken();

        // 儲存 Refresh Token
        await _userRepo.UpdateRefreshTokenAsync(user.Id, refreshToken);

        _logger.LogInformation("使用者 {Username} 登入成功", user.Username);

        var response = new LoginResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken.Token,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
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
    public async Task<ActionResult<ApiResponse<LoginResponse>>> RefreshToken(
        [FromBody] RefreshTokenRequest request)
    {
        // 從過期的 Access Token 取得使用者 ID
        var userId = _tokenService.GetUserIdFromExpiredToken(request.AccessToken);
        if (userId == null)
        {
            return Unauthorized(ApiResponse<LoginResponse>.Fail("無效的 Access Token"));
        }

        var user = await _userRepo.GetByIdAsync(userId);
        if (user == null || !user.IsActive)
        {
            return Unauthorized(ApiResponse<LoginResponse>.Fail("使用者不存在或已停用"));
        }

        // 驗證 Refresh Token
        if (user.RefreshToken == null ||
            user.RefreshToken.Token != request.RefreshToken ||
            !user.RefreshToken.IsActive)
        {
            // 可能的 Token 被盜用，撤銷所有 Token
            await _userRepo.UpdateRefreshTokenAsync(user.Id, null);
            _logger.LogWarning("Refresh Token 驗證失敗，可能遭盜用。使用者 ID: {UserId}", userId);
            return Unauthorized(ApiResponse<LoginResponse>.Fail("Refresh Token 無效或已過期"));
        }

        // Token 輪替 (Rotation) — 舊 Token 標記為已使用，生成新 Token
        var newAccessToken = _tokenService.GenerateAccessToken(user);
        var newRefreshToken = _tokenService.GenerateRefreshToken();

        // 記錄舊 Token 被替換
        user.RefreshToken.RevokedAt = DateTime.UtcNow;
        user.RefreshToken.ReplacedByToken = newRefreshToken.Token;

        await _userRepo.UpdateRefreshTokenAsync(user.Id, newRefreshToken);

        var response = new LoginResponse
        {
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken.Token,
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
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

        await _userRepo.UpdateRefreshTokenAsync(userId, null);
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
        _logger.LogInformation("Admin 建立了新帳號：{Username}", user.Username);

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
}
