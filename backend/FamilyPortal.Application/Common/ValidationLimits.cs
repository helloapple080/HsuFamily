namespace FamilyPortal.Application.Common;

/// <summary>API 與資料存取共用的資源上限。</summary>
public static class ValidationLimits
{
    public const int MaxPageSize = 100;
    public const int MaxDateRangeDays = 62;
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 4000;
    public const int MaxShortTextLength = 200;
    public const int MaxNotesLength = 8000;
    public const int MaxUrlLength = 2048;
    public const int MaxTags = 20;
    public const int MaxTagLength = 50;
    public const int MaxRelatedMembers = 100;
    public const int MaxParticipants = 100;
    public const int MaxPhotos = 100;
    public const int MaxRecurrenceInterval = 120;
}
