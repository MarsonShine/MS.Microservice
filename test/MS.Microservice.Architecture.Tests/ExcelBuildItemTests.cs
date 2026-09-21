using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace MS.Microservice.Architecture.Tests;

public sealed class ExcelBuildItemTests
{
    [Theory]
    [InlineData("MS.Microservice.Excel")]
    [InlineData("MS.Microservice.Excel.Aot")]
    [InlineData("MS.Microservice.Excel.Generator")]
    public async Task ProjectsHaveUniqueAndSeparatedCompileInputs(string name)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "MS.Microservice.slnx"))) root = root.Parent;
        Assert.NotNull(root);
        var generatorProject = name == "MS.Microservice.Excel.Generator";
        var project = Path.Combine(root.FullName, "MS.Microservice.Excel", "src", name, name + ".csproj");
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root.FullName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var argument in new[] { "msbuild", project, "-getItem:Compile,ProjectReference,PackageReference", "-getProperty:IsRoslynComponent,TargetFramework,EnableTrimAnalyzer,EnableAotAnalyzer" })
            start.ArgumentList.Add(argument);
        if (generatorProject)
        {
            start.ArgumentList.Add("-p:EnableTrimAnalyzer=true");
            start.ArgumentList.Add("-p:EnableAotAnalyzer=true");
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        Assert.True(process.ExitCode == 0, await error);
        using var document = JsonDocument.Parse(await output);
        var items = document.RootElement.GetProperty("Items");
        var paths = items.GetProperty("Compile").EnumerateArray()
            .Select(item => Path.GetRelativePath(Path.GetDirectoryName(project)!, item.GetProperty("FullPath").GetString()!)
                .Replace('\\', '/')).ToArray();
        Assert.NotEmpty(paths);
        Assert.DoesNotContain(paths, path => path.StartsWith("../", StringComparison.Ordinal));
        Assert.Equal(paths.Length, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        if (generatorProject)
        {
            Assert.Contains("ExcelSourceGenerator.cs", paths);
            Assert.Equal("false", document.RootElement.GetProperty("Properties").GetProperty("EnableTrimAnalyzer").GetString());
            Assert.Equal("false", document.RootElement.GetProperty("Properties").GetProperty("EnableAotAnalyzer").GetString());
            Assert.Equal("true", document.RootElement.GetProperty("Properties").GetProperty("IsRoslynComponent").GetString());
            Assert.Equal("netstandard2.0", document.RootElement.GetProperty("Properties").GetProperty("TargetFramework").GetString());
            Assert.Contains(items.GetProperty("PackageReference").EnumerateArray(), item =>
                item.GetProperty("Identity").GetString() == "Microsoft.CodeAnalysis.CSharp");
            var solution = System.Xml.Linq.XDocument.Load(Path.Combine(root.FullName, "MS.Microservice.slnx"));
            Assert.Contains(solution.Descendants("Project"), item => item.Attribute("Path")?.Value == "MS.Microservice.Excel/src/MS.Microservice.Excel.Generator/MS.Microservice.Excel.Generator.csproj");
        }
        else
        {
            Assert.DoesNotContain(paths, path => path.EndsWith("ExcelSourceGenerator.cs", StringComparison.Ordinal));
            Assert.DoesNotContain(paths, path => path.StartsWith("Aot/", StringComparison.Ordinal));
            var references = items.GetProperty("ProjectReference").EnumerateArray().ToArray();
            Assert.Equal(name == "MS.Microservice.Excel.Aot", references.Any(item => item.GetProperty("Identity").GetString()!.Contains("Excel.Generator")));
        }
    }
}
