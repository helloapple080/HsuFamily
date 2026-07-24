using System.Reflection;
using FamilyPortal.Domain.Entities;
using FamilyPortal.Domain.ValueObjects;
using FamilyPortal.Infrastructure.MongoDB;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Xunit;

namespace FamilyPortal.Security.Tests;

/// <summary>驗證 Refresh Token 更新使用無父子路徑衝突的原子 pipeline。</summary>
public sealed class RefreshTokenHistoryTests
{
    /// <summary>登入應附加序列化 Token，保留所有 active 與最後五十筆 inactive。</summary>
    [Fact]
    public void AddPipeline_AppendsWithoutRevokingAndAppliesRetentionPolicy()
    {
        var now = UtcNow();
        var rendered = Render(InvokeDefinition<UpdateDefinition<User>>(
            "BuildAddRefreshTokenUpdate",
            NewToken(now),
            now));
        var pipeline = rendered.AsBsonArray;

        Assert.Equal(2, pipeline.Count);
        Assert.DoesNotContain("\"$push\"", rendered.ToJson());
        Assert.DoesNotContain("refreshTokens.$", rendered.ToJson());
        var append = pipeline[0].AsBsonDocument["$set"].AsBsonDocument["refreshTokens"]
            .AsBsonDocument["$concatArrays"].AsBsonArray;
        Assert.Equal("$refreshTokens", append[0].AsBsonDocument["$ifNull"].AsBsonArray[0].AsString);
        var appendedToken = append[1].AsBsonArray.Single().AsBsonDocument["$literal"].AsBsonDocument;
        AssertSerializedToken(appendedToken);
        AssertRetentionStage(pipeline[1].AsBsonDocument, now);
    }

    /// <summary>輪替應保留 active-current filter，以 map 撤銷舊 Token 後附加 replacement。</summary>
    [Fact]
    public void RotationPipeline_MapsCurrentTokenAppendsReplacementAndAppliesRetentionPolicy()
    {
        var now = UtcNow();
        var filter = InvokeDefinition<FilterDefinition<User>>(
            "BuildActiveRefreshTokenFilter",
            "507f1f77bcf86cd799439011",
            "old-hash",
            now);
        var renderedFilter = Render(filter);
        var renderedUpdate = Render(InvokeDefinition<UpdateDefinition<User>>(
            "BuildRefreshTokenRotationUpdate",
            "old-hash",
            NewToken(now),
            now));
        var pipeline = renderedUpdate.AsBsonArray;

        AssertActiveCurrentFilter(renderedFilter, now);
        Assert.Equal(2, pipeline.Count);
        Assert.DoesNotContain("\"$push\"", renderedUpdate.ToJson());
        Assert.DoesNotContain("refreshTokens.$", renderedUpdate.ToJson());
        var concat = pipeline[0].AsBsonDocument["$set"].AsBsonDocument["refreshTokens"]
            .AsBsonDocument["$concatArrays"].AsBsonArray;
        var map = concat[0].AsBsonDocument["$map"].AsBsonDocument;
        var condition = map["in"].AsBsonDocument["$cond"].AsBsonArray;
        Assert.Equal(
            "old-hash",
            condition[0].AsBsonDocument["$eq"].AsBsonArray[1]
                .AsBsonDocument["$literal"].AsString);
        var merge = condition[1].AsBsonDocument["$mergeObjects"].AsBsonArray;
        var revokedFields = merge[1].AsBsonDocument;
        Assert.Equal(now, revokedFields["revokedAt"].ToUniversalTime());
        Assert.Equal("new-hash", revokedFields["replacedByTokenHash"].AsBsonDocument["$literal"].AsString);
        AssertSerializedToken(concat[1].AsBsonArray.Single().AsBsonDocument["$literal"].AsBsonDocument);
        AssertRetentionStage(pipeline[1].AsBsonDocument, now);
    }

