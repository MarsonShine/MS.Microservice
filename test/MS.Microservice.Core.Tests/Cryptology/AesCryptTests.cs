using System.Security.Cryptography;
using MS.Microservice.Core.Security.Cryptology;
using Xunit;

namespace MS.Microservice.Core.Tests.Cryptology;

public sealed class AesCryptTests
{
    private const string Prefix = "msenc:v1:aes-256-gcm:";

    [Theory]
    [InlineData("")]
    [InlineData("payload")]
    [InlineData("中文🙂\0正文")]
    public void RoundTripsText(string plaintext)
    {
        var key = RandomNumberGenerator.GetBytes(32);

        var encrypted = CryptologyHelper.AesCrypt.Encrypt(key, plaintext);

        Assert.StartsWith(Prefix, encrypted);
        Assert.Equal(plaintext, CryptologyHelper.AesCrypt.Decrypt(key, encrypted));
    }

    [Fact]
    public void RoundTripsLargeText()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var plaintext = new string('数', 16_384);

        Assert.Equal(plaintext,
            CryptologyHelper.AesCrypt.Decrypt(key, CryptologyHelper.AesCrypt.Encrypt(key, plaintext)));
    }

    [Fact]
    public void RepeatedEncryptionUsesDifferentNonce()
    {
        var key = RandomNumberGenerator.GetBytes(32);

        var first = CryptologyHelper.AesCrypt.Encrypt(key, "same text");
        var second = CryptologyHelper.AesCrypt.Encrypt(key, "same text");

        Assert.NotEqual(first, second);
        Assert.Equal("same text", CryptologyHelper.AesCrypt.Decrypt(key, first));
        Assert.Equal("same text", CryptologyHelper.AesCrypt.Decrypt(key, second));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    public void RejectsKeysThatAreNot256Bits(int keyLength)
    {
        var key = new byte[keyLength];
        var validCiphertext = CryptologyHelper.AesCrypt.Encrypt(RandomNumberGenerator.GetBytes(32), "payload");

        Assert.Throws<ArgumentException>(() => CryptologyHelper.AesCrypt.Encrypt(key, "payload"));
        Assert.Throws<ArgumentException>(() => CryptologyHelper.AesCrypt.Decrypt(key, validCiphertext));
    }

    [Fact]
    public void WrongKeyCannotDecrypt()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var otherKey = RandomNumberGenerator.GetBytes(32);
        var encrypted = CryptologyHelper.AesCrypt.Encrypt(key, "payload");

        Assert.ThrowsAny<CryptographicException>(() => CryptologyHelper.AesCrypt.Decrypt(otherKey, encrypted));
    }

    [Theory]
    [InlineData(0)]  // nonce
    [InlineData(12)] // authentication tag
    [InlineData(28)] // ciphertext
    public void RejectsModifiedCiphertextParts(int offset)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var encrypted = CryptologyHelper.AesCrypt.Encrypt(key, "payload");
        var payload = Convert.FromBase64String(encrypted[Prefix.Length..]);
        payload[offset] ^= 1;

        var modified = Prefix + Convert.ToBase64String(payload);
        Assert.ThrowsAny<CryptographicException>(() => CryptologyHelper.AesCrypt.Decrypt(key, modified));
    }

    [Theory]
    [InlineData("msenc:v2:aes-256-gcm:")]
    [InlineData("msenc:v1:aes-128-gcm:")]
    [InlineData("")]
    public void RejectsUnknownFormat(string replacementPrefix)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var encrypted = CryptologyHelper.AesCrypt.Encrypt(key, "payload");

        Assert.Throws<CryptographicException>(() =>
            CryptologyHelper.AesCrypt.Decrypt(key, replacementPrefix + encrypted[Prefix.Length..]));
    }

    [Theory]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA==")]
    [InlineData("AAAAAAAAAAA=")]
    public void RejectsUnversionedLegacyCiphertext(string ciphertext)
    {
        var key = RandomNumberGenerator.GetBytes(32);

        Assert.Throws<CryptographicException>(() => CryptologyHelper.AesCrypt.Decrypt(key, ciphertext));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(27)]
    public void RejectsTruncatedPayload(int payloadLength)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var ciphertext = Prefix + Convert.ToBase64String(new byte[payloadLength]);

        Assert.Throws<CryptographicException>(() => CryptologyHelper.AesCrypt.Decrypt(key, ciphertext));
    }

    [Fact]
    public void RejectsMalformedBase64()
    {
        var key = RandomNumberGenerator.GetBytes(32);

        Assert.Throws<FormatException>(() => CryptologyHelper.AesCrypt.Decrypt(key, Prefix + "!"));
    }

    [Fact]
    public void RejectsInvalidUtf8Input()
    {
        var key = RandomNumberGenerator.GetBytes(32);

        Assert.Throws<System.Text.EncoderFallbackException>(() =>
            CryptologyHelper.AesCrypt.Encrypt(key, "\ud800"));
    }
}
