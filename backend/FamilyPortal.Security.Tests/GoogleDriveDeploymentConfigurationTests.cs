using Xunit;

namespace FamilyPortal.Security.Tests;

/// <summary>驗證 Docker Compose 將 host environment 明確傳入 backend Google Drive 設定。</summary>
public sealed class GoogleDriveDeploymentConfigurationTests
{
    private static readonly string[] RequiredMappings =
    [
        "GoogleDrive__Enabled: ${GOOGLE_DRIVE_ENABLED:-false}",
        "GoogleDrive__ClientId: ${GOOGLE_DRIVE_CLIENT_ID:-}",
        "GoogleDrive__ClientSecret: ${GOOGLE_DRIVE_CLIENT_SECRET:-}",
        "GoogleDrive__RefreshToken: ${GOOGLE_DRIVE_REFRESH_TOKEN:-}",
        "GoogleDrive__FolderId: ${GOOGLE_DRIVE_FOLDER_ID:-}"
    ];

    /// <summary>開發與正式 compose 都必須包含五個無 secret literal 的 backend mappings。</summary>
    [Theory]
    [InlineData("docker-compose.yml")]
    [InlineData("docker-compose.production.yml")]
    public void BackendEnvironment_MapsAllGoogleDriveSettingsWithoutSecretLiterals(
        string composeFileName)
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRootPath(), composeFileName));
        var googleDriveLines = source.Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith("GoogleDrive__", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(RequiredMappings.Length, googleDriveLines.Length);
        Assert.Equal(RequiredMappings.Order(), googleDriveLines.Order());
        Assert.DoesNotContain("client-id", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("client-secret", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refresh-token", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("folder-id", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string RepositoryRootPath()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDirectory is not null)
        {
            if (File.Exists(Path.Combine(
                    currentDirectory.FullName,
                    "backend",
                    "FamilyPortal.slnx")))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Unable to locate repository root from '{AppContext.BaseDirectory}'.");
    }
}
