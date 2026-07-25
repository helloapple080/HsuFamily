using FamilyPortal.Application.Models;
using FamilyPortal.Domain.Enums;

namespace FamilyPortal.Application.DTOs.Media;

/// <summary>媒體上傳請求。</summary>
/// <param name="Purpose">媒體用途。</param>
/// <param name="OwnerEntityId">關聯的領域實體 ID。</param>
/// <param name="Visibility">媒體可見性。</param>
/// <param name="Upload">待驗證的上傳內容。</param>
public sealed record UploadMediaRequest(
    MediaPurpose Purpose,
    string OwnerEntityId,
    MediaVisibility Visibility,
    MediaUpload Upload);

/// <summary>可安全回傳給用戶端的媒體中繼資料。</summary>
/// <param name="Id">入口網系統內的媒體 ID。</param>
/// <param name="OriginalFileName">上傳時的原始檔名。</param>
/// <param name="ContentType">已驗證的 MIME 類型。</param>
/// <param name="Length">內容長度。</param>
/// <param name="Width">影像寬度。</param>
/// <param name="Height">影像高度。</param>
/// <param name="Visibility">媒體可見性。</param>
/// <param name="Purpose">媒體用途。</param>
/// <param name="OwnerEntityId">關聯的領域實體 ID。</param>
public sealed record MediaResponse(
    string Id,
    string OriginalFileName,
    string ContentType,
    long Length,
    int Width,
    int Height,
    MediaVisibility Visibility,
    MediaPurpose Purpose,
    string OwnerEntityId);

/// <summary>提供給 HTTP adapter 的供應商無關媒體內容描述。</summary>
/// <param name="Content">唯讀內容資料流。</param>
/// <param name="ContentType">信任的媒體 MIME 類型。</param>
/// <param name="Length">內容長度。</param>
public sealed record MediaContentResponse(Stream Content, string ContentType, long Length);
