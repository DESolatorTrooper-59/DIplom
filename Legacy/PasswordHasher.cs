using System.Security.Cryptography;
using System.Text;

namespace Tournaments
{
    internal static class PasswordHasher
    {
        public static string HashPassword(string password)
        {
            using (SHA512 sha512 = SHA512.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(password ?? string.Empty);
                byte[] hash = sha512.ComputeHash(bytes);
                StringBuilder builder = new StringBuilder(hash.Length * 2);

                foreach (byte item in hash)
                {
                    builder.Append(item.ToString("x2"));
                }

                return builder.ToString();
            }
        }
    }
}
