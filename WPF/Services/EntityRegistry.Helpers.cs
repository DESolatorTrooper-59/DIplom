using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Tournaments.WPF.Models;

namespace Tournaments.WPF.Services
{
    public static partial class EntityRegistry
    {
        private static int Count(DatabaseService database, string tableName, Func<DataRow, bool> predicate)
        {
            return database.CountRows(tableName, predicate);
        }

        private static bool IsDuplicate(DatabaseService database, string tableName, string valueColumn, string mainValue, string idColumn, int currentId, bool isInsert)
        {
            return Count(database, tableName, row =>
                StringEquals(row[valueColumn], mainValue) &&
                (isInsert || !ValuesEqual(row[idColumn], currentId))) > 0;
        }

        private static FieldDefinition CreateLookupField(string name, string label, string lookupTableName, string lookupColumnName, string lookupDisplayColumnName = null, bool isRequired = false, bool isKey = false)
        {
            return new FieldDefinition(name, label, FieldType.Integer)
            {
                IsRequired = isRequired,
                IsKey = isKey,
                LookupTableName = lookupTableName,
                LookupColumnName = lookupColumnName,
                LookupDisplayColumnName = lookupDisplayColumnName
            };
        }

        private static string GetString(IDictionary<string, object> values, string key)
        {
            if (!values.ContainsKey(key) || values[key] == null)
            {
                return null;
            }

            return Convert.ToString(values[key]);
        }

        private static int GetInt(IDictionary<string, object> values, string key)
        {
            return Convert.ToInt32(values[key]);
        }

        private static int? GetNullableInt(IDictionary<string, object> values, string key)
        {
            if (!values.ContainsKey(key) || values[key] == null)
            {
                return null;
            }

            return Convert.ToInt32(values[key]);
        }

        private static int GetOriginalInt(EntityEditContext context, string key)
        {
            if (context.OriginalValues == null || !context.OriginalValues.ContainsKey(key) || context.OriginalValues[key] == null)
            {
                return 0;
            }

            return Convert.ToInt32(context.OriginalValues[key]);
        }

        private static DateTime GetDate(IDictionary<string, object> values, string key)
        {
            return Convert.ToDateTime(values[key]);
        }

        private static DateTime? GetNullableDate(IDictionary<string, object> values, string key)
        {
            if (!values.ContainsKey(key) || values[key] == null)
            {
                return null;
            }

            return Convert.ToDateTime(values[key]);
        }

        private static bool GetBool(IDictionary<string, object> values, string key)
        {
            return Convert.ToBoolean(values[key]);
        }

        private static string GetTournamentParticipantMode(DatabaseService database, int tournamentId)
        {
            DataTable tournaments = database.GetTable("Tournaments");
            if (!tournaments.Columns.Contains("TournamentID"))
            {
                return "Команды";
            }

            DataRow row = tournaments.Rows
                .Cast<DataRow>()
                .FirstOrDefault(item => ValuesEqual(item["TournamentID"], tournamentId));

            if (row == null || !tournaments.Columns.Contains("ParticipantMode") || row["ParticipantMode"] == DBNull.Value)
            {
                return "Команды";
            }

            string mode = Convert.ToString(row["ParticipantMode"]);
            return string.IsNullOrWhiteSpace(mode) ? "Команды" : mode.Trim();
        }

        private static bool ValuesEqual(object left, object right)
        {
            if (left == DBNull.Value)
            {
                left = null;
            }

            if (right == DBNull.Value)
            {
                right = null;
            }

            if (left == null && right == null)
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            if (left is string || right is string)
            {
                return StringEquals(left, right);
            }

            if (left is DateTime || right is DateTime)
            {
                return Convert.ToDateTime(left).Date == Convert.ToDateTime(right).Date;
            }

            if (left is bool || right is bool)
            {
                return Convert.ToBoolean(left) == Convert.ToBoolean(right);
            }

            if (left is decimal || right is decimal || left is double || right is double || left is float || right is float)
            {
                return Convert.ToDecimal(left) == Convert.ToDecimal(right);
            }

            return Convert.ToInt64(left) == Convert.ToInt64(right);
        }

        private static bool StringEquals(object left, object right)
        {
            return string.Equals(Convert.ToString(left), Convert.ToString(right), StringComparison.CurrentCultureIgnoreCase);
        }
    }
}

