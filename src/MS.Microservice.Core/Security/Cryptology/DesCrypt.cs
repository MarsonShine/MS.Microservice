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
            public static string Encrypt(string value)
            {
                return EnCrypt(value, key);
            }

            public static string EnCrypt(string value, string key)
            {
                return EnCrypt(value, key, Encoding.UTF8);
            }
            /// <summary>
            /// Des 加密
            /// </summary>
            /// <param name="value"></param>
            /// <param name="key">24 位密钥</param>
            /// <param name="encoding"></param>
            /// <returns></returns>
            public static string EnCrypt(string value, string key, Encoding encoding)
            {
                if (!ValidateDesValueAndKey(value, key))
                {
                    return "";
                }
                using var transform = CreateDesProvider(key).CreateDecryptor();
                return GetEncryptResult(value, encoding, transform);
            }

            public static string Decrypt(string value) => Decrypt(value, key);

            public static string Decrypt(string value, string key) => Decrypt(value, key, Encoding.UTF8);

            public static string Decrypt(string value, string key, Encoding encoding)
            {
                if (!ValidateDesValueAndKey(value, key))
                    return string.Empty;
                using (var transform = CreateDesProvider(key).CreateDecryptor())
                {
                    return GetDecryptResult(value, encoding, transform);
                }
            }

            private static string GetDecryptResult(string value, Encoding encoding, ICryptoTransform transform)
            {
                var bytes = System.Convert.FromBase64String(value);
                var result = transform.TransformFinalBlock(bytes, 0, bytes.Length);
                return encoding.GetString(result);
            }

            private static bool ValidateDesValueAndKey(string value, string key)
            {
                if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(key))
                    return false;
                return key.Length == 24;
            }

            private static TripleDESCryptoServiceProvider CreateDesProvider(string key)
            {
                return new TripleDESCryptoServiceProvider { Key = Encoding.ASCII.GetBytes(key), Mode = CipherMode.ECB, Padding = PaddingMode.PKCS7 };
            }

            private static string GetEncryptResult(string value, Encoding encoding, ICryptoTransform transform)
            {
                var bytes = encoding.GetBytes(value);
                var result = transform.TransformFinalBlock(bytes, 0, bytes.Length);
                return Convert.ToBase64String(result);
            }
        }

        //原激活码系统DES解密算法
        public static string DecryptDES(string decryptString, string decryptKey, string iv = "12345678")
        {
            try
            {
                byte[] rgbKey = Encoding.UTF8.GetBytes(decryptKey);
                byte[] rgbIV = Encoding.UTF8.GetBytes(iv);
                byte[] inputByteArray = Convert.FromBase64String(decryptString);
                DESCryptoServiceProvider dCSP = new DESCryptoServiceProvider();
                dCSP.Mode = CipherMode.CBC;
                dCSP.Padding = PaddingMode.PKCS7;
                using var mStream = new MemoryStream();
                using var cStream = new CryptoStream(mStream, dCSP.CreateDecryptor(rgbKey, rgbIV), CryptoStreamMode.Write);
                cStream.Write(inputByteArray, 0, inputByteArray.Length);
                cStream.FlushFinalBlock();
                return Encoding.UTF8.GetString(mStream.ToArray());
            }
            catch (Exception)
            {
                return decryptString;
            }
        }

        public static string EncryptDES(string encryptString, string encryptKey, string iv = "12345678")
        {
            try
            {
                byte[] rgbKey = Encoding.UTF8.GetBytes(encryptKey);
                byte[] rgbIV = Encoding.UTF8.GetBytes(iv);
                byte[] inputByteArray = Encoding.UTF8.GetBytes(encryptString);
                DESCryptoServiceProvider dCSP = new DESCryptoServiceProvider
                {
                    Mode = CipherMode.CBC,
                    Padding = PaddingMode.PKCS7
                };
                using var mStream = new MemoryStream();
                using var cStream = new CryptoStream(mStream, dCSP.CreateEncryptor(rgbKey, rgbIV), CryptoStreamMode.Write);
                cStream.Write(inputByteArray, 0, inputByteArray.Length);
                cStream.FlushFinalBlock();

                return Convert.ToBase64String(mStream.ToArray());
            }
            catch (Exception)
            {
                return encryptString;
            }
        }
    }
}
