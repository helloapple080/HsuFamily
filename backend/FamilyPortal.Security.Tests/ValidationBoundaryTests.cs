using System.ComponentModel.DataAnnotations;
using System.Reflection;
using FamilyPortal.Api.Controllers;
using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs.Auth;
using FamilyPortal.Application.DTOs.Common;
using FamilyPortal.Application.DTOs.Event;
using FamilyPortal.Application.DTOs.Itinerary;
using FamilyPortal.Application.DTOs.Member;
using FamilyPortal.Domain.Enums;
using FamilyPortal.Domain.ValueObjects;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace FamilyPortal.Security.Tests;

/// <summary>驗證 API transport DTO 的資源與巢狀輸入邊界。</summary>
public sealed class ValidationBoundaryTests
{
    /// <summary>事件日期查詢 action 必須綁定受限制的 transport request。</summary>
    [Fact]
    public void EventsController_GetByDateRange_BindsValidatedRangeRequest()
    {
        var method = typeof(EventsController).GetMethod(nameof(EventsController.GetByDateRange));

        Assert.NotNull(method);
        var parameter = method.GetParameters()
            .Single(item => item.ParameterType == typeof(EventRangeRequest));
        Assert.Equal(typeof(EventRangeRequest), parameter.ParameterType);
        Assert.NotNull(parameter.GetCustomAttribute<FromQueryAttribute>());
        Assert.True(method.GetParameters()
            .Any(item => item.ParameterType == typeof(CancellationToken)));
    }

    /// <summary>Request DTO 不得直接公開 Domain 巢狀值物件，Response DTO 則維持既有 shape。</summary>
    [Fact]
    public void NestedDtoTypes_SeparateRequestsFromDomainWhileKeepingResponsesCompatible()
    {
        Assert.Equal(typeof(LunarDateRequest), PropertyType<UpsertEventRequest>(nameof(UpsertEventRequest.LunarDate)));
        Assert.Equal(typeof(RecurrenceRuleRequest), PropertyType<UpsertEventRequest>(nameof(UpsertEventRequest.RecurrenceRule)));
        Assert.Equal(typeof(WorshipDetailsRequest), PropertyType<UpsertEventRequest>(nameof(UpsertEventRequest.WorshipDetails)));
        Assert.Equal(typeof(LunarDateRequest), PropertyType<UpsertMemberRequest>(nameof(UpsertMemberRequest.LunarBirthday)));
        Assert.Equal(typeof(ContactInfoRequest), PropertyType<UpsertMemberRequest>(nameof(UpsertMemberRequest.ContactInfo)));
        Assert.Equal(typeof(List<ParticipantRequest>), PropertyType<UpsertItineraryRequest>(nameof(UpsertItineraryRequest.Participants)));

        Assert.Equal(typeof(LunarDate), PropertyType<EventResponse>(nameof(EventResponse.LunarDate)));
        Assert.Equal(typeof(RecurrenceRule), PropertyType<EventResponse>(nameof(EventResponse.RecurrenceRule)));
        Assert.Equal(typeof(WorshipDetails), PropertyType<EventResponse>(nameof(EventResponse.WorshipDetails)));
        Assert.Equal(typeof(LunarDate), PropertyType<MemberResponse>(nameof(MemberResponse.LunarBirthday)));
        Assert.Equal(typeof(ContactInfo), PropertyType<MemberResponse>(nameof(MemberResponse.ContactInfo)));
        Assert.Equal(typeof(List<ParticipantSnapshot>), PropertyType<ItineraryResponse>(nameof(ItineraryResponse.Participants)));
    }

    /// <summary>農曆日期應拒絕超出月日範圍的值。</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(13, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 31)]
    public void LunarDateRequest_RejectsOutOfRangeValues(int month, int day)
    {
        var request = new LunarDateRequest { Year = 2026, Month = month, Day = day };

        Assert.False(IsValid(request));
    }

    /// <summary>農曆日期應接受所有精確邊界。</summary>
    [Theory]
    [InlineData(1900, 1, 1)]
    [InlineData(2200, 12, 30)]
    public void LunarDateRequest_AcceptsExactBoundaries(int year, int month, int day)
    {
        var request = new LunarDateRequest { Year = year, Month = month, Day = day };

        Assert.True(IsValid(request));
    }

