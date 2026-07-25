using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FamilyPortal.Application.Interfaces;
using FamilyPortal.Domain.Entities;
using FamilyPortal.Domain.Enums;
using FamilyPortal.Infrastructure.MongoDB;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Xunit;

namespace FamilyPortal.Security.Tests;

/// <summary>驗證專案遵循 Clean Architecture 的相依方向。</summary>
public sealed class ArchitectureDependencyTests
{
    private static readonly Regex InfrastructureReferencePattern = new(
        @"(?<![\p{L}\p{N}\p{M}_])FamilyPortal\.Infrastructure(?![\p{L}\p{N}\p{M}_])",
        RegexOptions.CultureInvariant);

    private readonly record struct StringLiteralStart(
        int QuoteIndex,
        int QuoteLength,
        bool IsVerbatim,
        int DollarCount);

    /// <summary>媒體 Domain 不得引用任何 Google 命名空間或套件。</summary>
    [Fact]
    public void MediaDomain_MustNotReferenceGoogleNamespaces()
    {
        var domainFiles = Directory.GetFiles(
                DomainSourcePath(),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(file => !Path.GetFileName(file).StartsWith("._", StringComparison.Ordinal));

        foreach (var file in domainFiles)
        {
            var source = File.ReadAllText(file);

            Assert.DoesNotMatch(
                new Regex(@"^\s*(?:global\s+)?using\s+Google(?:\.|;)", RegexOptions.Multiline),
                source);
        }
    }

    /// <summary>媒體列舉欄位必須使用字串格式寫入 MongoDB。</summary>
    [Fact]
    public void MediaAsset_EnumFields_SerializeAsStrings()
    {
        var asset = new MediaAsset
        {
            Visibility = MediaVisibility.FamilyOnly,
            Purpose = MediaPurpose.MemberAvatar,
            OwnerEntityId = ObjectId.GenerateNewId().ToString(),
            UploadedBy = ObjectId.GenerateNewId().ToString()
        };

        var document = asset.ToBsonDocument();

        Assert.Equal(BsonType.String, document["visibility"].BsonType);
        Assert.Equal("FamilyOnly", document["visibility"].AsString);
        Assert.Equal(BsonType.String, document["purpose"].BsonType);
        Assert.Equal("MemberAvatar", document["purpose"].AsString);
    }

    /// <summary>媒體 Repository 的所有公開操作都必須支援取消權杖。</summary>
    [Fact]
    public void MediaRepositoryMethods_MustAcceptCancellationTokens()
    {
        var requiredMethodNames = new[]
        {
            "GetByIdAsync",
            "CountActiveByOwnerAndPurposeAsync",
            "CreateAsync",
            "SoftDeleteAsync",
            "GetPendingCleanupAsync"
        };

        var methods = typeof(IMediaRepository).GetMethods();

        foreach (var methodName in requiredMethodNames)
        {
            var method = Assert.Single(methods, method => method.Name == methodName);
            var cancellationToken = method.GetParameters().Last();

            Assert.Equal(typeof(CancellationToken), cancellationToken.ParameterType);
            Assert.True(cancellationToken.HasDefaultValue);
        }
    }

    /// <summary>媒體 Repository 只能讀取未刪除資料，且軟刪除必須可安全重複執行。</summary>
    [Fact]
    public void MediaRepository_ActiveAndSoftDeleteFilters_ExcludeDeletedAssets()
    {
        var assetId = ObjectId.GenerateNewId().ToString();
        var ownerId = ObjectId.GenerateNewId().ToString();
        var activeIdFilter = InvokeMediaDefinition<FilterDefinition<MediaAsset>>(
            "BuildActiveIdFilter",
            assetId);
        var ownerFilter = InvokeMediaDefinition<FilterDefinition<MediaAsset>>(
            "BuildActiveOwnerPurposeFilter",
            ownerId,
            MediaPurpose.MemberAvatar);
        var softDeleteFilter = InvokeMediaDefinition<FilterDefinition<MediaAsset>>(
            "BuildSoftDeleteFilter",
            assetId);

        var activeIdDocument = RenderFilter(activeIdFilter);
        var ownerDocument = RenderFilter(ownerFilter);
        var softDeleteDocument = RenderFilter(softDeleteFilter);

        Assert.Equal(ObjectId.Parse(assetId), activeIdDocument["_id"].AsObjectId);
        Assert.True(activeIdDocument["deletedAt"].IsBsonNull);
        Assert.Equal(ObjectId.Parse(ownerId), ownerDocument["ownerEntityId"].AsObjectId);
        Assert.Equal("MemberAvatar", ownerDocument["purpose"].AsString);
        Assert.True(ownerDocument["deletedAt"].IsBsonNull);
        Assert.Equal(ObjectId.Parse(assetId), softDeleteDocument["_id"].AsObjectId);
        Assert.True(softDeleteDocument["deletedAt"].IsBsonNull);
    }

    /// <summary>軟刪除必須同時記錄 UTC 時間、更新時間與待清理狀態。</summary>
    [Fact]
    public void MediaRepository_SoftDeleteUpdate_MarksCleanupPendingAtomically()
    {
        var deletedAt = new DateTime(2026, 7, 25, 9, 30, 0, DateTimeKind.Utc);
        var update = InvokeMediaDefinition<UpdateDefinition<MediaAsset>>(
            "BuildSoftDeleteUpdate",
            deletedAt);
        var document = RenderUpdate(update);
        var setDocument = document["$set"].AsBsonDocument;

        Assert.Equal(deletedAt, setDocument["deletedAt"].ToUniversalTime());
        Assert.Equal(deletedAt, setDocument["updatedAt"].ToUniversalTime());
        Assert.True(setDocument["cleanupPending"].AsBoolean);
    }

    /// <summary>待清理查詢必須有上限，並以刪除時間與 ID 穩定排序。</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(25, 25)]
    [InlineData(101, 100)]
    public void MediaRepository_PendingCleanupQuery_IsBoundedAndDeterministic(
        int requestedLimit,
        int expectedLimit)
    {
        var filter = InvokeMediaDefinition<FilterDefinition<MediaAsset>>(
            "BuildPendingCleanupFilter");
        var options = InvokeMediaDefinition<FindOptions<MediaAsset>>(
            "BuildPendingCleanupOptions",
            requestedLimit);
        var filterDocument = RenderFilter(filter);
        var sortDocument = RenderSort(options.Sort);

        Assert.True(filterDocument["cleanupPending"].AsBoolean);
        Assert.True(filterDocument["deletedAt"].AsBsonDocument["$ne"].IsBsonNull);
        Assert.Equal(expectedLimit, options.Limit);
        Assert.Equal(1, sortDocument["deletedAt"].AsInt32);
        Assert.Equal(1, sortDocument["_id"].AsInt32);
    }

    /// <summary>媒體索引必須支援主動與待清理查詢，且不得阻擋軟刪除後重新建立。</summary>
    [Fact]
    public void MediaIndexes_SupportQueriesWithoutUniqueConstraint()
    {
        var indexes = InvokePrivateStatic<List<CreateIndexModel<MediaAsset>>>(
            typeof(MongoDbIndexInitializer),
            "BuildMediaIndexes");
        var serializer = BsonSerializer.SerializerRegistry.GetSerializer<MediaAsset>();
        var renderArgs = new RenderArgs<MediaAsset>(serializer, BsonSerializer.SerializerRegistry);

        var activeIndex = Assert.Single(
            indexes,
            index => index.Options.Name == "idx_media_active_owner_purpose");
        var cleanupIndex = Assert.Single(
            indexes,
            index => index.Options.Name == "idx_media_cleanup_pending");
        var activeKeys = activeIndex.Keys.Render(renderArgs);
        var cleanupKeys = cleanupIndex.Keys.Render(renderArgs);

        Assert.Equal(["ownerEntityId", "purpose", "deletedAt"], activeKeys.Names);
        Assert.Equal(["cleanupPending", "deletedAt", "_id"], cleanupKeys.Names);
        Assert.All(indexes, index => Assert.False(index.Options.Unique.GetValueOrDefault()));
    }

    /// <summary>Controller 不得直接引用 Infrastructure 命名空間。</summary>
    [Fact]
    public void Controllers_MustNotReferenceInfrastructureNamespaces()
    {
        var controllerFiles = Directory.GetFiles(
                ApiSourcePath(),
                "*Controller.cs",
                SearchOption.AllDirectories)
            .Where(file => !Path.GetFileName(file).StartsWith("._", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(controllerFiles);

        foreach (var file in controllerFiles)
        {
            var source = File.ReadAllText(file);

            Assert.False(
                ContainsInfrastructureReference(source),
                $"Controller '{file}' directly references FamilyPortal.Infrastructure.");
        }
    }

    /// <summary>Infrastructure 參考偵測應忽略註解與所有字串常值形式。</summary>
    /// <param name="source">要檢查的 C# 原始碼。</param>
    /// <param name="expected">是否應偵測到 Infrastructure 參考。</param>
    [Theory]
    [MemberData(nameof(InfrastructureReferenceSources))]
    public void Source_ContainsInfrastructureReference_RecognizesOnlyCode(string source, bool expected)
    {
        Assert.Equal(expected, ContainsInfrastructureReference(source));
    }

    /// <summary>提供 Infrastructure 參考偵測的真偽回歸案例。</summary>
    public static TheoryData<string, bool> InfrastructureReferenceSources => new()
    {
        { "using FamilyPortal.Infrastructure.Security;", true },
        { "using InfrastructureAlias = global::FamilyPortal.Infrastructure.Security;", true },
        { "using static FamilyPortal.Infrastructure.Security.SecurityDefaults;", true },
        { "var value = global::FamilyPortal.Infrastructure.Security.SecurityDefaults.Value;", true },
        { "// FamilyPortal.Infrastructure.Security", false },
        { "/* FamilyPortal.Infrastructure.Security */", false },
        { "var regular = \"FamilyPortal.Infrastructure.Security\";", false },
        { "var verbatim = @\"FamilyPortal.Infrastructure.Security\";", false },
        { "var interpolated = $\"FamilyPortal.Infrastructure.Security\";", false },
        { "var interpolatedVerbatim = $@\"FamilyPortal.Infrastructure.Security\";", false },
        { "var expression = $\"{global::FamilyPortal.Infrastructure.Security.SecurityDefaults.Value}\";", true },
        { "var verbatimExpression = $@\"{global::FamilyPortal.Infrastructure.Security.SecurityDefaults.Value}\";", true },
        { "var alternateVerbatimExpression = @$\"{global::FamilyPortal.Infrastructure.Security.SecurityDefaults.Value}\";", true },
        { "var nestedExpression = $\"{new { Value = global::FamilyPortal.Infrastructure.Security.SecurityDefaults.Value }.Value}\";", true },
        { "var escapedBraces = $\"{{FamilyPortal.Infrastructure.Security}}\";", false },
        { "var escapedVerbatimBraces = $@\"{{FamilyPortal.Infrastructure.Security}}\";", false },
        { "var expressionString = $\"{\"FamilyPortal.Infrastructure.Security\".Length}\";", false },
        { "var expressionVerbatimString = $\"{@\"FamilyPortal.Infrastructure.Security\".Length}\";", false },
        { "var expressionCharacter = $\"{'x'}\";", false },
        { "var expressionComment = $\"{/* FamilyPortal.Infrastructure.Security */ 1}\";", false },
        { "var character = 'FamilyPortal.Infrastructure.Security';", false },
        { "$\"\"\"{global::FamilyPortal.Infrastructure.Security.SecurityDefaults.Value}\"\"\"", true },
        { "$$\"\"\"{{global::FamilyPortal.Infrastructure.Security.SecurityDefaults.Value}}\"\"\"", true },
        { "$\"\"\"FamilyPortal.Infrastructure literal\"\"\"", false },
        { "$$\"\"\"FamilyPortal.Infrastructure literal\"\"\"", false },
        {
            """"
            var raw = """FamilyPortal.Infrastructure.Security""";
            """",
            false
        }
    };

    /// <summary>Application 專案只能直接參考 Domain 專案。</summary>
    [Fact]
    public void ApplicationProject_MustOnlyReferenceDomainProject()
    {
        var references = ApplicationProjectReferencePaths();
        var expectedPath = Path.GetFullPath(Path.Combine(
            RepositoryRootPath(),
            "backend",
            "FamilyPortal.Domain",
            "FamilyPortal.Domain.csproj"));

        Assert.True(
            references.Count == 1,
            "Application project must contain exactly one ProjectReference. " +
            $"Found {references.Count}: {string.Join(", ", references)}.");
        Assert.True(
            PathsAreEqual(expectedPath, references[0]),
            "Application project must reference the canonical Domain project path. " +
            $"Expected: {expectedPath}. Found: {references[0]}.");
    }

    private static bool ContainsInfrastructureReference(string source)
    {
        return InfrastructureReferencePattern.IsMatch(SanitizeSource(source));
    }

    private static string SanitizeSource(string source)
    {
        var sanitized = source.ToCharArray();
        var index = 0;

        while (index < source.Length)
        {
            if (source[index] == '/' && index + 1 < source.Length)
            {
                if (source[index + 1] == '/')
                {
                    var endIndex = source.IndexOf('\n', index + 2);
                    endIndex = endIndex < 0 ? source.Length : endIndex;
                    Mask(sanitized, index, endIndex);
                    index = endIndex;
                    continue;
                }

                if (source[index + 1] == '*')
                {
                    var closingIndex = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                    var endIndex = closingIndex < 0 ? source.Length : closingIndex + 2;
                    Mask(sanitized, index, endIndex);
                    index = endIndex;
                    continue;
                }
            }

            if (source[index] == '\'' )
            {
                var endIndex = FindCharacterLiteralEnd(source, index);
                Mask(sanitized, index, endIndex);
                index = endIndex;
                continue;
            }

            if (TryGetStringLiteralStart(source, index, out var literal))
            {
                var stringEndIndex = literal.DollarCount > 0
                    ? MaskInterpolatedString(source, sanitized, index, literal)
                    : FindStringLiteralEnd(source, literal);

                if (literal.DollarCount == 0)
                {
                    Mask(sanitized, index, stringEndIndex);
                }

                index = stringEndIndex;
                continue;
            }

            index++;
        }

        return new string(sanitized);
    }

    private static IReadOnlyList<string> ApplicationProjectReferencePaths()
    {
        var projectPath = ApplicationProjectPath();
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new DirectoryNotFoundException(
                $"Unable to determine the directory for Application project '{projectPath}'.");
        var projectDocument = XDocument.Load(projectPath);
        var includes = projectDocument
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "ProjectReference", StringComparison.Ordinal))
            .Select(element => element.Attribute("Include")?.Value)
            .ToArray();

        if (includes.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException(
                $"Every ProjectReference in '{projectPath}' must declare a non-empty Include attribute.");
        }

        return includes
            .Select(include => NormalizeProjectReferencePath(include!, projectDirectory))
            .ToArray();
    }

    private static int FindCharacterLiteralEnd(string source, int startIndex)
    {
        var index = startIndex + 1;

        while (index < source.Length)
        {
            if (source[index] == '\\')
            {
                index += 2;
                continue;
            }

            if (source[index] == '\'')
            {
                return index + 1;
            }

            index++;
        }

        return source.Length;
    }

    private static int FindRegularStringLiteralEnd(string source, int quoteStartIndex)
    {
        var index = quoteStartIndex + 1;

        while (index < source.Length)
        {
            if (source[index] == '\\')
            {
                index += 2;
                continue;
            }

            if (source[index] == '"')
            {
                return index + 1;
            }

            index++;
        }

        return source.Length;
    }

    private static int FindRawStringLiteralEnd(string source, int quoteStartIndex, int delimiterLength)
    {
        var index = quoteStartIndex + delimiterLength;

        while (index < source.Length)
        {
            if (source[index] == '"' && CountConsecutiveQuotes(source, index) == delimiterLength)
            {
                return index + delimiterLength;
            }

            index++;
        }

        return source.Length;
    }

    private static int FindRawInterpolatedStringEnd(string source, StringLiteralStart literal)
    {
        var index = literal.QuoteIndex + literal.QuoteLength;

        while (index < source.Length)
        {
            if (source[index] == '"' && CountConsecutiveQuotes(source, index) == literal.QuoteLength)
            {
                return index + literal.QuoteLength;
            }

            if (source[index] == '{' &&
                CountConsecutiveCharacters(source, index, '{') == literal.DollarCount)
            {
                var closingBraceIndex = FindInterpolatedExpressionEnd(source, index, literal.DollarCount);
                index = closingBraceIndex < source.Length
                    ? closingBraceIndex + literal.DollarCount
                    : source.Length;
                continue;
            }

            index++;
        }

        return source.Length;
    }

    private static int FindVerbatimStringLiteralEnd(string source, int quoteStartIndex)
    {
        var index = quoteStartIndex + 1;

        while (index < source.Length)
        {
            if (source[index] != '"')
            {
                index++;
                continue;
            }

            if (index + 1 < source.Length && source[index + 1] == '"')
            {
                index += 2;
                continue;
            }

            return index + 1;
        }

        return source.Length;
    }

    private static void Mask(char[] characters, int startIndex, int endIndex)
    {
        for (var index = startIndex; index < endIndex; index++)
        {
            if (characters[index] != '\r' && characters[index] != '\n')
            {
                characters[index] = ' ';
            }
        }
    }

    private static string NormalizeProjectReferencePath(string include, string projectDirectory)
    {
        var normalizedInclude = include
            .Trim()
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(normalizedInclude, projectDirectory);

        if (!string.Equals(Path.GetExtension(fullPath), ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"ProjectReference Include '{include}' does not resolve to a .csproj path.");
        }

        return fullPath;
    }

    private static void CopySanitizedRange(char[] characters, string source, int startIndex, int endIndex)
    {
        var sanitizedRange = SanitizeSource(source.Substring(startIndex, endIndex - startIndex));
        sanitizedRange.CopyTo(0, characters, startIndex, sanitizedRange.Length);
    }

    private static int FindInterpolatedExpressionEnd(
        string source,
        int openingBraceIndex,
        int braceDelimiterLength)
    {
        var depth = 1;
        var index = openingBraceIndex + braceDelimiterLength;

        while (index < source.Length)
        {
            if (source[index] == '/' && index + 1 < source.Length)
            {
                if (source[index + 1] == '/')
                {
                    var lineEndIndex = source.IndexOf('\n', index + 2);
                    index = lineEndIndex < 0 ? source.Length : lineEndIndex;
                    continue;
                }

                if (source[index + 1] == '*')
                {
                    var closingCommentIndex = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                    index = closingCommentIndex < 0 ? source.Length : closingCommentIndex + 2;
                    continue;
                }
            }

            if (source[index] == '\'')
            {
                index = FindCharacterLiteralEnd(source, index);
                continue;
            }

            if (TryGetStringLiteralStart(source, index, out var literal))
            {
                index = FindStringLiteralEnd(source, literal);
                continue;
            }

            if (source[index] == '{')
            {
                depth++;
                index++;
                continue;
            }

            if (source[index] == '}')
            {
                if (depth == 1 && CountConsecutiveCharacters(source, index, '}') == braceDelimiterLength)
                {
                    return index;
                }

                depth--;
                index++;
                continue;
            }

            index++;
        }

        return source.Length;
    }

    private static int FindInterpolatedStringEnd(string source, StringLiteralStart literal)
    {
        var index = literal.QuoteIndex + literal.QuoteLength;

        while (index < source.Length)
        {
            if (literal.IsVerbatim)
            {
                if (source[index] == '"')
                {
                    if (index + 1 < source.Length && source[index + 1] == '"')
                    {
                        index += 2;
                        continue;
                    }

                    return index + 1;
                }
            }
            else
            {
                if (source[index] == '\\')
                {
                    index += 2;
                    continue;
                }

                if (source[index] == '"')
                {
                    return index + 1;
                }
            }

            if (source[index] == '{')
            {
                if (index + 1 < source.Length && source[index + 1] == '{')
                {
                    index += 2;
                    continue;
                }

                var closingBraceIndex = FindInterpolatedExpressionEnd(source, index, 1);
                index = closingBraceIndex < source.Length ? closingBraceIndex + 1 : source.Length;
                continue;
            }

            if (source[index] == '}' && index + 1 < source.Length && source[index + 1] == '}')
            {
                index += 2;
                continue;
            }

            index++;
        }

        return source.Length;
    }

    private static int FindStringLiteralEnd(string source, StringLiteralStart literal)
    {
        if (literal.QuoteLength >= 3)
        {
            return literal.DollarCount > 0
                ? FindRawInterpolatedStringEnd(source, literal)
                : FindRawStringLiteralEnd(source, literal.QuoteIndex, literal.QuoteLength);
        }

        if (literal.DollarCount > 0)
        {
            return FindInterpolatedStringEnd(source, literal);
        }

        return literal.IsVerbatim
            ? FindVerbatimStringLiteralEnd(source, literal.QuoteIndex)
            : FindRegularStringLiteralEnd(source, literal.QuoteIndex);
    }

    private static int MaskInterpolatedString(
        string source,
        char[] sanitized,
        int startIndex,
        StringLiteralStart literal)
    {
        if (literal.QuoteLength >= 3)
        {
            return MaskRawInterpolatedString(source, sanitized, startIndex, literal);
        }

        var index = literal.QuoteIndex + literal.QuoteLength;
        Mask(sanitized, startIndex, index);

        while (index < source.Length)
        {
            if (literal.IsVerbatim)
            {
                if (source[index] == '"')
                {
                    if (index + 1 < source.Length && source[index + 1] == '"')
                    {
                        Mask(sanitized, index, index + 2);
                        index += 2;
                        continue;
                    }

                    Mask(sanitized, index, index + 1);
                    return index + 1;
                }
            }
            else
            {
                if (source[index] == '\\')
                {
                    var escapedEndIndex = Math.Min(index + 2, source.Length);
                    Mask(sanitized, index, escapedEndIndex);
                    index = escapedEndIndex;
                    continue;
                }

                if (source[index] == '"')
                {
                    Mask(sanitized, index, index + 1);
                    return index + 1;
                }
            }

            if (source[index] == '{')
            {
                if (index + 1 < source.Length && source[index + 1] == '{')
                {
                    Mask(sanitized, index, index + 2);
                    index += 2;
                    continue;
                }

                var closingBraceIndex = FindInterpolatedExpressionEnd(source, index, 1);
                if (closingBraceIndex == source.Length)
                {
                    Mask(sanitized, index, source.Length);
                    return source.Length;
                }

                Mask(sanitized, index, index + 1);
                CopySanitizedRange(sanitized, source, index + 1, closingBraceIndex);
                Mask(sanitized, closingBraceIndex, closingBraceIndex + 1);
                index = closingBraceIndex + 1;
                continue;
            }

            if (source[index] == '}')
            {
                var escapedEndIndex = index + 1 < source.Length && source[index + 1] == '}'
                    ? index + 2
                    : index + 1;
                Mask(sanitized, index, escapedEndIndex);
                index = escapedEndIndex;
                continue;
            }

            Mask(sanitized, index, index + 1);
            index++;
        }

        return source.Length;
    }

    private static int MaskRawInterpolatedString(
        string source,
        char[] sanitized,
        int startIndex,
        StringLiteralStart literal)
    {
        var index = literal.QuoteIndex + literal.QuoteLength;
        Mask(sanitized, startIndex, index);

        while (index < source.Length)
        {
            if (source[index] == '"' && CountConsecutiveQuotes(source, index) == literal.QuoteLength)
            {
                Mask(sanitized, index, index + literal.QuoteLength);
                return index + literal.QuoteLength;
            }

            if (source[index] == '{' &&
                CountConsecutiveCharacters(source, index, '{') == literal.DollarCount)
            {
                var closingBraceIndex = FindInterpolatedExpressionEnd(source, index, literal.DollarCount);
                if (closingBraceIndex == source.Length)
                {
                    Mask(sanitized, index, source.Length);
                    return source.Length;
                }

                Mask(sanitized, index, index + literal.DollarCount);
                CopySanitizedRange(
                    sanitized,
                    source,
                    index + literal.DollarCount,
                    closingBraceIndex);
                Mask(sanitized, closingBraceIndex, closingBraceIndex + literal.DollarCount);
                index = closingBraceIndex + literal.DollarCount;
                continue;
            }

            Mask(sanitized, index, index + 1);
            index++;
        }

        return source.Length;
    }

    private static bool PathsAreEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(left, right, comparison);
    }

