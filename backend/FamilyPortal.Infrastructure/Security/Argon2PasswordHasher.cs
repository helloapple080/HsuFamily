using System.Security.Cryptography;
using System.Text;
using FamilyPortal.Application.Interfaces;
using Konscious.Security.Cryptography;

namespace FamilyPortal.Infrastructure.Security;

/// <summary>
/// Argon2id 密碼雜湊服務 — 實作 OWASP 建議的參數配置
/// 
/// 參數說明：
/// - Memory: 65536 KB (64 MB) — 記憶體使用量，越高越能抵抗 GPU 攻擊
/// - Iterations: 3 — 迭代次數，影響 CPU 時間
/// - Parallelism: 4 — 平行執行緒數
/// - Salt: 16 bytes 隨機值 — 確保相同密碼產生不同雜湊
/// - Hash: 32 bytes — 輸出長度
/// </summary>
public class Argon2PasswordHasher : IPasswordHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int MemorySize = 65536; // 64 MB
    private const int Iterations = 3;
    private const int Parallelism = 4;

    /// <inheritdoc />
    public string HashPassword(string password)
    {
        // 生成隨機 Salt
        var salt = RandomNumberGenerator.GetBytes(SaltSize);

        // 使用 Argon2id 進行雜湊
        using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            MemorySize = MemorySize,
            Iterations = Iterations,
            DegreeOfParallelism = Parallelism
        };

        var hash = argon2.GetBytes(HashSize);

        // 將參數、Salt、Hash 組合成可驗證的字串格式
        // 格式: $argon2id$v=19$m=65536,t=3,p=4${base64Salt}${base64Hash}
        return $"$argon2id$v=19$m={MemorySize},t={Iterations},p={Parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <inheritdoc />
    public bool VerifyPassword(string password, string hash)
    {
        try
        {
            // 解析儲存的雜湊字串
            var parts = hash.Split('$', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 5) return false;

            // parts[0] = "argon2id"
            // parts[1] = "v=19"
            // parts[2] = "m=65536,t=3,p=4" (參數)
            // parts[3] = base64 salt
            // parts[4] = base64 hash

            var paramParts = parts[2].Split(',');
            var memory = int.Parse(paramParts[0].Split('=')[1]);
            var iterations = int.Parse(paramParts[1].Split('=')[1]);
            var parallelism = int.Parse(paramParts[2].Split('=')[1]);

            var salt = Convert.FromBase64String(parts[3]);
            var expectedHash = Convert.FromBase64String(parts[4]);

            // 使用相同參數重新計算
            using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
            {
                Salt = salt,
                MemorySize = memory,
                Iterations = iterations,
                DegreeOfParallelism = parallelism
            };

            var computedHash = argon2.GetBytes(expectedHash.Length);

            // 使用常數時間比較，防止 Timing Attack
            return CryptographicOperations.FixedTimeEquals(computedHash, expectedHash);
        }
        catch
        {
            return false;
        }
    }
}
