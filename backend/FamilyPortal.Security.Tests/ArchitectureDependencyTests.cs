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
        { "var character = 'FamilyPortal.Infrastructure.Security';", false },
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
        var references = ApplicationProjectReferenceFileNames();

        Assert.True(
            references.SetEquals(new[] { "FamilyPortal.Domain.csproj" }),
            "Application project references must contain only FamilyPortal.Domain.csproj. " +
            $"Found: {string.Join(", ", references.OrderBy(reference => reference, StringComparer.Ordinal))}.");
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

            if (TryGetStringLiteralEnd(source, index, out var stringEndIndex))
            {
                Mask(sanitized, index, stringEndIndex);
                index = stringEndIndex;
                continue;
            }

            index++;
        }

        return new string(sanitized);
    }

    private static ISet<string> ApplicationProjectReferenceFileNames()
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
            .ToHashSet(StringComparer.Ordinal);
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
        var fileName = Path.GetFileName(fullPath);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidDataException(
                $"ProjectReference Include '{include}' does not resolve to a project file name.");
        }

        return fileName;
    }

    private static bool TryGetStringLiteralEnd(string source, int startIndex, out int endIndex)
    {
        var quoteIndex = startIndex;
        var isVerbatim = false;

        if (source[quoteIndex] == '@')
        {
            isVerbatim = true;
            quoteIndex++;
        }

        while (quoteIndex < source.Length && source[quoteIndex] == '$')
        {
            quoteIndex++;
        }

        if (quoteIndex < source.Length && source[quoteIndex] == '@' && !isVerbatim)
        {
            isVerbatim = true;
            quoteIndex++;
        }

        if (quoteIndex >= source.Length || source[quoteIndex] != '"')
        {
            endIndex = startIndex;
            return false;
        }

        var delimiterLength = CountConsecutiveQuotes(source, quoteIndex);
        if (delimiterLength >= 3)
        {
            endIndex = FindRawStringLiteralEnd(source, quoteIndex, delimiterLength);
            return true;
        }

        endIndex = isVerbatim
            ? FindVerbatimStringLiteralEnd(source, quoteIndex)
            : FindRegularStringLiteralEnd(source, quoteIndex);
        return true;
    }

    private static int CountConsecutiveQuotes(string source, int startIndex)
    {
        var index = startIndex;

        while (index < source.Length && source[index] == '"')
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
