using FamilyPortal.Infrastructure.Security;
using Xunit;

namespace FamilyPortal.Security.Tests;

/// <summary>驗證密碼只以 Argon2id 雜湊比對。</summary>
public class Argon2PasswordHasherTests
{
    /// <summary>正確密碼應通過，錯誤密碼與原文都不得通過。</summary>
    [Fact]
    public void HashAndVerify_UsesSaltedArgon2id()
    {
        var hasher = new Argon2PasswordHasher();
        const string password = "Correct-Horse-Battery-Staple-2026";

        var firstHash = hasher.HashPassword(password);
        var secondHash = hasher.HashPassword(password);

        Assert.StartsWith("$argon2id$", firstHash);
        Assert.NotEqual(password, firstHash);
        Assert.NotEqual(firstHash, secondHash);
        Assert.True(hasher.VerifyPassword(password, firstHash));
        Assert.False(hasher.VerifyPassword("wrong-password", firstHash));
    }
}
