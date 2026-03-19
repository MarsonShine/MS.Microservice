using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace MS.Microservice.Core.Extension
{
    public static partial class StringExtensions
    {
        extension([NotNullWhen(false)] string? str)
        {
            public bool IsNullOrEmpty() => string.IsNullOrEmpty(str);
            public bool IsNullOrWhiteSpace() => string.IsNullOrWhiteSpace(str);
        }

        extension([NotNullWhen(true)] string? str)
        {
            public bool IsNotNullOrEmpty() => !string.IsNullOrEmpty(str);
            public bool IsNotNullOrWhiteSpace() => !string.IsNullOrWhiteSpace(str);
        }

        extension(string? str)
        {
            public byte[] ReadAsByte(Encoding encoding) => encoding.GetBytes(str!);
        }
    }
}