    private static void AssertRetentionStage(BsonDocument stage, DateTime now)
    {
        var set = stage["$set"].AsBsonDocument;
        Assert.Equal(now, set["updatedAt"].ToUniversalTime());
        var retained = set["refreshTokens"].AsBsonDocument["$let"].AsBsonDocument["in"]
            .AsBsonDocument["$concatArrays"].AsBsonArray;
        var active = retained[0].AsBsonDocument["$filter"].AsBsonDocument;
        Assert.Equal("$$tokens", active["input"].AsString);
        var activeParts = active["cond"].AsBsonDocument["$and"].AsBsonArray;
        Assert.Equal(
            "$$token.revokedAt",
            activeParts[0].AsBsonDocument["$eq"].AsBsonArray[0]
                .AsBsonDocument["$ifNull"].AsBsonArray[0].AsString);
        Assert.True(activeParts[0].AsBsonDocument["$eq"].AsBsonArray[1].IsBsonNull);
        Assert.Equal(
            "$$token.expiresAt",
            activeParts[1].AsBsonDocument["$gt"].AsBsonArray[0].AsString);
        Assert.Equal(
            now,
            activeParts[1].AsBsonDocument["$gt"].AsBsonArray[1].ToUniversalTime());
        var inactiveSlice = retained[1].AsBsonDocument["$slice"].AsBsonArray;
        Assert.Equal(-50, inactiveSlice[1].AsInt32);
        var inactiveSort = inactiveSlice[0].AsBsonDocument["$sortArray"].AsBsonDocument;
        var inactive = inactiveSort["input"].AsBsonDocument["$filter"].AsBsonDocument;
        Assert.True(inactive["cond"].AsBsonDocument.Contains("$not"));
        Assert.Equal(1, inactiveSort["sortBy"].AsBsonDocument["createdAt"].AsInt32);
        Assert.Equal(1, inactiveSort["sortBy"].AsBsonDocument["revokedAt"].AsInt32);
    }

    private static void AssertActiveCurrentFilter(BsonDocument filter, DateTime now)
    {
        Assert.Equal("507f1f77bcf86cd799439011", filter["_id"].AsObjectId.ToString());
        var tokenMatch = filter["refreshTokens"].AsBsonDocument["$elemMatch"].AsBsonDocument;
        Assert.Equal("old-hash", tokenMatch["tokenHash"].AsString);
        Assert.True(tokenMatch["revokedAt"].IsBsonNull);
        Assert.Equal(now, tokenMatch["expiresAt"].AsBsonDocument["$gt"].ToUniversalTime());
    }

    private static void AssertSerializedToken(BsonDocument token)
    {
        Assert.Equal("new-hash", token["tokenHash"].AsString);
        Assert.Equal("family-1", token["familyId"].AsString);
        Assert.True(token.Contains("expiresAt"));
        Assert.True(token.Contains("createdAt"));
        Assert.False(token.Contains("TokenHash"));
    }

    private static DateTime UtcNow() =>
        new(2026, 7, 24, 1, 2, 3, DateTimeKind.Utc);

    private static RefreshToken NewToken(DateTime now) => new()
    {
        TokenHash = "new-hash",
        FamilyId = "family-1",
        CreatedAt = now,
        ExpiresAt = now.AddDays(1)
    };

    private static BsonDocument Render(FilterDefinition<User> filter)
    {
        var serializer = BsonSerializer.SerializerRegistry.GetSerializer<User>();
        return filter.Render(new RenderArgs<User>(serializer, BsonSerializer.SerializerRegistry));
    }

    private static BsonValue Render(UpdateDefinition<User> update)
    {
        var serializer = BsonSerializer.SerializerRegistry.GetSerializer<User>();
        return update.Render(new RenderArgs<User>(serializer, BsonSerializer.SerializerRegistry));
    }

    private static T InvokeDefinition<T>(string methodName, params object[] arguments)
    {
        var method = typeof(UserRepository).GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<T>(method.Invoke(null, arguments));
    }
}
