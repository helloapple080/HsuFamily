using FamilyPortal.Application.Interfaces;
using FamilyPortal.Application.Models;
using FamilyPortal.Infrastructure.Media;
using Microsoft.Extensions.Options;
using Xunit;

namespace FamilyPortal.Security.Tests.Infrastructure;

/// <summary>以 fake Drive boundary 驗證 folder ownership、stream 與 metadata 契約。</summary>
public sealed class GoogleDriveMediaStorageTests
{
    /// <summary>上傳必須使用隨機物件名、指定資料夾與 inspector 的可信 metadata。</summary>
    [Fact]
    public async Task StoreAsync_UsesRandomNameFolderAndInspectedMetadata()
    {
        var client = new FakeGoogleDriveClient();
        var inspector = new FakeImageInspector(
            new ImageInspectionResult("image/png", 20, 10));
        var storage = CreateStorage(client, inspector);
        var upload = new MediaUpload(
            new MemoryStream([1, 2, 3]),
            "private-family-name.png",
            "image/png",
            3);

        var first = await storage.StoreAsync(upload, CancellationToken.None);
        upload.Content.Position = 0;
        var second = await storage.StoreAsync(upload, CancellationToken.None);

        Assert.Equal("folder-1", client.Uploads[0].FolderId);
        Assert.DoesNotContain("private-family-name", client.Uploads[0].ObjectName);
        Assert.EndsWith(".png", client.Uploads[0].ObjectName, StringComparison.Ordinal);
        Assert.NotEqual(client.Uploads[0].ObjectName, client.Uploads[1].ObjectName);
        Assert.Equal([1, 2, 3], client.Uploads[0].Content);
        Assert.Equal("image/png", first.ContentType);
        Assert.Equal(3, first.Length);
        Assert.Equal(20, first.Width);
        Assert.Equal(10, first.Height);
        Assert.Equal(20, second.Width);
    }

    /// <summary>Inspector 無法確認格式或尺寸時不得上傳。</summary>
    [Fact]
    public async Task StoreAsync_InvalidInspectionFailsBeforeUpload()
    {
        var client = new FakeGoogleDriveClient();
        var storage = CreateStorage(client, new FakeImageInspector(result: null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.StoreAsync(
            new MediaUpload(new MemoryStream([1]), "file.png", "image/png", 1),
            CancellationToken.None));

        Assert.Empty(client.Uploads);
    }

    /// <summary>上傳取消必須傳遞且不可轉成一般 provider 錯誤。</summary>
    [Fact]
    public async Task StoreAsync_CancellationPropagates()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        var client = new FakeGoogleDriveClient { CancelUpload = true };
        var storage = CreateStorage(
            client,
            new FakeImageInspector(new ImageInspectionResult("image/png", 1, 1)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => storage.StoreAsync(
            new MediaUpload(new MemoryStream([1]), "file.png", "image/png", 1),
            source.Token));
    }

    /// <summary>讀取前必須驗證物件仍在設定資料夾，且成功 stream 由呼叫端擁有。</summary>
    [Fact]
    public async Task OpenReadAsync_VerifiesParentAndReturnsOwnedStream()
    {
        var returned = new TrackingMemoryStream([1, 2, 3]);
        var client = new FakeGoogleDriveClient
        {
            Metadata = OwnedMetadata(),
            ReadStream = returned
        };
        var storage = CreateStorage(client);

        var result = await storage.OpenReadAsync("object-1", CancellationToken.None);

        Assert.Same(returned, result);
        Assert.False(returned.IsDisposed);
        result.Dispose();
        Assert.True(returned.IsDisposed);
    }

    /// <summary>資料夾外物件不得被讀取。</summary>
    [Fact]
    public async Task OpenReadAsync_WrongParentFailsClosed()
    {
        var client = new FakeGoogleDriveClient
        {
            Metadata = new GoogleDriveObjectMetadata(
                "object-1", ["other-folder"], "image/png", 3)
        };
        var storage = CreateStorage(client);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.OpenReadAsync("object-1", CancellationToken.None));

        Assert.Null(client.OpenedObjectId);
    }

    /// <summary>超大、不支援 MIME 或 trashed 物件必須在下載前拒絕。</summary>
    [Theory]
    [MemberData(nameof(UnreadableMetadata))]
    public async Task OpenReadAsync_InvalidMetadataFailsBeforeDownload(
        GoogleDriveObjectMetadata metadata)
    {
        var client = new FakeGoogleDriveClient { Metadata = metadata };
        var storage = CreateStorage(client);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.OpenReadAsync("object-1", CancellationToken.None));