    /// <summary>農曆年份與顯示文字應拒絕 one-beyond 值。</summary>
    [Fact]
    public void LunarDateRequest_RejectsYearAndDisplayTextBeyondLimits()
    {
        Assert.False(IsValid(new LunarDateRequest { Year = 1899, Month = 1, Day = 1 }));
        Assert.False(IsValid(new LunarDateRequest { Year = 2201, Month = 1, Day = 1 }));
        Assert.False(IsValid(new LunarDateRequest
        {
            Year = 2026,
            Month = 1,
            Day = 1,
            DisplayText = new string('農', 33)
        }));
    }

    /// <summary>週期規則應接受 interval 與農曆日清單的精確邊界。</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(ValidationLimits.MaxRecurrenceInterval)]
    public void RecurrenceRuleRequest_AcceptsIntervalBoundaries(int interval)
    {
        var request = new RecurrenceRuleRequest
        {
            Type = RecurrenceType.LunarMonthly,
            Interval = interval,
            LunarDays = [1, 30]
        };

        Assert.True(IsValid(request));
    }

    /// <summary>週期規則應拒絕 interval 與農曆日清單超界。</summary>
    [Fact]
    public void RecurrenceRuleRequest_RejectsInvalidIntervalAndLunarDays()
    {
        Assert.False(IsValid(new RecurrenceRuleRequest { Interval = 0 }));
        Assert.False(IsValid(new RecurrenceRuleRequest { Interval = ValidationLimits.MaxRecurrenceInterval + 1 }));
        Assert.False(IsValid(new RecurrenceRuleRequest { Interval = 1, LunarDays = [0] }));
        Assert.False(IsValid(new RecurrenceRuleRequest { Interval = 1, LunarDays = [31] }));
        Assert.False(IsValid(new RecurrenceRuleRequest
        {
            Interval = 1,
            LunarDays = Enumerable.Repeat(1, 31).ToList()
        }));
    }

    /// <summary>事件日期查詢應接受 62 天的精確上限。</summary>
    [Fact]
    public void EventRangeRequest_AcceptsExactlySixtyTwoDays()
    {
        var start = UtcDate(2026, 1, 1);
        var request = new EventRangeRequest
        {
            Start = start,
            End = start.AddDays(ValidationLimits.MaxDateRangeDays)
        };

        Assert.True(IsValid(request));
    }

    /// <summary>事件日期查詢應拒絕超過 62 天。</summary>
    [Fact]
    public void EventRangeRequest_RejectsMoreThanSixtyTwoDays()
    {
        var request = new EventRangeRequest
        {
            Start = UtcDate(2026, 1, 1),
            End = UtcDate(2026, 3, 5)
        };

        Assert.False(IsValid(request));
    }

    /// <summary>事件日期查詢應拒絕結束早於開始。</summary>
    [Fact]
    public void EventRangeRequest_RejectsEndBeforeStart()
    {
        var request = new EventRangeRequest
        {
            Start = UtcDate(2026, 1, 2),
            End = UtcDate(2026, 1, 1)
        };

        Assert.False(IsValid(request));
    }

    /// <summary>事件 DTO 應接受文字、集合與巢狀值的精確上限。</summary>
    [Fact]
    public void UpsertEventRequest_AcceptsExactResourceBoundaries()
    {
        var request = ValidEvent();
        request.Title = new string('t', ValidationLimits.MaxTitleLength);
        request.Description = new string('d', ValidationLimits.MaxDescriptionLength);
        request.Tags = Enumerable.Repeat(new string('t', ValidationLimits.MaxTagLength), ValidationLimits.MaxTags).ToList();
        request.RelatedMemberIds = Enumerable.Repeat(new string('m', ValidationLimits.MaxShortTextLength), ValidationLimits.MaxRelatedMembers).ToList();
        request.WorshipDetails = new WorshipDetailsRequest
        {
            Deity = new string('d', ValidationLimits.MaxShortTextLength),
            Offerings = Enumerable.Repeat(new string('o', ValidationLimits.MaxShortTextLength), ValidationLimits.MaxTags).ToList(),
            Preparations = Enumerable.Repeat(new string('p', ValidationLimits.MaxShortTextLength), ValidationLimits.MaxTags).ToList(),
            Notes = new string('n', ValidationLimits.MaxNotesLength)
        };

        Assert.True(IsValid(request));
    }

