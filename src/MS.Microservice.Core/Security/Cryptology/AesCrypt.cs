using System.Security.Cryptography;
using System.Text;

namespace MS.Microservice.Core.Security.Cryptology;

public partial class CryptologyHelper
{
    public static class AesCrypt
    {
        private const int KeySize = 32;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const string Prefix = "msenc:v1:aes-256-gcm:";

        private static readonly byte[] AssociatedData = Encoding.ASCII.GetBytes(Prefix);
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);

        /// <summary>
        /// 使用调用方提供的 32 字节密钥加密 UTF-8 文本，返回带格式版本的密文。
        /// 同一密钥必须由调用方安全保存；每次加密会生成新的随机 nonce。
        /// </summary>
        public static string Encrypt(ReadOnlySpan<byte> key, string content)
        {
            ValidateKey(key);
            ArgumentNullException.ThrowIfNull(content);

            var plaintext = StrictUtf8.GetBytes(content);
            try
            {
                var payload = new byte[checked(NonceSize + TagSize + plaintext.Length)];
                var nonce = payload.AsSpan(0, NonceSize);
                var tag = payload.AsSpan(NonceSize, TagSize);
                var ciphertext = payload.AsSpan(NonceSize + TagSize);
                RandomNumberGenerator.Fill(nonce);

                using var aes = new AesGcm(key, TagSize);
                aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData);
                return Prefix + Convert.ToBase64String(payload);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        /// <summary>
        /// 仅接受本类型产生的 v1 AES-256-GCM 密文；旧 AES-ECB 和 3DES 密文不受支持。
        /// 密文、认证标签或关联的格式信息被修改时，解密失败。
        /// </summary>
        public static string Decrypt(ReadOnlySpan<byte> key, string content)
        {
            ValidateKey(key);
            ArgumentNullException.ThrowIfNull(content);

            if (!content.StartsWith(Prefix, StringComparison.Ordinal))
            {
                throw new CryptographicException("Unsupported encrypted data format.");
            }

            var payload = Convert.FromBase64String(content[Prefix.Length..]);
            if (payload.Length < NonceSize + TagSize)
            {
                throw new CryptographicException("Encrypted data is incomplete.");
            }

            var plaintext = new byte[payload.Length - NonceSize - TagSize];
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(
                    payload.AsSpan(0, NonceSize),
                    payload.AsSpan(NonceSize + TagSize),
                    payload.AsSpan(NonceSize, TagSize),
                    plaintext,
                    AssociatedData);
                return StrictUtf8.GetString(plaintext);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }

        private static void ValidateKey(ReadOnlySpan<byte> key)
        {
            if (key.Length != KeySize)
            {
                throw new ArgumentException("AES-256-GCM requires a 32-byte key.", nameof(key));
            }
        }
    }
}
