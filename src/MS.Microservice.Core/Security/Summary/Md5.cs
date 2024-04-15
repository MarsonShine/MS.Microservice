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
            var md5 = MD5.Create();
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

        public static string Encrypt(ReadOnlySpan<byte> bytes) => ComputeMD5(bytes);

		private static string ComputeMD5(ReadOnlySpan<byte> bytes)
		{
			Span<byte> hashBytes = stackalloc byte[16];
			MD5.TryHashData(bytes, hashBytes, out int bytesWritten);
			return BitConverter.ToString(hashBytes.ToArray());
		}

		private static string BytesToMD5String(ReadOnlySpan<byte> hashBytes)
		{
			StringBuilder builder = new(32);
			foreach (byte b in hashBytes)
			{
				builder.AppendFormat("{0:x2}", b);
			}
			return builder.ToString();
		}
	}
}
