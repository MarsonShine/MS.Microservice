using System.Security.Cryptography;
using System.Text;
using MS.Microservice.Core.Security.Cryptology;
using Xunit;

namespace MS.Microservice.Core.Tests.Cryptology;

public sealed class RsaKeyFormatTests
{
    [Fact]
    public void ShortCrtIntegerEncodingImportsWithoutLosingParameterWidth()
    {
        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(ShortRsaParameterFixture.PublicKey), out _);
        var encrypted = Convert.ToBase64String(rsa.Encrypt(Encoding.UTF8.GetBytes("fixture"), RSAEncryptionPadding.Pkcs1));
        Assert.Equal("fixture", CryptologyHelper.RsaCrypt.Decrypt(encrypted, ShortRsaParameterFixture.PrivateKey, Encoding.UTF8));
    }

    [Theory]
    [InlineData(2048)]
    [InlineData(3072)]
    public void BlockSizeComesFromImportedKey(int bits)
    {
        using var rsa = RSA.Create(bits);
        var publicKey = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
        var privateKey = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());
        var encrypted = CryptologyHelper.RsaCrypt.Encrypt("中文 payload", publicKey, Encoding.UTF8);
        const string prefix = "msenc:v1:rsa-oaep-sha256:";
        Assert.StartsWith(prefix, encrypted);
        Assert.Equal(bits / 8, Convert.FromBase64String(encrypted[prefix.Length..]).Length);
        Assert.Equal("中文 payload", CryptologyHelper.RsaCrypt.Decrypt(encrypted, privateKey, Encoding.UTF8));
    }

    [Fact]
    public void LegacyConcatenatedBlocksPreserveSplitUtf8Characters()
    {
        using var rsa = RSA.Create(2048);
        var data = Encoding.UTF8.GetBytes("中间字符");
        var encrypted = rsa.Encrypt(data[..2], RSAEncryptionPadding.Pkcs1)
            .Concat(rsa.Encrypt(data[2..], RSAEncryptionPadding.Pkcs1)).ToArray();
        Assert.Equal("中间字符", CryptologyHelper.RsaCrypt.Decrypt(Convert.ToBase64String(encrypted),
            Convert.ToBase64String(rsa.ExportPkcs8PrivateKey()), Encoding.UTF8));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(129)]
    public void TruncatedCiphertextCannotBeSilentlyIgnored(int length)
        => Assert.Throws<CryptographicException>(() => CryptologyHelper.RsaCrypt.Decrypt(
            Convert.ToBase64String(new byte[length]), ShortRsaParameterFixture.PrivateKey, Encoding.UTF8));

    [Fact]
    public void TrailingDerDataIsRejected()
    {
        var key = Convert.FromBase64String(ShortRsaParameterFixture.PublicKey).Concat(new byte[] { 0 }).ToArray();
        Assert.Throws<CryptographicException>(() => CryptologyHelper.RsaCrypt.Encrypt("data", Convert.ToBase64String(key), Encoding.UTF8));
    }
}
