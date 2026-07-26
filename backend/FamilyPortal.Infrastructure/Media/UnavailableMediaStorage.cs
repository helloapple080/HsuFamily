using FamilyPortal.Application.Interfaces;
using FamilyPortal.Application.Models;

namespace FamilyPortal.Infrastructure.Media;

/// <summary>未設定媒體供應商時的 fail-closed 儲存 adapter。</summary>
public sealed class UnavailableMediaStorage : IMediaStorage
{
    /// <inheritdoc />
    public string ProviderName => "Unavailable";

    /// <inheritdoc />
    public Task<StoredMediaObject> StoreAsync(
        MediaUpload upload,
        CancellationToken cancellationToken) =>
        Task.FromException<StoredMediaObject>(Unavailable());

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(
        string storageObjectId,
        CancellationToken cancellationToken) =>
        Task.FromException<Stream>(Unavailable());

    /// <inheritdoc />
    public Task DeleteAsync(
        string storageObjectId,
        CancellationToken cancellationToken) =>
        Task.FromException(Unavailable());

    private static InvalidOperationException Unavailable() =>
        new("媒體儲存供應商尚未設定");
}
