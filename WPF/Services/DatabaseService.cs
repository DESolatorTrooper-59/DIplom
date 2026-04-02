using System;
using System.Collections.Generic;
using System.Data;
using Tournaments.WPF.Models;

namespace Tournaments.WPF.Services
{
    public sealed class DatabaseService
    {
        private readonly InMemoryDataStore _store;

        public DatabaseService()
            : this(InMemoryDataStore.Instance)
        {
        }

        internal DatabaseService(InMemoryDataStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public DataTable GetTable(string tableName)
        {
            return _store.GetTableCopy(tableName);
        }

        public bool ValidateLogin(string login, string password)
        {
            return _store.ValidateUser(login, password);
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

        public void ReplaceTableValidated(EntityDefinition definition, DataTable importedTable)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (importedTable == null)
            {
                throw new ArgumentNullException(nameof(importedTable));
            }

            InMemoryDataStore snapshot = _store.CloneStore();
            snapshot.ReplaceTableContents(definition.TableName, importedTable);

            DatabaseService validationDatabase = new DatabaseService(snapshot);
            SnapshotValidationService.Validate(EntityRegistry.All, validationDatabase);

            _store.ReplaceTableContents(definition.TableName, importedTable);
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
