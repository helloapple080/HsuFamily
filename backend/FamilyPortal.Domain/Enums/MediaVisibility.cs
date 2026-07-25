namespace FamilyPortal.Domain.Enums;

/// <summary>媒體內容的讀取範圍。</summary>
public enum MediaVisibility
{
    /// <summary>允許匿名使用者讀取。</summary>
    Public,

    /// <summary>僅允許已驗證的家族成員讀取。</summary>
    FamilyOnly
}
