using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FamilyPortal.Api.Configuration;

/// <summary>驗證正式環境必要安全設定。</summary>
public static class ProductionSecurityOptions
{
    /// <summary>正式環境若使用不安全 Cookie 或非 HTTPS origin 時拒絕啟動。</summary>
    /// <param name="configuration">應用程式設定。</param>
    /// <param name="environmentName">應用程式環境名稱。</param>
    /// <exception cref="InvalidOperationException">正式環境安全設定無效時擲出。</exception>
    public static void Validate(IConfiguration configuration, string environmentName)
    {
        if (!string.Equals(environmentName, Environments.Production, StringComparison.Ordinal))
        {
            return;
        }

        if (!configuration.GetValue("SecuritySettings:CookieSecure", true))
        {
            throw new InvalidOperationException("Production 必須啟用 Secure Cookie。");
        }

        var origins = configuration.GetSection("AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length == 0 || origins.Any(origin =>
                !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Production AllowedOrigins 必須全部使用 HTTPS。");
        }
    }
}
