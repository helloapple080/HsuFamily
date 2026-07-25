using FamilyPortal.Application.Interfaces;
using FamilyPortal.Domain.Enums;
using FamilyPortal.Domain.ValueObjects;
using FamilyPortal.Infrastructure;
using FamilyPortal.Infrastructure.Calendar;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DomainEvent = FamilyPortal.Domain.Entities.Event;
using Xunit;

namespace FamilyPortal.Security.Tests.Application;

/// <summary>驗證農曆週期在有界 UTC 日期範圍內的展開結果。</summary>
public sealed class LunarRecurrenceServiceTests
{
    private readonly LunarRecurrenceService service = new();

    /// <summary>每年農曆八月十五應對應已知的國曆日期。</summary>
    /// <param name="year">查詢的國曆年。</param>
    /// <param name="expectedMonth">預期國曆月。</param>
    /// <param name="expectedDay">預期國曆日。</param>
    [Theory]
    [InlineData(2026, 9, 25)]
    [InlineData(2036, 10, 4)]
    public async Task ExpandAsync_ProducesKnownSolarDateForLunarMidAutumn(
        int year,
        int expectedMonth,
        int expectedDay)
    {
        var rule = YearlyRule(8, 15);

        var result = await service.ExpandAsync(
            rule,
            Utc(year, 1, 1),
            Utc(year, 12, 31, 23, 59, 59),
            CancellationToken.None);

        var occurrence = Assert.Single(result);
        var expectedDate = Utc(year, expectedMonth, expectedDay);
        Assert.Equal(expectedDate, occurrence.SolarDate);
        Assert.Equal(DateTimeKind.Utc, occurrence.SolarDate.Kind);
        Assert.Equal($"event-rule:{expectedDate:yyyy-MM-dd}", occurrence.Id);
        Assert.Equal("event-rule", occurrence.SourceRuleId);
        Assert.Equal(year, occurrence.LunarYear);
        Assert.Equal(8, occurrence.LunarMonth);
        Assert.Equal(15, occurrence.LunarDay);
        Assert.False(occurrence.IsLeapMonth);
    }

    /// <summary>閏六月規則應依策略只產生一般月、閏月或兩者。</summary>
    /// <param name="policy">閏月發生策略。</param>
    /// <param name="expectedDates">預期國曆日期字串。</param>
    [Theory]
    [InlineData(LeapMonthPolicy.RegularOnly, "2025-06-25")]
    [InlineData(LeapMonthPolicy.LeapOnly, "2025-07-25")]
    [InlineData(LeapMonthPolicy.Both, "2025-06-25,2025-07-25")]
    public async Task ExpandAsync_AppliesLeapMonthPolicy(
        LeapMonthPolicy policy,
        string expectedDates)
    {
        var rule = YearlyRule(6, 1, policy, lunarYear: 2025);

        var result = await service.ExpandAsync(
            rule,
            Utc(2025, 1, 1),
            Utc(2025, 12, 31, 23, 59, 59),
            CancellationToken.None);

        Assert.Equal(
            expectedDates.Split(','),
            result.Select(occurrence => occurrence.SolarDate.ToString("yyyy-MM-dd")));
        Assert.Equal(
            policy == LeapMonthPolicy.Both ? [false, true] : [policy == LeapMonthPolicy.LeapOnly],
            result.Select(occurrence => occurrence.IsLeapMonth));
    }

    /// <summary>農曆小月沒有三十日時應略過，不得移到廿九。</summary>
    [Fact]
    public async Task ExpandAsync_SkipsDayThirtyWhenLunarMonthIsShort()
    {
        var result = await service.ExpandAsync(
            YearlyRule(8, 30),
            Utc(2026, 1, 1),
            Utc(2026, 12, 31, 23, 59, 59),
            CancellationToken.None);

        Assert.Empty(result);
    }

    /// <summary>農曆每月規則應依 LunarDays 展開並尊重月間隔。</summary>
    [Fact]
    public async Task ExpandAsync_LunarMonthlyUsesConfiguredDaysAndAnchoredInterval()
    {
        var rule = MonthlyRule(interval: 2, lunarDays: [1, 15]);

        var result = await service.ExpandAsync(
            rule,
            Utc(2026, 2, 17),
            Utc(2026, 6, 30, 23, 59, 59),
            CancellationToken.None);

        Assert.Equal(
            [(1, 1), (1, 15), (3, 1), (3, 15), (5, 1), (5, 15)],
            result.Select(occurrence => (occurrence.LunarMonth, occurrence.LunarDay)));
    }

    /// <summary>農曆每年規則應從來源農曆年開始套用年間隔。</summary>
    [Fact]
    public async Task ExpandAsync_LunarYearlyAnchorsIntervalToSourceLunarYear()
    {
        var rule = YearlyRule(8, 15);
        rule.RecurrenceRule!.Interval = 2;

        var result = await service.ExpandAsync(
            rule,
            Utc(2026, 1, 1),
            Utc(2030, 12, 31, 23, 59, 59),
            CancellationToken.None);

        Assert.Equal([2026, 2028, 2030], result.Select(occurrence => occurrence.LunarYear));
    }

