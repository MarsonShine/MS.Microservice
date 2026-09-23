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
			// 使用 SHA1 创建哈希值
			using SHA256 sha256 = System.Security.Cryptography.SHA256.Create();
			byte[] inputBytes = Encoding.UTF8.GetBytes(value);
			byte[] hashBytes = sha256.ComputeHash(inputBytes);

			// 将字节数组转换成十六进制字符串
			StringBuilder sb = new();
			foreach (byte b in hashBytes)
			{
				sb.Append(b.ToString("x2"));
			}
			return sb.ToString();
		}

        #endregion
    }
}