        Assert.Null(client.OpenedObjectId);
    }

    /// <summary>提供不可下載的 Drive metadata 案例。</summary>
    public static TheoryData<GoogleDriveObjectMetadata> UnreadableMetadata => new()
    {
        new GoogleDriveObjectMetadata("object-1", ["folder-1"], "application/pdf", 3),
        new GoogleDriveObjectMetadata(
            "object-1", ["folder-1"], "image/png", 10 * 1024 * 1024 + 1),
        new GoogleDriveObjectMetadata("object-1", ["folder-1"], "image/png", 3, Trashed: true)
    };

    /// <summary>刪除前必須驗證 parent；missing 與 delete race 404 均視為冪等成功。</summary>
    [Fact]
    public async Task DeleteAsync_IsFolderScopedAndNotFoundIsIdempotent()
    {
        var missingClient = new FakeGoogleDriveClient { Metadata = null };
        await CreateStorage(missingClient).DeleteAsync("missing", CancellationToken.None);
        Assert.Null(missingClient.DeletedObjectId);

        var raceClient = new FakeGoogleDriveClient
        {
            Metadata = OwnedMetadata(),
            DeleteNotFound = true
        };
        await CreateStorage(raceClient).DeleteAsync("object-1", CancellationToken.None);
        Assert.Equal("object-1", raceClient.DeletedObjectId);

        var wrongParentClient = new FakeGoogleDriveClient
        {
            Metadata = new GoogleDriveObjectMetadata(
                "object-1", ["other-folder"], "image/png", 3)
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateStorage(wrongParentClient).DeleteAsync("object-1", CancellationToken.None));
        Assert.Null(wrongParentClient.DeletedObjectId);
    }

    private static GoogleDriveMediaStorage CreateStorage(
        IGoogleDriveClient client,
        IImageInspector? inspector = null) => new(
            Options.Create(new GoogleDriveOptions
            {
                Enabled = true,
                ClientId = "client-id",
                ClientSecret = "client-secret",
                RefreshToken = "refresh-token",
                FolderId = "folder-1"
            }),
            client,
            inspector ?? new FakeImageInspector(new ImageInspectionResult("image/png", 1, 1)));

    private static GoogleDriveObjectMetadata OwnedMetadata() => new(
        "object-1", ["folder-1"], "image/png", 3);

    private sealed class FakeImageInspector(ImageInspectionResult? result) : IImageInspector
    {
        /// <inheritdoc />
        public Task<ImageInspectionResult?> InspectAsync(
            Stream content,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }
    }

    private sealed class FakeGoogleDriveClient : IGoogleDriveClient
    {
        public List<UploadCall> Uploads { get; } = [];
        public GoogleDriveObjectMetadata? Metadata { get; init; } = OwnedMetadata();
        public Stream ReadStream { get; init; } = new MemoryStream([1, 2, 3]);
        public bool CancelUpload { get; init; }
        public bool DeleteNotFound { get; init; }
        public string? OpenedObjectId { get; private set; }
        public string? DeletedObjectId { get; private set; }

        /// <inheritdoc />
        public async Task<GoogleDriveObjectMetadata> UploadAsync(
            Stream content,
            string contentType,
            string objectName,
            string folderId,
            CancellationToken cancellationToken)
        {
            if (CancelUpload)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            Uploads.Add(new UploadCall(objectName, folderId, copy.ToArray()));
            return new GoogleDriveObjectMetadata(
                $"object-{Uploads.Count}", [folderId], contentType, copy.Length);
        }

        /// <inheritdoc />
        public Task<GoogleDriveObjectMetadata?> GetMetadataAsync(
            string objectId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Metadata);
        }

        /// <inheritdoc />
        public Task<Stream> OpenReadAsync(
            string objectId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenedObjectId = objectId;
            return Task.FromResult(ReadStream);
        }

        /// <inheritdoc />
        public Task DeleteAsync(string objectId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DeletedObjectId = objectId;
            return DeleteNotFound
                ? Task.FromException(new GoogleDriveObjectNotFoundException())
                : Task.CompletedTask;
        }
    }

    private sealed record UploadCall(string ObjectName, string FolderId, byte[] Content);

    private sealed class TrackingMemoryStream(byte[] content) : MemoryStream(content)
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
