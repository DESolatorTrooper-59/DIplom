using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Tournaments.WPF.Models;

namespace Tournaments.WPF.Services
{
    internal sealed class SnapshotDataBackend : IDataBackend
    {
        private readonly Dictionary<string, DataTable> _tables;

        public SnapshotDataBackend(IDictionary<string, DataTable> tables)
        {
            if (tables == null)
            {
                throw new ArgumentNullException(nameof(tables));
            }

            _tables = new Dictionary<string, DataTable>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, DataTable> entry in tables)
            {
                DataTable copy = entry.Value?.Copy() ?? throw new ArgumentException("Таблица снимка не может быть null.", nameof(tables));
                copy.TableName = entry.Key;
                _tables[entry.Key] = copy;
            }
        }

        public string ModeTitle => "Снимок проверки";

        public string StorageLabel => "Import validation snapshot";

        public bool IsTestMode => false;

        public void EnsurePasswordStorage()
        {
        }

        public DataTable GetTable(string tableName)
        {
            return GetRequiredTable(tableName).Copy();
        }

        public IReadOnlyCollection<string> GetColumns(string tableName)
        {
            return GetRequiredTable(tableName)
                .Columns
                .Cast<DataColumn>()
                .Select(column => column.ColumnName)
                .ToArray();
        }

        public bool ValidateLogin(string login, string password)
        {
            return false;
        }

        public bool RecordExists(string tableName, string columnName, object value)
        {
            return GetRequiredTable(tableName)
                .Rows
                .Cast<DataRow>()
                .Any(row => ValuesEqual(row[columnName], value));
        }

        public int CountRows(string tableName, Func<DataRow, bool> predicate)
        {
            return GetRequiredTable(tableName)
                .Rows
                .Cast<DataRow>()
                .Count(predicate);
        }

        public int? PeekNextIdentityValue(string tableName)
        {
            return null;
        }

        public int? Insert(string tableName, IDictionary<string, object> values)
        {
            throw CreateUnsupportedException();
        }

        public void Update(string tableName, string[] keyColumns, IDictionary<string, object> values, IDictionary<string, object> originalValues)
        {
            throw CreateUnsupportedException();
        }

        public void Delete(string tableName, string[] keyColumns, IDictionary<string, object> originalValues)
        {
            throw CreateUnsupportedException();
        }

        public void DeleteCascade(string tableName, string[] keyColumns, IDictionary<string, object> originalValues)
        {
            throw CreateUnsupportedException();
        }

        public void DeleteTournamentCascade(int tournamentId)
        {
            throw CreateUnsupportedException();
        }

        public void ReplaceTableContents(string tableName, DataTable importedTable)
        {
            DataTable copy = importedTable?.Copy() ?? throw new ArgumentNullException(nameof(importedTable));
            copy.TableName = tableName;
            _tables[tableName] = copy;
        }

        public int GenerateTournamentBracket(int tournamentId)
        {
            throw CreateUnsupportedException();
        }

        public void UpdateBracketMatch(int tournamentId, BracketMatchUpdateRequest request)
        {
            throw CreateUnsupportedException();
        }

        private DataTable GetRequiredTable(string tableName)
        {
            if (!_tables.TryGetValue(tableName, out DataTable table))
            {
                throw new InvalidOperationException("Неизвестная таблица снимка: " + tableName);
            }

            return table;
        }

        private static InvalidOperationException CreateUnsupportedException()
        {
            return new InvalidOperationException("Снимок проверки поддерживает только операции чтения.");
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

            return string.Equals(Convert.ToString(left), Convert.ToString(right), StringComparison.CurrentCultureIgnoreCase);
        }
    }
}
