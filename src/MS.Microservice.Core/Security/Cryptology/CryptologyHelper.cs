using System.Security.Cryptography;
using System.Text;

namespace MS.Microservice.Core.Security.Cryptology
{
    public partial class CryptologyHelper
    {
        #region HMAC-SHA256 和 SHA-256 摘要

        /// <summary>
        /// 使用调用方提供的密钥计算 HMAC-SHA256。
        /// </summary>
        /// <param name="value">值</param>
        /// <param name="key">密钥</param>
        public static string HmacSha256(string value, string key)
        {
            return HmacSha256(value, key, Encoding.UTF8);
        }

        /// <summary>
        /// 使用调用方提供的密钥计算 HMAC-SHA256。
        /// </summary>
        /// <param name="value">值</param>
        /// <param name="key">密钥</param>
        /// <param name="encoding">字符编码</param>
        public static string HmacSha256(string value, string key, Encoding encoding)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(key))
                return string.Empty;

            return HmacSha256(encoding.GetBytes(value), key, encoding);
        }

        public static string HmacSha256(byte[] bytes,string key,Encoding encoding)
        {
            using var sha256 = new HMACSHA256(encoding.GetBytes(key));
            var hash = sha256.ComputeHash(bytes);
            return Convert.ToHexStringLower(hash);
        }

        public static string SHA256(string value)
        {
            Span<byte> digest = stackalloc byte[System.Security.Cryptography.SHA256.HashSizeInBytes];
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value), digest);
            return Convert.ToHexStringLower(digest);
        }

        #endregion
    }
}