    /// <summary>事件 DTO 應拒絕超長文字欄位。</summary>
    [Fact]
    public void UpsertEventRequest_RejectsOversizedTextFields()
    {
        var title = ValidEvent();
        title.Title = new string('t', ValidationLimits.MaxTitleLength + 1);
        var description = ValidEvent();
        description.Description = new string('d', ValidationLimits.MaxDescriptionLength + 1);
        var eventType = ValidEvent();
        eventType.EventType = new string('e', ValidationLimits.MaxShortTextLength + 1);
        var color = ValidEvent();
        color.Color = new string('c', ValidationLimits.MaxShortTextLength + 1);

        Assert.False(IsValid(title));
        Assert.False(IsValid(description));
        Assert.False(IsValid(eventType));
        Assert.False(IsValid(color));
    }

    /// <summary>事件 DTO 應拒絕超出上限的集合。</summary>
    [Fact]
    public void UpsertEventRequest_RejectsOversizedCollections()
    {
        var tags = ValidEvent();
        tags.Tags = Enumerable.Repeat("family", ValidationLimits.MaxTags + 1).ToList();
        var relatedMembers = ValidEvent();
        relatedMembers.RelatedMemberIds = Enumerable.Repeat("member", ValidationLimits.MaxRelatedMembers + 1).ToList();

        Assert.False(IsValid(tags));
        Assert.False(IsValid(relatedMembers));
    }

    /// <summary>事件 DTO 應逐項拒絕 null 與超長集合元素。</summary>
    [Fact]
    public void UpsertEventRequest_RejectsInvalidCollectionItems()
    {
        var nullTag = ValidEvent();
        nullTag.Tags = [null!];
        var longTag = ValidEvent();
        longTag.Tags = [new string('t', ValidationLimits.MaxTagLength + 1)];
        var nullMember = ValidEvent();
        nullMember.RelatedMemberIds = [null!];
        var longMember = ValidEvent();
        longMember.RelatedMemberIds = [new string('m', ValidationLimits.MaxShortTextLength + 1)];

        Assert.False(IsValid(nullTag));
        Assert.False(IsValid(longTag));
        Assert.False(IsValid(nullMember));
        Assert.False(IsValid(longMember));
    }

    /// <summary>事件 DTO 應明確拒絕 null 集合。</summary>
    [Fact]
    public void UpsertEventRequest_RejectsNullCollections()
    {
        var tags = ValidEvent();
        tags.Tags = null!;
        var relatedMembers = ValidEvent();
        relatedMembers.RelatedMemberIds = null!;

        Assert.False(IsValid(tags));
        Assert.False(IsValid(relatedMembers));
    }

    /// <summary>事件 DTO 應遞迴拒絕無效的農曆日期。</summary>
    [Fact]
    public void UpsertEventRequest_RejectsInvalidNestedLunarDate()
    {
        var request = ValidEvent();
        request.LunarDate = new LunarDateRequest { Year = 2026, Month = 13, Day = 1 };

        Assert.False(IsValid(request));
    }

    /// <summary>事件 DTO 應遞迴拒絕無效的週期規則。</summary>
    [Fact]
    public void UpsertEventRequest_RejectsInvalidNestedRecurrenceRule()
    {
        var request = ValidEvent();
        request.RecurrenceRule = new RecurrenceRuleRequest { Interval = 0 };

        Assert.False(IsValid(request));
    }

    /// <summary>事件 DTO 應遞迴拒絕祭祀細節中的無效集合與文字。</summary>
    [Fact]
    public void UpsertEventRequest_RejectsInvalidNestedWorshipDetails()
    {
        var nullOffering = ValidEvent();
        nullOffering.WorshipDetails = new WorshipDetailsRequest { Offerings = [null!] };
        var longPreparation = ValidEvent();
        longPreparation.WorshipDetails = new WorshipDetailsRequest
        {
            Preparations = [new string('p', ValidationLimits.MaxShortTextLength + 1)]
        };
        var tooManyOfferings = ValidEvent();
        tooManyOfferings.WorshipDetails = new WorshipDetailsRequest
        {
            Offerings = Enumerable.Repeat("fruit", ValidationLimits.MaxTags + 1).ToList()
        };

        Assert.False(IsValid(nullOffering));
        Assert.False(IsValid(longPreparation));
        Assert.False(IsValid(tooManyOfferings));
    }

