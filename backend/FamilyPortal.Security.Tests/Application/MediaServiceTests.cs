using System.Buffers.Binary;
using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs.Media;
using FamilyPortal.Application.Interfaces;
using FamilyPortal.Application.Models;
using FamilyPortal.Application.Services;
using FamilyPortal.Domain.Entities;
using FamilyPortal.Domain.Enums;
using MongoDB.Bson;
using Xunit;

namespace FamilyPortal.Security.Tests.Application;

/// <summary>驗證媒體驗證、權限、數量上限與儲存補償流程。</summary>
public sealed class MediaServiceTests
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    /// <summary>公開請求與回應 DTO 不得暴露儲存物件識別碼。</summary>
    [Fact]
    public void MediaDtos_DoNotExposeStorageObjectIdentifiers()
    {
        var uploadProperties = typeof(UploadMediaRequest).GetProperties();
        var responseProperties = typeof(MediaResponse).GetProperties();

        Assert.Equal(
            ["OwnerEntityId", "Purpose", "Upload", "Visibility"],
            uploadProperties.Select(property => property.Name).Order().ToArray());
        Assert.DoesNotContain(
            responseProperties,
            property => property.Name.Contains("Storage", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>簽章、宣告 MIME 與尺寸正確時應儲存影像與中繼資料。</summary>
    [Theory]
    [InlineData("image/jpeg", 41, 23)]
    [InlineData("image/png", 41, 23)]
    [InlineData("image/webp", 41, 23)]
    public async Task UploadAsync_ValidImageStoresContentAndMetadata(
        string contentType,
        int width,
        int height)
    {
        var bytes = CreateImage(contentType, width, height);
        var repository = new FakeMediaRepository();
        var storage = new FakeMediaStorage
        {
            StoreResult = new StoredMediaObject(
                "storage-1",
                contentType,
                bytes.LongLength,
                width,
                height)
        };
        var service = new MediaService(repository, storage);
        var request = CreateRequest(bytes, contentType);
        var userId = ObjectId.GenerateNewId().ToString();

        var result = await service.UploadAsync(request, userId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(contentType, result.Value.ContentType);
        Assert.Equal(width, result.Value.Width);
        Assert.Equal(height, result.Value.Height);
        Assert.DoesNotContain("storage-1", result.Value.ToString());
        Assert.NotNull(repository.Created);
        Assert.Equal("storage-1", repository.Created!.StorageObjectId);
        Assert.Equal(storage.ProviderName, repository.Created.StorageProvider);
        Assert.Equal(userId, repository.Created.UploadedBy);
        Assert.Equal(request.OwnerEntityId, repository.Created.OwnerEntityId);
        Assert.NotNull(storage.StoredUpload);
        Assert.Equal(contentType, storage.StoredUpload!.DeclaredContentType);
        Assert.Equal(0, storage.StoreInputPosition);
    }

    /// <summary>不支援的檔案簽章不得進入儲存層。</summary>
    [Fact]
    public async Task UploadAsync_UnsupportedSignatureReturnsValidation()
    {
        var repository = new FakeMediaRepository();
        var storage = new FakeMediaStorage();
        var service = new MediaService(repository, storage);
        var request = CreateRequest("<svg></svg>"u8.ToArray(), "image/png");

        var result = await service.UploadAsync(
            request,
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Null(storage.StoredUpload);
        Assert.Null(repository.Created);
    }

    /// <summary>宣告 MIME 與實際簽章不同時不得儲存。</summary>
    [Fact]
    public async Task UploadAsync_DeclaredContentTypeMismatchReturnsValidation()
    {
        var repository = new FakeMediaRepository();
        var storage = new FakeMediaStorage();
        var service = new MediaService(repository, storage);
        var request = CreateRequest(CreatePng(20, 10), "image/jpeg");

        var result = await service.UploadAsync(
            request,
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Null(storage.StoredUpload);
    }

    /// <summary>空白或截斷的影像不得進入儲存層。</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(23)]
    public async Task UploadAsync_EmptyOrTruncatedImageReturnsValidation(int retainedBytes)
    {
        var repository = new FakeMediaRepository();
        var storage = new FakeMediaStorage();
        var service = new MediaService(repository, storage);
        var bytes = CreatePng(20, 10).Take(retainedBytes).ToArray();
        var request = CreateRequest(bytes, "image/png");

        var result = await service.UploadAsync(
            request,
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Null(storage.StoredUpload);
    }

    /// <summary>實際內容或宣告長度超過 10 MB 時均應拒絕。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UploadAsync_FileOverTenMegabytesReturnsValidation(bool underreportedLength)
    {
        var repository = new FakeMediaRepository();
        var storage = new FakeMediaStorage();
        var service = new MediaService(repository, storage);
        var bytes = new byte[MaxUploadBytes + 1];
        CreatePng(20, 10).CopyTo(bytes, 0);
        var declaredLength = underreportedLength ? MaxUploadBytes : bytes.LongLength;
        var request = CreateRequest(bytes, "image/png", declaredLength);

        var result = await service.UploadAsync(
            request,
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Null(storage.StoredUpload);
    }

    /// <summary>擁有者已達用途上限時不得儲存新影像。</summary>
    [Theory]
    [InlineData(MediaPurpose.MemberAvatar, 1)]
    [InlineData(MediaPurpose.Worship, 30)]
    [InlineData(MediaPurpose.Itinerary, 30)]
    public async Task UploadAsync_OwnerAtPurposeLimitReturnsConflict(
        MediaPurpose purpose,
        long currentCount)
    {
        var repository = new FakeMediaRepository { ActiveCount = currentCount };
        var storage = new FakeMediaStorage();
        var service = new MediaService(repository, storage);
        var request = CreateRequest(CreatePng(20, 10), "image/png", purpose: purpose);

        var result = await service.UploadAsync(
            request,
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.Null(storage.StoredUpload);
    }

    /// <summary>擁有者或上傳者不是 ObjectId 時不得進入持久層。</summary>
    [Theory]
    [InlineData("invalid", true)]
    [InlineData("invalid", false)]
    public async Task UploadAsync_InvalidOwnerOrUserIdReturnsValidation(
        string invalidId,
        bool invalidOwner)
    {
        var repository = new FakeMediaRepository();
        var storage = new FakeMediaStorage();
        var service = new MediaService(repository, storage);
        var request = CreateRequest(CreatePng(20, 10), "image/png");
        var userId = ObjectId.GenerateNewId().ToString();
        if (invalidOwner)
        {
            request = request with { OwnerEntityId = invalidId };
        }
        else
        {
            userId = invalidId;
        }

        var result = await service.UploadAsync(request, userId, CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Null(storage.StoredUpload);
        Assert.Null(repository.Created);
    }

    /// <summary>儲存成功但中繼資料寫入失敗時，應以不受請求取消影響的權杖補償刪除。</summary>
    [Fact]
    public async Task UploadAsync_RepositoryFailureCompensatesWithoutLeakingProviderError()
    {
        var repository = new FakeMediaRepository
        {
            CreateException = new InvalidOperationException("mongo-secret-detail")
        };
        var storage = new FakeMediaStorage();
        var service = new MediaService(repository, storage);
        using var source = new CancellationTokenSource();

        var result = await service.UploadAsync(
            CreateRequest(CreatePng(20, 10), "image/png"),
            ObjectId.GenerateNewId().ToString(),
            source.Token);

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.DoesNotContain("mongo-secret-detail", result.Message);
        Assert.Equal("storage-1", storage.DeletedObjectId);
        Assert.Equal(CancellationToken.None, storage.DeleteCancellationToken);
    }

    /// <summary>補償刪除本身失敗時不得覆蓋原始的中繼資料失敗。</summary>
    [Fact]
    public async Task UploadAsync_CompensationFailurePreservesOriginalFailure()
    {
        var repository = new FakeMediaRepository
        {
            CreateException = new InvalidOperationException("metadata-secret")
        };
        var storage = new FakeMediaStorage
        {
            DeleteException = new InvalidOperationException("provider-secret")
        };
        var service = new MediaService(repository, storage);

        var result = await service.UploadAsync(
            CreateRequest(CreatePng(20, 10), "image/png"),
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.Equal("媒體中繼資料寫入失敗", result.Message);
        Assert.DoesNotContain("metadata-secret", result.Message);
        Assert.DoesNotContain("provider-secret", result.Message);
    }

    /// <summary>儲存上傳的取消不得轉換成一般錯誤。</summary>
    [Fact]
    public async Task UploadAsync_StorageCancellationRethrows()
    {
        using var source = new CancellationTokenSource();
        var storage = new FakeMediaStorage
        {
            StoreException = new OperationCanceledException(source.Token)
        };
        var service = new MediaService(new FakeMediaRepository(), storage);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.UploadAsync(
            CreateRequest(CreatePng(20, 10), "image/png"),
            ObjectId.GenerateNewId().ToString(),
            source.Token));
    }

    /// <summary>中繼資料寫入取消時仍應補償，並在補償後繼續傳遞取消。</summary>
    [Fact]
    public async Task UploadAsync_RepositoryCancellationCompensatesThenRethrows()
    {
        using var source = new CancellationTokenSource();
        var repository = new FakeMediaRepository
        {
            CreateException = new OperationCanceledException(source.Token)
        };
        var storage = new FakeMediaStorage();
        var service = new MediaService(repository, storage);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.UploadAsync(
            CreateRequest(CreatePng(20, 10), "image/png"),
            ObjectId.GenerateNewId().ToString(),
            source.Token));

        Assert.Equal("storage-1", storage.DeletedObjectId);
        Assert.Equal(CancellationToken.None, storage.DeleteCancellationToken);
    }

    /// <summary>匿名使用者不得讀取 FamilyOnly 媒體。</summary>
    [Fact]
    public async Task OpenReadAsync_PrivateAnonymousReturnsUnauthorizedWithoutStorageRead()
    {
        var repository = new FakeMediaRepository { Stored = CreateAsset(MediaVisibility.FamilyOnly) };
        var storage = new FakeMediaStorage();
        var service = new MediaService(repository, storage);

        var result = await service.OpenReadAsync(repository.Stored.Id, false, CancellationToken.None);

        Assert.Equal(ServiceError.Unauthorized, result.Error);
        Assert.Null(storage.OpenedObjectId);
    }

    /// <summary>未知可見性必須對匿名讀取 fail closed。</summary>
    [Fact]
    public async Task OpenReadAsync_UnknownVisibilityAnonymousReturnsUnauthorized()
    {
        var repository = new FakeMediaRepository { Stored = CreateAsset((MediaVisibility)999) };
        var storage = new FakeMediaStorage();
        var service = new MediaService(repository, storage);

        var result = await service.OpenReadAsync(repository.Stored.Id, false, CancellationToken.None);

        Assert.Equal(ServiceError.Unauthorized, result.Error);
        Assert.Null(storage.OpenedObjectId);
    }

    /// <summary>公開媒體應允許匿名讀取並回傳不含供應商資訊的內容描述。</summary>
    [Fact]
    public async Task OpenReadAsync_PublicAnonymousReturnsContentDescriptor()
    {
        var repository = new FakeMediaRepository { Stored = CreateAsset(MediaVisibility.Public) };
        var storage = new FakeMediaStorage { ReadStream = new MemoryStream([1, 2, 3]) };
        var service = new MediaService(repository, storage);

        var result = await service.OpenReadAsync(repository.Stored.Id, false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(storage.ReadStream, result.Value!.Content);
        Assert.Equal(repository.Stored.ContentType, result.Value.ContentType);
        Assert.Equal(repository.Stored.SizeBytes, result.Value.Length);
        Assert.Equal(repository.Stored.StorageObjectId, storage.OpenedObjectId);
    }

    /// <summary>儲存讀取失敗不得暴露供應商錯誤詳情。</summary>
    [Fact]
    public async Task OpenReadAsync_StorageFailureReturnsProviderNeutralError()
    {
        var repository = new FakeMediaRepository { Stored = CreateAsset(MediaVisibility.Public) };
        var storage = new FakeMediaStorage
        {
            ReadException = new InvalidOperationException("provider-secret-detail")
        };
        var service = new MediaService(repository, storage);

        var result = await service.OpenReadAsync(repository.Stored.Id, false, CancellationToken.None);

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.DoesNotContain("provider-secret-detail", result.Message);
    }

    /// <summary>儲存讀取的取消不得轉換成一般錯誤。</summary>
    [Fact]
    public async Task OpenReadAsync_StorageCancellationRethrows()
    {
        using var source = new CancellationTokenSource();
        var repository = new FakeMediaRepository { Stored = CreateAsset(MediaVisibility.Public) };
        var storage = new FakeMediaStorage
        {
            ReadException = new OperationCanceledException(source.Token)
        };
        var service = new MediaService(repository, storage);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.OpenReadAsync(
            repository.Stored.Id,
            false,
            source.Token));
    }

    /// <summary>媒體 ID 不是 ObjectId 時不得查詢持久層。</summary>
    [Fact]
    public async Task OpenReadAsync_InvalidMediaIdReturnsValidation()
    {
        var repository = new FakeMediaRepository();
        var storage = new FakeMediaStorage();
        var service = new MediaService(repository, storage);

        var result = await service.OpenReadAsync("invalid", true, CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Null(repository.RequestedId);
    }

    /// <summary>刪除應先軟刪除，再刪除儲存物件並完成清理狀態。</summary>
    [Fact]
    public async Task DeleteAsync_SoftDeletesBeforeStorageAndMarksCleanupComplete()
    {
        var repository = new FakeMediaRepository { Stored = CreateAsset(MediaVisibility.FamilyOnly) };
        var storage = new FakeMediaStorage();
        var operations = new List<string>();
        repository.Operations = operations;
        storage.Operations = operations;
        var service = new MediaService(repository, storage);

        var result = await service.DeleteAsync(
            repository.Stored.Id,
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["soft-delete", "storage-delete", "cleanup-complete"], operations);
        Assert.Equal(repository.Stored.Id, repository.CleanupCompletedId);
    }

    /// <summary>儲存刪除失敗時應回傳統一錯誤，並保留 CleanupPending 供重試。</summary>
    [Fact]
    public async Task DeleteAsync_StorageFailureLeavesCleanupPending()
    {
        var repository = new FakeMediaRepository { Stored = CreateAsset(MediaVisibility.FamilyOnly) };
        var storage = new FakeMediaStorage
        {
            DeleteException = new InvalidOperationException("provider-secret-detail")
        };
        var service = new MediaService(repository, storage);

        var result = await service.DeleteAsync(
            repository.Stored.Id,
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.DoesNotContain("provider-secret-detail", result.Message);
        Assert.True(repository.SoftDeleted);
        Assert.Null(repository.CleanupCompletedId);
    }

    /// <summary>儲存刪除的取消不得被吞掉，且記錄必須保留待清理狀態。</summary>
    [Fact]
    public async Task DeleteAsync_StorageCancellationRethrowsAndLeavesCleanupPending()
    {
        using var source = new CancellationTokenSource();
        var repository = new FakeMediaRepository { Stored = CreateAsset(MediaVisibility.FamilyOnly) };
        var storage = new FakeMediaStorage
        {
            DeleteException = new OperationCanceledException(source.Token)
        };
        var service = new MediaService(repository, storage);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.DeleteAsync(
            repository.Stored.Id,
            ObjectId.GenerateNewId().ToString(),
            source.Token));

        Assert.True(repository.SoftDeleted);
        Assert.Null(repository.CleanupCompletedId);
    }

    /// <summary>刪除使用者 ID 無效時不得讀取或變更媒體。</summary>
    [Fact]
    public async Task DeleteAsync_InvalidUserIdReturnsValidation()
    {
        var repository = new FakeMediaRepository { Stored = CreateAsset(MediaVisibility.FamilyOnly) };
        var storage = new FakeMediaStorage();
        var service = new MediaService(repository, storage);

        var result = await service.DeleteAsync(repository.Stored.Id, "invalid", CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Null(repository.RequestedId);
        Assert.False(repository.SoftDeleted);
    }

    private static UploadMediaRequest CreateRequest(
        byte[] bytes,
        string contentType,
        long? declaredLength = null,
        MediaPurpose purpose = MediaPurpose.MemberAvatar) => new(
            purpose,
            ObjectId.GenerateNewId().ToString(),
            MediaVisibility.FamilyOnly,
            new MediaUpload(
                new MemoryStream(bytes),
                "family-photo.png",
                contentType,
                declaredLength ?? bytes.LongLength));

    private static MediaAsset CreateAsset(MediaVisibility visibility) => new()
    {
        StorageProvider = "Fake",
        StorageObjectId = "storage-1",
        OriginalFileName = "family-photo.png",
        ContentType = "image/png",
        SizeBytes = 123,
        Width = 20,
        Height = 10,
        Visibility = visibility,
        Purpose = MediaPurpose.MemberAvatar,
        OwnerEntityId = ObjectId.GenerateNewId().ToString(),
        UploadedBy = ObjectId.GenerateNewId().ToString()
    };

    private static byte[] CreateImage(string contentType, int width, int height) => contentType switch
    {
        "image/jpeg" => CreateJpeg(width, height),
        "image/png" => CreatePng(width, height),
        "image/webp" => CreateWebP(width, height),
        _ => throw new ArgumentOutOfRangeException(nameof(contentType))
    };

    private static byte[] CreatePng(int width, int height)
    {
        var bytes = new byte[57];
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8, 4), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
        bytes[24] = 8;
        bytes[25] = 2;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(33, 4), 0);
        "IDAT"u8.CopyTo(bytes.AsSpan(37));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(45, 4), 0);
        "IEND"u8.CopyTo(bytes.AsSpan(49));
        return bytes;
    }

    private static byte[] CreateJpeg(int width, int height) =>
    [
        0xFF, 0xD8,
        0xFF, 0xC0, 0x00, 0x11, 0x08,
        (byte)(height >> 8), (byte)height,
        (byte)(width >> 8), (byte)width,
        0x03, 0x01, 0x11, 0x00, 0x02, 0x11, 0x00, 0x03, 0x11, 0x00,
        0xFF, 0xD9
    ];

    private static byte[] CreateWebP(int width, int height)
    {
        var bytes = new byte[30];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 22);
        "WEBPVP8X"u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), 10);
        WriteUInt24LittleEndian(bytes.AsSpan(24, 3), width - 1);
        WriteUInt24LittleEndian(bytes.AsSpan(27, 3), height - 1);
        return bytes;
    }

    private static void WriteUInt24LittleEndian(Span<byte> destination, int value)
    {
        destination[0] = (byte)value;
        destination[1] = (byte)(value >> 8);
        destination[2] = (byte)(value >> 16);
    }

    private sealed class FakeMediaStorage : IMediaStorage
    {
        public string ProviderName { get; init; } = "Fake";
        public StoredMediaObject StoreResult { get; init; } =
            new("storage-1", "image/png", 57, 20, 10);
        public Stream ReadStream { get; init; } = new MemoryStream([1, 2, 3]);
        public Exception? StoreException { get; init; }
        public Exception? ReadException { get; init; }
        public Exception? DeleteException { get; init; }
        public MediaUpload? StoredUpload { get; private set; }
        public string? OpenedObjectId { get; private set; }
        public string? DeletedObjectId { get; private set; }
        public long? StoreInputPosition { get; private set; }
        public CancellationToken DeleteCancellationToken { get; private set; }
        public List<string>? Operations { get; set; }

        /// <inheritdoc />
        public Task<StoredMediaObject> StoreAsync(
            MediaUpload upload,
            CancellationToken cancellationToken)
        {
            StoredUpload = upload;
            StoreInputPosition = upload.Content.Position;
            return StoreException is null
                ? Task.FromResult(StoreResult)
                : Task.FromException<StoredMediaObject>(StoreException);
        }

        /// <inheritdoc />
        public Task<Stream> OpenReadAsync(
            string storageObjectId,
            CancellationToken cancellationToken)
        {
            OpenedObjectId = storageObjectId;
            return ReadException is null
                ? Task.FromResult(ReadStream)
                : Task.FromException<Stream>(ReadException);
        }

        /// <inheritdoc />
        public Task DeleteAsync(string storageObjectId, CancellationToken cancellationToken)
        {
            Operations?.Add("storage-delete");
            DeletedObjectId = storageObjectId;
            DeleteCancellationToken = cancellationToken;
            return DeleteException is null
                ? Task.CompletedTask
                : Task.FromException(DeleteException);
        }
    }

    private sealed class FakeMediaRepository : IMediaRepository
    {
        public MediaAsset? Stored { get; init; }
        public long ActiveCount { get; init; }
        public Exception? CreateException { get; init; }
        public MediaAsset? Created { get; private set; }
        public string? RequestedId { get; private set; }
        public bool SoftDeleted { get; private set; }
        public string? CleanupCompletedId { get; private set; }
        public List<string>? Operations { get; set; }

        /// <inheritdoc />
        public Task<MediaAsset?> GetByIdAsync(
            string id,
            CancellationToken cancellationToken = default)
        {
            RequestedId = id;
            return Task.FromResult(Stored?.Id == id ? Stored : null);
        }

        /// <inheritdoc />
        public Task<long> CountActiveByOwnerAndPurposeAsync(
            string ownerEntityId,
            MediaPurpose purpose,
            CancellationToken cancellationToken = default) => Task.FromResult(ActiveCount);

        /// <inheritdoc />
        public Task CreateAsync(MediaAsset asset, CancellationToken cancellationToken = default)
        {
            Created = asset;
            return CreateException is null
                ? Task.CompletedTask
                : Task.FromException(CreateException);
        }

        /// <inheritdoc />
        public Task<bool> SoftDeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            Operations?.Add("soft-delete");
            SoftDeleted = Stored?.Id == id;
            return Task.FromResult(SoftDeleted);
        }

        /// <inheritdoc />
        public Task MarkCleanupCompleteAsync(
            string id,
            CancellationToken cancellationToken = default)
        {
            Operations?.Add("cleanup-complete");
            CleanupCompletedId = id;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<List<MediaAsset>> GetPendingCleanupAsync(
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<MediaAsset>());
    }
}
