namespace MS.Microservice.Core.Text;

public enum TextDiffKind
{
    Unchanged,
    Added,
    Removed
}

/// <summary>A line including its original CR, LF or CRLF terminator, if present.</summary>
public sealed record TextDiffLine(TextDiffKind Kind, string Text);

public static class TextDiff
{
    /// <summary>Compares exact lines using a linear-memory longest common subsequence.</summary>
    /// <param name="maximumComparisonWork">Upper bound on LCS cell comparisons after common edges are removed.</param>
    public static IReadOnlyList<TextDiffLine> Compare(
        string original,
        string updated,
        long maximumComparisonWork = 4_000_000)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(updated);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumComparisonWork);
        var left = SplitLines(original);
        var right = SplitLines(updated);
        var prefix = 0;
        while (prefix < left.Length && prefix < right.Length && left[prefix] == right[prefix])
            prefix++;

        var suffix = 0;
        while (suffix < left.Length - prefix && suffix < right.Length - prefix
            && left[^(suffix + 1)] == right[^(suffix + 1)])
            suffix++;

        var leftCount = left.Length - prefix - suffix;
        var rightCount = right.Length - prefix - suffix;
        if (2L * leftCount * rightCount > maximumComparisonWork)
            throw new ArgumentException("The line comparison exceeds the configured work limit.", nameof(maximumComparisonWork));

        var result = new List<TextDiffLine>();
        var forward = new int[rightCount + 1];
        var backward = new int[rightCount + 1];
        for (var index = 0; index < prefix; index++)
            result.Add(new(TextDiffKind.Unchanged, left[index]));

        Match(prefix, leftCount, prefix, rightCount);
        for (var index = left.Length - suffix; index < left.Length; index++)
            result.Add(new(TextDiffKind.Unchanged, left[index]));
        return result;

        void Match(int aStart, int aCount, int bStart, int bCount)
        {
            if (aCount == 0)
            {
                for (var index = bStart; index < bStart + bCount; index++)
                    result.Add(new(TextDiffKind.Added, right[index]));
                return;
            }

            if (bCount == 0)
            {
                for (var index = aStart; index < aStart + aCount; index++)
                    result.Add(new(TextDiffKind.Removed, left[index]));
                return;
            }

            if (aCount == 1)
            {
                var match = Array.IndexOf(right, left[aStart], bStart, bCount);
                if (match < 0)
                    result.Add(new(TextDiffKind.Removed, left[aStart]));
                for (var index = bStart; index < bStart + bCount; index++)
                    result.Add(new(index == match ? TextDiffKind.Unchanged : TextDiffKind.Added, right[index]));
                return;
            }

            var half = aCount / 2;
            CalculateLengths(aStart, half, bStart, bCount, forward, reverse: false);
            CalculateLengths(aStart + half, aCount - half, bStart, bCount, backward, reverse: true);
            var split = 0;
            var best = -1;
            for (var index = 0; index <= bCount; index++)
            {
                var length = forward[index] + backward[bCount - index];
                if (length > best)
                {
                    best = length;
                    split = index;
                }
            }

            // Only the split survives recursion; both vectors are reused by each child.
            Match(aStart, half, bStart, split);
            Match(aStart + half, aCount - half, bStart + split, bCount - split);
        }

        void CalculateLengths(int aStart, int aCount, int bStart, int bCount, int[] row, bool reverse)
        {
            row.AsSpan(0, bCount + 1).Clear();
            for (var a = 0; a < aCount; a++)
            {
                var previous = 0;
                var line = left[reverse ? aStart + aCount - a - 1 : aStart + a];
                for (var b = 0; b < bCount; b++)
                {
                    var saved = row[b + 1];
                    row[b + 1] = line == right[reverse ? bStart + bCount - b - 1 : bStart + b]
                        ? previous + 1
                        : Math.Max(row[b], row[b + 1]);
                    previous = saved;
                }
            }
        }
    }

    private static string[] SplitLines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is not ('\r' or '\n'))
                continue;
            if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                index++;
            lines.Add(text[start..(index + 1)]);
            start = index + 1;
        }

        if (start < text.Length)
            lines.Add(text[start..]);
        return lines.ToArray();
    }
}
