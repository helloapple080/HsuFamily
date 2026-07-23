namespace FamilyPortal.Domain.ValueObjects;

/// <summary>
/// 聯絡資訊值物件 — 嵌套於 Member 實體
/// </summary>
public class ContactInfo
{
    /// <summary>電話號碼</summary>
    public string? Phone { get; set; }

    /// <summary>LINE ID</summary>
    public string? LineId { get; set; }
}
