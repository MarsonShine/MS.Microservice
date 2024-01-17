using Microsoft.AspNetCore.DataProtection.KeyManagement;
using MS.Microservice.Core.Security.Cryptology;
using Newtonsoft.Json.Linq;
using System;
using System.Security.Cryptography;
using System.Text;

namespace MS.Microservice.Core.Tests.Cryptology
{
    public class EncryptTest
    {
        [Fact]
        public void TestEncrypt()
        {
            string cipherText = "hello world";
            string key = "1234567890123456";
            string encrypt = CryptologyHelper.DesCrypt.Encrypt(cipherText, key);

            string decrypt = CryptologyHelper.DesCrypt.Decrypt(cipherText, key);
            Assert.Equal(encrypt, decrypt);
        }
        /*
         TransformFinalBlock 方法和 FlushFinalBlock 方法可以实现相同的加密效果，但在某些情况下，它们可能会产生不同的结果。

        TransformFinalBlock 方法是用于将最后的数据块进行加密转换。它将输入数据块进行加密并返回加密后的结果。这个方法会处理最后不完整的数据块，并将其加密。在使用 TransformFinalBlock 方法时，可以确保所有数据都已被加密处理。
        
        FlushFinalBlock 方法用于确保所有数据都被加密，并且将加密后的数据写入到底层流中。这个方法不返回加密结果，而是将加密后的数据直接写入到流中。它适用于流式处理数据的场景。

        这两种方法的实现细节和使用方式略有不同，因此在一些特定的情况下，它们可能会产生不同的加密结果。一种常见的情况是当使用了填充模式（如 PKCS7）时，TransformFinalBlock 方法会将最后的数据块进行填充后再进行加密，而 FlushFinalBlock 方法则不会填充数据块，直接进行加密。这可能会导致最后一个数据块的加密结果不同。
         */
        [Fact]
        public void TestEncrypt2()
        {
            string cipherText = "hello world";
            string key = "1234567890123456";
            string encrypt = CryptologyHelper.DesCrypt.Encrypt(cipherText, key);

            string encrypt2 = Encrypt(cipherText, key, "12345678");
            Assert.Equal(encrypt, encrypt2);
        }

        public static string Encrypt(string cipherText, string key, string iv)
        {
            var tripleDES = TripleDES.Create();
            tripleDES.Key = Encoding.UTF8.GetBytes(key);
            tripleDES.IV = Encoding.UTF8.GetBytes(iv);
            tripleDES.Mode = CipherMode.CBC;
            tripleDES.Padding = PaddingMode.PKCS7;
            using var encrypt = tripleDES.CreateEncryptor();
            byte[] inputBuffer = Encoding.UTF8.GetBytes(cipherText);
            var encryptBuffer = encrypt.TransformFinalBlock(inputBuffer, 0, inputBuffer.Length);
            return Convert.ToBase64String(encryptBuffer);
        }
    }
}
