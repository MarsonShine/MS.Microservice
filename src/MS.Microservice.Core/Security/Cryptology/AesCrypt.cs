using System;
using System.Security.Cryptography;
using System.Text;

namespace MS.Microservice.Core.Security.Cryptology
{
    public partial class CryptologyHelper
    {
        public static class AesCrypt
        {  
            /// <summary>
            /// 128 位零向量
            /// </summary>
            private static byte[] _iv;
            public static byte[] IV
            {
                get
                {
                    if (_iv == null)
                    {
                        var size = 16;
                        _iv = new byte[size];
                        for (int i = 0; i < size; i++)
                            _iv[i] = 0;
                    }
                    return _iv;
                }
            }

            #region AES加密
            public static string Encrypt(string value)
            {
                return AesEnCrypt(value, key);
            }

            public static string AesEnCrypt(string value, string key)
            {
                return AesEnCrypt(value, key, Encoding.UTF8);
            }

            public static string AesEnCrypt(string value, string key, Encoding encoding, CipherMode cipherMode = CipherMode.CBC)
            {
                if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(key))
                    return string.Empty;
                var rijndaelManaged = CreateRijndaelManaged(key, encoding, cipherMode);
                using (var transform = rijndaelManaged.CreateEncryptor(rijndaelManaged.Key, rijndaelManaged.IV))
                {
                    return GetEncryptResult(value, encoding, transform);
                }
            }

            private static string GetEncryptResult(string value, Encoding encoding, ICryptoTransform transform)
            {
                var bytes = encoding.GetBytes(value);
                var result = transform.TransformFinalBlock(bytes, 0, bytes.Length);

                return Convert.ToBase64String(result);
            }

            public static string AesEnCrypt(string value, string key, string iv, Encoding encoding, CipherMode cipherMode = CipherMode.CBC)
            {
                if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(key))
                    return string.Empty;
                var rijndaelManaged = CreateRijndaelManaged(key, iv, encoding, cipherMode);
                using (var transform = rijndaelManaged.CreateDecryptor(rijndaelManaged.Key, rijndaelManaged.IV))
                {
                    return GetEncryptResult(value, encoding, transform);
                }
            }
            #endregion

            #region AES解密
            public static string Decrypt(string value)
            {
                return AesDecrypt(value, key);
            }

            public static string AesDecrypt(string value, string key)
            {
                return AesDecrypt(value, key, Encoding.UTF8);
            }

            public static string AesDecrypt(string value, string key, Encoding encoding, CipherMode cipherMode = CipherMode.CBC)
            {
                if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(key))
                    return string.Empty;
                var rijndaelManaged = CreateRijndaelManaged(key, encoding, cipherMode);
                using (var transform = rijndaelManaged.CreateDecryptor(rijndaelManaged.Key, rijndaelManaged.IV))
                {
                    return GetDecryptResult(value, encoding, transform);
                }
            }
            #endregion

            /// <summary>
            /// 创建Des加密服务提供程序
            /// </summary>
            private static TripleDESCryptoServiceProvider CreateDesProvider(string key)
            {
                return new TripleDESCryptoServiceProvider { Key = Encoding.ASCII.GetBytes(key), Mode = CipherMode.ECB, Padding = PaddingMode.PKCS7 };
            }

            private static string GetDecryptResult(string value, Encoding encoding, ICryptoTransform transform)
            {
                var bytes = Convert.FromBase64String(value);
                var result = transform.TransformFinalBlock(bytes, 0, bytes.Length);
                return encoding.GetString(result);
            }

            /// <summary>
            /// 创建RijndaelManaged
            /// </summary>
            private static RijndaelManaged CreateRijndaelManaged(string key, Encoding encoding, CipherMode cipherMode = CipherMode.CBC)
            {
                return CreateRijndaelManaged(key, null, encoding, cipherMode);
            }

            /// <summary>
            /// 创建RijndaelManaged
            /// </summary>
            private static RijndaelManaged CreateRijndaelManaged(string key, string iv, Encoding encoding, CipherMode cipherMode = CipherMode.CBC)
            {
                if (!string.IsNullOrWhiteSpace(iv))
                    _iv = encoding.GetBytes(iv.Substring(0, 16));

                return new RijndaelManaged
                {
                    Key = encoding.GetBytes(key),
                    Mode = cipherMode,
                    Padding = PaddingMode.PKCS7,
                    IV = IV
                };
            }
        }
    }
    

}
