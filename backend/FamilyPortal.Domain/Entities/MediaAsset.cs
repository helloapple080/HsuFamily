using FamilyPortal.Domain.Enums;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace FamilyPortal.Domain.Entities;

/// <summary>媒體資產中繼資料，不包含特定儲存供應商的行為。</summary>
[BsonIgnoreExtraElements]
public sealed class MediaAsset : BaseEntity
{
    /// <summary>儲存供應商識別名稱。</summary>
    [BsonElement("storageProvider")]
    public string StorageProvider { get; set; } = string.Empty;

    /// <summary>儲存供應商中的物件識別碼。</summary>
    [BsonElement("storageObjectId")]
    public string StorageObjectId { get; set; } = string.Empty;

    /// <summary>上傳時的原始檔名。</summary>
    [BsonElement("originalFileName")]
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>媒體 MIME 類型。</summary>
    [BsonElement("contentType")]
    public string ContentType { get; set; } = string.Empty;

    /// <summary>檔案大小（bytes）。</summary>
    [BsonElement("sizeBytes")]
    public long SizeBytes { get; set; }

    /// <summary>影像寬度（pixels）。</summary>
    [BsonElement("width")]
    public int Width { get; set; }

    /// <summary>影像高度（pixels）。</summary>
    [BsonElement("height")]
    public int Height { get; set; }

    /// <summary>媒體的讀取範圍。</summary>
    [BsonElement("visibility")]
    [BsonRepresentation(BsonType.String)]
    public MediaVisibility Visibility { get; set; } = MediaVisibility.FamilyOnly;

    /// <summary>媒體的功能用途。</summary>
    [BsonElement("purpose")]
    [BsonRepresentation(BsonType.String)]
    public MediaPurpose Purpose { get; set; }

    /// <summary>同一擁有者與用途下的原子占位編號。</summary>
    [BsonElement("slot")]
    public int Slot { get; set; }

    /// <summary>擁有此媒體的領域實體 ID。</summary>
    [BsonElement("ownerEntityId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string OwnerEntityId { get; set; } = string.Empty;

    /// <summary>上傳者的 User ID。</summary>
    [BsonElement("uploadedBy")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string UploadedBy { get; set; } = string.Empty;

    /// <summary>軟刪除時間（UTC）；未刪除時為 null。</summary>
    [BsonElement("deletedAt")]
    public DateTime? DeletedAt { get; set; }

    /// <summary>儲存物件是否尚待清理。</summary>
    [BsonElement("cleanupPending")]
    public bool CleanupPending { get; set; }
}
