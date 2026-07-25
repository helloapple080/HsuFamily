using FamilyPortal.Application.Models;

namespace FamilyPortal.Application.Interfaces;

/// <summary>不依賴特定供應商 SDK 的媒體儲存邊界。</summary>
public interface IMediaStorage
{
    /// <summary>取得用於中繼資料的儲存供應商識別名稱。</summary>
    string ProviderName { get; }

    /// <summary>儲存已通過 Application 驗證的媒體內容。</summary>
    Task<StoredMediaObject> StoreAsync(
        MediaUpload upload,
        CancellationToken cancellationToken);

    /// <summary>開啟儲存物件的唯讀資料流。</summary>
    Task<Stream> OpenReadAsync(
        string storageObjectId,
        CancellationToken cancellationToken);

    /// <summary>刪除儲存物件。</summary>
    Task DeleteAsync(
        string storageObjectId,
        CancellationToken cancellationToken);
}
