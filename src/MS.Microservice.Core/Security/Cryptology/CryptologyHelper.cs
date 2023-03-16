using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace MS.Microservice.Core.Security.Cryptology
{
    public partial class CryptologyHelper
    {
        private const string key = "QaP1AF8utIarcBqdhYTZpVGbiNQ9M6IL";

        #region HmacSha256加密

        public static string HmacSha256(string value) => HmacSha256(value, key);

        /// <summary>
        /// HMACSHA256加密
        /// </summary>
        /// <param name="value">值</param>
        /// <param name="key">密钥</param>
        public static string HmacSha256(string value, string key)
        {
            return HmacSha256(value, key, Encoding.UTF8);
        }

        /// <summary>
        /// HMACSHA256加密
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
            var sha256 = new HMACSHA256(encoding.GetBytes(key));
            var hash = sha256.ComputeHash(bytes);
            return string.Join("", hash.ToList().Select(t => t.ToString("x2")).ToArray());
        }

        #endregion
    }
}
