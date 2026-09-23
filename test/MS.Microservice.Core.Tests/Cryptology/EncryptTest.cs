using System;
using System.Security.Cryptography;
using System.Text;
using MS.Microservice.Core.Security.Cryptology;
using Xunit;

namespace MS.Microservice.Core.Tests.Cryptology
{
    public class EncryptTest
    {
        [Fact]
        public void RsaCrypt_ShouldRoundTrip_WithPkcs8Keys()
        {
            using var rsa = RSA.Create(1024);
            string publicKey = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
            string privateKey = Convert.ToBase64String(rsa.ExportPkcs8PrivateKey());
            const string content = "hello world";

            string encrypted = CryptologyHelper.RsaCrypt.Encrypt(content, publicKey, Encoding.UTF8);
            string decrypted = CryptologyHelper.RsaCrypt.Decrypt(encrypted, privateKey, Encoding.UTF8);

            Assert.Equal(content, decrypted);
        }

        [Fact]
        public void RsaCrypt_ShouldThrow_WhenPublicKeyIsNotBase64()
        {
            Assert.Throws<FormatException>(() =>
                CryptologyHelper.RsaCrypt.Encrypt("hello", "not-base64", Encoding.UTF8));
        }

        [Fact]
        public void RsaCrypt_ShouldThrow_WhenDecodedPublicKeyIsInvalid()
        {
            string invalidKey = Convert.ToBase64String([1, 2, 3, 4]);

            Assert.Throws<CryptographicException>(() =>
                CryptologyHelper.RsaCrypt.Encrypt("hello", invalidKey, Encoding.UTF8));
        }

    }
}
