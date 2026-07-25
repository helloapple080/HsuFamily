namespace FamilyPortal.Application.Models;

/// <summary>媒體中繼資料原子建立結果。</summary>
public enum MediaCreateResult
{
    /// <summary>建立成功並占用指定 slot。</summary>
    Created,

    /// <summary>指定的 active owner/purpose/slot 已被占用。</summary>
    SlotConflict
}
