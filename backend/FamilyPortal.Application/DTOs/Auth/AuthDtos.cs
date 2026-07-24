using System.ComponentModel.DataAnnotations;
using FamilyPortal.Application.Common;

namespace FamilyPortal.Application.DTOs.Auth;

/// <summary>登入請求 DTO</summary>
public class LoginRequest
{
    /// <summary>使用者名稱或電子信箱</summary>
    [Required(ErrorMessage = "帳號為必填欄位")]
    [MaxLength(254, ErrorMessage = "帳號最多 254 個字元")]
    public string Username { get; set; } = string.Empty;

    /// <summary>密碼</summary>
    [Required(ErrorMessage = "密碼為必填欄位")]
    [MaxLength(128, ErrorMessage = "密碼最多 128 個字元")]
    public string Password { get; set; } = string.Empty;
}

/// <summary>登入回應 DTO</summary>
public class LoginResponse
{
    /// <summary>Access Token 過期時間</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>使用者資訊</summary>
    public UserInfo User { get; set; } = new();
}

/// <summary>使用者基本資訊 DTO（不包含敏感資訊）</summary>
public class UserInfo
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? LinkedMemberId { get; set; }
}

/// <summary>CSRF Token 回應 DTO</summary>
public class CsrfTokenResponse
{
    /// <summary>應放入 X-XSRF-TOKEN Header 的 Token。</summary>
    public string Token { get; set; } = string.Empty;
}

/// <summary>註冊請求 DTO（僅 Admin 可建立新帳號）</summary>
public class RegisterRequest
{
    [Required(ErrorMessage = "帳號為必填欄位")]
    [MinLength(3, ErrorMessage = "帳號至少 3 個字元")]
    [MaxLength(50, ErrorMessage = "帳號最多 50 個字元")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "信箱為必填欄位")]
    [EmailAddress(ErrorMessage = "信箱格式不正確")]
    [MaxLength(254, ErrorMessage = "信箱最多 254 個字元")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "密碼為必填欄位")]
    [MinLength(8, ErrorMessage = "密碼至少 8 個字元")]
    [MaxLength(128, ErrorMessage = "密碼最多 128 個字元")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "顯示名稱為必填欄位")]
    [MaxLength(ValidationLimits.MaxShortTextLength, ErrorMessage = "顯示名稱最多 200 個字元")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>角色（預設 User）</summary>
    [MaxLength(ValidationLimits.MaxShortTextLength, ErrorMessage = "角色最多 200 個字元")]
    public string Role { get; set; } = "User";
}