    private static bool TryGetStringLiteralStart(string source, int startIndex, out StringLiteralStart literal)
    {
        var quoteIndex = startIndex;
        var isVerbatim = false;
        var dollarCount = 0;

        if (source[quoteIndex] == '@')
        {
            isVerbatim = true;
            quoteIndex++;
        }

        while (quoteIndex < source.Length && source[quoteIndex] == '$')
        {
            dollarCount++;
            quoteIndex++;
        }

        if (quoteIndex < source.Length && source[quoteIndex] == '@' && !isVerbatim)
        {
            isVerbatim = true;
            quoteIndex++;
        }

        if (quoteIndex >= source.Length || source[quoteIndex] != '"')
        {
            literal = default;
            return false;
        }

        literal = new StringLiteralStart(
            quoteIndex,
            CountConsecutiveQuotes(source, quoteIndex),
            isVerbatim,
            dollarCount);
        return true;
    }

    private static int CountConsecutiveQuotes(string source, int startIndex)
    {
        return CountConsecutiveCharacters(source, startIndex, '"');
    }

    private static int CountConsecutiveCharacters(string source, int startIndex, char character)
    {
        var index = startIndex;

        while (index < source.Length && source[index] == character)
        {
            index++;
        }

        return index - startIndex;
    }

    private static T InvokeMediaDefinition<T>(string methodName, params object[] arguments)
    {
        return InvokePrivateStatic<T>(typeof(MediaRepository), methodName, arguments);
    }

