using FamilyPortal.Application.Interfaces;
using FamilyPortal.Application.Models;
using FamilyPortal.Infrastructure;
using FamilyPortal.Infrastructure.Media;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FamilyPortal.Security.Tests.Infrastructure;

/// <summary>驗證未設定媒體供應商時的 fail-closed 行為。</summary>
public sealed class UnavailableMediaStorageTests
{
    /// <summary>預設 DI 必須可啟動，並解析為明確拒絕操作的 unavailable adapter。</summary>
    [Fact]
    public async Task DefaultRegistration_ResolvesFailClosedAdapter()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(new ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var storage = scope.ServiceProvider.GetRequiredService<IMediaStorage>();

        Assert.IsType<UnavailableMediaStorage>(storage);
        Assert.Equal("Unavailable", storage.ProviderName);
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.StoreAsync(
            new MediaUpload(Stream.Null, "file.png", "image/png", 1),
            CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.OpenReadAsync(
            "object-1",
            CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.DeleteAsync(
            "object-1",
            CancellationToken.None));
    }

    /// <summary>呼叫端先註冊正式 provider 時，fallback 不得覆寫它。</summary>
    [Fact]
    public void ExistingProvider_IsNotOverriddenByFallback()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMediaStorage, FakeMediaStorage>();
        services.AddInfrastructure(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<FakeMediaStorage>(scope.ServiceProvider.GetRequiredService<IMediaStorage>());
    }

    /// <summary>啟用 Google Drive 時 DI 必須以 Google adapter 取代 unavailable fallback。</summary>
    [Fact]
    public void GoogleDriveEnabled_ResolvesGoogleAdapter()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GoogleDrive:Enabled"] = "true",
                ["GoogleDrive:ClientId"] = "client-id",
                ["GoogleDrive:ClientSecret"] = "client-secret",
                ["GoogleDrive:RefreshToken"] = "refresh-token",
                ["GoogleDrive:FolderId"] = "folder-id"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<GoogleDriveMediaStorage>(
            scope.ServiceProvider.GetRequiredService<IMediaStorage>());
    }

    private sealed class FakeMediaStorage : IMediaStorage
    {
        /// <inheritdoc />
        public string ProviderName => "Fake";

        /// <inheritdoc />
        public Task<StoredMediaObject> StoreAsync(
            MediaUpload upload,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        /// <inheritdoc />
        public Task<Stream> OpenReadAsync(
            string storageObjectId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        /// <inheritdoc />
        public Task DeleteAsync(
            string storageObjectId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
