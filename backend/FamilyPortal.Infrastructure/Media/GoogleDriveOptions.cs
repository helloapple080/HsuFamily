namespace FamilyPortal.Infrastructure.Media;

/// <summary>Google Drive 私有媒體儲存設定。</summary>
public sealed class GoogleDriveOptions
{
    /// <summary>設定區段名稱。</summary>
    public const string SectionName = "GoogleDrive";

    /// <summary>是否啟用 Google Drive adapter。</summary>
    public bool Enabled { get; set; }

    /// <summary>OAuth client ID。</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OAuth client secret。</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>OAuth refresh token。</summary>
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>所有入口網媒體必須位於的 Drive folder ID。</summary>
    public string FolderId { get; set; } = string.Empty;
}
