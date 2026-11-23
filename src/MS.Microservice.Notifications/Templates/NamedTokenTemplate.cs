using System.Net;
using System.Text;

namespace MS.Microservice.Notifications.Templates;

public static class NamedTokenTemplate
{
    /// <summary>Replaces {{Name}} tokens once. Replacement values are never interpreted as templates.</summary>
    public static string Render(string template, IReadOnlyDictionary<string, string?> values, bool htmlEncodeValues = false)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);
        StringBuilder? output = null;
        Dictionary<string, string?>.AlternateLookup<ReadOnlySpan<char>> lookup = default;
        bool useSpanKeys = false;
        var position = 0;
        while (position < template.Length)
        {
            var opening = template.IndexOf("{{", position, StringComparison.Ordinal);
            var literalEnd = opening >= 0 ? opening : template.Length;
            var literal = template.AsSpan(position, literalEnd - position);
            if (literal.Contains("}}", StringComparison.Ordinal))
            {
                throw new FormatException("A closing token delimiter has no opening delimiter.");
            }
            if (opening < 0)
            {
                output?.Append(literal);
                break;
            }

            if (output is null)
            {
                output = new StringBuilder(template.Length);
                // A derived dictionary can reimplement IReadOnlyDictionary.TryGetValue.
                useSpanKeys = values.GetType() == typeof(Dictionary<string, string?>)
                    && ((Dictionary<string, string?>)values).TryGetAlternateLookup(out lookup);
            }
            output.Append(literal);
            var closing = template.IndexOf("}}", opening + 2, StringComparison.Ordinal);
            if (closing < 0)
            {
                throw new FormatException("A token is not closed.");
            }
            var name = template.AsSpan(opening + 2, closing - opening - 2);
            if (!ValidName(name))
            {
                throw new FormatException("Use dot-separated ASCII identifiers for token names.");
            }
            bool found = useSpanKeys ? lookup.TryGetValue(name, out var value) : values.TryGetValue(name.ToString(), out value);
            if (!found)
            {
                throw new KeyNotFoundException($"Missing template value: {name}");
            }
            output.Append(htmlEncodeValues ? WebUtility.HtmlEncode(value) : value);
            position = closing + 2;
        }

        return output?.ToString() ?? template;
    }

    private static bool ValidName(ReadOnlySpan<char> name)
    {
        var atStart = true;
        foreach (var character in name)
        {
            if (character == '.')
            {
                if (atStart) return false;
                atStart = true;
            }
            else
            {
                if (!(char.IsAsciiLetter(character) || character == '_' || !atStart && char.IsAsciiDigit(character)))
                    return false;
                atStart = false;
            }
        }

        return !atStart;
    }
}
