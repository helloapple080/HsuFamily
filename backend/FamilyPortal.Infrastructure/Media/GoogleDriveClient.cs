using System.Net;
using FamilyPortal.Application.Common;
using Google;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;

namespace FamilyPortal.Infrastructure.Media;

/// <summary>Google Drive v3 SDK 的 resumable/streaming client。</summary>
public sealed class GoogleDriveClient : IGoogleDriveClient, IDisposable
{
    private const string MetadataFields = "id,parents,mimeType,size,trashed";
    private readonly DriveService _service;
    private readonly GoogleAuthorizationCodeFlow? _authorizationFlow;

    /// <summary>以 OAuth refresh token 建立只要求 DriveFile scope 的 client。</summary>
    public GoogleDriveClient(GoogleDriveOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _authorizationFlow = CreateAuthorizationFlow(options);
        var credential = new UserCredential(
            _authorizationFlow,
            "family-portal-media",
            new TokenResponse { RefreshToken = options.RefreshToken });
        _service = new DriveService(new BaseClientService.Initializer
        {
            ApplicationName = "FamilyPortal",
            HttpClientInitializer = credential
        });
    }

    /// <summary>以可注入 HTTP transport 的 Drive service 建立 client。</summary>
    public GoogleDriveClient(DriveService service)
    {
        _service = service;
    }

    /// <inheritdoc />
    public async Task<GoogleDriveObjectMetadata> UploadAsync(
        Stream content,
        string contentType,
        string objectName,
        string folderId,
        CancellationToken cancellationToken)
    {
        var file = new Google.Apis.Drive.v3.Data.File
        {
            Name = objectName,
            Parents = [folderId]
        };
        var request = _service.Files.Create(file, content, contentType);
        request.Fields = MetadataFields;
        request.ChunkSize = ResumableUpload.MinimumChunkSize;
        var progress = await request.UploadAsync(cancellationToken);
        if (progress.Status != UploadStatus.Completed || request.ResponseBody is null)
        {
            throw progress.Exception ?? new InvalidOperationException("Google Drive 上傳未完成");
        }

        return MapMetadata(request.ResponseBody);
    }

    /// <inheritdoc />
    public async Task<GoogleDriveObjectMetadata?> GetMetadataAsync(
        string objectId,
        CancellationToken cancellationToken)
    {
        try
        {
            var request = _service.Files.Get(objectId);
            request.Fields = MetadataFields;
            return MapMetadata(await request.ExecuteAsync(cancellationToken));
        }
        catch (GoogleApiException exception) when (exception.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<Stream> OpenReadAsync(
        string objectId,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"familyportal-media-{Guid.NewGuid():N}.tmp");
        var content = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
        try
        {
            using var cappedContent = new CappedWriteStream(
                content,
                ValidationLimits.MaxMediaBytes);
            var progress = await _service.Files.Get(objectId).DownloadAsync(
                cappedContent,
                cancellationToken);
            if (progress.Status != Google.Apis.Download.DownloadStatus.Completed)
            {
                throw progress.Exception ?? new InvalidOperationException("Google Drive 下載未完成");
            }

            content.Position = 0;
            return content;
        }
        catch (GoogleApiException exception) when (exception.HttpStatusCode == HttpStatusCode.NotFound)
        {
            await content.DisposeAsync();
            throw new GoogleDriveObjectNotFoundException();
        }
        catch
        {
            await content.DisposeAsync();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string objectId, CancellationToken cancellationToken)
    {
        try
        {
            await _service.Files.Delete(objectId).ExecuteAsync(cancellationToken);
        }
        catch (GoogleApiException exception) when (exception.HttpStatusCode == HttpStatusCode.NotFound)
        {
            throw new GoogleDriveObjectNotFoundException();
        }
    }

    /// <summary>釋放 Drive service 與底層 HTTP transport。</summary>
    public void Dispose()
    {
        _service.Dispose();
        _authorizationFlow?.Dispose();
    }

    private static GoogleAuthorizationCodeFlow CreateAuthorizationFlow(
        GoogleDriveOptions options)
    {
        return new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets
            {
                ClientId = options.ClientId,
                ClientSecret = options.ClientSecret
            },
            Scopes = [DriveService.Scope.DriveFile]
        });
    }

    private static GoogleDriveObjectMetadata MapMetadata(
        Google.Apis.Drive.v3.Data.File file) => new(
        file.Id ?? string.Empty,
        file.Parents?.ToArray() ?? [],
        file.MimeType ?? string.Empty,
        file.Size ?? 0,
        file.Trashed ?? false);

    private sealed class CappedWriteStream(Stream inner, long maxLength) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set
            {
                EnsurePositionWithinLimit(value);
                inner.Position = value;
            }
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin)
        {
            var target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => checked(inner.Position + offset),
                SeekOrigin.End => checked(inner.Length + offset),
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            EnsurePositionWithinLimit(target);
            return inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            EnsurePositionWithinLimit(value);
            inner.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureWriteWithinLimit(count);
            inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureWriteWithinLimit(buffer.Length);
            inner.Write(buffer);
        }

        public override Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            EnsureWriteWithinLimit(count);
            return inner.WriteAsync(buffer, offset, count, cancellationToken);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            EnsureWriteWithinLimit(buffer.Length);
            return inner.WriteAsync(buffer, cancellationToken);
        }

        public override void WriteByte(byte value)
        {
            EnsureWriteWithinLimit(1);
            inner.WriteByte(value);
        }

        protected override void Dispose(bool disposing)
        {
            // 底層 temporary stream 的生命週期由 OpenReadAsync 或呼叫端管理。
            base.Dispose(disposing);
        }

        private void EnsureWriteWithinLimit(int count)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            long endPosition;
            try
            {
                endPosition = checked(inner.Position + count);
            }
            catch (OverflowException)
            {
                throw DownloadLimitExceeded();
            }

            EnsurePositionWithinLimit(endPosition);
        }

        private static InvalidDataException DownloadLimitExceeded() =>
            new("媒體下載內容超過允許大小");

        private void EnsurePositionWithinLimit(long position)
        {
            if (position < 0)
            {
                throw new IOException("無法移至媒體資料流起點之前");
            }

            if (position > maxLength)
            {
                throw DownloadLimitExceeded();
            }
        }
    }
}
