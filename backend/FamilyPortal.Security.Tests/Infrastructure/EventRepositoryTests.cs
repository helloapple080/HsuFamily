using System.Reflection;
using FamilyPortal.Domain.Entities;
using FamilyPortal.Infrastructure.MongoDB;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Xunit;

namespace FamilyPortal.Security.Tests.Infrastructure;

/// <summary>驗證事件 Repository 的 MongoDB 查詢契約。</summary>
public sealed class EventRepositoryTests
{
    /// <summary>週期規則查詢只選農曆類型，且生效起訖以 UTC 日期含首尾比較。</summary>
    [Fact]
    public void RecurringRulesFilter_UsesLunarTypesAndInclusiveUtcDates()
    {
        var start = new DateTime(2026, 1, 2, 18, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 1, 3, 8, 0, 0, DateTimeKind.Utc);
        var filter = InvokeFilter(start, end);
        var serializer = BsonSerializer.SerializerRegistry.GetSerializer<Event>();
        var rendered = filter.Render(new RenderArgs<Event>(serializer, BsonSerializer.SerializerRegistry));

        var typeValues = FindComparison(rendered, "recurrenceRule.Type", "$in").AsBsonArray;
        Assert.Equal(["LunarYearly", "LunarMonthly"], typeValues.Select(value => value.AsString));
        Assert.Equal(
            new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            FindComparison(rendered, "recurrenceRule.EffectiveUntil", "$gte").ToUniversalTime());
        Assert.Equal(
            new DateTime(2026, 1, 3, 23, 59, 59, 999, DateTimeKind.Utc),
            FindComparison(rendered, "recurrenceRule.EffectiveFrom", "$lte").ToUniversalTime());
        var renderedJson = rendered.ToJson();
        Assert.DoesNotContain("\"Yearly\"", renderedJson);
        Assert.DoesNotContain("\"Monthly\"", renderedJson);
    }

    private static FilterDefinition<Event> InvokeFilter(DateTime start, DateTime end)
    {
        var method = typeof(EventRepository).GetMethod(
            "BuildRecurringRulesFilter",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<FilterDefinition<Event>>(method.Invoke(null, [start, end]));
    }

    private static BsonValue FindComparison(BsonDocument document, string field, string comparison)
    {
        foreach (var element in document.Elements)
        {
            if (element.Name == field &&
                element.Value.IsBsonDocument &&
                element.Value.AsBsonDocument.TryGetValue(comparison, out var value))
            {
                return value;
            }

            if (element.Value.IsBsonDocument)
            {
                try
                {
                    return FindComparison(element.Value.AsBsonDocument, field, comparison);
                }
                catch (KeyNotFoundException)
                {
                    // 繼續搜尋其他分支。
                }
            }

            if (element.Value.IsBsonArray)
            {
                foreach (var item in element.Value.AsBsonArray.Where(item => item.IsBsonDocument))
                {
                    try
                    {
                        return FindComparison(item.AsBsonDocument, field, comparison);
                    }
                    catch (KeyNotFoundException)
                    {
                        // 繼續搜尋其他陣列項目。
                    }
                }
            }
        }

        throw new KeyNotFoundException($"找不到 {field} {comparison}");
    }
}
