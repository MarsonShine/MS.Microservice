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
        public void DesCrypt_ShouldRoundTrip()
        {
            const string plainText = "hello world";
            const string key = "123456789012345678901234";

            string encrypted = CryptologyHelper.DesCrypt.Encrypt(plainText, key);
            string decrypted = CryptologyHelper.DesCrypt.Decrypt(encrypted, key);

            Assert.Equal(plainText, decrypted);
        }

        [Fact]
        public void DesCrypt_ShouldMatchManualTripleDesResult()
        {
            const string plainText = "hello world";
            const string key = "123456789012345678901234";

            string encrypted = CryptologyHelper.DesCrypt.Encrypt(plainText, key);
            string manual = EncryptWithTripleDes(plainText, key, "12345678");

            Assert.Equal(encrypted, manual);
        }

        [Fact]
        public void AesCrypt_ShouldRoundTrip_WithAutoHandledShortKey()
        {
            const string key = "short-key";
            const string content = "payload";

            string encrypted = CryptologyHelper.AesCrypt.Encrypt(key, content);
            string decrypted = CryptologyHelper.AesCrypt.Decrypt(key, encrypted);

            Assert.Equal(content, decrypted);
        }

        [Fact]
        public void AesCrypt_ShouldRoundTrip_WithExactLengthKey()
        {
            const string key = "1234567890ABCDEF";
            const string content = "payload";

            string encrypted = CryptologyHelper.AesCrypt.Encrypt(key, content, autoHandle: false);
            string decrypted = CryptologyHelper.AesCrypt.Decrypt(key, encrypted, autoHandle: false);

            Assert.Equal(content, decrypted);
        }

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

            Assert.Throws<ArgumentNullException>(() =>
                CryptologyHelper.RsaCrypt.Encrypt("hello", invalidKey, Encoding.UTF8));
        }

        private static string EncryptWithTripleDes(string plainText, string key, string iv)
        {
            using var tripleDes = TripleDES.Create();
            tripleDes.Key = Encoding.UTF8.GetBytes(key);
            tripleDes.IV = Encoding.UTF8.GetBytes(iv);
            tripleDes.Mode = CipherMode.CBC;
            tripleDes.Padding = PaddingMode.PKCS7;

            using var encryptor = tripleDes.CreateEncryptor();
            byte[] inputBuffer = Encoding.UTF8.GetBytes(plainText);
            byte[] encryptedBuffer = encryptor.TransformFinalBlock(inputBuffer, 0, inputBuffer.Length);
            return Convert.ToBase64String(encryptedBuffer);
        }
    }
}
