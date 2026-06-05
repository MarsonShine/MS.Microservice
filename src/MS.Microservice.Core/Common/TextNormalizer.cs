using MS.Microservice.Core.Extension;
using System.Text.RegularExpressions;

namespace MS.Microservice.Core.Common;

public class TextNormalizer
{
    /// <summary>
    /// 规范化目录名称：中文标点→英文标点，去首尾空白，合并连续空白。
    /// null 或纯空白返回原值（通常 null，供调用方自行判断）。
    /// </summary>
    public static string? Normalize(string? title)
    {
        if (title.IsNullOrWhiteSpace())
            return title;

        string normalized = title!;

        // 中文标点 → 英文标点
        normalized = normalized
            .Replace('\uFF0C', ',')  // 中文逗号 ，
            .Replace('\u3001', ',')  // 顿号 、
            .Replace('\uFF0E', '.')  // 中文句号 。
            .Replace('\u3002', '.')  // 中文句号 。
            .Replace('\uFF08', '(')  // 中文左括号 （
            .Replace('\uFF09', ')')  // 中文右括号 ）
            .Replace('\uFF1A', ':')  // 中文冒号 ：
            .Replace('\uFF1B', ';')  // 中文分号 ；
            .Replace('\uFF1F', '?')  // 中文问号 ？
            .Replace('\uFF01', '!')  // 中文感叹号 ！
            .Replace('\u201C', '"')  // 中文左双引号 "
            .Replace('\u201D', '"')  // 中文右双引号 "
            .Replace('\u2018', '\'') // 中文左单引号 '
            .Replace('\u2019', '\'') // 中文右单引号 '
            .Replace('\u3000', ' ')  // 全角空格
            .Replace('\u00A0', ' '); // 不换行空格

        // 合并连续空白，去首尾空白
        normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

        return normalized;
    }
}
