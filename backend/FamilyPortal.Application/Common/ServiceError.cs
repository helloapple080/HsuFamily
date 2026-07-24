namespace FamilyPortal.Application.Common;

/// <summary>Application use case 可回傳的失敗類型。</summary>
public enum ServiceError
{
    /// <summary>沒有錯誤。</summary>
    None,

    /// <summary>輸入資料驗證失敗。</summary>
    Validation,

    /// <summary>尚未通過身分驗證。</summary>
    Unauthorized,

    /// <summary>已驗證身分但沒有操作權限。</summary>
    Forbidden,

    /// <summary>找不到要求的資源。</summary>
    NotFound,

    /// <summary>要求與現有資料發生衝突。</summary>
    Conflict
}
