using System.Text;
using MS.Microservice.Core.Security.Cryptology;
using Xunit;

namespace MS.Microservice.Core.Tests.Cryptology;

public sealed class HmacSha256Tests
{
    [Fact]
    public void ExplicitKeyPreservesStandardHmacSha256Result()
    {
        const string message = "The quick brown fox jumps over the lazy dog";
        const string expected = "f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8";

        Assert.Equal(expected, CryptologyHelper.HmacSha256(message, "key"));
        Assert.Equal(expected,
            CryptologyHelper.HmacSha256(Encoding.UTF8.GetBytes(message), "key", Encoding.UTF8));
    }

    [Fact]
    public void EmptyStringStillReturnsEmptyHashText()
        => Assert.Equal(string.Empty, CryptologyHelper.HmacSha256(string.Empty, "key"));
}
