namespace FamilyPortal.Infrastructure.Media;

/// <summary>Google Drive SDK 的可測試窄邊界。</summary>
public interface IGoogleDriveClient
{
    /// <summary>以 resumable upload 將內容寫入指定 folder。</summary>
    Task<GoogleDriveObjectMetadata> UploadAsync(
        Stream content,
        string contentType,
        string objectName,
        string folderId,
        CancellationToken cancellationToken);

    /// <summary>讀取判斷 folder ownership 所需的最小 metadata；404 回傳 null。</summary>
    Task<GoogleDriveObjectMetadata?> GetMetadataAsync(
        string objectId,
        CancellationToken cancellationToken);

    /// <summary>開啟下載內容；回傳 stream 由呼叫端負責 dispose。</summary>
    Task<Stream> OpenReadAsync(string objectId, CancellationToken cancellationToken);

    /// <summary>刪除物件；404 以 <see cref="GoogleDriveObjectNotFoundException"/> 表示。</summary>
    Task DeleteAsync(string objectId, CancellationToken cancellationToken);
}

/// <summary>Drive object 的最小可信 metadata。</summary>
/// <param name="ObjectId">Drive object ID。</param>
/// <param name="ParentIds">直接 parent folder IDs。</param>
/// <param name="ContentType">Drive 回報的 MIME 類型。</param>
/// <param name="Length">Drive 回報的內容長度。</param>
/// <param name="Trashed">物件是否位於垃圾桶。</param>
public sealed record GoogleDriveObjectMetadata(
    string ObjectId,
    IReadOnlyList<string> ParentIds,
    string ContentType,
    long Length,
    bool Trashed = false);

/// <summary>Drive object 已不存在。</summary>
public sealed class GoogleDriveObjectNotFoundException : Exception
{
    /// <summary>建立不含供應商 object ID 的 not-found 例外。</summary>
    public GoogleDriveObjectNotFoundException()
        : base("Google Drive 媒體物件不存在")
    {
    }
}
