using System.Reflection;
using MS.Microservice.Excel.Aot;
using NPOI.HSSF.Util;
using Xunit;

namespace MS.Microservice.Excel.Tests.Excel;

public sealed class ExcelColorMapTests
{
    [Fact]
    public void StaticPaletteContainsEveryInstalledNpoiColorWithSameNameAndIndex()
    {
        // Reflection is confined to this compatibility test, so dependency changes cannot silently omit a color.
        var expected = typeof(HSSFColor).GetNestedTypes(BindingFlags.Public)
            .Where(t => typeof(HSSFColor).IsAssignableFrom(t))
            .ToDictionary(t => t.Name, t => (short)t.GetField("Index")!.GetValue(null)!, StringComparer.Ordinal);
        var map = new ExcelColorMap();
        Assert.True(expected.Count == map.Colors.Count,
            $"Palette differs. Missing: {string.Join(", ", expected.Keys.Except(map.Colors.Keys))}; extra: {string.Join(", ", map.Colors.Keys.Except(expected.Keys))}");
        foreach (var (name, index) in expected)
        {
            Assert.True(map.TryGetColor(name, out short actual));
            Assert.Equal(index, actual);
        }
    }

    [Fact]
    public void PaletteIsSharedAndRetainsOrdinalNameMatching()
    {
        var first = new ExcelColorMap();
        Assert.Same(first.Colors, new ExcelColorMap().Colors);
        Assert.True(first.TryGetColor("Red", out short red));
        Assert.Equal(HSSFColor.Red.Index, red);
        Assert.False(first.TryGetColor("red", out _));
        Assert.False(first.TryGetColor("Unknown", out _));
    }
}
