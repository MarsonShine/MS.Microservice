using System.Security.Cryptography;
using System.Text;

namespace MS.Microservice.Core.Security.Cryptology;

public partial class CryptologyHelper
{
    public static class RsaCrypt
    {
        /// <summary>Encrypts one PKCS#1 v1.5 block using a base64 DER SubjectPublicKeyInfo key.</summary>
        public static string Encrypt(string resData, string publicKey, Encoding encoding)
        {
            ArgumentNullException.ThrowIfNull(resData);
            ArgumentNullException.ThrowIfNull(encoding);
            var key = Convert.FromBase64String(publicKey);
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(key, out var consumed);
            if (consumed != key.Length) throw new CryptographicException("Unexpected data after the public key.");
            return Convert.ToBase64String(rsa.Encrypt(encoding.GetBytes(resData), RSAEncryptionPadding.Pkcs1));
        }

        /// <summary>Decrypts complete modulus-sized blocks using a base64 DER PKCS#8 private key.</summary>
        public static string Decrypt(string rsaData, string privateKey, Encoding encoding)
        {
            ArgumentNullException.ThrowIfNull(encoding);
            var encrypted = Convert.FromBase64String(rsaData);
            var key = Convert.FromBase64String(privateKey);
            using var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(key, out var consumed);
            if (consumed != key.Length) throw new CryptographicException("Unexpected data after the private key.");
            var blockSize = rsa.KeySize / 8;
            if (encrypted.Length % blockSize != 0) throw new CryptographicException("Ciphertext must contain complete RSA blocks.");
            using var plaintext = new MemoryStream();
            for (var offset = 0; offset < encrypted.Length; offset += blockSize)
            {
                var block = rsa.Decrypt(encrypted.AsSpan(offset, blockSize).ToArray(), RSAEncryptionPadding.Pkcs1);
                plaintext.Write(block);
                CryptographicOperations.ZeroMemory(block);
            }
            // Decode once so a multibyte character may span legacy encrypted blocks.
            return encoding.GetString(plaintext.GetBuffer(), 0, checked((int)plaintext.Length));
        }
    }
}
