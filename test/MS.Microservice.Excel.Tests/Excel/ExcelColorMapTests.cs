using MS.Microservice.Infrastructure.Utils.Excel;
using NPOI.HSSF.Util;
using Xunit;

namespace MS.Microservice.Excel.Tests;

public sealed class ExcelColorMapTests
{
    [Fact]
    public void MutatingOnePaletteDoesNotChangeExistingOrFutureMaps()
    {
        var first = new ExcelColorMap();
        var second = new ExcelColorMap();
        first.Colors!.Clear();

        Assert.False(first.TryGetColor("Black", out _));
        foreach (var map in new[] { second, new ExcelColorMap() })
        {
            Assert.True(map.TryGetColor("Black", out var black));
            Assert.Equal(HSSFColor.Black.Index, black);
            Assert.False(map.TryGetColor("black", out _));
        }
    }
}
