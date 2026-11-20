namespace MS.Microservice.Core.Common;

public class TextNormalizer
{
    /// <summary>
    /// 规范化目录名称：中文标点→英文标点，去首尾空白，合并连续空白。
    /// null 或纯空白返回原值（通常 null，供调用方自行判断）。
    /// </summary>
    public static string? Normalize(string? title)
    {
        var text = title.AsSpan().Trim();
        if (text.IsEmpty)
            return title;

        // Printable ASCII needs normalization only when two spaces are adjacent.
        if (!text.ContainsAnyExceptInRange(' ', '~') && !text.Contains("  ", StringComparison.Ordinal))
            return text.Length == title!.Length ? title : text.ToString();

        int length = text.Length;
        bool changed = false;
        char previous = '\0';
        foreach (char character in text)
        {
            char normalized = NormalizeCharacter(character);
            if (normalized == ' ' && previous == ' ')
            {
                length--;
                changed = true;
            }
            else if (normalized != character)
            {
                changed = true;
            }
            previous = normalized;
        }

        if (!changed)
            return length == title!.Length ? title : text.ToString();

        return string.Create(length, title!, static (destination, original) =>
        {
            int written = 0;
            foreach (char character in original.AsSpan().Trim())
            {
                char normalized = NormalizeCharacter(character);
                if (normalized == ' ' && written > 0 && destination[written - 1] == ' ')
                    continue;
                destination[written++] = normalized;
            }
        });
    }

    private static char NormalizeCharacter(char character) => character switch
    {
        '，' or '、' => ',',
        '．' or '。' => '.',
        '（' => '(',
        '）' => ')',
        '：' => ':',
        '；' => ';',
        '？' => '?',
        '！' => '!',
        '“' or '”' => '"',
        '‘' or '’' => '\'',
        _ => char.IsWhiteSpace(character) ? ' ' : character
    };
}
