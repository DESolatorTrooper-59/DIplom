using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Tournaments.WPF.Models;

namespace Tournaments.WPF.Services
{
    internal sealed class InMemoryDataBackend : IDataBackend
    {
        private readonly InMemoryDataStore _store;

        public InMemoryDataBackend(InMemoryDataStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public string ModeTitle => "Тестовый режим";

        public string StorageLabel => "singleton Dictionary (in-memory)";

        public bool IsTestMode => true;

        public void EnsurePasswordStorage()
        {
        }

        public DataTable GetTable(string tableName)
        {
            return _store.GetTableCopy(tableName);
        }

        public IReadOnlyCollection<string> GetColumns(string tableName)
        {
            return _store.GetTableCopy(tableName)
                .Columns
                .Cast<DataColumn>()
                .Select(column => column.ColumnName)
                .ToArray();
        }

        public bool ValidateLogin(string login, string password)
        {
            return _store.ValidateUser(login, password);
        }

        public bool ValidateOrganizerLogin(string login, string password)
        {
            return _store.ValidateOrganizerUser(login, password);
        }

        public bool ValidatePlayerLogin(string login, string password)
        {
            return _store.ValidatePlayerUser(login, password);
        }

        public void EnsureOrganizerUser(string login, string password)
        {
            _store.EnsureUser(login, password);
        }

        public bool RecordExists(string tableName, string columnName, object value)
        {
            return _store.RecordExists(tableName, columnName, value);
        }

        public int CountRows(string tableName, Func<DataRow, bool> predicate)
        {
            return _store.CountRows(tableName, predicate);
        }

        public int? PeekNextIdentityValue(string tableName)
        {
            return _store.PeekNextIdentityValue(tableName);
        }

        public void Insert(string tableName, IDictionary<string, object> values)
        {
            _store.Insert(tableName, values);
        }

        public void Update(string tableName, string[] keyColumns, IDictionary<string, object> values, IDictionary<string, object> originalValues)
        {
            _store.Update(tableName, keyColumns, values, originalValues);
        }

        public void Delete(string tableName, string[] keyColumns, IDictionary<string, object> originalValues)
        {
            _store.Delete(tableName, keyColumns, originalValues);
        }

        public void DeleteCascade(string tableName, string[] keyColumns, IDictionary<string, object> originalValues)
        {
            _store.DeleteCascade(tableName, keyColumns, originalValues);
        }

        public void DeleteTournamentCascade(int tournamentId)
        {
            _store.DeleteTournamentCascade(tournamentId);
        }

        public void ReplaceTableContents(string tableName, DataTable importedTable)
        {
            _store.ReplaceTableContents(tableName, importedTable);
        }

        public int GenerateTournamentBracket(int tournamentId)
        {
            return _store.GenerateTournamentBracket(tournamentId);
        }

        public void UpdateBracketMatch(int tournamentId, BracketMatchUpdateRequest request)
        {
            _store.UpdateBracketMatch(tournamentId, request);
        }
    }
}
