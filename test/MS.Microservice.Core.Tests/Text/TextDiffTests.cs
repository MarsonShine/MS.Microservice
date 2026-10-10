using MS.Microservice.Core.Text;

namespace MS.Microservice.Core.Tests.Text;

public sealed class TextDiffTests
{
    [Fact]
    public void Compare_ReportsInsertionRemovalAndUnchangedLines()
    {
        var result = TextDiff.Compare("first\nold\nlast", "first\nnew\nlast");

        Assert.Equal(new[]
        {
            new TextDiffLine(TextDiffKind.Unchanged, "first\n"),
            new TextDiffLine(TextDiffKind.Removed, "old\n"),
            new TextDiffLine(TextDiffKind.Added, "new\n"),
            new TextDiffLine(TextDiffKind.Unchanged, "last")
        }, result);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("", "new\n")]
    [InlineData("old\r", "")]
    [InlineData("same", "same")]
    [InlineData("same", "same\n")]
    [InlineData("a\r\nb\r\n", "a\nb\n")]
    [InlineData("a\rb\nc\r\n", "a\rb\r\nc\n")]
    [InlineData("\r\n\n\r", "\n\r\n\r")]
    [InlineData("中文🙂\r\n第二行\n", "中文🙂\r\n新行é\n第二行\n")]
    [InlineData("  a\nb\t\n", "a\nb\n")]
    [InlineData("a\na\nb\na\n", "a\nb\na\na\n")]
    public void Compare_ReconstructsBothInputsExactly(string original, string updated)
    {
        AssertReconstruction(original, updated, TextDiff.Compare(original, updated));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public void Compare_UnchangedLinesRetainTerminatorsAndFinalLine(string newline)
    {
        var text = "one" + newline + newline + "two";

        var result = TextDiff.Compare(text, text);

        Assert.Equal(new[] { "one" + newline, newline, "two" }, result.Select(line => line.Text));
        Assert.All(result, line => Assert.Equal(TextDiffKind.Unchanged, line.Kind));
    }

    [Fact]
    public void Compare_SmallRepeatedLineCorpusReconstructsInputsAndMinimizesEdits()
    {
        var sequences = Enumerable.Range(0, 5)
            .SelectMany(length => Enumerable.Range(0, 1 << length)
                .Select(mask => Enumerable.Range(0, length)
                    .Select(bit => (mask & (1 << bit)) == 0 ? "a\n" : "b\n").ToArray()))
            .ToArray();
        foreach (var original in sequences)
        foreach (var updated in sequences)
        {
            var left = string.Concat(original);
            var right = string.Concat(updated);
            var result = TextDiff.Compare(left, right);

            AssertReconstruction(left, right, result);
            Assert.Equal(LongestCommonSubsequence(original, updated),
                result.Count(line => line.Kind == TextDiffKind.Unchanged));
        }
    }

    [Fact]
    public void Compare_WorkLimitAllowsBoundaryAndRejectsAboveIt()
    {
        var result = TextDiff.Compare("a\nb\n", "c\nd\n", maximumComparisonWork: 8);

        AssertReconstruction("a\nb\n", "c\nd\n", result);
        Assert.Throws<ArgumentException>(() => TextDiff.Compare("a\nb\n", "c\nd\n", maximumComparisonWork: 7));
        Assert.Throws<ArgumentOutOfRangeException>(() => TextDiff.Compare("", "", maximumComparisonWork: 0));
    }

    [Fact]
    public void Compare_LargeCommonEdgesDoNotSpendQuadraticWork()
    {
        var common = string.Concat(Enumerable.Range(0, 10_000).Select(value => value + "\n"));
        var original = common + "old\n" + common;
        var updated = common + "new\n" + common;

        var result = TextDiff.Compare(original, updated, maximumComparisonWork: 2);

        AssertReconstruction(original, updated, result);
        Assert.Equal(20_000, result.Count(line => line.Kind == TextDiffKind.Unchanged));
    }

    [Fact]
    public void Compare_NullInputsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => TextDiff.Compare(null!, ""));
        Assert.Throws<ArgumentNullException>(() => TextDiff.Compare("", null!));
    }

    private static void AssertReconstruction(string original, string updated, IReadOnlyList<TextDiffLine> result)
    {
        Assert.Equal(original, string.Concat(result.Where(line => line.Kind != TextDiffKind.Added).Select(line => line.Text)));
        Assert.Equal(updated, string.Concat(result.Where(line => line.Kind != TextDiffKind.Removed).Select(line => line.Text)));
    }

    private static int LongestCommonSubsequence(string[] left, string[] right)
    {
        var lengths = new int[left.Length + 1, right.Length + 1];
        for (var a = 0; a < left.Length; a++)
        for (var b = 0; b < right.Length; b++)
            lengths[a + 1, b + 1] = left[a] == right[b]
                ? lengths[a, b] + 1
                : Math.Max(lengths[a, b + 1], lengths[a + 1, b]);
        return lengths[left.Length, right.Length];
    }
}
