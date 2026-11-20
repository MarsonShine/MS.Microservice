using MS.Microservice.Core.Common;
using static MS.Microservice.Core.Functional.Data.LinkedList.LinkedList;

internal static class CollectionCases
{
    internal static void Run()
    {
        MeasureList(8, 20_000);
        MeasureList(1024, 8);

        const string unchanged = "readme.md";
        const string mixed = "  第一章：数据，\t\u00a0“输入”  和（输出）。  ";
        Bench.Measure("Normalize unchanged", 20_000, () => TextNormalizer.Normalize(unchanged), text => text == unchanged);
        Bench.Measure("Normalize mixed", 5_000, () => TextNormalizer.Normalize(mixed), text => text == "第一章:数据, \"输入\" 和(输出).");
        var longTitle = new string('a', 4096);
        Bench.Measure("Normalize 4096 unchanged", 256, () => TextNormalizer.Normalize(longTitle), text => text == longTitle);
        var longChinese = new string('文', 4096);
        Bench.Measure("Normalize 4096 CJK unchanged", 256, () => TextNormalizer.Normalize(longChinese), text => text == longChinese);
    }

    private static void MeasureList(int count, int iterations)
    {
        var list = List(Enumerable.Range(0, count).ToArray());
        int expected = count * (count - 1) / 2;
        Bench.Measure($"AsEnumerable {count}", iterations, () =>
        {
            int sum = 0;
            foreach (int value in list.AsEnumerable()) sum += value;
            return sum;
        }, sum => sum == expected);

        int total = 0;
        Action<int> add = value => total += value;
        Bench.Measure($"ForEach {count}", count == 8 ? iterations : 256, () =>
        {
            total = 0;
            list.ForEach(add);
            return total;
        }, sum => sum == expected);
    }
}
