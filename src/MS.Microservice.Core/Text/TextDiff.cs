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
        var leftLines = FindLines(original);
        var rightLines = ReferenceEquals(original, updated) ? leftLines : FindLines(updated);
        var prefix = 0;
        while (prefix < leftLines.Count && prefix < rightLines.Count
            && original.AsSpan(leftLines[prefix]).SequenceEqual(updated.AsSpan(rightLines[prefix])))
            prefix++;

        var suffix = 0;
        while (suffix < leftLines.Count - prefix && suffix < rightLines.Count - prefix
            && original.AsSpan(leftLines[^(suffix + 1)]).SequenceEqual(updated.AsSpan(rightLines[^(suffix + 1)])))
            suffix++;

        var leftCount = leftLines.Count - prefix - suffix;
        var rightCount = rightLines.Count - prefix - suffix;
        if (2L * leftCount * rightCount > maximumComparisonWork)
            throw new ArgumentException("The line comparison exceeds the configured work limit.", nameof(maximumComparisonWork));

        var result = new List<TextDiffLine>(Math.Max(leftLines.Count, rightLines.Count));
        for (var index = 0; index < prefix; index++)
            result.Add(new(TextDiffKind.Unchanged, original[leftLines[index]]));

        if (leftCount == 0 || rightCount == 0)
        {
            for (var i = 0; i < leftCount; i++)
                result.Add(new(TextDiffKind.Removed, original[leftLines[prefix + i]]));
            for (var i = 0; i < rightCount; i++)
                result.Add(new(TextDiffKind.Added, updated[rightLines[prefix + i]]));
        }
        else
        {
            var left = new string[leftCount];
            var right = new string[rightCount];
            for (var i = 0; i < left.Length; i++) left[i] = original[leftLines[prefix + i]];
            for (var i = 0; i < right.Length; i++) right[i] = updated[rightLines[prefix + i]];
            AppendChanges(left, right, result);
        }
        for (var index = leftLines.Count - suffix; index < leftLines.Count; index++)
            result.Add(new(TextDiffKind.Unchanged, original[leftLines[index]]));
        return result;
    }

    private static void AppendChanges(string[] left, string[] right, List<TextDiffLine> result)
    {
        var forward = new int[right.Length + 1];
        var backward = new int[right.Length + 1];
        Match(0, left.Length, 0, right.Length);

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

    private static List<Range> FindLines(string text)
    {
        var lines = new List<Range>();
        var start = 0;
        while (start < text.Length)
        {
            var offset = text.AsSpan(start).IndexOfAny('\r', '\n');
            if (offset < 0)
            {
                lines.Add(start..text.Length);
                break;
            }
            var end = start + offset + 1;
            if (text[end - 1] == '\r' && end < text.Length && text[end] == '\n') end++;
            lines.Add(start..end);
            start = end;
        }
        return lines;
    }
}