    /// <summary>事件 DTO 應拒絕結束日期早於開始日期。</summary>
    [Fact]
    public void UpsertEventRequest_RejectsEndBeforeStart()
    {
        var request = ValidEvent();
        request.EndDate = request.StartDate.AddTicks(-1);

        Assert.False(IsValid(request));
    }

    /// <summary>成員 DTO 應接受文字與 URL 的精確上限。</summary>
    [Fact]
    public void UpsertMemberRequest_AcceptsExactResourceBoundaries()
    {
        var request = ValidMember();
        request.Name = new string('n', ValidationLimits.MaxShortTextLength);
        request.Title = new string('t', ValidationLimits.MaxShortTextLength);
        request.Bio = new string('b', ValidationLimits.MaxDescriptionLength);
        request.BloodType = new string('b', ValidationLimits.MaxShortTextLength);
        request.AvatarUrl = HttpUrlOfLength(ValidationLimits.MaxUrlLength);
        request.ContactInfo = new ContactInfoRequest
        {
            Phone = new string('p', ValidationLimits.MaxShortTextLength),
            LineId = new string('l', ValidationLimits.MaxShortTextLength)
        };

        Assert.True(IsValid(request));
    }

    /// <summary>成員 DTO 應拒絕超長文字欄位。</summary>
    [Fact]
    public void UpsertMemberRequest_RejectsOversizedTextFields()
    {
        var name = ValidMember();
        name.Name = new string('n', ValidationLimits.MaxShortTextLength + 1);
        var title = ValidMember();
        title.Title = new string('t', ValidationLimits.MaxShortTextLength + 1);
        var bio = ValidMember();
        bio.Bio = new string('b', ValidationLimits.MaxDescriptionLength + 1);
        var bloodType = ValidMember();
        bloodType.BloodType = new string('b', ValidationLimits.MaxShortTextLength + 1);

        Assert.False(IsValid(name));
        Assert.False(IsValid(title));
        Assert.False(IsValid(bio));
        Assert.False(IsValid(bloodType));
    }

    /// <summary>成員 DTO 應拒絕超長或格式錯誤的大頭貼 URL。</summary>
    [Fact]
    public void UpsertMemberRequest_RejectsInvalidAvatarUrls()
    {
        var tooLong = ValidMember();
        tooLong.AvatarUrl = HttpUrlOfLength(ValidationLimits.MaxUrlLength + 1);
        var invalid = ValidMember();
        invalid.AvatarUrl = "not-a-url";

        Assert.False(IsValid(tooLong));
        Assert.False(IsValid(invalid));
    }

    /// <summary>成員 DTO 應遞迴拒絕無效的農曆生日與聯絡資訊。</summary>
    [Fact]
    public void UpsertMemberRequest_RejectsInvalidNestedObjects()
    {
        var lunarBirthday = ValidMember();
        lunarBirthday.LunarBirthday = new LunarDateRequest { Year = 2026, Month = 1, Day = 31 };
        var contactInfo = ValidMember();
        contactInfo.ContactInfo = new ContactInfoRequest
        {
            Phone = new string('p', ValidationLimits.MaxShortTextLength + 1)
        };

        Assert.False(IsValid(lunarBirthday));
        Assert.False(IsValid(contactInfo));
    }

    /// <summary>行程 DTO 應接受文字、集合與 URL 的精確上限。</summary>
    [Fact]
    public void UpsertItineraryRequest_AcceptsExactResourceBoundaries()
    {
        var request = ValidItinerary();
        request.Title = new string('t', ValidationLimits.MaxTitleLength);
        request.Description = new string('d', ValidationLimits.MaxDescriptionLength);
        request.Location = new string('l', ValidationLimits.MaxShortTextLength);
        request.Notes = new string('n', ValidationLimits.MaxNotesLength);
        request.LinkedEventId = new string('e', ValidationLimits.MaxShortTextLength);
        request.Tags = Enumerable.Repeat(new string('t', ValidationLimits.MaxTagLength), ValidationLimits.MaxTags).ToList();
        request.Participants = Enumerable.Range(0, ValidationLimits.MaxParticipants)
            .Select(_ => new ParticipantRequest
            {
                MemberId = new string('m', ValidationLimits.MaxShortTextLength),
                Name = new string('n', ValidationLimits.MaxShortTextLength)
            })
            .ToList();
        request.Photos = Enumerable.Repeat(HttpUrlOfLength(ValidationLimits.MaxUrlLength), ValidationLimits.MaxPhotos).ToList();

        Assert.True(IsValid(request));
    }

