namespace FamilyPortal.Application.Models;

/// <summary>描述已儲存的媒體物件，不包含供應商 SDK 類型。</summary>
/// <param name="ObjectId">儲存系統中的物件識別碼。</param>
/// <param name="ContentType">儲存內容的 MIME 類型。</param>
/// <param name="Length">儲存內容長度。</param>
/// <param name="Width">影像寬度。</param>
/// <param name="Height">影像高度。</param>
public sealed record StoredMediaObject(
    string ObjectId,
    string ContentType,
    long Length,
    int Width,
    int Height);
