using System;

namespace MS.Microservice.Domain.Aggregates.IdentityModel
{
    public class PasswordSaltHelper
    {
        private static readonly object[] chars = new object[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p", "q", "r", "s", "t", "u", "v", "w", "x", "y", "z" };

        public static string Generate()
        {
            string salt = "";
            var rd = new Random((int)DateTime.Now.Ticks);
            for (int i = 0; i < 4; i++)
            {
                salt += chars[rd.Next(0, chars.Length)].ToString();
            }
            return salt;
        }
    }
}
