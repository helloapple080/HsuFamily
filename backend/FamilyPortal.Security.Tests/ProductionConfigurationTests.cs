using FamilyPortal.Api.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace FamilyPortal.Security.Tests;

/// <summary>驗證正式環境的安全設定。</summary>
public sealed class ProductionConfigurationTests
{
    /// <summary>正式環境名稱不應因字母大小寫而繞過安全驗證。</summary>
    [Theory]
    [InlineData("production")]
    [InlineData("PRODUCTION")]
    public void ValidateProduction_WithDifferentEnvironmentCasing_RejectsInsecureSettings(
        string environmentName)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecuritySettings:CookieSecure"] = "false",
                ["AllowedOrigins:0"] = "https://family.example"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            ProductionSecurityOptions.Validate(configuration, environmentName));
    }

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

    /// <summary>正式環境應拒絕不是純 origin 的 HTTPS URI。</summary>
    [Theory]
    [InlineData("https://trusted.example@evil")]
    [InlineData("https://family.example/path")]
    [InlineData("https://family.example?source=test")]
    [InlineData("https://family.example#section")]
    [InlineData(" https://family.example")]
    [InlineData("https://family.example ")]
    [InlineData("https://family.example/")]
    public void ValidateProduction_RejectsHttpsValuesThatAreNotPureOrigins(string origin)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecuritySettings:CookieSecure"] = "true",
                ["AllowedOrigins:0"] = origin
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            ProductionSecurityOptions.Validate(configuration, Environments.Production));
    }

    /// <summary>正式環境應拒絕缺少 AllowedOrigins 設定。</summary>
    [Fact]
    public void ValidateProduction_RejectsAbsentOrigins()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecuritySettings:CookieSecure"] = "true"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            ProductionSecurityOptions.Validate(configuration, Environments.Production));
    }

    /// <summary>正式環境應拒絕明確設定的空 AllowedOrigins 陣列。</summary>
    [Fact]
    public void ValidateProduction_RejectsEmptyOriginsArray()
    {
        using var json = new MemoryStream(
            """{"SecuritySettings":{"CookieSecure":true},"AllowedOrigins":[]}"""u8.ToArray());
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(json)
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            ProductionSecurityOptions.Validate(configuration, Environments.Production));
    }

    /// <summary>非正式環境不應套用正式環境安全限制。</summary>
    [Theory]
    [InlineData("Development")]
    [InlineData("Testing")]
    public void ValidateProduction_NonProductionEnvironment_BypassesValidation(string environmentName)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecuritySettings:CookieSecure"] = "false",
                ["AllowedOrigins:0"] = "http://localhost:3000"
            })
            .Build();

        ProductionSecurityOptions.Validate(configuration, environmentName);
    }

    /// <summary>多個 origin 中任一值不安全時應拒絕正式環境啟動。</summary>
    [Fact]
    public void ValidateProduction_WhenAnyOriginIsInsecure_RejectsSettings()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecuritySettings:CookieSecure"] = "true",
                ["AllowedOrigins:0"] = "https://family.example",
                ["AllowedOrigins:1"] = "http://admin.family.example"
            })
            .Build();

        Assert.Throws<InvalidOperationException>(() =>
            ProductionSecurityOptions.Validate(configuration, Environments.Production));
    }

    /// <summary>合法的 IPv6 與含 port HTTPS origin 應通過驗證。</summary>
    [Theory]
    [InlineData("https://[2001:db8::1]")]
    [InlineData("https://family.example:8443")]
    public void ValidateProduction_AcceptsValidOriginVariants(string origin)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SecuritySettings:CookieSecure"] = "true",
                ["AllowedOrigins:0"] = origin
            })
            .Build();

        ProductionSecurityOptions.Validate(configuration, Environments.Production);
    }
}
