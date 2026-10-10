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
        var output = new StringBuilder(template.Length);
        var position = 0;
        while (position < template.Length)
        {
            var opening = template.IndexOf("{{", position, StringComparison.Ordinal);
            var strayClosing = template.IndexOf("}}", position, StringComparison.Ordinal);
            if (strayClosing >= 0 && (opening < 0 || strayClosing < opening))
                throw new FormatException("A closing token delimiter has no opening delimiter.");
            if (opening < 0)
            {
                output.Append(template, position, template.Length - position);
                break;
            }

            output.Append(template, position, opening - position);
            var closing = template.IndexOf("}}", opening + 2, StringComparison.Ordinal);
            if (closing < 0) throw new FormatException("A token is not closed.");
            var name = template[(opening + 2)..closing];
            if (!ValidName(name)) throw new FormatException("Use dot-separated ASCII identifiers for token names.");
            if (!values.TryGetValue(name, out var value)) throw new KeyNotFoundException($"Missing template value: {name}");
            output.Append(htmlEncodeValues ? WebUtility.HtmlEncode(value) : value);
            position = closing + 2;
        }

        return output.ToString();
    }

    private static bool ValidName(string name)
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
