using System.Globalization;
using FamilyPortal.Application.Interfaces;
using FamilyPortal.Application.Models;
using FamilyPortal.Domain.Enums;
using FamilyPortal.Domain.ValueObjects;
using DomainEvent = FamilyPortal.Domain.Entities.Event;

namespace FamilyPortal.Infrastructure.Calendar;

/// <summary>使用 .NET 中國農曆曆展開有界農曆週期規則。</summary>
public sealed class LunarRecurrenceService : ILunarRecurrenceService
{
    /// <inheritdoc />
    public Task<IReadOnlyList<LunarOccurrence>> ExpandAsync(
        DomainEvent rule,
        DateTime rangeStart,
        DateTime rangeEnd,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rule);
        EnsureUtcRange(rangeStart, rangeEnd);
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryGetLunarRule(rule, out var recurrenceRule, out var sourceLunarDate))
        {
            return Task.FromResult<IReadOnlyList<LunarOccurrence>>([]);
        }

        EnsureUtcEffectiveBounds(recurrenceRule, nameof(rule));
        var effectiveStart = recurrenceRule.EffectiveFrom is { } effectiveFrom
            ? Max(rangeStart, AsUtcDate(effectiveFrom))
            : rangeStart;
        var effectiveEnd = recurrenceRule.EffectiveUntil is { } effectiveUntil
            ? Min(rangeEnd, AsUtcDate(effectiveUntil))
            : rangeEnd;

        if (effectiveEnd < effectiveStart)
        {
            return Task.FromResult<IReadOnlyList<LunarOccurrence>>([]);
        }

        var calendar = new ChineseLunisolarCalendar();
        var supportedStart = AsUtcDate(calendar.MinSupportedDateTime);
        var supportedEnd = AsUtcDate(calendar.MaxSupportedDateTime);
        var boundedStart = Max(effectiveStart, supportedStart);
        var boundedEnd = Min(effectiveEnd, supportedEnd);

        if (boundedEnd < boundedStart)
        {
            return Task.FromResult<IReadOnlyList<LunarOccurrence>>([]);
        }

        var firstLunarYear = calendar.GetYear(boundedStart);
        var lastLunarYear = calendar.GetYear(boundedEnd);
        var occurrences = new List<LunarOccurrence>();

        if (recurrenceRule.Type == RecurrenceType.LunarYearly)
        {
            ExpandYearly(
                calendar,
                rule.Id,
                sourceLunarDate,
                recurrenceRule,
                firstLunarYear,
                lastLunarYear,
                effectiveStart,
                effectiveEnd,
                occurrences,
                cancellationToken);
        }
        else
        {
            ExpandMonthly(
                calendar,
                rule.Id,
                sourceLunarDate,
                recurrenceRule,
                firstLunarYear,
                lastLunarYear,
                effectiveStart,
                effectiveEnd,
                occurrences,
                cancellationToken);
        }

        IReadOnlyList<LunarOccurrence> result = occurrences
            .OrderBy(occurrence => occurrence.SolarDate)
            .ThenBy(occurrence => occurrence.Id, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult(result);
    }

    private static void ExpandYearly(
        ChineseLunisolarCalendar calendar,
        string ruleId,
        LunarDate sourceLunarDate,
        RecurrenceRule recurrenceRule,
        int firstLunarYear,
        int lastLunarYear,
        DateTime effectiveStart,
        DateTime effectiveEnd,
        List<LunarOccurrence> occurrences,
        CancellationToken cancellationToken)
    {
        for (var lunarYear = firstLunarYear; lunarYear <= lastLunarYear; lunarYear++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (lunarYear < sourceLunarDate.Year ||
                (lunarYear - sourceLunarDate.Year) % recurrenceRule.Interval != 0)
            {
                continue;
            }

            var leapMonth = calendar.GetLeapMonth(lunarYear);
            if (recurrenceRule.LeapMonthPolicy is LeapMonthPolicy.RegularOnly or LeapMonthPolicy.Both)
            {
                TryAddOccurrence(
                    calendar,
                    ruleId,
                    lunarYear,
                    sourceLunarDate.Month,
                    sourceLunarDate.Day,
                    isLeapMonth: false,
                    leapMonth,
                    effectiveStart,
                    effectiveEnd,
                    occurrences);
            }

            if (recurrenceRule.LeapMonthPolicy is (LeapMonthPolicy.LeapOnly or LeapMonthPolicy.Both) &&
                leapMonth == sourceLunarDate.Month + 1)
            {
                TryAddOccurrence(
                    calendar,
                    ruleId,
                    lunarYear,
                    sourceLunarDate.Month,
                    sourceLunarDate.Day,
                    isLeapMonth: true,
                    leapMonth,
                    effectiveStart,
                    effectiveEnd,
                    occurrences);
            }
        }
    }

    private static void ExpandMonthly(
        ChineseLunisolarCalendar calendar,
        string ruleId,
        LunarDate sourceLunarDate,
        RecurrenceRule recurrenceRule,
        int firstLunarYear,
        int lastLunarYear,
        DateTime effectiveStart,
        DateTime effectiveEnd,
        List<LunarOccurrence> occurrences,
        CancellationToken cancellationToken)
    {
        if (!TryGetMonthSequenceIndex(
                calendar,
                sourceLunarDate.Year,
                sourceLunarDate.Month,
                sourceLunarDate.IsLeapMonth,
                out var anchorIndex))
        {
            return;
        }

        var days = recurrenceRule.LunarDays!
            .Distinct()
            .Order()
            .ToArray();

        var yearStartIndex = GetYearStartSequenceIndex(calendar, firstLunarYear);
        for (var lunarYear = firstLunarYear; lunarYear <= lastLunarYear; lunarYear++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var leapMonth = calendar.GetLeapMonth(lunarYear);
            var monthsInYear = calendar.GetMonthsInYear(lunarYear);

            for (var calendarMonth = 1; calendarMonth <= monthsInYear; calendarMonth++)
            {
                var isLeapMonth = leapMonth > 0 && calendarMonth == leapMonth;
                if (!PolicyIncludesMonth(recurrenceRule.LeapMonthPolicy, isLeapMonth))
                {
                    continue;
                }

                var sequenceIndex = yearStartIndex + calendarMonth - 1;
                if (sequenceIndex < anchorIndex ||
                    (sequenceIndex - anchorIndex) % recurrenceRule.Interval != 0)
                {
                    continue;
                }

                var lunarMonth = ToLunarMonth(calendarMonth, leapMonth);
                foreach (var lunarDay in days)
                {
                    TryAddOccurrence(
                        calendar,
                        ruleId,
                        lunarYear,
                        lunarMonth,
                        lunarDay,
                        isLeapMonth,
                        leapMonth,
                        effectiveStart,
                        effectiveEnd,
                        occurrences);
                }
            }

            yearStartIndex += monthsInYear;
        }
    }

    private static bool TryGetLunarRule(
        DomainEvent rule,
        out RecurrenceRule recurrenceRule,
        out LunarDate sourceLunarDate)
    {
        recurrenceRule = rule.RecurrenceRule!;
        sourceLunarDate = rule.LunarDate!;

        return rule.IsLunarDate &&
               rule.RecurrenceRule is not null &&
               rule.LunarDate is not null &&
               rule.RecurrenceRule.Type is RecurrenceType.LunarYearly or RecurrenceType.LunarMonthly &&
               rule.RecurrenceRule.Interval > 0 &&
               Enum.IsDefined(rule.RecurrenceRule.LeapMonthPolicy) &&
               rule.LunarDate.Month is >= 1 and <= 12 &&
               rule.LunarDate.Day is >= 1 and <= 30 &&
               (rule.RecurrenceRule.Type != RecurrenceType.LunarMonthly ||
                rule.RecurrenceRule.LunarDays is { Count: > 0 } lunarDays &&
                lunarDays.All(day => day is >= 1 and <= 30));
    }

    private static void TryAddOccurrence(
        ChineseLunisolarCalendar calendar,
        string ruleId,
        int lunarYear,
        int lunarMonth,
        int lunarDay,
        bool isLeapMonth,
        int leapMonth,
        DateTime effectiveStart,
        DateTime effectiveEnd,
        List<LunarOccurrence> occurrences)
    {
        var calendarMonth = isLeapMonth
            ? leapMonth
            : ToCalendarMonth(lunarMonth, leapMonth);

        DateTime converted;
        try
        {
            converted = calendar.ToDateTime(
                lunarYear,
                calendarMonth,
                lunarDay,
                0,
                0,
                0,
                0,
                ChineseLunisolarCalendar.ChineseEra);
        }
        catch (ArgumentOutOfRangeException)
        {
            // 農曆小月沒有三十日，或舊資料日期無效時，固定略過該 occurrence。
            return;
        }

        var solarDate = AsUtcDate(converted);
        if (solarDate < effectiveStart || solarDate > effectiveEnd)
        {
            return;
        }

        occurrences.Add(new LunarOccurrence
        {
            Id = $"{ruleId}:{solarDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}",
            SourceRuleId = ruleId,
            SolarDate = solarDate,
            LunarYear = lunarYear,
            LunarMonth = lunarMonth,
            LunarDay = lunarDay,
            IsLeapMonth = isLeapMonth
        });
    }

    private static bool TryGetMonthSequenceIndex(
        ChineseLunisolarCalendar calendar,
        int lunarYear,
        int lunarMonth,
        bool isLeapMonth,
        out int sequenceIndex)
    {
        sequenceIndex = 0;
        var minimumLunarYear = calendar.GetYear(calendar.MinSupportedDateTime);
        var maximumLunarYear = calendar.GetYear(calendar.MaxSupportedDateTime);
        if (lunarYear < minimumLunarYear || lunarYear > maximumLunarYear)
        {
            return false;
        }

        var leapMonth = calendar.GetLeapMonth(lunarYear);
        if (isLeapMonth && leapMonth != lunarMonth + 1)
        {
            return false;
        }

        var calendarMonth = isLeapMonth ? leapMonth : ToCalendarMonth(lunarMonth, leapMonth);
        sequenceIndex = GetYearStartSequenceIndex(calendar, lunarYear) + calendarMonth - 1;
        return true;
    }

    private static int GetYearStartSequenceIndex(ChineseLunisolarCalendar calendar, int lunarYear)
    {
        var minimumLunarYear = calendar.GetYear(calendar.MinSupportedDateTime);
        var index = 0;
        for (var year = minimumLunarYear; year < lunarYear; year++)
        {
            index += calendar.GetMonthsInYear(year);
        }

        return index;
    }

    private static bool PolicyIncludesMonth(LeapMonthPolicy policy, bool isLeapMonth) =>
        policy switch
        {
            LeapMonthPolicy.RegularOnly => !isLeapMonth,
            LeapMonthPolicy.LeapOnly => isLeapMonth,
            LeapMonthPolicy.Both => true,
            _ => false
        };

    private static int ToCalendarMonth(int lunarMonth, int leapMonth) =>
        leapMonth > 0 && lunarMonth >= leapMonth ? lunarMonth + 1 : lunarMonth;

    private static int ToLunarMonth(int calendarMonth, int leapMonth) =>
        leapMonth > 0 && calendarMonth >= leapMonth ? calendarMonth - 1 : calendarMonth;

    private static DateTime AsUtcDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);

    private static DateTime Max(DateTime left, DateTime right) => left >= right ? left : right;

    private static DateTime Min(DateTime left, DateTime right) => left <= right ? left : right;

    private static void EnsureUtcRange(DateTime rangeStart, DateTime rangeEnd)
    {
        if (rangeStart.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("查詢開始必須使用 UTC。", nameof(rangeStart));
        }

        if (rangeEnd.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("查詢結束必須使用 UTC。", nameof(rangeEnd));
        }

        if (rangeEnd < rangeStart)
        {
            throw new ArgumentException("查詢結束不可早於開始。", nameof(rangeEnd));
        }
    }

    private static void EnsureUtcEffectiveBounds(RecurrenceRule recurrenceRule, string parameterName)
    {
        if (recurrenceRule.EffectiveFrom is { Kind: not DateTimeKind.Utc })
        {
            throw new ArgumentException("規則生效日期必須使用 UTC。", parameterName);
        }

        if (recurrenceRule.EffectiveUntil is { Kind: not DateTimeKind.Utc })
        {
            throw new ArgumentException("規則結束日期必須使用 UTC。", parameterName);
        }
    }
}