    private static T InvokePrivateStatic<T>(
        Type declaringType,
        string methodName,
        params object[] arguments)
    {
        var method = declaringType.GetMethod(
            methodName,
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        return Assert.IsAssignableFrom<T>(method.Invoke(null, arguments));
    }

    private static BsonDocument RenderFilter(FilterDefinition<MediaAsset> filter)
    {
        var serializer = BsonSerializer.SerializerRegistry.GetSerializer<MediaAsset>();
        return filter.Render(new RenderArgs<MediaAsset>(
            serializer,
            BsonSerializer.SerializerRegistry));
    }

    private static BsonDocument RenderSort(SortDefinition<MediaAsset> sort)
    {
        var serializer = BsonSerializer.SerializerRegistry.GetSerializer<MediaAsset>();
        return sort.Render(new RenderArgs<MediaAsset>(
            serializer,
            BsonSerializer.SerializerRegistry));
    }

    private static BsonDocument RenderUpdate(UpdateDefinition<MediaAsset> update)
    {
        var serializer = BsonSerializer.SerializerRegistry.GetSerializer<MediaAsset>();
        return update.Render(new RenderArgs<MediaAsset>(
            serializer,
            BsonSerializer.SerializerRegistry)).AsBsonDocument;
    }

    private static string ApiSourcePath()
    {
        return Path.Combine(RepositoryRootPath(), "backend", "FamilyPortal.Api", "Controllers");
    }

    private static string DomainSourcePath()
    {
        return Path.Combine(RepositoryRootPath(), "backend", "FamilyPortal.Domain");
    }

    private static string ApplicationProjectPath()
    {
        return Path.Combine(
            RepositoryRootPath(),
            "backend",
            "FamilyPortal.Application",
            "FamilyPortal.Application.csproj");
    }

    private static string RepositoryRootPath()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);

        while (currentDirectory is not null)
        {
            var solutionPath = Path.Combine(currentDirectory.FullName, "backend", "FamilyPortal.slnx");
            if (File.Exists(solutionPath))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Unable to locate the repository root from '{AppContext.BaseDirectory}'. " +
            "Expected an ancestor containing backend/FamilyPortal.slnx.");
    }
}
