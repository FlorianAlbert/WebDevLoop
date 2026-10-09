using System.Xml.Linq;

namespace WebDevLoop.Core.Tests;

public sealed class SolutionArchitectureTests
{
    [Fact]
    public void core_has_no_infrastructure_reference()
    {
        string[] projectReferences = ReadProjectReferenceNames("src/WebDevLoop.Core/WebDevLoop.Core.csproj");

        Assert.DoesNotContain("WebDevLoop.Infrastructure.csproj", projectReferences);
        Assert.DoesNotContain("WebDevLoop.Web.csproj", projectReferences);
    }

    [Fact]
    public void web_references_core_and_infrastructure()
    {
        string[] projectReferences = ReadProjectReferenceNames("src/WebDevLoop.Web/WebDevLoop.Web.csproj");

        Assert.Contains("WebDevLoop.Core.csproj", projectReferences);
        Assert.Contains("WebDevLoop.Infrastructure.csproj", projectReferences);
    }

    private static string[] ReadProjectReferenceNames(string relativeProjectPath)
    {
        string projectPath = Path.Combine(FindRepositoryRoot(), relativeProjectPath);
        XDocument project = XDocument.Load(projectPath);

        return project
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(include => include is not null)
            .Select(include => Path.GetFileName(include!.Replace('\\', Path.DirectorySeparatorChar)))
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WebDevLoop.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
