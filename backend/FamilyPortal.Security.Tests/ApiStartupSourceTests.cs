using Xunit;

namespace FamilyPortal.Security.Tests;

/// <summary>防止 API 啟動安全控制在重構時退化。</summary>
public sealed class ApiStartupSourceTests
{
    /// <summary>API 應限制 request body，並依安全相依順序套用核心 middleware。</summary>
    [Fact]
    public void Program_EnforcesBodyLimitAndSecurityMiddlewareOrder()
    {
        var source = File.ReadAllText(ProgramSourcePath());
        Assert.Contains("options.Limits.MaxRequestBodySize = 1024 * 1024", source);

        var markers = new[]
        {
            "app.UseForwardedHeaders();",
            "app.UseHttpsRedirection();",
            "app.UseCors(\"FrontendPolicy\");",
            "app.UseAuthentication();",
            "app.UseRateLimiter();",
            "app.UseAuthorization();"
        };
        var previous = -1;
        foreach (var marker in markers)
        {
            var current = source.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(current > previous, $"Middleware '{marker}' is missing or out of order.");
            previous = current;
        }
    }

    /// <summary>一般 limiter 應優先採用不可變使用者 ID，匿名請求才退回來源 IP。</summary>
    [Fact]
    public void GeneralRateLimiter_UsesImmutableAuthenticatedIdentity()
    {
        var source = File.ReadAllText(ProgramSourcePath());
        var nameIdentifier = source.IndexOf(
            "FindFirstValue(ClaimTypes.NameIdentifier)",
            StringComparison.Ordinal);
        var remoteIp = source.IndexOf(
            "context.Connection.RemoteIpAddress?.ToString()",
            nameIdentifier,
            StringComparison.Ordinal);

        Assert.True(nameIdentifier >= 0, "The immutable NameIdentifier partition key is missing.");
        Assert.True(remoteIp > nameIdentifier, "Remote IP must only be used after NameIdentifier fallback.");
    }

    /// <summary>解析 Program.cs 原始碼位置。</summary>
    private static string ProgramSourcePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "backend", "FamilyPortal.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "backend", "FamilyPortal.Api", "Program.cs");
    }
}
