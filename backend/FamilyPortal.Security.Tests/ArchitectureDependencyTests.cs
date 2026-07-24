using Xunit;

namespace FamilyPortal.Security.Tests;

/// <summary>驗證專案遵循 Clean Architecture 的相依方向。</summary>
public sealed class ArchitectureDependencyTests
{
    /// <summary>Controller 不得直接引用 Infrastructure 命名空間。</summary>
    [Fact]
    public void Controllers_MustNotReferenceInfrastructureNamespaces()
    {
        var controllerFiles = Directory.GetFiles(ApiSourcePath(), "*Controller.cs");

        Assert.NotEmpty(controllerFiles);

        foreach (var file in controllerFiles)
        {
            var source = File.ReadAllText(file);

            Assert.DoesNotContain("using FamilyPortal.Infrastructure", source);
        }
    }

    /// <summary>Application 專案只能直接參考 Domain 專案。</summary>
    [Fact]
    public void ApplicationProject_MustOnlyReferenceDomainProject()
    {
        var project = File.ReadAllText(ApplicationProjectPath());

        Assert.Contains("FamilyPortal.Domain.csproj", project);
        Assert.DoesNotContain("FamilyPortal.Infrastructure.csproj", project);
        Assert.DoesNotContain("FamilyPortal.Api.csproj", project);
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
