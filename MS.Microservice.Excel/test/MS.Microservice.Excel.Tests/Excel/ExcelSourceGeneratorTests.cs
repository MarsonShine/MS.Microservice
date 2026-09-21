using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using MS.Microservice.Excel.Aot;
using MS.Microservice.Excel.Generator;
using Xunit;

namespace MS.Microservice.Excel.Tests.Generation;

public sealed class ExcelSourceGeneratorTests
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp14);
    private static readonly ImmutableArray<MetadataReference> References =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Append(typeof(ExcelSerializableAttribute).Assembly.Location).Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToImmutableArray();

    public static IEnumerable<object[]> ValidDeclarations()
    {
        yield return ["public class Row { public int Value { get; set; } } [ExcelSerializable(typeof(Row))] public static partial class Maps;"];
        yield return ["public class Row { public string? @event { get; set; } public int? Id { get; set; } } [ExcelSerializable(typeof(Row))] public static partial class Maps;"];
        yield return ["public class Base { public int Id { get; set; } } public class Row : Base { public string Name => \"read only\"; } [ExcelSerializable(typeof(Row))] public static partial class Maps;"];
        yield return ["public class Row<T> { public T Value { get; set; } = default!; } [ExcelSerializable(typeof(Row<int>))] public static partial class Maps;"];
        yield return ["public class Row { public required string Name { get; init; } } [ExcelSerializable(typeof(Row), Factory = \"Create\")] public static partial class Maps { private static Row Create() => new() { Name = \"seed\" }; }"];
        yield return ["public class Row { public int Value { get; set; } } public class Other { public bool Value { get; set; } } [ExcelSerializable(typeof(Row))] public static partial class Maps; [ExcelSerializable(typeof(Other))] public static partial class Maps;"];
    }

    [Theory]
    [MemberData(nameof(ValidDeclarations))]
    public void GeneratedSourceCompilesForRealModelShapes(string declarations)
    {
        var (result, compilation) = Generate(declarations);
        Assert.Empty(result.Diagnostics);
        Assert.NotEmpty(result.GeneratedTrees);
        using var assembly = new MemoryStream();
        var emitted = compilation.Emit(assembly);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
    }

    [Theory]
    [InlineData("public class Row { public int Value { get; set; } } [ExcelSerializable(typeof(Row))] public partial class Maps;", "static partial")]
    [InlineData("public class Row { public int Value { get; set; } } [ExcelSerializable(typeof(Row))] public static class Maps;", "partial")]
    [InlineData("public class Row { public int Value { get; set; } } [ExcelSerializable(typeof(Row))] public static partial class Maps<T>;", "non-generic")]
    [InlineData("public class Row { [ExcelColumn(\"A\")] public int One { get; set; } [ExcelColumn(\" A \")] public int Two { get; set; } } [ExcelSerializable(typeof(Row))] public static partial class Maps;", "duplicated")]
    [InlineData("public class Row { public System.DateTimeOffset Value { get; set; } } [ExcelSerializable(typeof(Row))] public static partial class Maps;", "unsupported")]
    [InlineData("public class Row(int seed) { public int Value { get; set; } = seed; } [ExcelSerializable(typeof(Row))] public static partial class Maps;", "Factory")]
    [InlineData("public class Row { public required string Value { get; init; } } [ExcelSerializable(typeof(Row))] public static partial class Maps;", "Factory")]
    [InlineData("public class Row { public int Value { get; set; } } [ExcelSerializable(typeof(Row), Factory=\"Missing\")] public static partial class Maps;", "Factory")]
    [InlineData("public class Row { [ExcelColumn(Ignore=true)] public int Value { get; set; } } [ExcelSerializable(typeof(Row))] public static partial class Maps;", "no eligible")]
    [InlineData("""public class Row { [ExcelColumn("   ")] public int Value { get; set; } } [ExcelSerializable(typeof(Row))] public static partial class Maps;""", "empty")]
    [InlineData("public class Row { public int Value { get; set; } } [ExcelSerializable(typeof(Row)), ExcelSerializable(typeof(Row))] public static partial class Maps;", "duplicated")]
    [InlineData("public class Row { [ExcelColumn(Converter=typeof(Bad))] public int Value { get; set; } } public class Bad; [ExcelSerializable(typeof(Row))] public static partial class Maps;", "Converter")]
    [InlineData("[ExcelSerializable(typeof(System.Uri))] public static partial class Maps;", "Factory")]
    public void InvalidDeclarationsProduceAnActionableCompileTimeDiagnostic(string declarations, string message)
    {
        var (result, _) = Generate(declarations);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("EXCEL001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains(message, diagnostic.GetMessage());
    }

    [Fact]
    public void SeparateContextsWithTheSameShortNameDoNotCollide()
    {
        var (result, compilation) = Generate("""
            public class Row { public int Value { get; set; } }
            namespace First { [ExcelSerializable(typeof(Row))] public static partial class Maps; }
            namespace Second { [ExcelSerializable(typeof(Row))] public static partial class Maps; }
            """);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(2, result.GeneratedTrees.Length);
        Assert.DoesNotContain(compilation.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
    }

    private static (GeneratorDriverRunResult Result, Compilation Compilation) Generate(string declarations)
    {
        var source = "#nullable enable\nusing MS.Microservice.Excel.Aot;\n" + declarations;
        var compilation = CSharpCompilation.Create("ExcelGeneratorProbe",
            [CSharpSyntaxTree.ParseText(source, ParseOptions)], References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new ExcelSourceGenerator().AsSourceGenerator()], parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var result, out _);
        return (driver.GetRunResult(), result);
    }
}
