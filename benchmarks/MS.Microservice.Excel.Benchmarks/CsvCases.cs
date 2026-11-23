using System.Text;
using MS.Microservice.Infrastructure.Utils;
using MS.Microservice.Infrastructure.Utils.Excel;

internal static class CsvCases
{
    public static void Run()
    {
        MeasureRaw("CSV / one plain cell", [["x"]], "x\r\n", 1000);
        MeasureRaw("CSV / 1024 tiny rows", Enumerable.Repeat<IReadOnlyList<string?>>(["x"], 1024).ToArray(),
            string.Concat(Enumerable.Repeat("x\r\n", 1024)), 48);
        IReadOnlyList<string?>[] typical = Enumerable.Repeat<IReadOnlyList<string?>>(
            ["订单", "x,y", "say \"hi\"", "a\nb", null], 256).ToArray();
        MeasureRaw("CSV / 256 mixed rows", typical,
            string.Concat(Enumerable.Repeat("订单,\"x,y\",\"say \"\"hi\"\"\",\"a\nb\",\r\n", 256)), 64);
        var quoted = string.Concat(Enumerable.Repeat("A\"中\"", 4096));
        MeasureRaw("CSV / long quoted field", [[quoted]], Quoted(quoted) + "\r\n", 128);
        var sparse = string.Concat(Enumerable.Repeat(new string('a', 255) + "\"", 64));
        MeasureRaw("CSV / sparse quotes 1 in 256", [[sparse]], Quoted(sparse) + "\r\n", 96);
        var allQuotes = new string('"', quoted.Length);
        MeasureRaw("CSV / all quotes", [[allQuotes]], Quoted(allQuotes) + "\r\n", 64);
        const string json = """{"id":42,"ok":true}""";
        MeasureRaw("CSV / 256 JSON cells", Enumerable.Repeat<IReadOnlyList<string?>>([json], 256).ToArray(),
            string.Concat(Enumerable.Repeat(Quoted(json) + "\r\n", 256)), 64);
        var plain = new string('x', 16_384);
        MeasureRaw("CSV / long plain field", [[plain]], plain + "\r\n", 128);
        using var cancellation = new CancellationTokenSource();
        MeasureRaw("CSV / long plain field with cancellation", [[plain]], plain + "\r\n", 128,
            cancellationToken: cancellation.Token);

        var semicolon = new CsvOptions { Delimiter = ';' };
        MeasureRaw("CSV / reused semicolon options", [["a;b", "x"]], "\"a;b\";x\r\n", 2000, () => semicolon);
        MeasureRaw("CSV / fresh semicolon options", [["a;b", "x"]], "\"a;b\";x\r\n", 2000,
            static () => new CsvOptions { Delimiter = ';' });
        MeasureRaw("CSV / long plain field semicolon", [[plain]], plain + "\r\n", 128, () => semicolon);
        var pipe = semicolon with { Delimiter = '|' };
        int next = 0;
        MeasureRaw("CSV / alternating delimiters", [["x"]], "x\r\n", 2000,
            () => (next++ & 1) == 0 ? semicolon : pipe);
        var plus = semicolon with { Delimiter = '+' };
        MeasureRaw("CSV / alternating colliding delimiters", [["x"]], "x\r\n", 2000,
            () => (next++ & 1) == 0 ? semicolon : plus);

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

    private static string Quoted(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    private static void MeasureRaw(string name, IReadOnlyList<string?>[] rows, string csv, int iterations,
        Func<CsvOptions>? options = null, CancellationToken cancellationToken = default)
    {
        var expected = Encoding.UTF8.GetBytes(csv);
        using var output = new MemoryStream(expected.Length);
        Bench.Measure(name, iterations, () =>
        {
            output.SetLength(0);
            CsvHelper.WriteAsync(output, rows, options?.Invoke(), cancellationToken).GetAwaiter().GetResult();
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
