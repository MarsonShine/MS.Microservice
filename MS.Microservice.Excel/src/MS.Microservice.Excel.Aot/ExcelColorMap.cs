using NPOI.HSSF.Util;
using System.Collections.Frozen;

namespace MS.Microservice.Excel.Aot;

public sealed class ExcelColorMap
{
    private static readonly FrozenDictionary<string, short> Palette = new Dictionary<string, short>(StringComparer.Ordinal)
    {
        [nameof(HSSFColor.Black)] = HSSFColor.Black.Index,
        [nameof(HSSFColor.Brown)] = HSSFColor.Brown.Index,
        [nameof(HSSFColor.OliveGreen)] = HSSFColor.OliveGreen.Index,
        [nameof(HSSFColor.DarkGreen)] = HSSFColor.DarkGreen.Index,
        [nameof(HSSFColor.DarkTeal)] = HSSFColor.DarkTeal.Index,
        [nameof(HSSFColor.DarkBlue)] = HSSFColor.DarkBlue.Index,
        [nameof(HSSFColor.Indigo)] = HSSFColor.Indigo.Index,
        [nameof(HSSFColor.Grey80Percent)] = HSSFColor.Grey80Percent.Index,
        [nameof(HSSFColor.DarkRed)] = HSSFColor.DarkRed.Index,
        [nameof(HSSFColor.Orange)] = HSSFColor.Orange.Index,
        [nameof(HSSFColor.DarkYellow)] = HSSFColor.DarkYellow.Index,
        [nameof(HSSFColor.Green)] = HSSFColor.Green.Index,
        [nameof(HSSFColor.Teal)] = HSSFColor.Teal.Index,
        [nameof(HSSFColor.Blue)] = HSSFColor.Blue.Index,
        [nameof(HSSFColor.LightBlue)] = HSSFColor.LightBlue.Index,
        [nameof(HSSFColor.Violet)] = HSSFColor.Violet.Index,
        [nameof(HSSFColor.Pink)] = HSSFColor.Pink.Index,
        [nameof(HSSFColor.Gold)] = HSSFColor.Gold.Index,
        [nameof(HSSFColor.Yellow)] = HSSFColor.Yellow.Index,
        [nameof(HSSFColor.BrightGreen)] = HSSFColor.BrightGreen.Index,
        [nameof(HSSFColor.BlueGrey)] = HSSFColor.BlueGrey.Index,
        [nameof(HSSFColor.Grey50Percent)] = HSSFColor.Grey50Percent.Index,
        [nameof(HSSFColor.Red)] = HSSFColor.Red.Index,
        [nameof(HSSFColor.LightOrange)] = HSSFColor.LightOrange.Index,
        [nameof(HSSFColor.Lime)] = HSSFColor.Lime.Index,
        [nameof(HSSFColor.SeaGreen)] = HSSFColor.SeaGreen.Index,
        [nameof(HSSFColor.Aqua)] = HSSFColor.Aqua.Index,
        [nameof(HSSFColor.Grey40Percent)] = HSSFColor.Grey40Percent.Index,
        [nameof(HSSFColor.Turquoise)] = HSSFColor.Turquoise.Index,
        [nameof(HSSFColor.SkyBlue)] = HSSFColor.SkyBlue.Index,
        [nameof(HSSFColor.Plum)] = HSSFColor.Plum.Index,
        [nameof(HSSFColor.Grey25Percent)] = HSSFColor.Grey25Percent.Index,
        [nameof(HSSFColor.Rose)] = HSSFColor.Rose.Index,
        [nameof(HSSFColor.Tan)] = HSSFColor.Tan.Index,
        [nameof(HSSFColor.LightYellow)] = HSSFColor.LightYellow.Index,
        [nameof(HSSFColor.LightGreen)] = HSSFColor.LightGreen.Index,
        [nameof(HSSFColor.LightTurquoise)] = HSSFColor.LightTurquoise.Index,
        [nameof(HSSFColor.PaleBlue)] = HSSFColor.PaleBlue.Index,
        [nameof(HSSFColor.Lavender)] = HSSFColor.Lavender.Index,
        [nameof(HSSFColor.White)] = HSSFColor.White.Index,
        [nameof(HSSFColor.CornflowerBlue)] = HSSFColor.CornflowerBlue.Index,
        [nameof(HSSFColor.LemonChiffon)] = HSSFColor.LemonChiffon.Index,
        [nameof(HSSFColor.Maroon)] = HSSFColor.Maroon.Index,
        [nameof(HSSFColor.Orchid)] = HSSFColor.Orchid.Index,
        [nameof(HSSFColor.Coral)] = HSSFColor.Coral.Index,
        [nameof(HSSFColor.RoyalBlue)] = HSSFColor.RoyalBlue.Index,
        [nameof(HSSFColor.LightCornflowerBlue)] = HSSFColor.LightCornflowerBlue.Index,
        [nameof(HSSFColor.Automatic)] = HSSFColor.Automatic.Index,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, short> Colors => Palette;

    public bool TryGetColor(string color, out short value) => Palette.TryGetValue(color, out value);
}