    /// <summary>生效起訖應以 UTC 日期為含首含尾邊界。</summary>
    [Fact]
    public async Task ExpandAsync_TreatsEffectiveBoundsAsInclusiveUtcDates()
    {
        var rule = YearlyRule(8, 15);
        rule.RecurrenceRule!.EffectiveFrom = Utc(2026, 9, 25, 12);
        rule.RecurrenceRule.EffectiveUntil = Utc(2026, 9, 25, 23, 59, 59);

        var result = await service.ExpandAsync(
            rule,
            Utc(2025, 1, 1),
            Utc(2036, 12, 31, 23, 59, 59),
            CancellationToken.None);

        var occurrence = Assert.Single(result);
        Assert.Equal(Utc(2026, 9, 25), occurrence.SolarDate);
    }

    /// <summary>國曆日期不在查詢範圍時應排除 occurrence。</summary>
    [Fact]
    public async Task ExpandAsync_ExcludesOccurrenceOutsideExactQueryBoundary()
    {
        var rule = YearlyRule(8, 15);

        var before = await service.ExpandAsync(
            rule,
            Utc(2026, 1, 1),
            Utc(2026, 9, 24, 23, 59, 59),
            CancellationToken.None);
        var after = await service.ExpandAsync(
            rule,
            Utc(2026, 9, 25, 0, 0, 1),
            Utc(2026, 12, 31, 23, 59, 59),
            CancellationToken.None);

        Assert.Empty(before);
        Assert.Empty(after);
    }

    /// <summary>查詢完全超出農曆曆可支援範圍時應穩定回傳空集合。</summary>
    /// <param name="rangeStart">查詢開始。</param>
    /// <param name="rangeEnd">查詢結束。</param>
    [Theory]
    [MemberData(nameof(UnsupportedRanges))]
    public async Task ExpandAsync_ReturnsEmptyOutsideSupportedCalendarRange(
        DateTime rangeStart,
        DateTime rangeEnd)
    {
        var result = await service.ExpandAsync(
            YearlyRule(8, 15),
            rangeStart,
            rangeEnd,
            CancellationToken.None);

        Assert.Empty(result);
    }

    /// <summary>查詢尾端超出支援範圍時應保留交集內的 occurrence。</summary>
    [Fact]
    public async Task ExpandAsync_ClampsPartialRangeToSupportedCalendarDates()
    {
        var rule = YearlyRule(8, 15, lunarYear: 2099);

        var result = await service.ExpandAsync(
            rule,
            Utc(2099, 1, 1),
            Utc(2200, 12, 31, 23, 59, 59),
            CancellationToken.None);

        Assert.Equal([2099, 2100], result.Select(occurrence => occurrence.LunarYear));
    }

    /// <summary>已取消的展開作業應立即終止。</summary>
    [Fact]
    public async Task ExpandAsync_ThrowsWhenCancellationIsRequested()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.ExpandAsync(
            YearlyRule(8, 15),
            Utc(2026, 1, 1),
            Utc(2026, 12, 31),
            source.Token));
    }

    /// <summary>Infrastructure DI 應提供農曆週期服務的介面實作。</summary>
    [Fact]
    public void AddInfrastructure_RegistersLunarRecurrenceService()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        Assert.IsType<LunarRecurrenceService>(
            provider.GetRequiredService<ILunarRecurrenceService>());
    }

    /// <summary>提供完全不在曆法支援範圍內的 UTC 查詢邊界。</summary>
    public static TheoryData<DateTime, DateTime> UnsupportedRanges => new()
    {
        { Utc(1800, 1, 1), Utc(1900, 12, 31, 23, 59, 59) },
        { Utc(2102, 1, 1), Utc(2200, 12, 31, 23, 59, 59) }
    };

    private static DomainEvent YearlyRule(
        int month,
        int day,
        LeapMonthPolicy leapMonthPolicy = LeapMonthPolicy.RegularOnly,
        int lunarYear = 2026) => new()
        {
            Id = "event-rule",
            IsLunarDate = true,
            LunarDate = new LunarDate
            {
                Year = lunarYear,
                Month = month,
                Day = day
            },
            RecurrenceRule = new RecurrenceRule
            {
                Type = RecurrenceType.LunarYearly,
                Interval = 1,
                LeapMonthPolicy = leapMonthPolicy
            }
        };

    private static DomainEvent MonthlyRule(int interval, List<int> lunarDays) => new()
    {
        Id = "event-rule",
        IsLunarDate = true,
        LunarDate = new LunarDate
        {
            Year = 2026,
            Month = 1,
            Day = 1
        },
        RecurrenceRule = new RecurrenceRule
        {
            Type = RecurrenceType.LunarMonthly,
            Interval = interval,
            LunarDays = lunarDays,
            LeapMonthPolicy = LeapMonthPolicy.RegularOnly
        }
    };

    private static DateTime Utc(
        int year,
        int month,
        int day,
        int hour = 0,
        int minute = 0,
        int second = 0) =>
        new(year, month, day, hour, minute, second, DateTimeKind.Utc);
}
