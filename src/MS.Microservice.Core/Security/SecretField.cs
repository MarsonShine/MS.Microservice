
using System;
using System.Text;

namespace MS.Microservice.Core.Security
{
    /// <summary>
    /// 字段脱敏
    /// </summary>
    public class SecretField
    {
        public static string Phone(string ph)
        {
            return ph.Length > 9 ? ph.Remove(3, 4).Insert(3, "****") : ph;
        }


        /// <summary>
        /// 隐藏邮件详情
        /// </summary>
        /// <param name="email">邮件地址</param>
        /// <param name="left">邮件头保留字符个数，默认值设置为3</param>
        /// <returns></returns>
        public static string HideEmailDetails(string email, int left = 3)
        {
            if (String.IsNullOrEmpty(email))
            {
                return "";
            }
            if (System.Text.RegularExpressions.Regex.IsMatch(email, @"\w+([-+.]\w+)*@\w+([-.]\w+)*\.\w+([-.]\w+)*"))//如果是邮件地址
            {
                int suffixLen = email.Length - email.LastIndexOf('@');
                return HideSensitiveInfo(email, left, suffixLen, false);
            }
            else
            {
                return HideSensitiveInfo(email);
            }
        }


        /// <summary>
        /// 隐藏敏感信息
        /// </summary>
        /// <param name="info">信息实体</param>
        /// <param name="left">左边保留的字符数</param>
        /// <param name="right">右边保留的字符数</param>
        /// <param name="basedOnLeft">当长度异常时，是否显示左边 </param>
        /// <returns></returns>
        public static string HideSensitiveInfo(string info, int left, int right, bool basedOnLeft = true)
        {
            if (String.IsNullOrEmpty(info))
            {
                return "";
            }
            StringBuilder sbText = new StringBuilder();
            int hiddenCharCount = info.Length - left - right;
            if (hiddenCharCount > 0)
            {
                string prefix = info[..left], suffix = info[^right..];
                sbText.Append(prefix);
                for (int i = 0; i < hiddenCharCount; i++)
                {
                    sbText.Append('*');
                }
                sbText.Append(suffix);
            }
            else
            {
                if (basedOnLeft)
                {
                    if (info.Length > left && left > 0)
                    {
                        sbText.Append(info[..left] + "****");
                    }
                    else
                    {
                        sbText.Append(info[..1] + "****");
                    }
                }
                else
                {
                    if (info.Length > right && right > 0)
                    {
                        sbText.Append("****" + info[^right..]);
                    }
                    else
                    {
                        sbText.Append("****" + info[^1..]);
                    }
                }
            }
            return sbText.ToString();
        }

        /// <summary>
        /// 隐藏敏感信息
        /// </summary>
        /// <param name="info">信息</param>
        /// <param name="sublen">信息总长与左子串（或右子串）的比例</param>
        /// <param name="basedOnLeft">当长度异常时，是否显示左边，默认true，默认显示左边</param>
        /// <code>true</code>显示左边，<code>false</code>显示右边
        /// <returns></returns>
        public static string HideSensitiveInfo(string info, int sublen = 3, bool basedOnLeft = true)
        {
            if (String.IsNullOrEmpty(info))
            {
                return "";
            }
            if (sublen <= 1)
            {
                sublen = 3;
            }
            int subLength = info.Length / sublen;
            if (subLength > 0 && info.Length > (subLength * 2))
            {
                string prefix = info[..subLength], suffix = info[^subLength..];
                return prefix + "****" + suffix;
            }
            else
            {
                if (basedOnLeft)
                {
                    string prefix = subLength > 0 ? info[..subLength] : info[..1];
                    return prefix + "****";
                }
                else
                {
                    string suffix = subLength > 0 ? info[^subLength..] : info[^1..];
                    return "****" + suffix;
                }
            }
        }
    }
}
