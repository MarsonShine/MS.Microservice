using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MS.Microservice.Core.Security.Cryptology
{
    public partial class CryptologyHelper
    {
        public static class DesCrypt
        {
            public static string Encrypt(string value, string key)
            {
                return Encrypt(value, key, "12345678", Encoding.UTF8);
            }
            /// <summary>
            /// Des 加密
            /// </summary>
            /// <param name="value"></param>
            /// <param name="key">24 位密钥</param>
            /// <param name="encoding"></param>
            /// <returns></returns>
            public static string Encrypt(string value, string key, Encoding encoding) => Encrypt(value, key, "12345678", encoding);

            public static string Encrypt(string value, string key, string iv, Encoding encoding)
            {
                if (!ValidateDesValueAndKey(value, key))
                {
                    return "";
                }
                using var tripleDES = CreateDesProvider(key, iv);
                using var mStream = new MemoryStream();
                using var cStream = new CryptoStream(mStream, tripleDES.CreateEncryptor(), CryptoStreamMode.Write);
                byte[] inputByteArray = Encoding.UTF8.GetBytes(value);
                cStream.Write(inputByteArray, 0, inputByteArray.Length);
                cStream.FlushFinalBlock();

                return Convert.ToBase64String(mStream.ToArray());
            }

            public static string Decrypt(string value, string key) => Decrypt(value, key, Encoding.UTF8);

            public static string Decrypt(string value, string key, Encoding encoding) => Decrypt(value, key, "12345678", encoding);
            public static string Decrypt(string value, string key, string iv, Encoding encoding)
            {
                if (!ValidateDesValueAndKey(value, key))
                    return string.Empty;

                using var tripleDES = CreateDesProvider(key, iv);
                using var mStream = new MemoryStream();
                using var cStream = new CryptoStream(mStream, tripleDES.CreateDecryptor(), CryptoStreamMode.Write);
                byte[] inputByteArray = Convert.FromBase64String(value);
                cStream.Write(inputByteArray, 0, inputByteArray.Length);
                cStream.FlushFinalBlock();
                return Encoding.UTF8.GetString(mStream.ToArray());
            }

            private static bool ValidateDesValueAndKey(string value, string key)
            {
                if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(key))
                    return false;
                return key.Length == 24;
            }

            private static TripleDES CreateDesProvider(string key, string iv)
            {
                var tripleDES = TripleDES.Create();
                tripleDES.Key = Encoding.UTF8.GetBytes(key);
                tripleDES.IV = Encoding.UTF8.GetBytes(iv);
                tripleDES.Mode = CipherMode.CBC;
                tripleDES.Padding = PaddingMode.PKCS7;
                return tripleDES;
            }
        }
    }
}
