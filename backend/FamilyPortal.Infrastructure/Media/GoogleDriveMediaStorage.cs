using FamilyPortal.Application.Common;
using FamilyPortal.Application.Interfaces;
using FamilyPortal.Application.Models;
using Microsoft.Extensions.Options;

namespace FamilyPortal.Infrastructure.Media;

/// <summary>以單一 private Google Drive folder 儲存入口網媒體。</summary>
public sealed class GoogleDriveMediaStorage(
    IOptions<GoogleDriveOptions> options,
    IGoogleDriveClient client,
    IImageInspector imageInspector) : IMediaStorage
{
    private readonly GoogleDriveOptions _options = ValidateOptions(options.Value);

    /// <inheritdoc />
    public string ProviderName => "GoogleDrive";

    /// <inheritdoc />
    public async Task<StoredMediaObject> StoreAsync(
        MediaUpload upload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (upload?.Content is null || !upload.Content.CanRead || !upload.Content.CanSeek)
        {
            throw new InvalidOperationException("媒體內容必須是可重設的唯讀資料流");
        }

        var originalPosition = upload.Content.Position;
        GoogleDriveObjectMetadata? stored = null;
        try
        {
            upload.Content.Position = 0;
            var inspection = await imageInspector.InspectAsync(upload.Content, cancellationToken);
            if (!InspectionIsTrusted(inspection))
            {
                throw new InvalidOperationException("媒體內容無法通過完整影像檢查");
            }

            var actualLength = upload.Content.Length;
            if (actualLength is <= 0 or > ValidationLimits.MaxMediaBytes)
            {
                throw new InvalidOperationException("媒體內容長度無效");
            }

            upload.Content.Position = 0;
            stored = await client.UploadAsync(
                upload.Content,
                inspection!.ContentType,
                CreateObjectName(inspection.ContentType),
                _options.FolderId,
                cancellationToken);
            if (!StoredMetadataIsTrusted(stored, inspection.ContentType, actualLength))
            {
                await TryDeleteUploadedAsync(stored?.ObjectId);
                throw new InvalidOperationException("Google Drive 上傳回應無效");
            }

            return new StoredMediaObject(
                stored.ObjectId,
                inspection.ContentType,
                actualLength,
                inspection.Width,
                inspection.Height);
        }
        finally
        {
            upload.Content.Position = originalPosition;
        }
    }

    /// <inheritdoc />
    public async Task<Stream> OpenReadAsync(
        string storageObjectId,
        CancellationToken cancellationToken)
    {
        ValidateObjectId(storageObjectId);
        var metadata = await client.GetMetadataAsync(storageObjectId, cancellationToken);
        EnsureReadableAndOwned(metadata);
        var content = await client.OpenReadAsync(storageObjectId, cancellationToken);
        if (content is null)
        {
            throw new InvalidOperationException("Google Drive 媒體內容無法讀取");
        }

        try
        {
            if (!content.CanRead ||
                !content.CanSeek ||
                content.Length is <= 0 or > ValidationLimits.MaxMediaBytes ||
                content.Length != metadata!.Length)
            {
                throw new InvalidOperationException("Google Drive 媒體內容長度無效");
            }

            content.Position = 0;
            return content;
        }
        catch
        {
            content.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        string storageObjectId,
        CancellationToken cancellationToken)
    {
        ValidateObjectId(storageObjectId);
        var metadata = await client.GetMetadataAsync(storageObjectId, cancellationToken);
        if (metadata is null)
        {
            return;
        }

        EnsureOwned(metadata);
        try
        {
            await client.DeleteAsync(storageObjectId, cancellationToken);
        }
        catch (GoogleDriveObjectNotFoundException)
        {
            // 在 metadata 與 delete 間被移除仍視為冪等成功。
        }
    }

    private bool StoredMetadataIsTrusted(
        GoogleDriveObjectMetadata? metadata,
        string contentType,
        long actualLength) =>
        metadata is not null &&
        !string.IsNullOrWhiteSpace(metadata.ObjectId) &&
        !metadata.Trashed &&
        metadata.ParentIds.Contains(_options.FolderId, StringComparer.Ordinal) &&
        string.Equals(metadata.ContentType, contentType, StringComparison.OrdinalIgnoreCase) &&
        metadata.Length == actualLength;

    private void EnsureOwned(GoogleDriveObjectMetadata? metadata)
    {
        if (metadata is null ||
            !metadata.ParentIds.Contains(_options.FolderId, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Google Drive 媒體物件不屬於設定資料夾");
        }
    }

    private void EnsureReadableAndOwned(GoogleDriveObjectMetadata? metadata)
    {
        EnsureOwned(metadata);
        if (metadata!.Trashed ||
            metadata.ContentType is not ("image/jpeg" or "image/png" or "image/webp") ||
            metadata.Length is <= 0 or > ValidationLimits.MaxMediaBytes)
        {
            throw new InvalidOperationException("Google Drive 媒體 metadata 無效");
        }
    }

    private async Task TryDeleteUploadedAsync(string? objectId)
    {
        if (string.IsNullOrWhiteSpace(objectId))
        {
            return;
        }

        try
        {
            await client.DeleteAsync(objectId, CancellationToken.None);
        }
        catch (Exception)
        {
            // 補償失敗不覆蓋原本的無效上傳回應。
        }
    }

    private static bool InspectionIsTrusted(ImageInspectionResult? inspection) =>
        inspection is not null &&
        inspection.Width > 0 &&
        inspection.Height > 0 &&
        inspection.ContentType is "image/jpeg" or "image/png" or "image/webp";

    private static string CreateObjectName(string contentType)
    {
        var extension = contentType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => throw new InvalidOperationException("不支援的媒體類型")
        };
        return $"{Guid.NewGuid():N}{extension}";
    }

    private static GoogleDriveOptions ValidateOptions(GoogleDriveOptions options)
    {
        if (!options.Enabled ||
            string.IsNullOrWhiteSpace(options.ClientId) ||
            string.IsNullOrWhiteSpace(options.ClientSecret) ||
            string.IsNullOrWhiteSpace(options.RefreshToken) ||
            string.IsNullOrWhiteSpace(options.FolderId))
        {
            throw new InvalidOperationException("Google Drive 媒體儲存設定不完整");
        }

        return options;
    }

    private static void ValidateObjectId(string objectId)
    {
        if (string.IsNullOrWhiteSpace(objectId))
        {
            throw new InvalidOperationException("Google Drive 媒體物件識別碼無效");
        }
    }
}
