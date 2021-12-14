using System;
using System.Security.Cryptography;
using System.Text;

namespace MS.Microservice.Core.Security.Summary
{
    public static class Md5
    {
        public static string Encrypt(string value, Encoding encoding)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;
            var md5 = new MD5CryptoServiceProvider();
            string result;
            try
            {
                var hash = md5.ComputeHash(encoding.GetBytes(value));
                result = BitConverter.ToString(hash);
            }
            finally
            {
                md5.Clear();
            }
            return result.Replace("-", "");
        }
    }
}