    /// <summary>行程 DTO 應拒絕超長文字欄位。</summary>
    [Fact]
    public void UpsertItineraryRequest_RejectsOversizedTextFields()
    {
        var title = ValidItinerary();
        title.Title = new string('t', ValidationLimits.MaxTitleLength + 1);
        var description = ValidItinerary();
        description.Description = new string('d', ValidationLimits.MaxDescriptionLength + 1);
        var location = ValidItinerary();
        location.Location = new string('l', ValidationLimits.MaxShortTextLength + 1);
        var notes = ValidItinerary();
        notes.Notes = new string('n', ValidationLimits.MaxNotesLength + 1);
        var linkedEvent = ValidItinerary();
        linkedEvent.LinkedEventId = new string('e', ValidationLimits.MaxShortTextLength + 1);

        Assert.False(IsValid(title));
        Assert.False(IsValid(description));
        Assert.False(IsValid(location));
        Assert.False(IsValid(notes));
        Assert.False(IsValid(linkedEvent));
    }

    /// <summary>行程 DTO 應拒絕超出上限的集合。</summary>
    [Fact]
    public void UpsertItineraryRequest_RejectsOversizedCollections()
    {
        var tags = ValidItinerary();
        tags.Tags = Enumerable.Repeat("family", ValidationLimits.MaxTags + 1).ToList();
        var participants = ValidItinerary();
        participants.Participants = Enumerable.Range(0, ValidationLimits.MaxParticipants + 1)
            .Select(_ => new ParticipantRequest { MemberId = "member", Name = "name" })
            .ToList();
        var photos = ValidItinerary();
        photos.Photos = Enumerable.Repeat("https://example.com/photo", ValidationLimits.MaxPhotos + 1).ToList();

        Assert.False(IsValid(tags));
        Assert.False(IsValid(participants));
        Assert.False(IsValid(photos));
    }

    /// <summary>行程 DTO 應明確拒絕 null 集合。</summary>
    [Fact]
    public void UpsertItineraryRequest_RejectsNullCollections()
    {
        var tags = ValidItinerary();
        tags.Tags = null!;
        var participants = ValidItinerary();
        participants.Participants = null!;
        var photos = ValidItinerary();
        photos.Photos = null!;

        Assert.False(IsValid(tags));
        Assert.False(IsValid(participants));
        Assert.False(IsValid(photos));
    }

    /// <summary>行程 DTO 應逐項拒絕 null 與超長標籤。</summary>
    [Fact]
    public void UpsertItineraryRequest_RejectsInvalidTagItems()
    {
        var nullTag = ValidItinerary();
        nullTag.Tags = [null!];
        var longTag = ValidItinerary();
        longTag.Tags = [new string('t', ValidationLimits.MaxTagLength + 1)];

        Assert.False(IsValid(nullTag));
        Assert.False(IsValid(longTag));
    }

    /// <summary>行程 DTO 應遞迴拒絕 null 或無效參與者。</summary>
    [Fact]
    public void UpsertItineraryRequest_RejectsInvalidParticipantItems()
    {
        var nullParticipant = ValidItinerary();
        nullParticipant.Participants = [null!];
        var missingMember = ValidItinerary();
        missingMember.Participants = [new ParticipantRequest { MemberId = string.Empty, Name = "name" }];
        var longName = ValidItinerary();
        longName.Participants = [new ParticipantRequest
        {
            MemberId = "member",
            Name = new string('n', ValidationLimits.MaxShortTextLength + 1)
        }];

        Assert.False(IsValid(nullParticipant));
        Assert.False(IsValid(missingMember));
        Assert.False(IsValid(longName));
    }

    /// <summary>行程 DTO 應逐項拒絕 null、超長或格式錯誤的照片 URL。</summary>
    [Fact]
    public void UpsertItineraryRequest_RejectsInvalidPhotoItems()
    {
        var nullPhoto = ValidItinerary();
        nullPhoto.Photos = [null!];
        var tooLong = ValidItinerary();
        tooLong.Photos = [HttpUrlOfLength(ValidationLimits.MaxUrlLength + 1)];
        var invalid = ValidItinerary();
        invalid.Photos = ["not-a-url"];

        Assert.False(IsValid(nullPhoto));
        Assert.False(IsValid(tooLong));
        Assert.False(IsValid(invalid));
    }

