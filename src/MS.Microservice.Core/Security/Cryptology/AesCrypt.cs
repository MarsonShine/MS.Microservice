using System;
using System.Security.Cryptography;
using System.Text;

namespace MS.Microservice.Core.Security.Cryptology
{
    public partial class CryptologyHelper
    {
        public static class AesCrypt
        {
            private static byte[] GetAesKey(byte[] keyArray)
            {
                byte[] newArray = new byte[16];
                if (keyArray.Length < 16)
                {
                    for (int i = 0; i < newArray.Length; i++)
                    {
                        if (i >= keyArray.Length)
                        {
                            newArray[i] = 0;
                        }
                        else
                        {
                            newArray[i] = keyArray[i];
                        }
                    }
                }
                return newArray;
            }

            /// <summary>
            /// 使用AES加密字符串,按128位处理key
            /// </summary>
            /// <param name="content">加密内容</param>
            /// <param name="key">秘钥，需要128位、256位.....</param>
            /// <returns>Base64字符串结果</returns>
            public static string Encrypt(string key, string content, bool autoHandle = true)
            {
                byte[] keyArray = Encoding.UTF8.GetBytes(key);
                if (autoHandle)
                {
                    keyArray = GetAesKey(keyArray);
                }
                byte[] toEncryptArray = Encoding.UTF8.GetBytes(content);

                SymmetricAlgorithm des = Aes.Create();
                des.Key = keyArray;
                des.Mode = CipherMode.ECB;
                des.Padding = PaddingMode.PKCS7;
                ICryptoTransform cTransform = des.CreateEncryptor();
                byte[] resultArray = cTransform.TransformFinalBlock(toEncryptArray, 0, toEncryptArray.Length);
                return Convert.ToBase64String(resultArray);
            }

            /// <summary>
            /// 使用AES解密字符串,按128位处理key
            /// </summary>
            /// <param name="content">内容</param>
            /// <param name="key">秘钥，需要128位、256位.....</param>
            /// <returns>UTF8解密结果</returns>
            public static string Decrypt(string key, string content, bool autoHandle = true)
            {
                byte[] keyArray = Encoding.UTF8.GetBytes(key);
                if (autoHandle)
                {
                    keyArray = GetAesKey(keyArray);
                }
                byte[] toEncryptArray = Convert.FromBase64String(content);

                SymmetricAlgorithm des = Aes.Create();
                des.Key = keyArray;
                des.Mode = CipherMode.ECB;
                des.Padding = PaddingMode.PKCS7;

                ICryptoTransform cTransform = des.CreateDecryptor();
                byte[] resultArray = cTransform.TransformFinalBlock(toEncryptArray, 0, toEncryptArray.Length);

                return Encoding.UTF8.GetString(resultArray);
            }
        }
    }
    

}
