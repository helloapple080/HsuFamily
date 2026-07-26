using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs.Media;
using FamilyPortal.Application.Interfaces;
using FamilyPortal.Application.Models;
using FamilyPortal.Domain.Entities;
using FamilyPortal.Domain.Enums;

namespace FamilyPortal.Application.Services;

/// <summary>協調媒體驗證、可見性授權、儲存與中繼資料補償。</summary>
public sealed class MediaService : IMediaService
{
    private const int AvatarSlotCount = 1;

    private readonly IMediaRepository _repository;
    private readonly IMediaStorage _storage;
    private readonly IImageInspector _imageInspector;

    /// <summary>建立媒體 Application service。</summary>
    /// <param name="repository">媒體中繼資料邊界。</param>
    /// <param name="storage">供應商無關的媒體儲存邊界。</param>
    /// <param name="imageInspector">完整解碼影像的供應商無關邊界。</param>
    public MediaService(
        IMediaRepository repository,
        IMediaStorage storage,
        IImageInspector imageInspector)
    {
        _repository = repository;
        _storage = storage;
        _imageInspector = imageInspector;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<MediaResponse>> UploadAsync(
        UploadMediaRequest request,
        string userId,
        CancellationToken cancellationToken)
    {
        if (request is null || request.Upload is null)
        {
            return ValidationFailure<MediaResponse>("缺少媒體上傳內容");
        }

        if (!IsValidObjectId(request.OwnerEntityId) || !IsValidObjectId(userId))
        {
            return ValidationFailure<MediaResponse>("無效的媒體關聯識別碼");
        }

        if (!Enum.IsDefined(request.Purpose) || !Enum.IsDefined(request.Visibility))
        {
            return ValidationFailure<MediaResponse>("無效的媒體用途或可見性");
        }

        ValidatedMediaUpload? validated;
        try
        {
            validated = await SecureImageUploadValidator.ValidateAsync(
                request.Upload,
                _imageInspector,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return ValidationFailure<MediaResponse>("無法讀取媒體上傳內容");
        }

        if (validated is null)
        {
            return ValidationFailure<MediaResponse>("媒體必須是 10 MB 以內且完整的 JPEG、PNG 或 WebP 影像");
        }

        using var validatedContent = validated.Upload.Content;
        string providerName;
        try
        {
            providerName = _storage.ProviderName;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return ConflictFailure<MediaResponse>("媒體儲存供應商設定無效");
        }

        if (string.IsNullOrWhiteSpace(providerName))
        {
            return ConflictFailure<MediaResponse>("媒體儲存供應商設定無效");
        }

        StoredMediaObject stored;
        try
        {
            stored = await _storage.StoreAsync(validated.Upload, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return ConflictFailure<MediaResponse>("媒體儲存失敗");
        }

        if (!StoredObjectMatches(stored, validated))
        {
            await TryCompensateStorageAsync(stored?.ObjectId);
            return ConflictFailure<MediaResponse>("媒體儲存回應無效");
        }

        var slotCount = request.Purpose == MediaPurpose.MemberAvatar
            ? AvatarSlotCount
            : ValidationLimits.MaxMediaGalleryItems;
        for (var slot = 0; slot < slotCount; slot++)
        {
            var asset = BuildAsset(request, userId, providerName, stored.ObjectId, validated, slot);
            try
            {
                var createResult = await _repository.TryCreateAsync(asset, cancellationToken);
                if (createResult == MediaCreateResult.Created)
                {
                    return ServiceResult<MediaResponse>.Success(MapToResponse(asset));
                }
            }
            catch (OperationCanceledException)
            {
                await TryCompensateStorageAsync(stored.ObjectId);
                throw;
            }
            catch (Exception)
            {
                await TryCompensateStorageAsync(stored.ObjectId);
                return ConflictFailure<MediaResponse>("媒體中繼資料寫入失敗");
            }
        }

        await TryCompensateStorageAsync(stored.ObjectId);
        return ConflictFailure<MediaResponse>("此項目的媒體數量已達上限");
    }

    /// <inheritdoc />
    public async Task<ServiceResult<MediaContentResponse>> OpenReadAsync(
        string mediaId,
        bool isAuthenticated,
        CancellationToken cancellationToken)
    {
        if (!IsValidObjectId(mediaId))
        {
            return ValidationFailure<MediaContentResponse>("無效的媒體識別碼");
        }

        MediaAsset? asset;
        try
        {
            asset = await _repository.GetByIdAsync(mediaId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return ConflictFailure<MediaContentResponse>("無法讀取媒體中繼資料");
        }

        if (asset is null)
        {
            return ServiceResult<MediaContentResponse>.Failure(ServiceError.NotFound, "媒體不存在");
        }

        var authorizationError = asset.Visibility switch
        {
            MediaVisibility.Public => ServiceError.None,
            MediaVisibility.FamilyOnly when isAuthenticated => ServiceError.None,
            MediaVisibility.FamilyOnly => ServiceError.Unauthorized,
            _ => ServiceError.Forbidden
        };
        if (authorizationError != ServiceError.None)
        {
            return ServiceResult<MediaContentResponse>.Failure(
                authorizationError,
                authorizationError == ServiceError.Unauthorized
                    ? "需要登入才能讀取此媒體"
                    : "媒體可見性無效");
        }

        if (asset.SizeBytes is <= 0 or > ValidationLimits.MaxMediaBytes)
        {
            return ConflictFailure<MediaContentResponse>("媒體內容長度無效");
        }

        try
        {
            using var providerContent = await _storage.OpenReadAsync(
                asset.StorageObjectId,
                cancellationToken);
            var content = await CopyProviderContentAsync(
                providerContent,
                asset.SizeBytes,
                cancellationToken);
            if (content is null)
            {
                return ConflictFailure<MediaContentResponse>("媒體內容長度不符");
            }

            return ServiceResult<MediaContentResponse>.Success(
                new MediaContentResponse(content, asset.ContentType, asset.SizeBytes));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return ConflictFailure<MediaContentResponse>("媒體內容目前無法讀取");
        }
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> DeleteAsync(
        string mediaId,
        string userId,
        CancellationToken cancellationToken)
    {
        if (!IsValidObjectId(mediaId) || !IsValidObjectId(userId))
        {
            return ValidationFailure<bool>("無效的媒體或使用者識別碼");
        }

        MediaAsset? asset;
        try
        {
            asset = await _repository.GetByIdAsync(mediaId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return ConflictFailure<bool>("無法讀取媒體中繼資料");
        }

        if (asset is null)
        {
            return ServiceResult<bool>.Failure(ServiceError.NotFound, "媒體不存在");
        }

        try
        {
            var softDeleted = await _repository.SoftDeleteAsync(mediaId, cancellationToken);
            if (!softDeleted)
            {
                return ServiceResult<bool>.Failure(ServiceError.NotFound, "媒體不存在");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return ConflictFailure<bool>("媒體軟刪除失敗");
        }

        try
        {
            await _storage.DeleteAsync(asset.StorageObjectId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return ConflictFailure<bool>("媒體已軟刪除，儲存內容將稍後重試清理");
        }

        try
        {
            await _repository.MarkCleanupCompleteAsync(mediaId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return ConflictFailure<bool>("儲存內容已刪除，清理狀態將稍後重試更新");
        }

        return ServiceResult<bool>.Success(true);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<int>> CleanupPendingAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        var boundedLimit = Math.Clamp(limit, 1, ValidationLimits.MaxPageSize);
        List<MediaAsset> pending;
        try
        {
            pending = await _repository.GetPendingCleanupAsync(boundedLimit, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return ConflictFailure<int>("無法讀取待清理媒體");
        }

        var completed = 0;
        foreach (var asset in pending.Take(boundedLimit))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await _storage.DeleteAsync(asset.StorageObjectId, cancellationToken);
                await _repository.MarkCleanupCompleteAsync(asset.Id, cancellationToken);
                completed++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // 單一項清理失敗保留 pending，並繼續處理同批其他媒體。
            }
        }

        return ServiceResult<int>.Success(completed);
    }

    private static bool IsValidObjectId(string? id)
    {
        if (id is not { Length: 24 })
        {
            return false;
        }

        foreach (var character in id)
        {
            if (character is not (>= '0' and <= '9') and
                not (>= 'a' and <= 'f') and
                not (>= 'A' and <= 'F'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool StoredObjectMatches(StoredMediaObject? stored, ValidatedMediaUpload validated) =>
        stored is not null &&
        !string.IsNullOrWhiteSpace(stored.ObjectId) &&
        string.Equals(stored.ContentType, validated.ContentType, StringComparison.OrdinalIgnoreCase) &&
        stored.Length == validated.Length &&
        stored.Width == validated.Width &&
        stored.Height == validated.Height;

    private MediaAsset BuildAsset(
        UploadMediaRequest request,
        string userId,
        string providerName,
        string storageObjectId,
        ValidatedMediaUpload validated,
        int slot) => new()
        {
            StorageProvider = providerName,
            StorageObjectId = storageObjectId,
            OriginalFileName = validated.Upload.FileName,
            ContentType = validated.ContentType,
            SizeBytes = validated.Length,
            Width = validated.Width,
            Height = validated.Height,
            Visibility = request.Visibility,
            Purpose = request.Purpose,
            Slot = slot,
            OwnerEntityId = request.OwnerEntityId,
            UploadedBy = userId
        };

    private static async Task<MemoryStream?> CopyProviderContentAsync(
        Stream providerContent,
        long expectedLength,
        CancellationToken cancellationToken)
    {
        var copy = new MemoryStream((int)expectedLength);
        var buffer = new byte[81920];
        long total = 0;
        try
        {
            while (true)
            {
                var read = await providerContent.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > expectedLength || total > ValidationLimits.MaxMediaBytes)
                {
                    copy.Dispose();
                    return null;
                }

                await copy.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            if (total != expectedLength)
            {
                copy.Dispose();
                return null;
            }

            var ownedReadOnlyContent = new MemoryStream(copy.ToArray(), writable: false);
            copy.Dispose();
            return ownedReadOnlyContent;
        }
        catch
        {
            copy.Dispose();
            throw;
        }
    }

    private async Task TryCompensateStorageAsync(string? storageObjectId)
    {
        if (string.IsNullOrWhiteSpace(storageObjectId))
        {
            return;
        }

        try
        {
            await _storage.DeleteAsync(storageObjectId, CancellationToken.None);
        }
        catch (Exception)
        {
            // 補償失敗不得覆蓋原始的持久化失敗。
        }
    }

    private static MediaResponse MapToResponse(MediaAsset asset) => new(
        asset.Id,
        asset.OriginalFileName,
        asset.ContentType,
        asset.SizeBytes,
        asset.Width,
        asset.Height,
        asset.Visibility,
        asset.Purpose,
        asset.OwnerEntityId);

    private static ServiceResult<T> ValidationFailure<T>(string message) =>
        ServiceResult<T>.Failure(ServiceError.Validation, message);

    private static ServiceResult<T> ConflictFailure<T>(string message) =>
        ServiceResult<T>.Failure(ServiceError.Conflict, message);

}
