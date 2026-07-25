namespace FamilyPortal.Application.Models;

/// <summary>描述一個待儲存的媒體上傳內容。</summary>
/// <param name="Content">媒體內容資料流。</param>
/// <param name="FileName">使用者提供的原始檔名，儲存層不得當作儲存物件名稱。</param>
/// <param name="DeclaredContentType">用戶端宣告的 MIME 類型。</param>
/// <param name="Length">用戶端宣告的內容長度。</param>
public sealed record MediaUpload(
    Stream Content,
    string FileName,
    string DeclaredContentType,
    long Length);
