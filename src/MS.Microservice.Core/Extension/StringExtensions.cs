using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace MS.Microservice.Core.Extension
{
    public static partial class StringExtensions
    {
        extension(string? str)
        {
            public bool IsNullOrEmpty() => string.IsNullOrEmpty(str);
            public bool IsNotNullOrEmpty() => !string.IsNullOrEmpty(str);
            public bool IsNullOrWhiteSpace() => string.IsNullOrWhiteSpace(str);
            public bool IsNotNullOrWhiteSpace() => !string.IsNullOrWhiteSpace(str);
            public byte[] ReadAsByte(Encoding encoding) => encoding.GetBytes(str!);
        }
    }
}
