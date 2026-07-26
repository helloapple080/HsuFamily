using System.Buffers.Binary;
using System.IO.Compression;
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

    private static MediaService CreateService(
        IMediaRepository repository,
        IMediaStorage storage,
        IImageInspector? imageInspector = null) =>
        new(repository, storage, imageInspector ?? new HeaderMatchingImageInspector());

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
    [InlineData("image/jpeg", 1, 1)]
    [InlineData("image/png", 1, 1)]
    [InlineData("image/webp", 1, 1)]
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
        var service = CreateService(repository, storage);
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
        var service = CreateService(repository, storage);
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
        var service = CreateService(repository, storage);
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
        var service = CreateService(repository, storage);
        var bytes = CreatePng(20, 10).Take(retainedBytes).ToArray();
        var request = CreateRequest(bytes, "image/png");

        var result = await service.UploadAsync(
            request,
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Null(storage.StoredUpload);
    }

    /// <summary>PNG chunk CRC 失效時必須拒絕。</summary>
    [Fact]
    public async Task UploadAsync_PngCorruptCrcReturnsValidation()
    {
        var bytes = CreatePng(1, 1);
        bytes[bytes.Length - 5] ^= 0x01;
        var storage = new FakeMediaStorage();
        var service = CreateService(new FakeMediaRepository(), storage);

        var result = await service.UploadAsync(
            CreateRequest(bytes, "image/png"),
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Null(storage.StoredUpload);
    }

    /// <summary>PLTE 位於 IDAT 之後時違反 PNG chunk 順序。</summary>
    [Fact]
    public async Task UploadAsync_PngPaletteAfterImageDataReturnsValidation()
    {
        var bytes = CreateBoundaryPng(1, 1);
        var paletteChunk = CreatePngChunk("PLTE"u8, [0, 0, 0]);
        var iendOffset = bytes.Length - 12;
        var malformed = new byte[bytes.Length + paletteChunk.Length];
        bytes.AsSpan(0, iendOffset).CopyTo(malformed);
        paletteChunk.CopyTo(malformed, iendOffset);
        bytes.AsSpan(iendOffset).CopyTo(malformed.AsSpan(iendOffset + paletteChunk.Length));
        var storage = new FakeMediaStorage();
        var service = CreateService(new FakeMediaRepository(), storage);

        var result = await service.UploadAsync(
            CreateRequest(malformed, "image/png"),
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Null(storage.StoredUpload);
    }

    /// <summary>Grayscale 與 grayscale-alpha PNG 不得含有 PLTE。</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(4, 8)]
    public async Task UploadAsync_PngForbiddenPaletteForColorTypeReturnsValidation(
        byte colorType,
        byte bitDepth)
    {
        var bytes = SetPngHeader(CreateBoundaryPng(1, 1), bitDepth, colorType);
        bytes = InsertPngChunkBefore(bytes, "IDAT"u8, "PLTE"u8, [0, 0, 0]);
        var storage = new FakeMediaStorage();
        var inspector = new HeaderMatchingImageInspector();
        var service = CreateService(new FakeMediaRepository(), storage, inspector);

        var result = await service.UploadAsync(
            CreateRequest(bytes, "image/png"),
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Equal(0, inspector.InvocationCount);
        Assert.Null(storage.StoredUpload);
    }

    /// <summary>Indexed PNG 的 PLTE 數量不得超過 bit depth 可表示的項目數。</summary>
    [Fact]
    public async Task UploadAsync_PngIndexedPaletteOverBitDepthReturnsValidation()
    {
        var bytes = SetPngHeader(CreateBoundaryPng(1, 1), bitDepth: 1, colorType: 3);
        bytes = InsertPngChunkBefore(
            bytes,
            "IDAT"u8,
            "PLTE"u8,
            [0, 0, 0, 127, 127, 127, 255, 255, 255]);
        var storage = new FakeMediaStorage();
        var inspector = new HeaderMatchingImageInspector();
        var service = CreateService(new FakeMediaRepository(), storage, inspector);

        var result = await service.UploadAsync(
            CreateRequest(bytes, "image/png"),
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Equal(0, inspector.InvocationCount);
        Assert.Null(storage.StoredUpload);
    }

    /// <summary>Indexed PNG 在 IDAT 前必須有 PLTE。</summary>
    [Fact]
    public async Task UploadAsync_PngIndexedWithoutPaletteReturnsValidation()
    {
        var bytes = SetPngHeader(CreateBoundaryPng(1, 1), bitDepth: 1, colorType: 3);
        var storage = new FakeMediaStorage();
        var inspector = new HeaderMatchingImageInspector();
        var service = CreateService(new FakeMediaRepository(), storage, inspector);

        var result = await service.UploadAsync(
            CreateRequest(bytes, "image/png"),
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Equal(0, inspector.InvocationCount);
    }

    /// <summary>PNG 結構合法但 IDAT zlib 資料損毀時，完整解碼器必須拒絕。</summary>
    [Fact]
    public async Task UploadAsync_CorruptPngImageDataRejectedByFullDecoder()
    {
        var bytes = CorruptPngImageData(CreateBoundaryPng(1, 1));
        var storage = new FakeMediaStorage();
        var inspector = new HeaderMatchingImageInspector { RejectAll = true };
        var service = CreateService(new FakeMediaRepository(), storage, inspector);

        var result = await service.UploadAsync(
            CreateRequest(bytes, "image/png"),
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Equal(1, inspector.InvocationCount);
        Assert.Null(storage.StoredUpload);
    }

    /// <summary>只有 SOF 與 EOI、沒有 SOS 影像資料的 JPEG 不是有效影像。</summary>
    [Fact]
    public async Task UploadAsync_JpegWithoutScanReturnsValidation()
    {
        byte[] bytes =
        [
            0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x11, 0x08,
            0x00, 0x01, 0x00, 0x01,
            0x03, 0x01, 0x11, 0x00, 0x02, 0x11, 0x00, 0x03, 0x11, 0x00,
            0xFF, 0xD9
        ];
        var storage = new FakeMediaStorage();
        var service = CreateService(new FakeMediaRepository(), storage);

        var result = await service.UploadAsync(
            CreateRequest(bytes, "image/jpeg"),
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Null(storage.StoredUpload);
    }

    /// <summary>只有 VP8X metadata 而無 VP8/VP8L payload 的 WebP 不得接受。</summary>
    [Fact]
    public async Task UploadAsync_WebPMetadataOnlyReturnsValidation()
    {
        var bytes = new byte[30];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 22);
        "WEBPVP8X"u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), 10);
        var storage = new FakeMediaStorage();
        var service = CreateService(new FakeMediaRepository(), storage);

        var result = await service.UploadAsync(
            CreateRequest(bytes, "image/webp"),
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Null(storage.StoredUpload);
    }

    /// <summary>VP8X flag byte 任一 reserved bit 非零時必須拒絕。</summary>
    [Theory]
    [InlineData(0x80)]
    [InlineData(0x40)]
    [InlineData(0x01)]
    public async Task UploadAsync_WebPReservedFlagBitsReturnsValidation(byte flagByte)
    {
        var bytes = CreateExtendedWebP(flagByte, includeLeadingJunk: false);
        var storage = new FakeMediaStorage();
        var service = CreateService(new FakeMediaRepository(), storage);

        var result = await service.UploadAsync(
            CreateRequest(bytes, "image/webp"),
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Null(storage.StoredUpload);
    }

    /// <summary>VP8X 功能旗標若缺少對應 chunk 必須拒絕。</summary>
    [Theory]
    [InlineData(0x20)]
    [InlineData(0x10)]
    [InlineData(0x08)]
    [InlineData(0x04)]
    [InlineData(0x02)]
    public async Task UploadAsync_WebPFeatureFlagWithoutChunkReturnsValidation(byte flagByte)
    {
        var bytes = CreateExtendedWebP(flagByte, includeLeadingJunk: false);
        var storage = new FakeMediaStorage();
        var inspector = new HeaderMatchingImageInspector();
        var service = CreateService(new FakeMediaRepository(), storage, inspector);

        var result = await service.UploadAsync(
            CreateRequest(bytes, "image/webp"),
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Equal(0, inspector.InvocationCount);
        Assert.Null(storage.StoredUpload);
    }

    /// <summary>WebP feature chunk 出現時必須同時設定對應 VP8X 旗標。</summary>
    [Theory]
    [InlineData("ICCP", false)]
    [InlineData("ALPH", false)]
    [InlineData("EXIF", true)]
    [InlineData("XMP ", true)]
    [InlineData("ANIM", false)]
    public async Task UploadAsync_WebPFeatureChunkWithoutFlagReturnsValidation(
        string chunkType,
        bool afterPayload)
    {
        var chunkData = chunkType switch
        {
            "ALPH" => new byte[2],
            "ANIM" => new byte[6],
            _ => new byte[1]
        };
        var bytes = CreateExtendedWebP(
            flagByte: 0,
            includeLeadingJunk: false,
            chunksBeforePayload: afterPayload ? null : [(chunkType, chunkData)],
            chunksAfterPayload: afterPayload ? [(chunkType, chunkData)] : null);
        var storage = new FakeMediaStorage();
        var inspector = new HeaderMatchingImageInspector();
        var service = CreateService(new FakeMediaRepository(), storage, inspector);

        var result = await service.UploadAsync(
            CreateRequest(bytes, "image/webp"),
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Equal(0, inspector.InvocationCount);
    }

    /// <summary>VP8X 功能旗標與合法位置的對應 chunk 應通過結構驗證。</summary>
    [Theory]
    [InlineData(0x20, "ICCP", false)]
    [InlineData(0x10, "ALPH", false)]
    [InlineData(0x08, "EXIF", true)]
    [InlineData(0x04, "XMP ", true)]
    public async Task UploadAsync_WebPMatchingFeatureFlagAndChunkAreAccepted(
        byte flagByte,
        string chunkType,
        bool afterPayload)
    {
        var chunkData = chunkType == "ALPH" ? new byte[2] : new byte[1];
        var bytes = CreateExtendedWebP(
            flagByte,
            includeLeadingJunk: false,
            chunksBeforePayload: afterPayload ? null : [(chunkType, chunkData)],
            chunksAfterPayload: afterPayload ? [(chunkType, chunkData)] : null);
        var storage = new FakeMediaStorage();
        var service = CreateService(new FakeMediaRepository(), storage);

        var result = await service.UploadAsync(
            CreateRequest(bytes, "image/webp"),
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>Animation 旗標只有在 ANIM 後接 ANMF 且沒有 still-image payload 時有效。</summary>
    [Fact]
    public async Task UploadAsync_WebPMatchingAnimationChunksAreAccepted()
    {
        var bytes = CreateExtendedWebP(
            flagByte: 0x02,
            includeLeadingJunk: false,
            chunksBeforePayload: [("ANIM", new byte[6]), ("ANMF", new byte[16])],
            includePayload: false);
        var storage = new FakeMediaStorage();
        var service = CreateService(new FakeMediaRepository(), storage);

        var result = await service.UploadAsync(
            CreateRequest(bytes, "image/webp"),
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>ICCP 與 ALPH 等重建必要 chunk 出現在影像資料後必須拒絕。</summary>
    [Theory]
    [InlineData(0x20, "ICCP")]
    [InlineData(0x10, "ALPH")]
    public async Task UploadAsync_WebPReconstructionChunkAfterPayloadReturnsValidation(
        byte flagByte,
        string chunkType)
    {
        var bytes = CreateExtendedWebP(
            flagByte,
            includeLeadingJunk: false,
            chunksAfterPayload: [(chunkType, new byte[] { 0 })]);
        var storage = new FakeMediaStorage();
        var inspector = new HeaderMatchingImageInspector();
        var service = CreateService(new FakeMediaRepository(), storage, inspector);

        var result = await service.UploadAsync(
            CreateRequest(bytes, "image/webp"),
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Equal(0, inspector.InvocationCount);
    }

    /// <summary>VP8X extended header 不是第一個 chunk 時必須拒絕。</summary>
    [Fact]
    public async Task UploadAsync_WebPExtendedHeaderAfterOtherChunkReturnsValidation()
    {
        var bytes = CreateExtendedWebP(flagByte: 0, includeLeadingJunk: true);
        var storage = new FakeMediaStorage();
        var service = CreateService(new FakeMediaRepository(), storage);

        var result = await service.UploadAsync(
            CreateRequest(bytes, "image/webp"),
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Null(storage.StoredUpload);
    }

    /// <summary>WebP chunk 長度超出 RIFF 邊界時必須拒絕。</summary>
    [Fact]
    public async Task UploadAsync_WebPChunkOverflowReturnsValidation()
    {
        var bytes = CreateWebP(1, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), uint.MaxValue);
        var storage = new FakeMediaStorage();
        var service = CreateService(new FakeMediaRepository(), storage);

        var result = await service.UploadAsync(
            CreateRequest(bytes, "image/webp"),
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Null(storage.StoredUpload);
    }

    /// <summary>WebP odd chunk 的 padding byte 必須是零。</summary>
    [Fact]
    public async Task UploadAsync_WebPNonZeroPaddingReturnsValidation()
    {
        var bytes = CreateWebP(1, 1);
        Array.Resize(ref bytes, bytes.Length + 10);
        "JUNK"u8.CopyTo(bytes.AsSpan(42));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(46, 4), 1);
        bytes[50] = 0xAA;
        bytes[51] = 0xFF;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), 44);
        var storage = new FakeMediaStorage();
        var service = CreateService(new FakeMediaRepository(), storage);

        var result = await service.UploadAsync(
            CreateRequest(bytes, "image/webp"),
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Null(storage.StoredUpload);
    }

    /// <summary>尺寸或總像素超過安全上限時不得儲存。</summary>
    [Theory]
    [InlineData(8193, 1)]
    [InlineData(1, 8193)]
    [InlineData(4000, 4001)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public async Task UploadAsync_DimensionsOverLimitsReturnsValidation(int width, int height)
    {
        var bytes = SetPngDimensions(CreatePng(1, 1), width, height);
        var storage = new FakeMediaStorage();
        var service = CreateService(new FakeMediaRepository(), storage);

        var result = await service.UploadAsync(
            CreateRequest(bytes, "image/png"),
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Null(storage.StoredUpload);
    }

    /// <summary>總像素恰好在上限時應可通過尺寸驗證。</summary>
    [Fact]
    public async Task UploadAsync_DimensionsAtPixelLimitAreAccepted()
    {
        var bytes = CreateBoundaryPng(4000, 4000);
        var storage = new FakeMediaStorage
        {
            StoreResult = new StoredMediaObject("storage-1", "image/png", bytes.Length, 4000, 4000)
        };
        var service = CreateService(new FakeMediaRepository(), storage);

        var result = await service.UploadAsync(
            CreateRequest(bytes, "image/png"),
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>實際內容或宣告長度超過 10 MB 時均應拒絕。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UploadAsync_FileOverTenMegabytesReturnsValidation(bool underreportedLength)
    {
        var repository = new FakeMediaRepository();
        var storage = new FakeMediaStorage();
        var service = CreateService(repository, storage);
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
    [InlineData(MediaPurpose.MemberAvatar)]
    [InlineData(MediaPurpose.Worship)]
    [InlineData(MediaPurpose.Itinerary)]
    public async Task UploadAsync_AllPurposeSlotsConflictReturnsConflictAndCompensates(
        MediaPurpose purpose)
    {
        var repository = new FakeMediaRepository();
        var limit = purpose == MediaPurpose.MemberAvatar ? 1 : 30;
        repository.ConflictingSlots.UnionWith(Enumerable.Range(0, limit));
        var storage = new FakeMediaStorage();
        var service = CreateService(repository, storage);
        var request = CreateRequest(CreatePng(20, 10), "image/png", purpose: purpose);

        var result = await service.UploadAsync(
            request,
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.NotNull(storage.StoredUpload);
        Assert.Equal("storage-1", storage.DeletedObjectId);
        Assert.Equal(Enumerable.Range(0, limit), repository.AttemptedSlots);
    }

    /// <summary>被其他請求占用的 gallery slot 應原子地跳至下一個有效 slot。</summary>
    [Fact]
    public async Task UploadAsync_ConcurrentSlotConflictsUsesNextAvailableSlot()
    {
        var repository = new FakeMediaRepository();
        repository.ConflictingSlots.UnionWith([0, 1]);
        var service = CreateService(repository, new FakeMediaStorage());
        var request = CreateRequest(CreatePng(1, 1), "image/png", purpose: MediaPurpose.Worship);

        var result = await service.UploadAsync(
            request,
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([0, 1, 2], repository.AttemptedSlots);
        Assert.Equal(2, repository.Created!.Slot);
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
        var service = CreateService(repository, storage);
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
        var service = CreateService(repository, storage);
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

    /// <summary>儲存供應商名稱若無法讀取，必須在寫入任何物件前失敗。</summary>
    [Fact]
    public async Task UploadAsync_ProviderNameFailureOccursBeforeStorage()
    {
        var storage = new FakeMediaStorage
        {
            ProviderNameException = new InvalidOperationException("provider-name-secret")
        };
        var service = CreateService(new FakeMediaRepository(), storage);

        var result = await service.UploadAsync(
            CreateRequest(CreatePng(1, 1), "image/png"),
            ObjectId.GenerateNewId().ToString(),
            CancellationToken.None);

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.DoesNotContain("provider-name-secret", result.Message);
        Assert.Null(storage.StoredUpload);
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
        var service = CreateService(repository, storage);

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
        var service = CreateService(new FakeMediaRepository(), storage);

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
        var service = CreateService(repository, storage);

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
        var service = CreateService(repository, storage);

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
        var service = CreateService(repository, storage);

        var result = await service.OpenReadAsync(repository.Stored.Id, false, CancellationToken.None);

        Assert.Equal(ServiceError.Forbidden, result.Error);
        Assert.Null(storage.OpenedObjectId);
    }

    /// <summary>即使已驗證，未知可見性仍必須 fail closed。</summary>
    [Fact]
    public async Task OpenReadAsync_UnknownVisibilityAuthenticatedReturnsForbidden()
    {
        var repository = new FakeMediaRepository { Stored = CreateAsset((MediaVisibility)999) };
        var storage = new FakeMediaStorage();
        var service = CreateService(repository, storage);

        var result = await service.OpenReadAsync(repository.Stored.Id, true, CancellationToken.None);

        Assert.Equal(ServiceError.Forbidden, result.Error);
        Assert.Null(storage.OpenedObjectId);
    }

    /// <summary>公開媒體應允許匿名讀取並回傳不含供應商資訊的內容描述。</summary>
    [Fact]
    public async Task OpenReadAsync_PublicAnonymousReturnsContentDescriptor()
    {
        var repository = new FakeMediaRepository { Stored = CreateAsset(MediaVisibility.Public) };
        var providerStream = new TrackingMemoryStream([1, 2, 3]);
        var storage = new FakeMediaStorage { ReadStream = providerStream };
        var service = CreateService(repository, storage);

        var result = await service.OpenReadAsync(repository.Stored.Id, false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotSame(storage.ReadStream, result.Value!.Content);
        Assert.Equal([1, 2, 3], ((MemoryStream)result.Value.Content).ToArray());
        Assert.True(providerStream.IsDisposed);
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
        var service = CreateService(repository, storage);

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
        var service = CreateService(repository, storage);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.OpenReadAsync(
            repository.Stored.Id,
            false,
            source.Token));
    }

    /// <summary>供應商 stream 延遲拋錯時應在 service 邊界轉換並 dispose。</summary>
    [Fact]
    public async Task OpenReadAsync_LazyProviderFailureReturnsGenericErrorAndDisposes()
    {
        var repository = new FakeMediaRepository { Stored = CreateAsset(MediaVisibility.Public) };
        var providerStream = new LazyFailingStream();
        var service = CreateService(repository, new FakeMediaStorage { ReadStream = providerStream });

        var result = await service.OpenReadAsync(repository.Stored.Id, false, CancellationToken.None);

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.True(providerStream.IsDisposed);
        Assert.DoesNotContain("lazy-provider-secret", result.Message);
    }

    /// <summary>供應商內容長度與 metadata 不同時應失敗並 dispose。</summary>
    [Fact]
    public async Task OpenReadAsync_LengthMismatchReturnsConflictAndDisposes()
    {
        var repository = new FakeMediaRepository { Stored = CreateAsset(MediaVisibility.Public) };
        var providerStream = new TrackingMemoryStream([1, 2]);
        var service = CreateService(repository, new FakeMediaStorage { ReadStream = providerStream });

        var result = await service.OpenReadAsync(repository.Stored.Id, false, CancellationToken.None);

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.True(providerStream.IsDisposed);
    }

    /// <summary>供應商資料流超過 10 MB 時應在 service 邊界停止並 dispose。</summary>
    [Fact]
    public async Task OpenReadAsync_ProviderContentOverLimitReturnsConflictAndDisposes()
    {
        var asset = CreateAsset(MediaVisibility.Public);
        asset.SizeBytes = ValidationLimits.MaxMediaBytes;
        var repository = new FakeMediaRepository { Stored = asset };
        var providerStream = new TrackingMemoryStream(new byte[ValidationLimits.MaxMediaBytes + 1]);
        var service = CreateService(repository, new FakeMediaStorage { ReadStream = providerStream });

        var result = await service.OpenReadAsync(asset.Id, false, CancellationToken.None);

        Assert.Equal(ServiceError.Conflict, result.Error);
        Assert.True(providerStream.IsDisposed);
    }

    /// <summary>待清理批次應繼續處理混合成敗，並保留失敗項目供重試。</summary>
    [Fact]
    public async Task CleanupPendingAsync_MixedFailuresContinuesAndIsIdempotent()
    {
        var first = CreateAsset(MediaVisibility.FamilyOnly);
        var second = CreateAsset(MediaVisibility.FamilyOnly);
        second.StorageObjectId = "storage-2";
        var third = CreateAsset(MediaVisibility.FamilyOnly);
        third.StorageObjectId = "storage-3";
        var repository = new FakeMediaRepository();
        repository.PendingCleanup.AddRange([first, second, third]);
        var storage = new FakeMediaStorage();
        storage.DeleteExceptions["storage-2"] = new InvalidOperationException("temporary");
        var service = CreateService(repository, storage);

        var firstResult = await service.CleanupPendingAsync(500, CancellationToken.None);
        var secondResult = await service.CleanupPendingAsync(500, CancellationToken.None);

        Assert.True(firstResult.IsSuccess);
        Assert.Equal(2, firstResult.Value);
        Assert.True(secondResult.IsSuccess);
        Assert.Equal(0, secondResult.Value);
        Assert.Equal([first.Id, third.Id], repository.CompletedCleanupIds);
        Assert.Single(repository.PendingCleanup);
        Assert.Equal(second.Id, repository.PendingCleanup[0].Id);
        Assert.All(repository.RequestedCleanupLimits, limit => Assert.Equal(100, limit));
    }

    /// <summary>待清理批次必須在取消後立即停止且不標記完成。</summary>
    [Fact]
    public async Task CleanupPendingAsync_CancellationStopsBatch()
    {
        var repository = new FakeMediaRepository();
        repository.PendingCleanup.Add(CreateAsset(MediaVisibility.FamilyOnly));
        var storage = new FakeMediaStorage();
        var service = CreateService(repository, storage);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.CleanupPendingAsync(10, source.Token));

        Assert.Empty(repository.CompletedCleanupIds);
        Assert.Null(storage.DeletedObjectId);
    }

    /// <summary>媒體 ID 不是 ObjectId 時不得查詢持久層。</summary>
    [Fact]
    public async Task OpenReadAsync_InvalidMediaIdReturnsValidation()
    {
        var repository = new FakeMediaRepository();
        var storage = new FakeMediaStorage();
        var service = CreateService(repository, storage);

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
        var service = CreateService(repository, storage);

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
        var service = CreateService(repository, storage);

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
        var service = CreateService(repository, storage);

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
        var service = CreateService(repository, storage);

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
        SizeBytes = 3,
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

    private static byte[] CreatePng(int width, int height) => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAHhlWElmTU0AKgAAAAgABAEaAAUAAAABAAAAPgEbAAUAAAABAAAARgEoAAMAAAABAAIAAIdpAAQAAAABAAAATgAAAAAAAABIAAAAAQAAAEgAAAABAAOgAQADAAAAAQABAACgAgAEAAAAAQAAAAGgAwAEAAAAAQAAAAEAAAAAyq2pyQAAAAlwSFlzAAALEwAACxMBAJqcGAAAAA1JREFUCB1j+HTv5H8ACPgDmWZXp3EAAAAASUVORK5CYII=");

    private static byte[] CreateJpeg(int width, int height)
    {
        var encoded = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQAASABIAAD/4QCARXhpZgAATU0AKgAAAAgABAEaAAUAAAABAAAAPgEbAAUAAAABAAAARgEoAAMAAAABAAIAAIdpAAQAAAABAAAATgAAAAAAAABIAAAAAQAAAEgAAAABAAOgAQADAAAAAQABAACgAgAEAAAAAQAAAAGgAwAEAAAAAQAAAAEAAAAA/+0AOFBob3Rvc2hvcCAzLjAAOEJJTQQEAAAAAAAAOEJJTQQlAAAAAAAQ1B2M2Y8AsgTpgAmY7PhCfv/AABEIAAEAAQMBIgACEQEDEQH/xAAfAAABBQEBAQEBAQAAAAAAAAAAAQIDBAUGBwgJCgv/xAC1EAACAQMDAgQDBQUEBAAAAX0BAgMABBEFEiExQQYTUWEHInEUMoGRoQgjQrHBFVLR8CQzYnKCCQoWFxgZGiUmJygpKjQ1Njc4OTpDREVGR0hJSlNUVVZXWFlaY2RlZmdoaWpzdHV2d3h5eoOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4eLj5OXm5+jp6vHy8/T19vf4+fr/xAAfAQADAQEBAQEBAQEBAAAAAAAAAQIDBAUGBwgJCgv/xAC1EQACAQIEBAMEBwUEBAABAncAAQIDEQQFITEGEkFRB2FxEyIygQgUQpGhscEJIzNS8BVictEKFiQ04SXxFxgZGiYnKCkqNTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqCg4SFhoeIiYqSk5SVlpeYmZqio6Slpqeoqaqys7S1tre4ubrCw8TFxsfIycrS09TV1tfY2dri4+Tl5ufo6ery8/T19vf4+fr/2wBDAAICAgICAgMCAgMFAwMDBQYFBQUFBggGBgYGBggKCAgICAgICgoKCgoKCgoMDAwMDAwODg4ODg8PDw8PDw8PDw//2wBDAQICAgQEBAcEBAcQCwkLEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBD/3QAEAAH/2gAMAwEAAhEDEQA/AP2Iooor4c+wP//Z");
        var genuine = new byte[encoded.Length + 3];
        encoded.AsSpan(0, 794).CopyTo(genuine);
        genuine.AsSpan(794, 3).Fill(0x10);
        encoded.AsSpan(794).CopyTo(genuine.AsSpan(797));
        return genuine;
    }

    private static byte[] CreateWebP(int width, int height) => Convert.FromBase64String(
        "UklGRiIAAABXRUJQVlA4IBYAAAAwAQCdASoBAAEADsD+JaQAA3AAAAAA");

    private static byte[] CreateExtendedWebP(
        byte flagByte,
        bool includeLeadingJunk,
        (string Type, byte[] Data)[]? chunksBeforePayload = null,
        (string Type, byte[] Data)[]? chunksAfterPayload = null,
        bool includePayload = true)
    {
        var payload = CreateWebP(1, 1).AsSpan(12).ToArray();
        using var webP = new MemoryStream();
        webP.Write("RIFF"u8);
        webP.Write([0, 0, 0, 0]);
        webP.Write("WEBP"u8);
        if (includeLeadingJunk)
        {
            WriteWebPChunk(webP, "JUNK"u8, []);
        }

        var extendedHeader = new byte[10];
        extendedHeader[0] = flagByte;
        WriteWebPChunk(webP, "VP8X"u8, extendedHeader);
        foreach (var chunk in chunksBeforePayload ?? [])
        {
            WriteWebPChunk(webP, System.Text.Encoding.ASCII.GetBytes(chunk.Type), chunk.Data);
        }

        if (includePayload)
        {
            webP.Write(payload);
        }
        foreach (var chunk in chunksAfterPayload ?? [])
        {
            WriteWebPChunk(webP, System.Text.Encoding.ASCII.GetBytes(chunk.Type), chunk.Data);
        }

        var bytes = webP.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), (uint)(bytes.Length - 8));
        return bytes;
    }

    private static void WriteWebPChunk(Stream destination, ReadOnlySpan<byte> type, byte[] data)
    {
        destination.Write(type);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(length, (uint)data.Length);
        destination.Write(length);
        destination.Write(data);
        if ((data.Length & 1) != 0)
        {
            destination.WriteByte(0);
        }
    }

    private static byte[] CreateBoundaryPng(int width, int height)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var row = new byte[1 + ((width + 7) / 8)];
            for (var index = 0; index < height; index++)
            {
                zlib.Write(row);
            }
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 1;
        WritePngChunk(png, "IHDR"u8, header);
        WritePngChunk(png, "IDAT"u8, compressed.ToArray());
        WritePngChunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    private static void WritePngChunk(Stream destination, ReadOnlySpan<byte> type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        destination.Write(length);
        destination.Write(type);
        destination.Write(data);
        var crcInput = new byte[type.Length + data.Length];
        type.CopyTo(crcInput);
        data.CopyTo(crcInput, type.Length);
        BinaryPrimitives.WriteUInt32BigEndian(length, ComputeCrc32(crcInput));
        destination.Write(length);
    }

    private static byte[] CreatePngChunk(ReadOnlySpan<byte> type, byte[] data)
    {
        using var chunk = new MemoryStream();
        WritePngChunk(chunk, type, data);
        return chunk.ToArray();
    }

    private static byte[] SetPngDimensions(byte[] bytes, int width, int height)
    {
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(29, 4), ComputeCrc32(bytes.AsSpan(12, 17)));
        return bytes;
    }

    private static byte[] SetPngHeader(byte[] bytes, byte bitDepth, byte colorType)
    {
        bytes[24] = bitDepth;
        bytes[25] = colorType;
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(29, 4), ComputeCrc32(bytes.AsSpan(12, 17)));
        return bytes;
    }

    private static byte[] InsertPngChunkBefore(
        byte[] bytes,
        ReadOnlySpan<byte> beforeType,
        ReadOnlySpan<byte> chunkType,
        byte[] chunkData)
    {
        var offset = 8;
        while (offset <= bytes.Length - 12)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));
            if (bytes.AsSpan(offset + 4, 4).SequenceEqual(beforeType))
            {
                var chunk = CreatePngChunk(chunkType, chunkData);
                var result = new byte[bytes.Length + chunk.Length];
                bytes.AsSpan(0, offset).CopyTo(result);
                chunk.CopyTo(result, offset);
                bytes.AsSpan(offset).CopyTo(result.AsSpan(offset + chunk.Length));
                return result;
            }

            offset += length + 12;
        }

        throw new InvalidOperationException("測試 PNG 缺少指定 chunk");
    }

    private static byte[] CorruptPngImageData(byte[] bytes)
    {
        var offset = 8;
        while (offset <= bytes.Length - 12)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));
            if (bytes.AsSpan(offset + 4, 4).SequenceEqual("IDAT"u8))
            {
                bytes[offset + 8] ^= 0xFF;
                var crcLength = 4 + length;
                BinaryPrimitives.WriteUInt32BigEndian(
                    bytes.AsSpan(offset + 8 + length, 4),
                    ComputeCrc32(bytes.AsSpan(offset + 4, crcLength)));
                return bytes;
            }

            offset += 12 + length;
        }

        throw new InvalidOperationException("測試 PNG 缺少 IDAT chunk");
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xEDB88320u;
            }
        }

        return ~crc;
    }

    private sealed class FakeMediaStorage : IMediaStorage
    {
        private string _providerName = "Fake";

        public string ProviderName
        {
            get => ProviderNameException is null
                ? _providerName
                : throw ProviderNameException;
            init => _providerName = value;
        }

        public Exception? ProviderNameException { get; init; }
        public StoredMediaObject? StoreResult { get; init; }
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
        public Dictionary<string, Exception> DeleteExceptions { get; } = [];

        /// <inheritdoc />
        public Task<StoredMediaObject> StoreAsync(
            MediaUpload upload,
            CancellationToken cancellationToken)
        {
            StoredUpload = upload;
            StoreInputPosition = upload.Content.Position;
            return StoreException is null
                ? Task.FromResult(StoreResult ?? new StoredMediaObject(
                    "storage-1",
                    upload.DeclaredContentType,
                    upload.Length,
                    1,
                    1))
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
            var exception = DeleteExceptions.GetValueOrDefault(storageObjectId) ?? DeleteException;
            return exception is null
                ? Task.CompletedTask
                : Task.FromException(exception);
        }
    }

    private sealed class HeaderMatchingImageInspector : IImageInspector
    {
        public bool RejectAll { get; init; }
        public int InvocationCount { get; private set; }

        /// <inheritdoc />
        public Task<ImageInspectionResult?> InspectAsync(
            Stream content,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            if (RejectAll)
            {
                return Task.FromResult<ImageInspectionResult?>(null);
            }

            var originalPosition = content.Position;
            Span<byte> header = stackalloc byte[30];
            var read = content.Read(header);
            content.Position = originalPosition;
            ImageInspectionResult? result = null;
            if (read >= 24 && header[..8].SequenceEqual(
                    new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            {
                result = new ImageInspectionResult(
                    "image/png",
                    BinaryPrimitives.ReadInt32BigEndian(header.Slice(16, 4)),
                    BinaryPrimitives.ReadInt32BigEndian(header.Slice(20, 4)));
            }
            else if (read >= 2 && header[0] == 0xFF && header[1] == 0xD8)
            {
                result = new ImageInspectionResult("image/jpeg", 1, 1);
            }
            else if (read >= 12 && header[..4].SequenceEqual("RIFF"u8) &&
                     header.Slice(8, 4).SequenceEqual("WEBP"u8))
            {
                result = new ImageInspectionResult("image/webp", 1, 1);
            }

            return Task.FromResult(result);
        }
    }

    private sealed class FakeMediaRepository : IMediaRepository
    {
        public MediaAsset? Stored { get; init; }
        public Exception? CreateException { get; init; }
        public MediaAsset? Created { get; private set; }
        public string? RequestedId { get; private set; }
        public bool SoftDeleted { get; private set; }
        public string? CleanupCompletedId { get; private set; }
        public List<string>? Operations { get; set; }
        public HashSet<int> ConflictingSlots { get; } = [];
        public List<int> AttemptedSlots { get; } = [];
        public List<MediaAsset> PendingCleanup { get; } = [];
        public List<string> CompletedCleanupIds { get; } = [];
        public List<int> RequestedCleanupLimits { get; } = [];

        /// <inheritdoc />
        public Task<MediaAsset?> GetByIdAsync(
            string id,
            CancellationToken cancellationToken = default)
        {
            RequestedId = id;
            return Task.FromResult(Stored?.Id == id ? Stored : null);
        }

        /// <inheritdoc />
        public Task<MediaCreateResult> TryCreateAsync(
            MediaAsset asset,
            CancellationToken cancellationToken = default)
        {
            AttemptedSlots.Add(asset.Slot);
            if (CreateException is not null)
            {
                return Task.FromException<MediaCreateResult>(CreateException);
            }

            if (ConflictingSlots.Contains(asset.Slot))
            {
                return Task.FromResult(MediaCreateResult.SlotConflict);
            }

            Created = asset;
            return Task.FromResult(MediaCreateResult.Created);
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
            CompletedCleanupIds.Add(id);
            PendingCleanup.RemoveAll(asset => asset.Id == id);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<List<MediaAsset>> GetPendingCleanupAsync(
            int limit,
            CancellationToken cancellationToken = default)
        {
            RequestedCleanupLimits.Add(limit);
            return Task.FromResult(PendingCleanup.Take(limit).ToList());
        }
    }

    private sealed class TrackingMemoryStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class LazyFailingStream : Stream
    {
        private bool _returnedFirstByte;
        public bool IsDisposed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_returnedFirstByte)
            {
                return ValueTask.FromException<int>(new IOException("lazy-provider-secret"));
            }

            _returnedFirstByte = true;
            buffer.Span[0] = 1;
            return ValueTask.FromResult(1);
        }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
