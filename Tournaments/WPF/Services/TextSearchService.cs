using System;
using System.Data;
using System.Text;

namespace Tournaments.WPF.Services
{
    public static class TextSearchService
    {
        public static string BuildRowFilter(DataTable source, string query)
        {
            if (source == null || string.IsNullOrWhiteSpace(query) || source.Columns.Count == 0)
            {
                return string.Empty;
            }

            string escapedQuery = EscapeLikeValue(query.Trim());
            StringBuilder builder = new StringBuilder();

            foreach (DataColumn column in source.Columns)
            {
                if (builder.Length > 0)
                {
                    builder.Append(" OR ");
                }

                builder.Append("CONVERT([");
                builder.Append(column.ColumnName.Replace("]", "]]"));
                builder.Append("], 'System.String') LIKE '%");
                builder.Append(escapedQuery);
                builder.Append("%'");
            }

            return builder.ToString();
        }

        public static bool RowMatches(DataRow row, string query)
        {
            if (row == null)
            {
                return false;
            }

            string normalizedQuery = NormalizeQuery(query);
            if (normalizedQuery == null)
            {
                return true;
            }

            foreach (object value in row.ItemArray)
            {
                if (CellMatchesNormalized(value, normalizedQuery))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool CellMatches(object value, string query)
        {
            string normalizedQuery = NormalizeQuery(query);
            return normalizedQuery != null && CellMatchesNormalized(value, normalizedQuery);
        }

        private static bool CellMatchesNormalized(object value, string normalizedQuery)
        {
            if (value == null || value == DBNull.Value)
            {
                return false;
            }

            string text = Convert.ToString(value);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            if (IsNumericValue(value, text) && !ContainsDigit(normalizedQuery))
            {
                return false;
            }

            string normalized = text.Trim().ToLowerInvariant();
            if (normalized.Contains(normalizedQuery))
            {
                return true;
            }

            return normalized.Length <= 64 &&
                   normalizedQuery.Length <= 64 &&
                   LevenshteinDistance(normalized, normalizedQuery) <= 2;
        }

        private static string NormalizeQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return null;
            }

            return query.Trim().ToLowerInvariant();
        }

        private static bool ContainsDigit(string value)
        {
            foreach (char symbol in value)
            {
                if (char.IsDigit(symbol))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsNumericValue(object value, string text)
        {
            if (value is sbyte || value is byte || value is short || value is ushort ||
                value is int || value is uint || value is long || value is ulong ||
                value is float || value is double || value is decimal)
            {
                return true;
            }

            foreach (char symbol in text.Trim())
            {
                if (!char.IsDigit(symbol) && symbol != '-' && symbol != '+' && symbol != '.' && symbol != ',')
                {
                    return false;
                }
            }

            return text.Trim().Length > 0;
        }

        private static string EscapeLikeValue(string value)
        {
            return value
                .Replace("'", "''")
                .Replace("[", "[[]")
                .Replace("%", "[%]")
                .Replace("*", "[*]");
        }

        private static int LevenshteinDistance(string source, string target)
        {
            if (string.IsNullOrEmpty(source))
            {
                return string.IsNullOrEmpty(target) ? 0 : target.Length;
            }

            if (string.IsNullOrEmpty(target))
            {
                return source.Length;
            }

            int[,] distance = new int[source.Length + 1, target.Length + 1];

            for (int i = 0; i <= source.Length; i++)
            {
                distance[i, 0] = i;
            }

            for (int j = 0; j <= target.Length; j++)
            {
                distance[0, j] = j;
            }

            for (int i = 1; i <= source.Length; i++)
            {
                for (int j = 1; j <= target.Length; j++)
                {
                    int cost = source[i - 1] == target[j - 1] ? 0 : 1;
                    distance[i, j] = Math.Min(
                        Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                        distance[i - 1, j - 1] + cost);
                }
            }

            return distance[source.Length, target.Length];
        }
    }
}
