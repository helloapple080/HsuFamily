namespace FamilyPortal.Application.Interfaces;

/// <summary>
/// 密碼雜湊服務介面 — 使用 Argon2id 演算法
/// </summary>
public interface IPasswordHasher
{
    /// <summary>加密密碼</summary>
    /// <param name="password">明文密碼</param>
    /// <returns>Argon2id 加密後的雜湊字串</returns>
    string HashPassword(string password);

    /// <summary>驗證密碼</summary>
    /// <param name="password">明文密碼</param>
    /// <param name="hash">已儲存的雜湊值</param>
    /// <returns>是否匹配</returns>
    bool VerifyPassword(string password, string hash);
}
