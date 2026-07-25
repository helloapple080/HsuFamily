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
    private const int AvatarLimit = 1;
    private const int GalleryLimit = 30;

    private readonly IMediaRepository _repository;
    private readonly IMediaStorage _storage;

    /// <summary>建立媒體 Application service。</summary>
    /// <param name="repository">媒體中繼資料邊界。</param>
    /// <param name="storage">供應商無關的媒體儲存邊界。</param>
    public MediaService(IMediaRepository repository, IMediaStorage storage)
    {
        _repository = repository;
        _storage = storage;
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

        long activeCount;
        try
        {
            activeCount = await _repository.CountActiveByOwnerAndPurposeAsync(
                request.OwnerEntityId,
                request.Purpose,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return ConflictFailure<MediaResponse>("無法確認媒體數量上限");
        }

        if (activeCount >= GetPurposeLimit(request.Purpose))
        {
            return ConflictFailure<MediaResponse>("此項目的媒體數量已達上限");
        }

        ValidatedMediaUpload? validated;
        try
        {
            validated = await SecureImageUploadValidator.ValidateAsync(
                request.Upload,
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

        var asset = new MediaAsset
        {
            StorageProvider = _storage.ProviderName,
            StorageObjectId = stored.ObjectId,
            OriginalFileName = validated.Upload.FileName,
            ContentType = validated.ContentType,
            SizeBytes = validated.Length,
            Width = validated.Width,
            Height = validated.Height,
            Visibility = request.Visibility,
            Purpose = request.Purpose,
            OwnerEntityId = request.OwnerEntityId,
            UploadedBy = userId
        };

        try
        {
            await _repository.CreateAsync(asset, cancellationToken);
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

        return ServiceResult<MediaResponse>.Success(MapToResponse(asset));
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

        if (asset.Visibility != MediaVisibility.Public && !isAuthenticated)
        {
            return ServiceResult<MediaContentResponse>.Failure(ServiceError.Unauthorized, "需要登入才能讀取此媒體");
        }

        try
        {
            var content = await _storage.OpenReadAsync(asset.StorageObjectId, cancellationToken);
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

    private static int GetPurposeLimit(MediaPurpose purpose) =>
        purpose == MediaPurpose.MemberAvatar ? AvatarLimit : GalleryLimit;

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
