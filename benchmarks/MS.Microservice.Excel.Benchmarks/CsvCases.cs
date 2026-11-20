using System.Text;
using MS.Microservice.Infrastructure.Utils;
using MS.Microservice.Infrastructure.Utils.Excel;

internal static class CsvCases
{
    public static void Run()
    {
        MeasureRaw("CSV / one plain cell", [["x"]], "x\r\n", 1000);
        IReadOnlyList<string?>[] typical = Enumerable.Repeat<IReadOnlyList<string?>>(
            ["订单", "x,y", "say \"hi\"", "a\nb", null], 256).ToArray();
        MeasureRaw("CSV / 256 mixed rows", typical,
            string.Concat(Enumerable.Repeat("订单,\"x,y\",\"say \"\"hi\"\"\",\"a\nb\",\r\n", 256)), 64);
        var quoted = string.Concat(Enumerable.Repeat("A\"中\"", 4096));
        MeasureRaw("CSV / long quoted field", [[quoted]], "\"" + quoted.Replace("\"", "\"\"") + "\"\r\n", 128);
        var plain = new string('x', 16_384);
        MeasureRaw("CSV / long plain field", [[plain]], plain + "\r\n", 128);

        var helper = new ExcelHelper();
        var products = Enumerable.Range(1, 256).Select(i => new CsvProduct { Id = i, Name = "中文" }).ToArray();
        var expected = Encoding.UTF8.GetBytes("编号,名称\r\n" + string.Concat(products.Select(p => $"{p.Id},中文\r\n")));
        using var output = new MemoryStream(expected.Length);
        Bench.Measure("CSV / 256 model rows", 64, () =>
        {
            output.SetLength(0);
            helper.ExportCsvAsync(products, output).GetAwaiter().GetResult();
            return output;
        }, stream => Matches(stream, expected));
    }

    private static void MeasureRaw(string name, IReadOnlyList<string?>[] rows, string csv, int iterations)
    {
        var expected = Encoding.UTF8.GetBytes(csv);
        using var output = new MemoryStream(expected.Length);
        Bench.Measure(name, iterations, () =>
        {
            output.SetLength(0);
            CsvHelper.WriteAsync(output, rows).GetAwaiter().GetResult();
            return output;
        }, stream => Matches(stream, expected));
    }

    private static bool Matches(MemoryStream stream, byte[] expected) =>
        stream.GetBuffer().AsSpan(0, checked((int)stream.Length)).SequenceEqual(expected);
}

public sealed class CsvProduct
{
    [ExcelColumn("编号", Order = 0)] public int Id { get; set; }
    [ExcelColumn("名称", Order = 1)] public string? Name { get; set; }
}
