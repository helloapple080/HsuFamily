using System.Net;
using System.Net.Http.Headers;
using System.IO.Compression;
using System.Text;
using FamilyPortal.Application.Common;
using FamilyPortal.Infrastructure.Media;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using Xunit;

namespace FamilyPortal.Security.Tests.Infrastructure;

/// <summary>以 fake HTTP handler 驗證 Google SDK boundary，不連外。</summary>
public sealed class GoogleDriveClientTests
{
    /// <summary>SDK client 必須使用 resumable session，並送出 folder parent 與原始 bytes。</summary>
    [Fact]
    public async Task UploadAsync_UsesResumableProtocolAndFolderParent()
    {
        var handler = new RecordingHandler(async request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                var metadata = await ReadContentAsStringAsync(request.Content!);
                Assert.Contains("folder-1", metadata, StringComparison.Ordinal);
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Headers.Location = new Uri("https://upload.example/session-1");
                return response;
            }

            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal([1, 2, 3], await request.Content!.ReadAsByteArrayAsync());
            return JsonResponse(
                """{"id":"object-1","parents":["folder-1"],"mimeType":"image/png","size":"3"}""");
        });
        using var service = CreateService(handler);
        using var client = new GoogleDriveClient(service);

        var result = await client.UploadAsync(
            new MemoryStream([1, 2, 3]),
            "image/png",
            "random.png",
            "folder-1",
            CancellationToken.None);

        Assert.Equal("object-1", result.ObjectId);
        Assert.Equal(3, result.Length);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains("uploadType=resumable", handler.Requests[0].Uri.Query, StringComparison.Ordinal);
        Assert.Equal("https://upload.example/session-1", handler.Requests[1].Uri.ToString());
    }

    /// <summary>下載必須串流至 caller-owned delete-on-close stream。</summary>
    [Fact]
    public async Task OpenReadAsync_UsesMediaDownloadAndReturnsReadableStream()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Contains("alt=media", request.RequestUri!.Query, StringComparison.Ordinal);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3])
            });
        });
        using var service = CreateService(handler);
        using var client = new GoogleDriveClient(service);

        await using var content = await client.OpenReadAsync("object-1", CancellationToken.None);

        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, TestContext.Current.CancellationToken);
        Assert.Equal([1, 2, 3], copy.ToArray());
        Assert.True(content.CanRead);
    }

    /// <summary>下載本文超過媒體上限時必須中止，且不得留下 temporary file。</summary>
    [Fact]
    public async Task OpenReadAsync_OversizedPayloadFailsAndDeletesTemporaryFile()
    {
        var filesBefore = TemporaryDownloadFiles();
        var handler = new RecordingHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new OversizedMediaContent()
            }));
        using var service = CreateService(handler);
        using var client = new GoogleDriveClient(service);

        var exception = await Record.ExceptionAsync(async () =>
        {
            await using var returned = await client.OpenReadAsync(
                "object-1",
                TestContext.Current.CancellationToken);
        });

        Assert.IsType<InvalidDataException>(exception);
        Assert.Equal(filesBefore, TemporaryDownloadFiles());
    }

    /// <summary>下載取消必須傳遞 cancellation，並關閉及刪除 temporary file。</summary>
    [Fact]
    public async Task OpenReadAsync_CancellationDeletesTemporaryFile()
    {
        var filesBefore = TemporaryDownloadFiles();
        var handler = new RecordingHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var service = CreateService(handler);
        using var client = new GoogleDriveClient(service);
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.OpenReadAsync("object-1", source.Token));

        Assert.Equal(filesBefore, TemporaryDownloadFiles());
    }

    /// <summary>SDK delete 404 必須轉成 storage 可辨識的 not-found 例外。</summary>
    [Fact]
    public async Task DeleteAsync_NotFoundMapsToIdempotentBoundaryException()
    {
        var handler = new RecordingHandler(_ => Task.FromResult(JsonResponse(
            """{"error":{"code":404,"message":"not found"}}""",
            HttpStatusCode.NotFound)));
        using var service = CreateService(handler);
        using var client = new GoogleDriveClient(service);

        await Assert.ThrowsAsync<GoogleDriveObjectNotFoundException>(() =>
            client.DeleteAsync("object-1", CancellationToken.None));
    }

    /// <summary>SDK client 必須公開 deterministic disposal contract。</summary>
    [Fact]
    public void Client_ImplementsDisposableContract()
    {
        var handler = new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var service = CreateService(handler);
        var client = new GoogleDriveClient(service);

        Assert.IsAssignableFrom<IDisposable>(client);
        client.Dispose();
    }

    private static DriveService CreateService(HttpMessageHandler handler)
    {
        var factory = new HttpClientFromMessageHandlerFactory(_ =>
            new HttpClientFromMessageHandlerFactory.ConfiguredHttpMessageHandler(
                handler,
                false,
                false));
        return new DriveService(new BaseClientService.Initializer
        {
            ApplicationName = "FamilyPortal.Tests",
            HttpClientFactory = factory
        });
    }

    private static HttpResponseMessage JsonResponse(
        string json,
        HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static async Task<string> ReadContentAsStringAsync(HttpContent content)
    {
        var bytes = await content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        if (!content.Headers.ContentEncoding.Contains("gzip", StringComparer.OrdinalIgnoreCase))
        {
            return Encoding.UTF8.GetString(bytes);
        }

        await using var compressed = new MemoryStream(bytes);
        await using var decompressor = new GZipStream(compressed, CompressionMode.Decompress);
        using var uncompressed = new MemoryStream();
        await decompressor.CopyToAsync(uncompressed, TestContext.Current.CancellationToken);
        return Encoding.UTF8.GetString(uncompressed.ToArray());
    }

    private static HashSet<string> TemporaryDownloadFiles() =>
        Directory.GetFiles(Path.GetTempPath(), "familyportal-media-*.tmp")
            .ToHashSet(StringComparer.Ordinal);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>
            _responseFactory;

        public RecordingHandler(
            Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
            : this((request, _) => responseFactory(request))
        {
        }

        public RecordingHandler(
            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!));
            var response = await _responseFactory(request, cancellationToken);
            response.RequestMessage = request;
            return response;
        }
    }

    private sealed class OversizedMediaContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) => WritePayloadAsync(stream, CancellationToken.None);

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken) => WritePayloadAsync(stream, cancellationToken);

        protected override bool TryComputeLength(out long length)
        {
            length = ValidationLimits.MaxMediaBytes + 1;
            return true;
        }

        private static async Task WritePayloadAsync(
            Stream stream,
            CancellationToken cancellationToken)
        {
            var buffer = new byte[81920];
            long remaining = ValidationLimits.MaxMediaBytes + 1;
            while (remaining > 0)
            {
                var count = (int)Math.Min(buffer.Length, remaining);
                await stream.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                remaining -= count;
            }
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri);
}
