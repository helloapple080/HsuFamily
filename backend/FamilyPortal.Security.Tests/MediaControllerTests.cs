using System.Security.Claims;
using FamilyPortal.Api.Controllers;
using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs;
using FamilyPortal.Application.DTOs.Media;
using FamilyPortal.Application.Interfaces;
using FamilyPortal.Application.Models;
using FamilyPortal.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MongoDB.Bson;
using Xunit;

namespace FamilyPortal.Security.Tests;

/// <summary>驗證媒體 HTTP adapter 的權限、輸入與回應安全契約。</summary>
public sealed class MediaControllerTests
{
    /// <summary>mutation 必須套用 AdminOnly，內容讀取才可匿名。</summary>
    [Fact]
    public void Actions_DeclareExpectedAuthorizationBoundaries()
    {
        var type = typeof(MediaController);
        AssertAdminOnly(type.GetMethod(nameof(MediaController.Upload))!);
        AssertAdminOnly(type.GetMethod(nameof(MediaController.UpdateVisibility))!);
        AssertAdminOnly(type.GetMethod(nameof(MediaController.Delete))!);
        Assert.NotNull(type.GetMethod(nameof(MediaController.GetContent))!
            .GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true).SingleOrDefault());

        var limiter = Assert.Single(type.GetMethod(nameof(MediaController.Upload))!
            .GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: true)
            .Cast<EnableRateLimitingAttribute>());
        Assert.Equal("media-upload", limiter.PolicyName);
    }

    /// <summary>超過 10 MB 的 IFormFile 必須在呼叫 Application service 前拒絕。</summary>
    [Fact]
    public async Task Upload_OversizedFileReturnsBadRequestWithoutServiceCall()
    {
        var service = new FakeMediaService();
        var controller = CreateController(service, authenticated: true, role: "Admin");
        var file = new FormFile(
            Stream.Null,
            0,
            ValidationLimits.MaxMediaBytes + 1,
            "file",
            "large.png")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/png"
        };

        var action = await controller.Upload(
            file,
            nameof(MediaPurpose.Worship),
            ObjectId.GenerateNewId().ToString(),
            nameof(MediaVisibility.FamilyOnly),
            CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(action.Result);
        Assert.IsType<ApiResponse<MediaResponse>>(badRequest.Value);
        Assert.Null(service.UploadRequest);
    }

    /// <summary>上傳者只能取自 NameIdentifier claim，不可由表單指定。</summary>
    [Fact]
    public async Task Upload_ForwardsNameIdentifierAndFileMetadata()
    {
        var userId = ObjectId.GenerateNewId().ToString();
        var ownerId = ObjectId.GenerateNewId().ToString();
        var service = new FakeMediaService
        {
            UploadResult = ServiceResult<MediaResponse>.Success(Response(ownerId))
        };
        var controller = CreateController(service, authenticated: true, role: "Admin", userId: userId);
        var file = FormFile([1, 2, 3], "family.png", "image/png");

        var action = await controller.Upload(
            file,
            nameof(MediaPurpose.MemberAvatar),
            ownerId,
            nameof(MediaVisibility.Public),
            CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(action.Result);
        Assert.Equal(userId, service.UploadUserId);
        Assert.Equal(file.Length, service.UploadRequest!.Upload.Length);
        Assert.Equal(file.FileName, service.UploadRequest.Upload.FileName);
        Assert.Equal(file.ContentType, service.UploadRequest.Upload.DeclaredContentType);
    }

    /// <summary>multipart enum 必須使用名稱，禁止以整數繞過字串契約。</summary>
    [Fact]
    public async Task Upload_NumericEnumReturnsBadRequestWithoutServiceCall()
    {
        var service = new FakeMediaService();
        var controller = CreateController(service, authenticated: true, role: "Admin", userId: "admin-1");

        var action = await controller.Upload(
            FormFile([1], "family.png", "image/png"),
            ((int)MediaPurpose.MemberAvatar).ToString(),
            ObjectId.GenerateNewId().ToString(),
            nameof(MediaVisibility.FamilyOnly),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(action.Result);
        Assert.Null(service.UploadRequest);
    }

    /// <summary>匿名狀態必須原樣傳給 service，Public 成功才可使用公開快取。</summary>
    [Fact]
    public async Task GetContent_PublicAnonymousSetsSafePublicHeaders()
    {
        var service = new FakeMediaService
        {
            OpenResult = ServiceResult<MediaContentResponse>.Success(
                new MediaContentResponse(
                    new MemoryStream([1, 2, 3], writable: false),
                    "image/png",
                    3,
                    MediaVisibility.Public))
        };
        var controller = CreateController(service, authenticated: false);

        var action = await controller.GetContent("media-1", CancellationToken.None);

        Assert.False(service.LastIsAuthenticated);
        var file = Assert.IsType<FileStreamResult>(action);
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal("nosniff", controller.Response.Headers.XContentTypeOptions);
        Assert.Equal("public, max-age=300", controller.Response.Headers.CacheControl);
    }

    /// <summary>FamilyOnly 成功必須禁止共享與瀏覽器儲存。</summary>
    [Fact]
    public async Task GetContent_PrivateAuthenticatedSetsNoStore()
    {
        var service = new FakeMediaService
        {
            OpenResult = ServiceResult<MediaContentResponse>.Success(
                new MediaContentResponse(
                    new MemoryStream([1], writable: false),
                    "image/webp",
                    1,
                    MediaVisibility.FamilyOnly))
        };
        var controller = CreateController(service, authenticated: true);

        var action = await controller.GetContent("media-1", CancellationToken.None);

        Assert.True(service.LastIsAuthenticated);
        Assert.IsType<FileStreamResult>(action);
        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl);
    }

    /// <summary>未知可見性或服務錯誤必須 fail closed 且不得回傳供應商細節。</summary>
    [Fact]
    public async Task GetContent_ForbiddenFailureIsPrivateAndRedacted()
    {
        var service = new FakeMediaService
        {
            OpenResult = ServiceResult<MediaContentResponse>.Failure(
                ServiceError.Forbidden,
                "Google Drive object storage-secret-123")
        };
        var controller = CreateController(service, authenticated: true);

        var action = await controller.GetContent("media-1", CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl);
        Assert.DoesNotContain("Google", forbidden.Value!.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storage-secret-123", forbidden.Value!.ToString(), StringComparison.Ordinal);
    }

    /// <summary>PATCH 必須只傳送 body 中已驗證的 visibility 與 claim user ID。</summary>
    [Fact]
    public async Task UpdateVisibility_ForwardsBodyAndUserClaim()
    {
        var userId = ObjectId.GenerateNewId().ToString();
        var service = new FakeMediaService
        {
            VisibilityResult = ServiceResult<MediaResponse>.Success(Response("owner"))
        };
        var controller = CreateController(service, authenticated: true, role: "Admin", userId: userId);

        var action = await controller.UpdateVisibility(
            "media-1",
            new UpdateMediaVisibilityRequest { Visibility = nameof(MediaVisibility.Public) },
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(action.Result);
        Assert.Equal(MediaVisibility.Public, service.Visibility);
        Assert.Equal(userId, service.VisibilityUserId);
    }

    /// <summary>PATCH 的未定義 enum 值必須在 service 前拒絕。</summary>
    [Fact]
    public async Task UpdateVisibility_UndefinedEnumReturnsBadRequestWithoutServiceCall()
    {
        var service = new FakeMediaService();
        var controller = CreateController(service, authenticated: true, role: "Admin", userId: "admin-1");

        var action = await controller.UpdateVisibility(
            "media-1",
            new UpdateMediaVisibilityRequest { Visibility = int.MaxValue.ToString() },
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(action.Result);
        Assert.Null(service.Visibility);
    }

    private static void AssertAdminOnly(System.Reflection.MethodInfo method)
    {
        var attribute = Assert.Single(method
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());
        Assert.Equal("AdminOnly", attribute.Policy);
    }

    private static MediaController CreateController(
        FakeMediaService service,
        bool authenticated,
        string? role = null,
        string? userId = null)
    {
        var claims = new List<Claim>();
        if (userId is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        }
        if (role is not null)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = authenticated
            ? new ClaimsIdentity(claims, "Test")
            : new ClaimsIdentity();
        return new MediaController(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            }
        };
    }

    private static IFormFile FormFile(byte[] content, string fileName, string contentType) =>
        new FormFile(new MemoryStream(content), 0, content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    private static MediaResponse Response(string ownerId) => new(
        "media-1", "file.png", "image/png", 3, 1, 1,
        MediaVisibility.Public, MediaPurpose.MemberAvatar, ownerId);

    private sealed class FakeMediaService : IMediaService
    {
        public UploadMediaRequest? UploadRequest { get; private set; }
        public string? UploadUserId { get; private set; }
        public bool LastIsAuthenticated { get; private set; }
        public MediaVisibility? Visibility { get; private set; }
        public string? VisibilityUserId { get; private set; }
        public ServiceResult<MediaResponse> UploadResult { get; init; } =
            ServiceResult<MediaResponse>.Failure(ServiceError.Validation, "驗證失敗");
        public ServiceResult<MediaContentResponse> OpenResult { get; init; } =
            ServiceResult<MediaContentResponse>.Failure(ServiceError.NotFound, "不存在");
        public ServiceResult<MediaResponse> VisibilityResult { get; init; } =
            ServiceResult<MediaResponse>.Failure(ServiceError.NotFound, "不存在");

        /// <inheritdoc />
        public Task<ServiceResult<MediaResponse>> UploadAsync(
            UploadMediaRequest request,
            string userId,
            CancellationToken cancellationToken)
        {
            UploadRequest = request;
            UploadUserId = userId;
            return Task.FromResult(UploadResult);
        }

        /// <inheritdoc />
        public Task<ServiceResult<MediaContentResponse>> OpenReadAsync(
            string mediaId,
            bool isAuthenticated,
            CancellationToken cancellationToken)
        {
            LastIsAuthenticated = isAuthenticated;
            return Task.FromResult(OpenResult);
        }

        /// <inheritdoc />
        public Task<ServiceResult<MediaResponse>> UpdateVisibilityAsync(
            string mediaId,
            MediaVisibility visibility,
            string userId,
            CancellationToken cancellationToken)
        {
            Visibility = visibility;
            VisibilityUserId = userId;
            return Task.FromResult(VisibilityResult);
        }

        /// <inheritdoc />
        public Task<ServiceResult<bool>> DeleteAsync(
            string mediaId,
            string userId,
            CancellationToken cancellationToken) =>
            Task.FromResult(ServiceResult<bool>.Success(true));

        /// <inheritdoc />
        public Task<ServiceResult<int>> CleanupPendingAsync(
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult(ServiceResult<int>.Success(0));
    }
}
