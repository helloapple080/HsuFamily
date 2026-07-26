using System.Security.Claims;
using FamilyPortal.Api.Controllers;
using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs.Media;
using FamilyPortal.Application.Interfaces;
using FamilyPortal.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FamilyPortal.Security.Tests;

/// <summary>驗證媒體 API 的授權決策與 MVC stream 生命週期。</summary>
public sealed class MediaApiAuthorizationTests
{
    /// <summary>AdminOnly 必須對匿名 challenge、對一般使用者 forbid，並允許 Admin。</summary>
    [Fact]
    public async Task AdminOnlyPolicy_ProducesExpectedPipelineDecisions()
    {
        await using var provider = CreateAuthorizationProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var evaluator = provider.GetRequiredService<IPolicyEvaluator>();
        var policy = await policyProvider.GetPolicyAsync("AdminOnly");
        Assert.NotNull(policy);

        var anonymous = await EvaluateAsync(
            evaluator,
            policy,
            new ClaimsPrincipal(new ClaimsIdentity()));
        Assert.True(anonymous.Challenged);

        var user = await EvaluateAsync(evaluator, policy, Principal("User"));
        Assert.True(user.Forbidden);

        var admin = await EvaluateAsync(evaluator, policy, Principal("Admin"));
        Assert.True(admin.Succeeded);
    }

    /// <summary>公開內容須由 MVC 寫入 response，且 executor 完成後 dispose stream。</summary>
    [Fact]
    public async Task PublicContent_MvcExecutorWritesResponseAndDisposesStream()
    {
        var stream = new TrackingMemoryStream([1, 2, 3]);
        var service = new FakeMediaService
        {
            OpenResult = ServiceResult<MediaContentResponse>.Success(
                new MediaContentResponse(stream, "image/png", 3, MediaVisibility.Public))
        };
        var responseBody = new MemoryStream();
        var actionContext = CreateActionContext(authenticated: false, responseBody);
        var controller = new MediaController(service)
        {
            ControllerContext = new ControllerContext(actionContext)
        };

        var action = await controller.GetContent("public", CancellationToken.None);
        var file = Assert.IsType<FileStreamResult>(action);
        var executor = new FileStreamResultExecutor(NullLoggerFactory.Instance);

        await executor.ExecuteAsync(actionContext, file);

        Assert.Equal([1, 2, 3], responseBody.ToArray());
        Assert.Equal("nosniff", actionContext.HttpContext.Response.Headers.XContentTypeOptions);
        Assert.Equal(
            "public, max-age=0, must-revalidate",
            actionContext.HttpContext.Response.Headers.CacheControl);
        Assert.True(stream.IsDisposed);
    }

    /// <summary>私有內容的匿名請求須維持 401 並禁止快取。</summary>
    [Fact]
    public async Task PrivateContent_AnonymousIsUnauthorizedAndNoStore()
    {
        var service = new FakeMediaService
        {
            OpenResult = ServiceResult<MediaContentResponse>.Failure(
                ServiceError.Unauthorized,
                "provider-secret-details")
        };
        var actionContext = CreateActionContext(authenticated: false, new MemoryStream());
        var controller = new MediaController(service)
        {
            ControllerContext = new ControllerContext(actionContext)
        };

        var action = await controller.GetContent("private", CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(action);
        Assert.Equal(StatusCodes.Status401Unauthorized, unauthorized.StatusCode);
        Assert.Equal("private, no-store", actionContext.HttpContext.Response.Headers.CacheControl);
        Assert.DoesNotContain("provider-secret-details", unauthorized.Value!.ToString(), StringComparison.Ordinal);
    }

    private static ServiceProvider CreateAuthorizationProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication();
        services.AddAuthorization(options =>
            options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin")));
        return services.BuildServiceProvider();
    }

    private static async Task<PolicyAuthorizationResult> EvaluateAsync(
        IPolicyEvaluator evaluator,
        AuthorizationPolicy policy,
        ClaimsPrincipal principal)
    {
        var context = new DefaultHttpContext { User = principal };
        var authentication = await evaluator.AuthenticateAsync(policy, context);
        return await evaluator.AuthorizeAsync(policy, authentication, context, resource: null);
    }

    private static ClaimsPrincipal Principal(string role) =>
        new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "user-1"),
                new Claim(ClaimTypes.Role, role)
            ],
            "Test"));

    private static ActionContext CreateActionContext(bool authenticated, Stream responseBody)
    {
        var identity = authenticated
            ? new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "user-1")], "Test")
            : new ClaimsIdentity();
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };
        httpContext.Response.Body = responseBody;
        return new ActionContext(httpContext, new RouteData(), new ControllerActionDescriptor());
    }

    private sealed class FakeMediaService : IMediaService
    {
        public ServiceResult<MediaContentResponse> OpenResult { get; init; } =
            ServiceResult<MediaContentResponse>.Failure(ServiceError.NotFound, "不存在");

        /// <inheritdoc />
        public Task<ServiceResult<MediaResponse>> UploadAsync(
            UploadMediaRequest request,
            string userId,
            CancellationToken cancellationToken) =>
            Task.FromResult(ServiceResult<MediaResponse>.Failure(
                ServiceError.Validation,
                "驗證失敗"));

        /// <inheritdoc />
        public Task<ServiceResult<MediaContentResponse>> OpenReadAsync(
            string mediaId,
            bool isAuthenticated,
            CancellationToken cancellationToken) => Task.FromResult(OpenResult);

        /// <inheritdoc />
        public Task<ServiceResult<MediaResponse>> UpdateVisibilityAsync(
            string mediaId,
            MediaVisibility visibility,
            string userId,
            CancellationToken cancellationToken) =>
            Task.FromResult(ServiceResult<MediaResponse>.Failure(
                ServiceError.NotFound,
                "不存在"));

        /// <inheritdoc />
        public Task<ServiceResult<bool>> DeleteAsync(
            string mediaId,
            string userId,
            CancellationToken cancellationToken) =>
            Task.FromResult(ServiceResult<bool>.Failure(ServiceError.NotFound, "不存在"));

        /// <inheritdoc />
        public Task<ServiceResult<int>> CleanupPendingAsync(
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult(ServiceResult<int>.Success(0));
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
}
