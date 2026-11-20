using System.Text;
using MS.Microservice.Core.Security.Cryptology;
using Xunit;

namespace MS.Microservice.Core.Tests.Cryptology;

public sealed class HmacSha256Tests
{
    [Theory]
    [InlineData("", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")]
    [InlineData("订单🔐", "23862f7e75ed64a25d20c9e674d0979dcbbba98e3758c6f1dd118e9a809d74e8")]
    public void UnkeyedSha256ReturnsLowercaseHexOfUtf8(string input, string expected)
        => Assert.Equal(expected, CryptologyHelper.SHA256(input));

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