    /// <summary>行程 DTO 應拒絕結束日期早於開始日期。</summary>
    [Fact]
    public void UpsertItineraryRequest_RejectsEndBeforeStart()
    {
        var request = ValidItinerary();
        request.EndDate = request.Date.AddTicks(-1);

        Assert.False(IsValid(request));
    }

    /// <summary>分頁 DTO 應接受全域 page size 上限。</summary>
    [Fact]
    public void PagedItineraryRequest_AcceptsMaximumPageSize()
    {
        Assert.True(IsValid(new PagedItineraryRequest { PageSize = ValidationLimits.MaxPageSize }));
    }

    /// <summary>分頁 DTO 應拒絕 page size 與文字 one-beyond 值。</summary>
    [Fact]
    public void PagedItineraryRequest_RejectsValuesBeyondLimits()
    {
        Assert.False(IsValid(new PagedItineraryRequest { PageSize = 0 }));
        Assert.False(IsValid(new PagedItineraryRequest { PageSize = ValidationLimits.MaxPageSize + 1 }));
        Assert.False(IsValid(new PagedItineraryRequest
        {
            PageSize = 20,
            Tag = new string('t', ValidationLimits.MaxTagLength + 1)
        }));
        Assert.False(IsValid(new PagedItineraryRequest
        {
            PageSize = 20,
            CursorId = new string('c', ValidationLimits.MaxShortTextLength + 1)
        }));
    }

    /// <summary>註冊 DTO 應拒絕超長 transport 字串。</summary>
    [Fact]
    public void RegisterRequest_RejectsOversizedEmailDisplayNameAndRole()
    {
        var email = ValidRegister();
        email.Email = $"a@{new string('d', 250)}.com";
        var displayName = ValidRegister();
        displayName.DisplayName = new string('d', ValidationLimits.MaxShortTextLength + 1);
        var role = ValidRegister();
        role.Role = new string('r', ValidationLimits.MaxShortTextLength + 1);

        Assert.False(IsValid(email));
        Assert.False(IsValid(displayName));
        Assert.False(IsValid(role));
    }

    private static Type PropertyType<T>(string propertyName) =>
        Nullable.GetUnderlyingType(typeof(T).GetProperty(propertyName)!.PropertyType)
        ?? typeof(T).GetProperty(propertyName)!.PropertyType;

    private static bool IsValid(object value)
    {
        var results = new List<ValidationResult>();
        return Validator.TryValidateObject(value, new ValidationContext(value), results, validateAllProperties: true);
    }

    private static UpsertEventRequest ValidEvent() => new()
    {
        Title = "祭祖",
        Description = "家族祭祀",
        EventType = "Worship",
        StartDate = UtcDate(2026, 1, 1),
        EndDate = UtcDate(2026, 1, 1),
        IsLunarDate = true,
        LunarDate = new LunarDateRequest { Year = 2026, Month = 1, Day = 1 },
        RelatedMemberIds = ["member-1"],
        Tags = ["family"]
    };

    private static UpsertMemberRequest ValidMember() => new()
    {
        Name = "許浴宸",
        Title = "管理員",
        Birthday = UtcDate(1990, 1, 1)
    };

    private static UpsertItineraryRequest ValidItinerary() => new()
    {
        Title = "家族旅遊",
        Date = UtcDate(2026, 1, 1),
        EndDate = UtcDate(2026, 1, 2),
        Tags = ["family"],
        Participants = [new ParticipantRequest { MemberId = "member-1", Name = "許浴宸" }],
        Photos = ["https://example.com/photo.jpg"]
    };

    private static RegisterRequest ValidRegister() => new()
    {
        Username = "admin",
        Email = "admin@example.com",
        Password = "ValidPassword!123",
        DisplayName = "許浴宸",
        Role = "Admin"
    };

    private static DateTime UtcDate(int year, int month, int day) =>
        new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static string HttpUrlOfLength(int length)
    {
        const string prefix = "https://example.com/";
        return prefix + new string('a', length - prefix.Length);
    }
}
