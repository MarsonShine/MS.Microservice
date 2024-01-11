using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace MS.Microservice.Core.Extension
{
    public static class StringExtensions
    {
        public static bool IsNullOrEmpty([NotNullWhen(false)] this string? str)
        {
            return string.IsNullOrEmpty(str);
        }

        public static bool IsNotNullOrEmpty([NotNullWhen(true)]this string? str)
        {
            return !IsNullOrEmpty(str);
        }

        public static bool IsNullOrWhiteSpace([NotNullWhen(false)] this string? str)
        {
            return string.IsNullOrWhiteSpace(str);
        }

        public static bool IsNotNullOrWhiteSpace([NotNullWhen(true)] this string? str)
        {
            return !IsNullOrWhiteSpace(str);
        }

        public static byte[] ReadAsByte(this string str,Encoding encoding)
        {
            return encoding.GetBytes(str);
        }

    }
}
