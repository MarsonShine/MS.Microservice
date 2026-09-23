using System.Security.Cryptography;
using System.Text;

namespace MS.Microservice.Core.Security.Cryptology;

public partial class CryptologyHelper
{
    public static class RsaCrypt
    {
        private const string EnvelopeNamespace = "msenc:";
        private const string OaepPrefix = "msenc:v1:rsa-oaep-sha256:";
        private const int MinimumModernKeyBits = 2048;
        private const int Sha256ByteLength = 32;

        /// <summary>
        /// Encrypts one OAEP-SHA256 block using a base64 DER SubjectPublicKeyInfo key of at least 2048 bits.
        /// The result includes a format prefix. For larger values, use a symmetric encryption protocol.
        /// </summary>
        public static string Encrypt(string resData, string publicKey, Encoding encoding)
        {
            ArgumentNullException.ThrowIfNull(resData);
            ArgumentNullException.ThrowIfNull(encoding);

            var key = Convert.FromBase64String(publicKey);
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(key, out var consumed);
            if (consumed != key.Length)
            {
                throw new CryptographicException("Unexpected data after the public key.");
            }

            EnsureModernKeySize(rsa);
            var plaintext = encoding.GetBytes(resData);
            try
            {
                var maximumLength = rsa.KeySize / 8 - 2 * Sha256ByteLength - 2;
                if (plaintext.Length > maximumLength)
                {
                    throw new CryptographicException("Plaintext exceeds one RSA-OAEP-SHA256 block.");
                }

                return OaepPrefix + Convert.ToBase64String(rsa.Encrypt(plaintext, RSAEncryptionPadding.OaepSHA256));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        /// <summary>
        /// Decrypts versioned OAEP-SHA256 ciphertext, or unversioned legacy PKCS#1 v1.5 ciphertext.
        /// New ciphertext is exactly one block; legacy ciphertext may contain concatenated blocks.
        /// </summary>
        public static string Decrypt(string rsaData, string privateKey, Encoding encoding)
        {
            ArgumentNullException.ThrowIfNull(rsaData);
            ArgumentNullException.ThrowIfNull(encoding);

            var isModern = rsaData.StartsWith(OaepPrefix, StringComparison.Ordinal);
            if (!isModern && rsaData.StartsWith(EnvelopeNamespace, StringComparison.Ordinal))
            {
                throw new CryptographicException("Unsupported encrypted data format.");
            }

            var encrypted = Convert.FromBase64String(isModern ? rsaData[OaepPrefix.Length..] : rsaData);
            var key = Convert.FromBase64String(privateKey);
            using var rsa = RSA.Create();
            int consumed;
            try
            {
                rsa.ImportPkcs8PrivateKey(key, out consumed);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }

            if (consumed != key.Length)
            {
                throw new CryptographicException("Unexpected data after the private key.");
            }

            if (!isModern)
            {
                return DecryptLegacy(encrypted, rsa, encoding);
            }

            EnsureModernKeySize(rsa);
            if (encrypted.Length != rsa.KeySize / 8)
            {
                throw new CryptographicException("Ciphertext must contain exactly one RSA block.");
            }

            var plaintext = rsa.Decrypt(encrypted, RSAEncryptionPadding.OaepSHA256);
            try
            {
                return encoding.GetString(plaintext);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        private static string DecryptLegacy(byte[] encrypted, RSA rsa, Encoding encoding)
        {
            var blockSize = rsa.KeySize / 8;
            if (encrypted.Length == 0 || encrypted.Length % blockSize != 0)
            {
                throw new CryptographicException("Ciphertext must contain complete RSA blocks.");
            }

            using var plaintext = new MemoryStream();
            try
            {
                for (var offset = 0; offset < encrypted.Length; offset += blockSize)
                {
                    var block = rsa.Decrypt(encrypted.AsSpan(offset, blockSize).ToArray(), RSAEncryptionPadding.Pkcs1);
                    try
                    {
                        plaintext.Write(block);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(block);
                    }
                }

                // Decode once so a multibyte character may span legacy encrypted blocks.
                return encoding.GetString(plaintext.GetBuffer(), 0, checked((int)plaintext.Length));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext.GetBuffer().AsSpan(0, checked((int)plaintext.Length)));
            }
        }

        private static void EnsureModernKeySize(RSA rsa)
        {
            if (rsa.KeySize < MinimumModernKeyBits)
            {
                throw new CryptographicException("RSA-OAEP-SHA256 requires a key of at least 2048 bits.");
            }
        }
    }
}
