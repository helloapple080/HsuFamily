using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs.Media;
using FamilyPortal.Domain.Enums;

namespace FamilyPortal.Application.Interfaces;

/// <summary>媒體驗證、授權與儲存編排的 use case 邊界。</summary>
public interface IMediaService
{
    /// <summary>驗證並上傳媒體；呼叫端必須先完成 AdminOnly 授權。</summary>
    Task<ServiceResult<MediaResponse>> UploadAsync(
        UploadMediaRequest request,
        string userId,
        CancellationToken cancellationToken);

    /// <summary>依媒體可見性與使用者驗證狀態開啟內容。</summary>
    Task<ServiceResult<MediaContentResponse>> OpenReadAsync(
        string mediaId,
        bool isAuthenticated,
        CancellationToken cancellationToken);

    /// <summary>更新媒體可見性；呼叫端必須先完成 AdminOnly 授權。</summary>
    Task<ServiceResult<MediaResponse>> UpdateVisibilityAsync(
        string mediaId,
        MediaVisibility visibility,
        string userId,
        CancellationToken cancellationToken);

    /// <summary>軟刪除媒體並嘗試清理儲存物件；呼叫端必須先完成 AdminOnly 授權。</summary>
    Task<ServiceResult<bool>> DeleteAsync(
        string mediaId,
        string userId,
        CancellationToken cancellationToken);

    /// <summary>重試有上限的待清理媒體批次；僅可由可信任背景工作者或已完成 AdminOnly 授權的入口呼叫。</summary>
    Task<ServiceResult<int>> CleanupPendingAsync(
        int limit,
        CancellationToken cancellationToken);
}
