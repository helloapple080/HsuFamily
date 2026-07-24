using FamilyPortal.Api.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FamilyPortal.Security.Tests;

/// <summary>驗證正式環境的安全設定。</summary>
public sealed class ProductionConfigurationTests
{
    /// <summary>不安全的正式環境設定應拒絕啟動。</summary>
    [Theory]
    [InlineData(false, "https://family.example")]
    [InlineData(true, "http://family.example")]
    [InlineData(true, "")]
    public void ValidateProduction_RejectsInsecureSettings(bool secureCookies, string origin)
    {
        var values = new Dictionary<string, string?>
        {
            ["SecuritySettings:CookieSecure"] = secureCookies.ToString(),
            ["AllowedOrigins:0"] = origin
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            ProductionSecurityOptions.Validate(configuration, Environments.Production));
    }

    /// <summary>安全的 HTTPS origin 與 Secure Cookie 應允許正式環境啟動。</summary>
    [Fact]
    public void ValidateProduction_AcceptsSecureHttpsOrigin()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecuritySettings:CookieSecure"] = "true",
                ["AllowedOrigins:0"] = "https://family.example"
            })
            .Build();

        ProductionSecurityOptions.Validate(configuration, Environments.Production);
    }
}
