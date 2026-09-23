using System.Security.Cryptography;
using System.Text;
using MS.Microservice.Core.Security.Cryptology;
using Xunit;

namespace MS.Microservice.Core.Tests.Cryptology;

public sealed class RsaCryptModernTests
{
    private const string Prefix = "msenc:v1:rsa-oaep-sha256:";

    [Theory]
    [InlineData("")]
    [InlineData("中文 payload")]
    public void NewCiphertextRoundTripsWithOaepSha256(string plaintext)
    {
        using var rsa = RSA.Create(2048);
        var publicKey = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
        var privateKey = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());

        var encrypted = CryptologyHelper.RsaCrypt.Encrypt(plaintext, publicKey, Encoding.UTF8);

        Assert.StartsWith(Prefix, encrypted);
        var block = Convert.FromBase64String(encrypted[Prefix.Length..]);
        Assert.Equal(256, block.Length);
        Assert.Equal(plaintext, Encoding.UTF8.GetString(rsa.Decrypt(block, RSAEncryptionPadding.OaepSHA256)));
        Assert.Equal(plaintext, CryptologyHelper.RsaCrypt.Decrypt(encrypted, privateKey, Encoding.UTF8));
    }

    [Fact]
    public void MaximumPlaintextLengthUsesOaepSha256Overhead()
    {
        using var rsa = RSA.Create(2048);
        var publicKey = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
        var privateKey = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());
        var maximumAscii = new string('a', 190);
        var maximumMultibyte = new string('中', 63);

        Assert.Equal(maximumAscii, CryptologyHelper.RsaCrypt.Decrypt(
            CryptologyHelper.RsaCrypt.Encrypt(maximumAscii, publicKey, Encoding.UTF8), privateKey, Encoding.UTF8));
        Assert.Equal(maximumMultibyte, CryptologyHelper.RsaCrypt.Decrypt(
            CryptologyHelper.RsaCrypt.Encrypt(maximumMultibyte, publicKey, Encoding.UTF8), privateKey, Encoding.UTF8));
        Assert.Throws<CryptographicException>(() =>
            CryptologyHelper.RsaCrypt.Encrypt(new string('a', 191), publicKey, Encoding.UTF8));
        Assert.Throws<CryptographicException>(() =>
            CryptologyHelper.RsaCrypt.Encrypt(new string('中', 64), publicKey, Encoding.UTF8));
    }

    [Fact]
    public void NewEncryptionRejects1024BitKey()
    {
        using var rsa = RSA.Create(1024);
        var publicKey = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());

        Assert.Throws<CryptographicException>(() =>
            CryptologyHelper.RsaCrypt.Encrypt("payload", publicKey, Encoding.UTF8));
    }

    [Fact]
    public void NewDecryptionRejects1024BitKey()
    {
        using var rsa = RSA.Create(1024);
        var encrypted = rsa.Encrypt(Encoding.UTF8.GetBytes("payload"), RSAEncryptionPadding.OaepSHA256);
        var privateKey = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());

        Assert.Throws<CryptographicException>(() =>
            CryptologyHelper.RsaCrypt.Decrypt(Prefix + Convert.ToBase64String(encrypted), privateKey, Encoding.UTF8));
    }

    [Fact]
    public void NewCiphertextRejectsDifferentPrivateKey()
    {
        using var sender = RSA.Create(2048);
        using var receiver = RSA.Create(2048);
        var encrypted = CryptologyHelper.RsaCrypt.Encrypt("payload",
            Convert.ToBase64String(sender.ExportSubjectPublicKeyInfo()), Encoding.UTF8);

        Assert.ThrowsAny<CryptographicException>(() => CryptologyHelper.RsaCrypt.Decrypt(encrypted,
            Convert.ToBase64String(receiver.ExportPkcs8PrivateKey()), Encoding.UTF8));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    public void NewCiphertextRejectsTampering(int offset)
    {
        using var rsa = RSA.Create(2048);
        var encrypted = CryptologyHelper.RsaCrypt.Encrypt("payload",
            Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()), Encoding.UTF8);
        var payload = Convert.FromBase64String(encrypted[Prefix.Length..]);
        payload[offset] ^= 1;

        Assert.ThrowsAny<CryptographicException>(() => CryptologyHelper.RsaCrypt.Decrypt(
            Prefix + Convert.ToBase64String(payload),
            Convert.ToBase64String(rsa.ExportPkcs8PrivateKey()), Encoding.UTF8));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(255)]
    [InlineData(257)]
    [InlineData(512)]
    public void NewCiphertextRequiresExactlyOneCompleteBlock(int length)
    {
        using var rsa = RSA.Create(2048);
        var privateKey = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());

        Assert.Throws<CryptographicException>(() => CryptologyHelper.RsaCrypt.Decrypt(
            Prefix + Convert.ToBase64String(new byte[length]), privateKey, Encoding.UTF8));
    }

    [Theory]
    [InlineData("msenc:v2:rsa-oaep-sha256:")]
    [InlineData("msenc:v1:rsa-pkcs1:")]
    [InlineData("msenc:v1:aes-256-gcm:")]
    public void UnknownEnvelopeCannotFallBackToLegacy(string otherPrefix)
    {
        using var rsa = RSA.Create(2048);
        var encrypted = CryptologyHelper.RsaCrypt.Encrypt("payload",
            Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo()), Encoding.UTF8);

        Assert.Throws<CryptographicException>(() => CryptologyHelper.RsaCrypt.Decrypt(
            otherPrefix + encrypted[Prefix.Length..],
            Convert.ToBase64String(rsa.ExportPkcs8PrivateKey()), Encoding.UTF8));
    }

    [Fact]
    public void MalformedModernBase64IsRejected()
    {
        using var rsa = RSA.Create(2048);

        Assert.Throws<FormatException>(() => CryptologyHelper.RsaCrypt.Decrypt(
            Prefix + "!", Convert.ToBase64String(rsa.ExportPkcs8PrivateKey()), Encoding.UTF8));
    }

    [Fact]
    public void UnversionedPkcs1CiphertextStillDecrypts()
    {
        using var rsa = RSA.Create(1024);
        var encrypted = Convert.ToBase64String(
            rsa.Encrypt(Encoding.UTF8.GetBytes("旧数据"), RSAEncryptionPadding.Pkcs1));

        Assert.Equal("旧数据", CryptologyHelper.RsaCrypt.Decrypt(
            encrypted, Convert.ToBase64String(rsa.ExportPkcs8PrivateKey()), Encoding.UTF8));
    }

    [Fact]
    public void UnversionedOaepCiphertextCannotFallBackToLegacyPadding()
    {
        using var rsa = RSA.Create(2048);
        var ciphertext = Convert.ToBase64String(
            rsa.Encrypt(Encoding.UTF8.GetBytes("payload"), RSAEncryptionPadding.OaepSHA256));

        Assert.ThrowsAny<CryptographicException>(() => CryptologyHelper.RsaCrypt.Decrypt(
            ciphertext, Convert.ToBase64String(rsa.ExportPkcs8PrivateKey()), Encoding.UTF8));
    }
}
