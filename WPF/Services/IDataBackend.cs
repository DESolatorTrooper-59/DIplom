using System;
using System.Collections.Generic;
using System.Data;
using Tournaments.WPF.Models;

namespace Tournaments.WPF.Services
{
    internal interface IDataBackend
    {
        string ModeTitle { get; }

        string StorageLabel { get; }

        bool IsTestMode { get; }

        DataTable GetTable(string tableName);

        IReadOnlyCollection<string> GetColumns(string tableName);

        bool ValidateLogin(string login, string password);

        void EnsureOrganizerUser(string login, string password);

        bool RecordExists(string tableName, string columnName, object value);

        int CountRows(string tableName, Func<DataRow, bool> predicate);

        int? PeekNextIdentityValue(string tableName);

        void Insert(string tableName, IDictionary<string, object> values);

        void Update(string tableName, string[] keyColumns, IDictionary<string, object> values, IDictionary<string, object> originalValues);

        void Delete(string tableName, string[] keyColumns, IDictionary<string, object> originalValues);

        void ReplaceTableContents(string tableName, DataTable importedTable);

        int GenerateTournamentBracket(int tournamentId);

        void UpdateBracketMatch(int tournamentId, BracketMatchUpdateRequest request);
    }
}
