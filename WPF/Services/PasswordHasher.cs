using System;
using System.Security.Cryptography;
using System.Text;

namespace Tournaments.WPF.Services
{
    internal static class PasswordHasher
    {
        public const int Sha512HexLength = 128;

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

        public static bool VerifyPassword(string password, string storedPassword)
        {
            if (string.IsNullOrEmpty(storedPassword))
            {
                return false;
            }

            if (IsSha512Hash(storedPassword))
            {
                return FixedTimeEquals(storedPassword, HashPassword(password));
            }

            return string.Equals(storedPassword, password ?? string.Empty, StringComparison.Ordinal);
        }

        public static bool IsSha512Hash(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != Sha512HexLength)
            {
                return false;
            }

            foreach (char item in value)
            {
                bool isHex =
                    (item >= '0' && item <= '9') ||
                    (item >= 'a' && item <= 'f') ||
                    (item >= 'A' && item <= 'F');

                if (!isHex)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool FixedTimeEquals(string left, string right)
        {
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }

            int difference = 0;
            for (int index = 0; index < left.Length; index++)
            {
                difference |= NormalizeHexChar(left[index]) ^ NormalizeHexChar(right[index]);
            }

            return difference == 0;
        }

        private static int NormalizeHexChar(char value)
        {
            return value >= 'A' && value <= 'F' ? value + 32 : value;
        }
    }
}
