using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Xml.Linq;
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

    private static string ApiSourcePath()
    {
        return Path.Combine(RepositoryRootPath(), "backend", "FamilyPortal.Api", "Controllers");
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
