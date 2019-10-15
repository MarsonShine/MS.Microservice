using System;
using System.Collections.Generic;
using System.Text;

namespace MS.Extensions
{
    public static class StringExtension
    {
        public static bool IsNullOrEmpty(this string str)
        {
            return string.IsNullOrEmpty(str);
        }
        public static bool IsNotNullOrEmpty(this string str)
        {
            return !string.IsNullOrEmpty(str);
        }
        public static bool IsNullOrWhiteSpace(this string str)
        {
            return string.IsNullOrWhiteSpace(str);
        }
        public static bool IsNotNullOrWhiteSpace(this string str)
        {
            return !string.IsNullOrWhiteSpace(str);
        }
        public static short ToShort(this string str)
        {
            if (short.TryParse(str, out short result))
                return result;
            return default;
        }
        public static double ToDouble(this string str)
        {
            if (double.TryParse(str, out double result))
                return result;
            return default;
        }
        public static float ToFloat(this string str)
        {
            if (float.TryParse(str, out float result))
                return result;
            return default;
        }
        public static long ToLong(this string str)
        {
            if (long.TryParse(str, out long result))
                return result;
            return default;
        }
    }
}
