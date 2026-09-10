using System.Xml.Linq;
using MS.Microservice.Reference.Application;
using MS.Microservice.Reference.Web;
using Xunit;

namespace MS.Microservice.Architecture.Tests;

public sealed class ComponentBoundaryTests
{
    [Fact]
    public void EveryReusableProjectReferencesOnlyReusableProjects()
    {
        var root = RepositoryRoot();
        var source = Path.Combine(root, "src") + Path.DirectorySeparatorChar;
        foreach (var directory in Directory.EnumerateDirectories(source))
        foreach (var project in Directory.EnumerateFiles(directory, "*.csproj"))
        foreach (var reference in XDocument.Load(project).Descendants("ProjectReference"))
        {
            var target = Path.GetFullPath(Path.Combine(directory, reference.Attribute("Include")!.Value));
            Assert.True(target.StartsWith(source, StringComparison.OrdinalIgnoreCase), $"{project} depends on {target}");
            Assert.True(File.Exists(target), $"Missing project: {target}");
        }
    }

    [Fact]
    public void BusinessApplicationUsesOnlyNeutralMessagingContracts()
    {
        var dependencies = typeof(ProfileService).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(dependencies, assembly => assembly.Name!.Contains("Wolverine") || assembly.Name.Contains("SelfManaged"));
        Assert.Contains(dependencies, assembly => assembly.Name == "MS.Microservice.Messaging.Abstractions");
    }

    [Fact]
    public void ReferenceDeploymentGraphContainsNoOptionalLessons()
    {
        var root = RepositoryRoot();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Visit(Path.Combine(root, "samples/Reference/MS.Microservice.Reference.Web/MS.Microservice.Reference.Web.csproj"));
        Assert.DoesNotContain(visited, path => path.Contains("Lab") || path.Contains("Audio") || path.Contains("Excel")
            || path.Contains("SqlSugar") || path.Contains(".AI.") || path.Contains("EventSourcing"));

        void Visit(string project)
        {
            project = Path.GetFullPath(project);
            if (!visited.Add(project)) return;
            foreach (var reference in XDocument.Load(project).Descendants("ProjectReference"))
                Visit(Path.Combine(Path.GetDirectoryName(project)!, reference.Attribute("Include")!.Value));
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MS.Microservice.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
