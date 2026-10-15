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
        var shared = SharedProjects(root).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var project in shared)
        foreach (var reference in XDocument.Load(project).Descendants("ProjectReference"))
        {
            var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, reference.Attribute("Include")!.Value));
            Assert.Contains(target, shared);
        }
    }

    [Theory]
    [InlineData("MS.Microservice.AI")]
    [InlineData("MS.Microservice.Logging")]
    [InlineData("MS.Microservice.Tasks")]
    [InlineData("MS.Microservice.Messaging")]
    [InlineData("MS.Microservice.Persistence")]
    [InlineData("MS.Microservice.Media")]
    public void ModuleSolutionContainsOwnedProjectsAndTheirCompleteDependencyGraph(string module)
    {
        var root = RepositoryRoot();
        var directory = Path.Combine(root, module);
        var solution = Path.Combine(directory, module + ".slnx");
        var projects = XDocument.Load(solution).Descendants("Project")
            .Select(node => Path.GetFullPath(Path.Combine(directory, node.Attribute("Path")!.Value)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains(projects, path => path.StartsWith(Path.Combine(directory, "src") + Path.DirectorySeparatorChar));
        Assert.Contains(projects, path => path.StartsWith(Path.Combine(directory, "test") + Path.DirectorySeparatorChar));
        foreach (var source in new[] { "src", "test" })
        foreach (var project in Directory.EnumerateFiles(Path.Combine(directory, source), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)))
            Assert.Contains(Path.GetFullPath(project), projects);
        foreach (var project in projects)
        {
            Assert.True(File.Exists(project), project);
            foreach (var reference in XDocument.Load(project).Descendants("ProjectReference"))
                Assert.Contains(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, reference.Attribute("Include")!.Value)), projects);
        }
    }

    private static IEnumerable<string> SharedProjects(string root)
    {
        var sources = new[] { Path.Combine(root, "src") }.Concat(
            Directory.EnumerateDirectories(root, "MS.Microservice.*")
                .Select(directory => Path.Combine(directory, "src")).Where(Directory.Exists));
        return sources.SelectMany(source => Directory.EnumerateDirectories(source))
            .SelectMany(directory => Directory.EnumerateFiles(directory, "*.csproj")).Select(Path.GetFullPath);
    }
    [Fact]
    public void BusinessApplicationUsesOnlyNeutralMessagingContracts()
    {
        var dependencies = typeof(ProfileService).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(dependencies, assembly => assembly.Name!.Contains("Wolverine") || assembly.Name.Contains("SelfManaged"));
        Assert.Contains(dependencies, assembly => assembly.Name == "MS.Microservice.Messaging.Abstractions");
    }

    [Fact]
    public void TaskCoreDoesNotDependOnPersistenceOrAnExecutionProvider()
    {
        var root = RepositoryRoot();
        var project = XDocument.Load(Path.Combine(root, "MS.Microservice.Tasks/src/MS.Microservice.Tasks/MS.Microservice.Tasks.csproj"));
        var packages = project.Descendants("PackageReference").Select(node => node.Attribute("Include")!.Value);
        Assert.DoesNotContain(packages, name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || name.StartsWith("Coravel", StringComparison.Ordinal) || name.StartsWith("Wolverine", StringComparison.Ordinal));
        var references = project.Descendants("ProjectReference").Select(node => node.Attribute("Include")!.Value);
        Assert.DoesNotContain(references, name => name.Contains(".EFCore", StringComparison.Ordinal)
            || name.Contains(".Coravel", StringComparison.Ordinal));
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
