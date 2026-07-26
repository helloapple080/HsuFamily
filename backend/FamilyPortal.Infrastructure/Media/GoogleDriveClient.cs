using System.Net;
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
            var progress = await _service.Files.Get(objectId).DownloadAsync(content, cancellationToken);
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
}
