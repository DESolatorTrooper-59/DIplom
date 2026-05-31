using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using Tournaments.WPF.Models;

namespace Tournaments.WPF.Services
{
    internal sealed class SqlServerDataBackend : IDataBackend
    {
        private const string Player1IdColumn = "Player1ID";
        private const string Player2IdColumn = "Player2ID";
        private const string WinnerPlayerIdColumn = "WinnerPlayerID";
        private readonly string _connectionString;
        private readonly string _storageLabel;
        private readonly Dictionary<string, IReadOnlyCollection<string>> _columnCache = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _identityCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _identityResolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public SqlServerDataBackend(string connectionString, string storageLabel)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("Строка подключения к SQL Server не может быть пустой.", nameof(connectionString));
            }

            _connectionString = connectionString;
            _storageLabel = string.IsNullOrWhiteSpace(storageLabel) ? "MS SQL Server" : storageLabel;
        }

        public string ModeTitle => "MS SQL Server";

        public string StorageLabel => _storageLabel;

        public bool IsTestMode => false;

        public void EnsurePasswordStorage()
        {
            using (SqlConnection connection = CreateOpenConnection())
            {
                EnsurePasswordColumnCanStoreHash(connection, "Players");
                MigratePlainTextPasswords(connection, "Players", "PlayerID");
            }
        }

        public DataTable GetTable(string tableName)
        {
            using (SqlConnection connection = CreateOpenConnection())
            {
                return LoadWholeTable(tableName, connection, null);
            }
        }

        public IReadOnlyCollection<string> GetColumns(string tableName)
        {
            if (_columnCache.TryGetValue(tableName, out IReadOnlyCollection<string> cachedColumns))
            {
                return cachedColumns;
            }

            using (SqlConnection connection = CreateOpenConnection())
            {
                string safeTable = EscapeIdentifier(tableName);
                DataTable schema = ExecuteTableQuery(connection, null, "SELECT TOP (0) * FROM [dbo].[" + safeTable + "]", tableName, null);
                IReadOnlyCollection<string> columns = schema.Columns.Cast<DataColumn>().Select(column => column.ColumnName).ToArray();
                _columnCache[tableName] = columns;
                return columns;
            }
        }

        public bool ValidateLogin(string login, string password)
        {
            return ValidateStoredPassword("Players", "Nickname", login, password);
        }

        private bool ValidateStoredPassword(string tableName, string loginColumn, string login, string password)
        {
            using (SqlConnection connection = CreateOpenConnection())
            using (SqlCommand command = connection.CreateCommand())
            {
                string safeTable = EscapeIdentifier(tableName);
                string safeLoginColumn = EscapeIdentifier(loginColumn);
                command.CommandText = "SELECT TOP (1) [Password] FROM [dbo].[" + safeTable + "] WHERE [" + safeLoginColumn + "] = @Login";
                AddParameter(command, "@Login", login);
                object result = command.ExecuteScalar();
                if (result == null || result == DBNull.Value)
                {
                    return false;
                }

                string storedPassword = Convert.ToString(result);
                if (!PasswordHasher.VerifyPassword(password, storedPassword))
                {
                    return false;
                }

                if (!PasswordHasher.IsSha512Hash(storedPassword))
                {
                    EnsurePasswordColumnCanStoreHash(connection, tableName);
                    UpdateStoredPasswordHash(connection, tableName, loginColumn, login, PasswordHasher.HashPassword(password));
                }

                return true;
            }
        }

        public bool RecordExists(string tableName, string columnName, object value)
        {
            string safeTable = EscapeIdentifier(tableName);
            string safeColumn = EscapeIdentifier(columnName);

            using (SqlConnection connection = CreateOpenConnection())
            using (SqlCommand command = connection.CreateCommand())
            {
                if (value == null)
                {
                    command.CommandText = "SELECT COUNT(1) FROM [dbo].[" + safeTable + "] WHERE [" + safeColumn + "] IS NULL";
                }
                else
                {
                    command.CommandText = "SELECT COUNT(1) FROM [dbo].[" + safeTable + "] WHERE [" + safeColumn + "] = @Value";
                    AddParameter(command, "@Value", value);
                }

                return Convert.ToInt32(command.ExecuteScalar()) > 0;
            }
        }

        public int CountRows(string tableName, Func<DataRow, bool> predicate)
        {
            return GetTable(tableName).Rows.Cast<DataRow>().Count(predicate);
        }

        public int? PeekNextIdentityValue(string tableName)
        {
            using (SqlConnection connection = CreateOpenConnection())
            {
                return ReadNextIdentityValue(tableName, connection, null);
            }
        }

        public void Insert(string tableName, IDictionary<string, object> values)
        {
            using (SqlConnection connection = CreateOpenConnection())
            {
                InsertRow(connection, null, tableName, values, false, false);
            }
        }

        public void Update(string tableName, string[] keyColumns, IDictionary<string, object> values, IDictionary<string, object> originalValues)
        {
            using (SqlConnection connection = CreateOpenConnection())
            {
                UpdateRow(connection, null, tableName, keyColumns, values, originalValues);
            }
        }

        public void Delete(string tableName, string[] keyColumns, IDictionary<string, object> originalValues)
        {
            using (SqlConnection connection = CreateOpenConnection())
            {
                DeleteRow(connection, null, tableName, keyColumns, originalValues);
            }
        }

        public void DeleteCascade(string tableName, string[] keyColumns, IDictionary<string, object> originalValues)
        {
            using (SqlConnection connection = CreateOpenConnection())
            using (SqlTransaction transaction = connection.BeginTransaction())
            {
                DeleteCascadeInternal(connection, transaction, tableName, keyColumns, originalValues);
                transaction.Commit();
            }
        }

        public void DeleteTournamentCascade(int tournamentId)
        {
            using (SqlConnection connection = CreateOpenConnection())
            using (SqlTransaction transaction = connection.BeginTransaction())
            {
                DeleteTournamentCascadeInternal(connection, transaction, tournamentId);
                transaction.Commit();
            }
        }

        public void ReplaceTableContents(string tableName, DataTable importedTable)
        {
            if (importedTable == null)
            {
                throw new ArgumentNullException(nameof(importedTable));
            }

            string[] keyColumns = importedTable.PrimaryKey
                .Select(column => column.ColumnName)
                .ToArray();
            string safePhysicalTable = EscapeIdentifier(SqlSchemaMap.GetPhysicalTableName(tableName));
            using (SqlConnection connection = CreateOpenConnection())
            using (SqlTransaction transaction = connection.BeginTransaction())
            {
                if (keyColumns.Length == 0)
                {
                    ReplaceTableContentsWithoutKeys(connection, transaction, tableName, importedTable, safePhysicalTable);
                    transaction.Commit();
                    return;
                }

                DataTable existingTable = LoadWholeTable(tableName, connection, transaction);
                Dictionary<string, DataRow> existingRows = BuildKeyMap(existingTable, tableName, keyColumns);
                Dictionary<string, DataRow> importedRows = BuildKeyMap(importedTable, tableName, keyColumns);

                string identityColumn = GetIdentityColumn(tableName, connection, transaction);
                HashSet<string> keySet = new HashSet<string>(keyColumns, StringComparer.OrdinalIgnoreCase);

                try
                {
                    foreach (KeyValuePair<string, DataRow> entry in existingRows.Where(entry => !importedRows.ContainsKey(entry.Key)))
                    {
                        DeleteRow(connection, transaction, tableName, keyColumns, ToDictionary(entry.Value));
                    }

                    foreach (KeyValuePair<string, DataRow> entry in importedRows.Where(entry => existingRows.ContainsKey(entry.Key)))
                    {
                        Dictionary<string, object> valuesToUpdate = ToDictionary(entry.Value, columnName =>
                            keySet.Contains(columnName) ||
                            IsIdentityColumn(tableName, columnName, identityColumn));
                        if (valuesToUpdate.Count == 0)
                        {
                            continue;
                        }

                        UpdateRow(connection, transaction, tableName, keyColumns, valuesToUpdate, ToDictionary(existingRows[entry.Key]));
                    }

                    List<DataRow> rowsToInsert = importedRows
                        .Where(entry => !existingRows.ContainsKey(entry.Key))
                        .Select(entry => entry.Value)
                        .ToList();

                    bool useIdentityInsert = identityColumn != null && rowsToInsert.Count > 0;
                    if (useIdentityInsert)
                    {
                        ExecuteNonQuery(connection, transaction, "SET IDENTITY_INSERT [dbo].[" + safePhysicalTable + "] ON", null);
                    }

                    try
                    {
                        foreach (DataRow row in rowsToInsert)
                        {
                            InsertPhysicalRow(connection, transaction, tableName, ToDictionary(row), true);
                        }
                    }
                    finally
                    {
                        if (useIdentityInsert)
                        {
                            ExecuteNonQuery(connection, transaction, "SET IDENTITY_INSERT [dbo].[" + safePhysicalTable + "] OFF", null);
                        }
                    }
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }

                transaction.Commit();
            }
        }

        public int GenerateTournamentBracket(int tournamentId)
        {
            using (SqlConnection connection = CreateOpenConnection())
            using (SqlTransaction transaction = connection.BeginTransaction())
            {
                DataTable tournaments = ExecuteTableQuery(connection, transaction, "SELECT * FROM [dbo].[Tournaments] WHERE [TournamentID] = @TournamentID", "Tournaments", command => AddParameter(command, "@TournamentID", tournamentId));
                DataRow tournament = tournaments.Rows.Cast<DataRow>().FirstOrDefault();
                if (tournament == null)
                {
                    throw new InvalidOperationException("Выбранный турнир не найден.");
                }

                bool isPlayerMode = IsPlayerMode(GetTournamentParticipantMode(tournament));
                if (isPlayerMode)
                {
                    EnsurePlayerBracketColumns(connection, transaction);
                }

                List<int> orderedParticipantIds = GetOrderedParticipantIds(tournamentId, isPlayerMode, connection, transaction);
                if (orderedParticipantIds.Count < 2)
                {
                    throw new InvalidOperationException("Для построения сетки нужно минимум два участника.");
                }

                RemoveGeneratedBracket(connection, transaction, tournamentId);

                int bracketSize = NextPowerOfTwo(orderedParticipantIds.Count);
                int roundCount = (int)Math.Log(bracketSize, 2);
                int[] seedPositions = BuildSeedPositions(bracketSize);
                int?[] slots = new int?[bracketSize];
                for (int index = 0; index < seedPositions.Length; index++)
                {
                    int seed = seedPositions[index];
                    if (seed <= orderedParticipantIds.Count)
                    {
                        slots[index] = orderedParticipantIds[seed - 1];
                    }
                }

                DateTime tournamentStartDate = Convert.ToDateTime(tournament["StartDate"]);
                int matchNumber = GetNextMatchNumber(connection, transaction, tournamentId);
                int matchesInRound = bracketSize / 2;
                int createdMatches = 0;

                for (int roundIndex = 0; roundIndex < roundCount; roundIndex++)
                {
                    int teamsInRound = bracketSize / (int)Math.Pow(2, roundIndex);
                    int stageId = InsertBracketStage(connection, transaction, tournamentId, roundIndex + 1, teamsInRound, roundIndex == roundCount - 1);

                    for (int matchIndex = 0; matchIndex < matchesInRound; matchIndex++)
                    {
                        int? participant1Id = roundIndex == 0 ? slots[matchIndex * 2] : (int?)null;
                        int? participant2Id = roundIndex == 0 ? slots[matchIndex * 2 + 1] : (int?)null;
                        int? autoWinnerId = ResolveAutoAdvanceParticipantId(participant1Id, participant2Id);
                        Dictionary<string, object> matchValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["TournamentID"] = tournamentId,
                            ["StageID"] = stageId,
                            ["MatchNumber"] = matchNumber++,
                            ["Team1ID"] = isPlayerMode ? null : (object)participant1Id,
                            ["Team2ID"] = isPlayerMode ? null : (object)participant2Id,
                            ["WinnerTeamID"] = isPlayerMode ? null : (object)autoWinnerId,
                            [Player1IdColumn] = isPlayerMode ? (object)participant1Id : null,
                            [Player2IdColumn] = isPlayerMode ? (object)participant2Id : null,
                            [WinnerPlayerIdColumn] = isPlayerMode ? (object)autoWinnerId : null,
                            ["Team1Score"] = 0,
                            ["Team2Score"] = 0,
                            ["MatchDate"] = tournamentStartDate.AddDays(roundIndex),
                            ["BestOf"] = roundIndex == roundCount - 1 ? 5 : 3,
                            ["Status"] = "Scheduled"
                        };

                        InsertRow(connection, transaction, "Matches", matchValues, true, false);
                        createdMatches++;
                    }

                    matchesInRound /= 2;
                }

                PropagateBracketState(connection, transaction, tournamentId);
                transaction.Commit();
                return createdMatches;
            }
        }

        public void UpdateBracketMatch(int tournamentId, BracketMatchUpdateRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            using (SqlConnection connection = CreateOpenConnection())
            using (SqlTransaction transaction = connection.BeginTransaction())
            {
                DataTable tournaments = ExecuteTableQuery(connection, transaction, "SELECT * FROM [dbo].[Tournaments] WHERE [TournamentID] = @TournamentID", "Tournaments", command => AddParameter(command, "@TournamentID", tournamentId));
                DataRow tournament = tournaments.Rows.Cast<DataRow>().FirstOrDefault();
                bool isPlayerMode = tournament != null && IsPlayerMode(GetTournamentParticipantMode(tournament));
                if (isPlayerMode)
                {
                    EnsurePlayerBracketColumns(connection, transaction);
                }

                List<SqlBracketRoundState> rounds = GetGeneratedBracketRounds(tournamentId, connection, transaction);
                if (rounds.Count == 0)
                {
                    throw new InvalidOperationException("Для выбранного турнира сетка еще не создана.");
                }

                SqlBracketRoundState targetRound = rounds.FirstOrDefault(round => round.Matches.Any(row => AreEqual(row["MatchID"], request.MatchId)));
                if (targetRound == null)
                {
                    throw new InvalidOperationException("Матч турнирной сетки не найден.");
                }

                DataRow match = targetRound.Matches.First(row => AreEqual(row["MatchID"], request.MatchId));
                List<int> availableTeams = GetOrderedParticipantIds(tournamentId, targetRound.IsPlayerMode, connection, transaction);
                int? team1Id = targetRound.RoundIndex == 0
                    ? NormalizeBracketTeamId(request.Team1Id, availableTeams)
                    : GetParticipantId(match, targetRound.IsPlayerMode, 1);
                int? team2Id = targetRound.RoundIndex == 0
                    ? NormalizeBracketTeamId(request.Team2Id, availableTeams)
                    : GetParticipantId(match, targetRound.IsPlayerMode, 2);

                if (team1Id.HasValue && team2Id.HasValue && team1Id.Value == team2Id.Value)
                {
                    throw new InvalidOperationException("В одном матче нельзя выбрать одного и того же участника дважды.");
                }

                if (request.Team1Score < 0 || request.Team2Score < 0)
                {
                    throw new InvalidOperationException("Счет участника не может быть отрицательным.");
                }

                if (request.BestOf <= 0 || request.BestOf % 2 == 0)
                {
                    throw new InvalidOperationException("Best Of должен быть положительным нечетным числом.");
                }

                if (request.MatchDate == default(DateTime))
                {
                    throw new InvalidOperationException("Укажите корректную дату матча.");
                }

                string status = string.IsNullOrWhiteSpace(request.Status) ? "Scheduled" : request.Status.Trim();
                SetParticipantId(match, targetRound.IsPlayerMode, 1, team1Id);
                SetParticipantId(match, targetRound.IsPlayerMode, 2, team2Id);
                match["Team1Score"] = request.Team1Score;
                match["Team2Score"] = request.Team2Score;
                match["BestOf"] = request.BestOf;
                match["MatchDate"] = request.MatchDate;
                match["Status"] = status;

                int? winnerTeamId = NormalizeWinnerTeamId(request.WinnerTeamId, team1Id, team2Id, status, request.Team1Score, request.Team2Score);
                SetParticipantId(match, targetRound.IsPlayerMode, 3, winnerTeamId);

                PropagateBracketState(rounds);
                PersistBracketMatches(connection, transaction, rounds);
                transaction.Commit();
            }
        }

        private SqlConnection CreateOpenConnection()
        {
            SqlConnection connection = new SqlConnection(_connectionString);
            connection.Open();
            return connection;
        }

        private DataTable LoadWholeTable(string tableName, SqlConnection connection, SqlTransaction transaction)
        {
            string safeTable = EscapeIdentifier(tableName);
            return ExecuteTableQuery(connection, transaction, "SELECT * FROM [dbo].[" + safeTable + "]", tableName, null);
        }

        private DataTable ExecuteTableQuery(SqlConnection connection, SqlTransaction transaction, string sql, string tableName, Action<SqlCommand> configure)
        {
            using (SqlCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                configure?.Invoke(command);

                using (SqlDataAdapter adapter = new SqlDataAdapter(command))
                {
                    DataTable table = new DataTable(tableName);
                    adapter.Fill(table);
                    return table;
                }
            }
        }

        private void ExecuteNonQuery(SqlConnection connection, SqlTransaction transaction, string sql, Action<SqlCommand> configure)
        {
            using (SqlCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = sql;
                configure?.Invoke(command);
                command.ExecuteNonQuery();
            }
        }

        private void EnsurePasswordColumnCanStoreHash(SqlConnection connection, string tableName)
        {
            PasswordColumnTarget target = ResolvePasswordColumn(connection, tableName);
            if (target == null ||
                target.Metadata.MaxLength == -1 ||
                target.Metadata.MaxLength >= PasswordHasher.Sha512HexLength)
            {
                return;
            }

            string safeTable = EscapeIdentifier(target.TableName);
            string safeColumn = EscapeIdentifier(target.ColumnName);
            string nullability = target.Metadata.IsNullable ? "NULL" : "NOT NULL";
            ExecuteNonQuery(
                connection,
                null,
                "ALTER TABLE [dbo].[" + safeTable + "] ALTER COLUMN [" + safeColumn + "] NVARCHAR(" + PasswordHasher.Sha512HexLength + ") " + nullability,
                null);
        }

        private PasswordColumnTarget ResolvePasswordColumn(SqlConnection connection, string tableName)
        {
            string physicalTable = SqlSchemaMap.GetPhysicalTableName(tableName);
            string physicalColumn = SqlSchemaMap.GetPhysicalColumnName(tableName, "Password");
            ColumnMetadata metadata = ReadColumnMetadata(connection, physicalTable, physicalColumn);
            if (metadata != null)
            {
                return new PasswordColumnTarget(physicalTable, physicalColumn, metadata);
            }

            metadata = ReadColumnMetadata(connection, tableName, "Password");
            return metadata == null ? null : new PasswordColumnTarget(tableName, "Password", metadata);
        }

        private ColumnMetadata ReadColumnMetadata(SqlConnection connection, string tableName, string columnName)
        {
            using (SqlCommand command = connection.CreateCommand())
            {
                command.CommandText = @"
SELECT CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = N'dbo'
  AND TABLE_NAME = @TableName
  AND COLUMN_NAME = @ColumnName";
                AddParameter(command, "@TableName", tableName);
                AddParameter(command, "@ColumnName", columnName);

                using (SqlDataReader reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                    {
                        return null;
                    }

                    int maxLength = reader.IsDBNull(0) ? -1 : Convert.ToInt32(reader.GetValue(0));
                    bool isNullable = string.Equals(Convert.ToString(reader.GetValue(1)), "YES", StringComparison.OrdinalIgnoreCase);
                    return new ColumnMetadata(maxLength, isNullable);
                }
            }
        }

        private void MigratePlainTextPasswords(SqlConnection connection, string tableName, string keyColumn)
        {
            string safeTable = EscapeIdentifier(tableName);
            string safeKeyColumn = EscapeIdentifier(keyColumn);
            List<PasswordMigrationRow> rows = new List<PasswordMigrationRow>();

            using (SqlCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT [" + safeKeyColumn + "], [Password] FROM [dbo].[" + safeTable + "] WHERE [Password] IS NOT NULL";
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        rows.Add(new PasswordMigrationRow(reader.GetValue(0), Convert.ToString(reader.GetValue(1))));
                    }
                }
            }

            foreach (PasswordMigrationRow row in rows)
            {
                if (!PasswordHasher.IsSha512Hash(row.Password))
                {
                    UpdateStoredPasswordHash(connection, tableName, keyColumn, row.Key, PasswordHasher.HashPassword(row.Password));
                }
            }
        }

        private void UpdateStoredPasswordHash(SqlConnection connection, string tableName, string keyColumn, object keyValue, string passwordHash)
        {
            string safeTable = EscapeIdentifier(tableName);
            string safeKeyColumn = EscapeIdentifier(keyColumn);
            using (SqlCommand command = connection.CreateCommand())
            {
                command.CommandText = "UPDATE [dbo].[" + safeTable + "] SET [Password] = @Password WHERE [" + safeKeyColumn + "] = @Key";
                AddParameter(command, "@Password", passwordHash);
                AddParameter(command, "@Key", keyValue);
                command.ExecuteNonQuery();
            }
        }

        private void EnsurePlayerBracketColumns(SqlConnection connection, SqlTransaction transaction)
        {
            bool changed = false;
            changed |= EnsureNullableIntColumn(connection, transaction, "Matches", Player1IdColumn);
            changed |= EnsureNullableIntColumn(connection, transaction, "Matches", Player2IdColumn);
            changed |= EnsureNullableIntColumn(connection, transaction, "Matches", WinnerPlayerIdColumn);

            if (changed)
            {
                InvalidateSchemaCache("Matches");
            }
        }

        private bool EnsureNullableIntColumn(SqlConnection connection, SqlTransaction transaction, string tableName, string columnName)
        {
            using (SqlCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                string safeTableName = EscapeIdentifier(SqlSchemaMap.GetPhysicalTableName(tableName));
                string safeColumnName = EscapeIdentifier(SqlSchemaMap.GetPhysicalColumnName(tableName, columnName));

                command.CommandText = $@"
IF COL_LENGTH(N'dbo.{safeTableName}', N'{safeColumnName}') IS NULL
BEGIN
    EXEC(N'ALTER TABLE [dbo].[{safeTableName}] ADD [{safeColumnName}] INT NULL');
    SELECT CAST(1 AS bit);
END
ELSE
BEGIN
    SELECT CAST(0 AS bit);
END";
                return Convert.ToBoolean(command.ExecuteScalar());
            }
        }

        private void InvalidateSchemaCache(string tableName)
        {
            _columnCache.Remove(tableName);
        }

        private int? ReadNextIdentityValue(string tableName, SqlConnection connection, SqlTransaction transaction)
        {
            using (SqlCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT CAST(COALESCE(CAST(ic.last_value AS int), CAST(ic.seed_value AS int) - CAST(ic.increment_value AS int)) + CAST(ic.increment_value AS int) AS int)
FROM sys.identity_columns ic
INNER JOIN sys.tables t ON t.object_id = ic.object_id
INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name = 'dbo' AND t.name = @TableName";
                AddParameter(command, "@TableName", SqlSchemaMap.GetPhysicalTableName(tableName));

                object result = command.ExecuteScalar();
                return result == null || result == DBNull.Value ? (int?)null : Convert.ToInt32(result);
            }
        }

        private string GetIdentityColumn(string tableName, SqlConnection connection, SqlTransaction transaction)
        {
            if (_identityCache.TryGetValue(tableName, out string identityColumn))
            {
                return identityColumn;
            }

            if (_identityResolved.Contains(tableName))
            {
                return null;
            }

            using (SqlCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
SELECT c.name
FROM sys.columns c
INNER JOIN sys.tables t ON t.object_id = c.object_id
INNER JOIN sys.schemas s ON s.schema_id = t.schema_id
WHERE s.name = 'dbo' AND t.name = @TableName AND c.is_identity = 1";
                AddParameter(command, "@TableName", SqlSchemaMap.GetPhysicalTableName(tableName));

                object result = command.ExecuteScalar();
                _identityResolved.Add(tableName);
                identityColumn = result == null || result == DBNull.Value ? null : Convert.ToString(result);
                if (!string.IsNullOrWhiteSpace(identityColumn))
                {
                    _identityCache[tableName] = identityColumn;
                }

                return identityColumn;
            }
        }

        private int InsertBracketStage(SqlConnection connection, SqlTransaction transaction, int tournamentId, int stageOrder, int teamsInRound, bool isFinal)
        {
            Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["TournamentID"] = tournamentId,
                ["StageName"] = "Bracket - " + GetRoundTitle(teamsInRound),
                ["StageOrder"] = 100 + stageOrder,
                ["BracketType"] = isFinal ? "Final" : "Winner"
            };

            return InsertRow(connection, transaction, "TournamentStages", values, true, true);
        }

        private void InsertPhysicalRow(SqlConnection connection, SqlTransaction transaction, string tableName, IDictionary<string, object> values, bool includeNullValues)
        {
            string safePhysicalTable = EscapeIdentifier(SqlSchemaMap.GetPhysicalTableName(tableName));
            IReadOnlyCollection<string> availableColumns = GetColumns(tableName);
            List<KeyValuePair<string, object>> columnsToInsert = values
                .Where(pair => ContainsColumn(availableColumns, pair.Key) && (includeNullValues || pair.Value != null))
                .ToList();

            using (SqlCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                if (columnsToInsert.Count == 0)
                {
                    command.CommandText = "INSERT INTO [dbo].[" + safePhysicalTable + "] DEFAULT VALUES";
                }
                else
                {
                    List<string> columnNames = new List<string>();
                    List<string> parameterNames = new List<string>();
                    for (int index = 0; index < columnsToInsert.Count; index++)
                    {
                        string physicalColumnName = EscapeIdentifier(SqlSchemaMap.GetPhysicalColumnName(tableName, columnsToInsert[index].Key));
                        string parameterName = "@P" + index;
                        columnNames.Add("[" + physicalColumnName + "]");
                        parameterNames.Add(parameterName);
                        AddParameter(command, parameterName, columnsToInsert[index].Value);
                    }

                    command.CommandText = "INSERT INTO [dbo].[" + safePhysicalTable + "] (" + string.Join(", ", columnNames) + ") VALUES (" + string.Join(", ", parameterNames) + ")";
                }

                command.ExecuteNonQuery();
            }
        }

        private void ReplaceTableContentsWithoutKeys(SqlConnection connection, SqlTransaction transaction, string tableName, DataTable importedTable, string safePhysicalTable)
        {
            string identityColumn = GetIdentityColumn(tableName, connection, transaction);
            bool useIdentityInsert = identityColumn != null && importedTable.Rows.Count > 0;

            ExecuteNonQuery(connection, transaction, "DELETE FROM [dbo].[" + safePhysicalTable + "]", null);
            if (useIdentityInsert)
            {
                ExecuteNonQuery(connection, transaction, "SET IDENTITY_INSERT [dbo].[" + safePhysicalTable + "] ON", null);
            }

            try
            {
                foreach (DataRow row in importedTable.Rows)
                {
                    InsertPhysicalRow(connection, transaction, tableName, ToDictionary(row), true);
                }
            }
            finally
            {
                if (useIdentityInsert)
                {
                    ExecuteNonQuery(connection, transaction, "SET IDENTITY_INSERT [dbo].[" + safePhysicalTable + "] OFF", null);
                }
            }
        }

        private int GetNextMatchNumber(SqlConnection connection, SqlTransaction transaction, int tournamentId)
        {
            using (SqlCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT ISNULL(MAX([MatchNumber]), 0) + 1 FROM [dbo].[Matches] WHERE [TournamentID] = @TournamentID";
                AddParameter(command, "@TournamentID", tournamentId);
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        private int InsertRow(SqlConnection connection, SqlTransaction transaction, string tableName, IDictionary<string, object> values, bool includeNullValues, bool returnIdentity)
        {
            string safeTable = EscapeIdentifier(tableName);
            IReadOnlyCollection<string> availableColumns = GetColumns(tableName);
            List<KeyValuePair<string, object>> columnsToInsert = values.Where(pair => ContainsColumn(availableColumns, pair.Key) && (includeNullValues || pair.Value != null)).ToList();

            using (SqlCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                if (columnsToInsert.Count == 0)
                {
                    command.CommandText = "INSERT INTO [dbo].[" + safeTable + "] DEFAULT VALUES";
                }
                else
                {
                    List<string> columnNames = new List<string>();
                    List<string> parameterNames = new List<string>();
                    for (int index = 0; index < columnsToInsert.Count; index++)
                    {
                        string columnName = EscapeIdentifier(columnsToInsert[index].Key);
                        string parameterName = "@P" + index;
                        columnNames.Add("[" + columnName + "]");
                        parameterNames.Add(parameterName);
                        AddParameter(command, parameterName, columnsToInsert[index].Value);
                    }

                    command.CommandText = "INSERT INTO [dbo].[" + safeTable + "] (" + string.Join(", ", columnNames) + ") VALUES (" + string.Join(", ", parameterNames) + ")";
                }

                if (returnIdentity)
                {
                    command.CommandText += "; SELECT CAST(SCOPE_IDENTITY() AS INT);";
                    return Convert.ToInt32(command.ExecuteScalar());
                }

                command.ExecuteNonQuery();
                return 0;
            }
        }

        private void UpdateRow(SqlConnection connection, SqlTransaction transaction, string tableName, string[] keyColumns, IDictionary<string, object> values, IDictionary<string, object> originalValues)
        {
            if (keyColumns == null || keyColumns.Length == 0)
            {
                throw new InvalidOperationException("Для обновления записи не заданы ключевые поля.");
            }

            string safeTable = EscapeIdentifier(tableName);
            IReadOnlyCollection<string> availableColumns = GetColumns(tableName);
            List<KeyValuePair<string, object>> columnsToUpdate = values.Where(pair => ContainsColumn(availableColumns, pair.Key)).ToList();
            if (columnsToUpdate.Count == 0)
            {
                return;
            }

            using (SqlCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                List<string> setParts = new List<string>();
                for (int index = 0; index < columnsToUpdate.Count; index++)
                {
                    string columnName = EscapeIdentifier(columnsToUpdate[index].Key);
                    string parameterName = "@Set" + index;
                    setParts.Add("[" + columnName + "] = " + parameterName);
                    AddParameter(command, parameterName, columnsToUpdate[index].Value);
                }

                List<string> whereParts = BuildWhereClause(command, keyColumns, originalValues);
                command.CommandText = "UPDATE [dbo].[" + safeTable + "] SET " + string.Join(", ", setParts) + " WHERE " + string.Join(" AND ", whereParts);
                if (command.ExecuteNonQuery() == 0)
                {
                    throw new InvalidOperationException("Запись для обновления не найдена.");
                }
            }
        }

        private void DeleteRow(SqlConnection connection, SqlTransaction transaction, string tableName, string[] keyColumns, IDictionary<string, object> originalValues)
        {
            if (keyColumns == null || keyColumns.Length == 0)
            {
                throw new InvalidOperationException("Для удаления записи не заданы ключевые поля.");
            }

            string safeTable = EscapeIdentifier(tableName);
            using (SqlCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                List<string> whereParts = BuildWhereClause(command, keyColumns, originalValues);
                command.CommandText = "DELETE FROM [dbo].[" + safeTable + "] WHERE " + string.Join(" AND ", whereParts);
                if (command.ExecuteNonQuery() == 0)
                {
                    throw new InvalidOperationException("Запись для удаления не найдена.");
                }
            }
        }

        private static List<string> BuildWhereClause(SqlCommand command, IEnumerable<string> keyColumns, IDictionary<string, object> originalValues)
        {
            List<string> whereParts = new List<string>();
            int parameterIndex = 0;

            foreach (string keyColumn in keyColumns)
            {
                if (originalValues == null || !originalValues.ContainsKey(keyColumn))
                {
                    throw new InvalidOperationException("Не удалось определить исходный ключ записи: " + keyColumn);
                }

                string safeColumn = EscapeIdentifier(keyColumn);
                object value = originalValues[keyColumn];
                if (value == null || value == DBNull.Value)
                {
                    whereParts.Add("[" + safeColumn + "] IS NULL");
                }
                else
                {
                    string parameterName = "@Key" + parameterIndex++;
                    whereParts.Add("[" + safeColumn + "] = " + parameterName);
                    AddParameter(command, parameterName, value);
                }
            }

            return whereParts;
        }

        private void DeleteCascadeInternal(SqlConnection connection, SqlTransaction transaction, string tableName, string[] keyColumns, IDictionary<string, object> originalValues)
        {
            switch (tableName)
            {
                case "Tournaments":
                    DeleteTournamentCascadeInternal(connection, transaction, GetRequiredInt(originalValues, "TournamentID"));
                    return;
                case "Teams":
                    DeleteTeamCascadeInternal(connection, transaction, GetRequiredInt(originalValues, "TeamID"));
                    return;
                case "Players":
                    DeletePlayerCascadeInternal(connection, transaction, GetRequiredInt(originalValues, "PlayerID"));
                    return;
                case "GameTitles":
                    DeleteGameCascadeInternal(connection, transaction, GetRequiredInt(originalValues, "GameID"));
                    return;
                case "Sponsors":
                    DeleteSponsorCascadeInternal(connection, transaction, GetRequiredInt(originalValues, "SponsorID"));
                    return;
                case "TournamentStages":
                    DeleteStageCascadeInternal(connection, transaction, GetRequiredInt(originalValues, "StageID"));
                    return;
                case "Matches":
                    DeleteMatchCascadeInternal(connection, transaction, GetRequiredInt(originalValues, "MatchID"));
                    return;
                default:
                    DeleteRow(connection, transaction, tableName, keyColumns, originalValues);
                    return;
            }
        }

        private void DeleteTournamentCascadeInternal(SqlConnection connection, SqlTransaction transaction, int tournamentId)
        {
            ExecuteNonQuery(connection, transaction, @"
DELETE FROM [dbo].[Streams]
WHERE [TournamentID] = @TournamentID
   OR [MatchID] IN (
        SELECT [MatchID]
        FROM [dbo].[Matches]
        WHERE [TournamentID] = @TournamentID
   )", command => AddParameter(command, "@TournamentID", tournamentId));

            ExecuteNonQuery(connection, transaction, "DELETE FROM [dbo].[Matches] WHERE [TournamentID] = @TournamentID", command => AddParameter(command, "@TournamentID", tournamentId));
            ExecuteNonQuery(connection, transaction, "DELETE FROM [dbo].[TournamentStages] WHERE [TournamentID] = @TournamentID", command => AddParameter(command, "@TournamentID", tournamentId));
            ExecuteNonQuery(connection, transaction, "DELETE FROM [dbo].[TournamentParticipants] WHERE [TournamentID] = @TournamentID", command => AddParameter(command, "@TournamentID", tournamentId));
            ExecuteNonQuery(connection, transaction, "DELETE FROM [dbo].[TournamentSponsors] WHERE [TournamentID] = @TournamentID", command => AddParameter(command, "@TournamentID", tournamentId));
            DeleteRow(connection, transaction, "Tournaments", new[] { "TournamentID" }, new Dictionary<string, object>
            {
                ["TournamentID"] = tournamentId
            });
        }

        private void DeleteTeamCascadeInternal(SqlConnection connection, SqlTransaction transaction, int teamId)
        {
            ExecuteNonQuery(connection, transaction, @"
DELETE FROM [dbo].[Streams]
WHERE [MatchID] IN (
    SELECT [MatchID]
    FROM [dbo].[Matches]
    WHERE [Team1ID] = @TeamID OR [Team2ID] = @TeamID OR [WinnerTeamID] = @TeamID
)", command => AddParameter(command, "@TeamID", teamId));

            ExecuteNonQuery(connection, transaction, "DELETE FROM [dbo].[Matches] WHERE [Team1ID] = @TeamID OR [Team2ID] = @TeamID OR [WinnerTeamID] = @TeamID", command => AddParameter(command, "@TeamID", teamId));
            ExecuteNonQuery(connection, transaction, "DELETE FROM [dbo].[TournamentParticipants] WHERE [TeamID] = @TeamID", command => AddParameter(command, "@TeamID", teamId));
            ExecuteNonQuery(connection, transaction, "DELETE FROM [dbo].[TeamPlayers] WHERE [TeamID] = @TeamID", command => AddParameter(command, "@TeamID", teamId));
            DeleteRow(connection, transaction, "Teams", new[] { "TeamID" }, new Dictionary<string, object>
            {
                ["TeamID"] = teamId
            });
        }

        private void DeletePlayerCascadeInternal(SqlConnection connection, SqlTransaction transaction, int playerId)
        {
            ExecuteNonQuery(connection, transaction, @"
DELETE FROM [dbo].[Streams]
WHERE [MatchID] IN (
    SELECT [MatchID]
    FROM [dbo].[Matches]
    WHERE [Player1ID] = @PlayerID OR [Player2ID] = @PlayerID OR [WinnerPlayerID] = @PlayerID
)", command => AddParameter(command, "@PlayerID", playerId));

            ExecuteNonQuery(connection, transaction, "DELETE FROM [dbo].[Matches] WHERE [Player1ID] = @PlayerID OR [Player2ID] = @PlayerID OR [WinnerPlayerID] = @PlayerID", command => AddParameter(command, "@PlayerID", playerId));
            ExecuteNonQuery(connection, transaction, "DELETE FROM [dbo].[TournamentParticipants] WHERE [PlayerID] = @PlayerID", command => AddParameter(command, "@PlayerID", playerId));
            ExecuteNonQuery(connection, transaction, "DELETE FROM [dbo].[TeamPlayers] WHERE [PlayerID] = @PlayerID", command => AddParameter(command, "@PlayerID", playerId));
            DeleteRow(connection, transaction, "Players", new[] { "PlayerID" }, new Dictionary<string, object>
            {
                ["PlayerID"] = playerId
            });
        }

        private void DeleteGameCascadeInternal(SqlConnection connection, SqlTransaction transaction, int gameId)
        {
            DataTable tournaments = ExecuteTableQuery(connection, transaction, "SELECT [TournamentID] FROM [dbo].[Tournaments] WHERE [GameID] = @GameID", "Tournaments", command => AddParameter(command, "@GameID", gameId));
            foreach (DataRow tournament in tournaments.Rows)
            {
                DeleteTournamentCascadeInternal(connection, transaction, Convert.ToInt32(tournament["TournamentID"]));
            }

            DeleteRow(connection, transaction, "GameTitles", new[] { "GameID" }, new Dictionary<string, object>
            {
                ["GameID"] = gameId
            });
        }

        private void DeleteSponsorCascadeInternal(SqlConnection connection, SqlTransaction transaction, int sponsorId)
        {
            ExecuteNonQuery(connection, transaction, "DELETE FROM [dbo].[TournamentSponsors] WHERE [SponsorID] = @SponsorID", command => AddParameter(command, "@SponsorID", sponsorId));
            DeleteRow(connection, transaction, "Sponsors", new[] { "SponsorID" }, new Dictionary<string, object>
            {
                ["SponsorID"] = sponsorId
            });
        }

        private void DeleteStageCascadeInternal(SqlConnection connection, SqlTransaction transaction, int stageId)
        {
            ExecuteNonQuery(connection, transaction, @"
DELETE FROM [dbo].[Streams]
WHERE [MatchID] IN (
    SELECT [MatchID]
    FROM [dbo].[Matches]
    WHERE [StageID] = @StageID
)", command => AddParameter(command, "@StageID", stageId));

            ExecuteNonQuery(connection, transaction, "DELETE FROM [dbo].[Matches] WHERE [StageID] = @StageID", command => AddParameter(command, "@StageID", stageId));
            DeleteRow(connection, transaction, "TournamentStages", new[] { "StageID" }, new Dictionary<string, object>
            {
                ["StageID"] = stageId
            });
        }

        private void DeleteMatchCascadeInternal(SqlConnection connection, SqlTransaction transaction, int matchId)
        {
            ExecuteNonQuery(connection, transaction, "DELETE FROM [dbo].[Streams] WHERE [MatchID] = @MatchID", command => AddParameter(command, "@MatchID", matchId));
            DeleteRow(connection, transaction, "Matches", new[] { "MatchID" }, new Dictionary<string, object>
            {
                ["MatchID"] = matchId
            });
        }

        private static int GetRequiredInt(IDictionary<string, object> values, string keyName)
        {
            if (values == null || string.IsNullOrWhiteSpace(keyName) || !values.ContainsKey(keyName) || values[keyName] == null || values[keyName] == DBNull.Value)
            {
                throw new InvalidOperationException("Не удалось определить ключ для каскадного удаления: " + keyName);
            }

            return Convert.ToInt32(values[keyName]);
        }

        private List<int> GetOrderedParticipantIds(int tournamentId, bool isPlayerMode, SqlConnection connection, SqlTransaction transaction)
        {
            DataTable participants = ExecuteTableQuery(connection, transaction, "SELECT * FROM [dbo].[TournamentParticipants] WHERE [TournamentID] = @TournamentID", "TournamentParticipants", command => AddParameter(command, "@TournamentID", tournamentId));
            string columnName = isPlayerMode ? "PlayerID" : "TeamID";
            return participants.Rows
                .Cast<DataRow>()
                .Where(row => row.Table.Columns.Contains(columnName) && row[columnName] != DBNull.Value)
                .OrderBy(row => row["Seed"] == DBNull.Value ? 1 : 0)
                .ThenBy(row => row["Seed"] == DBNull.Value ? int.MaxValue : Convert.ToInt32(row["Seed"]))
                .ThenBy(row => Convert.ToInt32(row[columnName]))
                .Select(row => Convert.ToInt32(row[columnName]))
                .ToList();
        }

        private void RemoveGeneratedBracket(SqlConnection connection, SqlTransaction transaction, int tournamentId)
        {
            ExecuteNonQuery(connection, transaction, @"DELETE ST
FROM [dbo].[Streams] ST
INNER JOIN [dbo].[Matches] M ON M.[MatchID] = ST.[MatchID]
INNER JOIN [dbo].[TournamentStages] S ON S.[StageID] = M.[StageID]
WHERE S.[TournamentID] = @TournamentID
  AND S.[StageName] LIKE 'Bracket - %'", command => AddParameter(command, "@TournamentID", tournamentId));

            ExecuteNonQuery(connection, transaction, @"DELETE M
FROM [dbo].[Matches] M
INNER JOIN [dbo].[TournamentStages] S ON S.[StageID] = M.[StageID]
WHERE S.[TournamentID] = @TournamentID
  AND S.[StageName] LIKE 'Bracket - %'", command => AddParameter(command, "@TournamentID", tournamentId));

            ExecuteNonQuery(connection, transaction, "DELETE FROM [dbo].[TournamentStages] WHERE [TournamentID] = @TournamentID AND [StageName] LIKE 'Bracket - %'", command => AddParameter(command, "@TournamentID", tournamentId));
        }

        private List<SqlBracketRoundState> GetGeneratedBracketRounds(int tournamentId, SqlConnection connection, SqlTransaction transaction)
        {
            bool isPlayerMode = IsPlayerMode(GetTournamentParticipantMode(tournamentId, connection, transaction));
            DataTable stages = ExecuteTableQuery(connection, transaction, "SELECT * FROM [dbo].[TournamentStages] WHERE [TournamentID] = @TournamentID", "TournamentStages", command => AddParameter(command, "@TournamentID", tournamentId));
            DataTable matches = ExecuteTableQuery(connection, transaction, "SELECT * FROM [dbo].[Matches] WHERE [TournamentID] = @TournamentID", "Matches", command => AddParameter(command, "@TournamentID", tournamentId));

            List<DataRow> orderedStages = stages.Rows.Cast<DataRow>()
                .Where(IsBracketStage)
                .OrderBy(row => ReadInt(row["StageOrder"], 0))
                .ThenBy(row => ReadInt(row["StageID"], 0))
                .ToList();

            List<SqlBracketRoundState> rounds = new List<SqlBracketRoundState>();
            for (int roundIndex = 0; roundIndex < orderedStages.Count; roundIndex++)
            {
                DataRow stage = orderedStages[roundIndex];
                SqlBracketRoundState round = new SqlBracketRoundState
                {
                    RoundIndex = roundIndex,
                    Stage = stage,
                    IsPlayerMode = isPlayerMode
                };

                foreach (DataRow match in matches.Rows.Cast<DataRow>().Where(row => AreEqual(row["StageID"], stage["StageID"])).OrderBy(row => ReadInt(row["MatchNumber"], 0)))
                {
                    round.Matches.Add(match);
                }

                if (round.Matches.Count > 0)
                {
                    rounds.Add(round);
                }
            }

            return rounds;
        }

        private void PropagateBracketState(SqlConnection connection, SqlTransaction transaction, int tournamentId)
        {
            List<SqlBracketRoundState> rounds = GetGeneratedBracketRounds(tournamentId, connection, transaction);
            PropagateBracketState(rounds);
            PersistBracketMatches(connection, transaction, rounds);
        }

        private static void PropagateBracketState(List<SqlBracketRoundState> rounds)
        {
            for (int roundIndex = 1; roundIndex < rounds.Count; roundIndex++)
            {
                List<DataRow> previousRoundMatches = rounds[roundIndex - 1].Matches;
                List<DataRow> currentRoundMatches = rounds[roundIndex].Matches;
                for (int matchIndex = 0; matchIndex < currentRoundMatches.Count; matchIndex++)
                {
                    bool isPlayerMode = rounds[roundIndex].IsPlayerMode;
                    int? team1Id = matchIndex * 2 < previousRoundMatches.Count ? ResolveAdvancingTeamId(previousRoundMatches[matchIndex * 2], isPlayerMode) : (int?)null;
                    int? team2Id = matchIndex * 2 + 1 < previousRoundMatches.Count ? ResolveAdvancingTeamId(previousRoundMatches[matchIndex * 2 + 1], isPlayerMode) : (int?)null;
                    ApplyPropagatedTeams(currentRoundMatches[matchIndex], isPlayerMode, team1Id, team2Id);
                }
            }
        }

        private void PersistBracketMatches(SqlConnection connection, SqlTransaction transaction, IEnumerable<SqlBracketRoundState> rounds)
        {
            foreach (SqlBracketRoundState round in rounds)
            {
                foreach (DataRow match in round.Matches)
                {
                    Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Team1ID"] = round.IsPlayerMode ? null : (object)GetParticipantId(match, false, 1),
                        ["Team2ID"] = round.IsPlayerMode ? null : (object)GetParticipantId(match, false, 2),
                        ["WinnerTeamID"] = round.IsPlayerMode ? null : (object)GetParticipantId(match, false, 3),
                        [Player1IdColumn] = round.IsPlayerMode ? (object)GetParticipantId(match, true, 1) : null,
                        [Player2IdColumn] = round.IsPlayerMode ? (object)GetParticipantId(match, true, 2) : null,
                        [WinnerPlayerIdColumn] = round.IsPlayerMode ? (object)GetParticipantId(match, true, 3) : null,
                        ["Team1Score"] = ReadInt(match["Team1Score"], 0),
                        ["Team2Score"] = ReadInt(match["Team2Score"], 0),
                        ["MatchDate"] = NormalizeDateValue(match["MatchDate"]),
                        ["BestOf"] = ReadInt(match["BestOf"], 3),
                        ["Status"] = Convert.ToString(match["Status"])
                    };

                    UpdateRow(connection, transaction, "Matches", new[] { "MatchID" }, values, new Dictionary<string, object>
                    {
                        ["MatchID"] = ReadInt(match["MatchID"], 0)
                    });
                }
            }
        }

        private static void ApplyPropagatedTeams(DataRow match, bool isPlayerMode, int? team1Id, int? team2Id)
        {
            int? currentTeam1Id = GetParticipantId(match, isPlayerMode, 1);
            int? currentTeam2Id = GetParticipantId(match, isPlayerMode, 2);
            bool changed = currentTeam1Id != team1Id || currentTeam2Id != team2Id;
            SetParticipantId(match, isPlayerMode, 1, team1Id);
            SetParticipantId(match, isPlayerMode, 2, team2Id);

            if (changed)
            {
                SetParticipantId(match, isPlayerMode, 3, null);
                match["Team1Score"] = 0;
                match["Team2Score"] = 0;
                match["Status"] = "Scheduled";
                return;
            }

            if (!HasCompatibleWinner(match, isPlayerMode, team1Id, team2Id))
            {
                SetParticipantId(match, isPlayerMode, 3, null);
            }
        }

        private static int? ResolveAdvancingTeamId(DataRow sourceMatch, bool isPlayerMode)
        {
            int? team1Id = GetParticipantId(sourceMatch, isPlayerMode, 1);
            int? team2Id = GetParticipantId(sourceMatch, isPlayerMode, 2);
            int? winnerTeamId = GetParticipantId(sourceMatch, isPlayerMode, 3);
            if (winnerTeamId.HasValue && (winnerTeamId == team1Id || winnerTeamId == team2Id))
            {
                return winnerTeamId;
            }

            if (team1Id.HasValue && !team2Id.HasValue)
            {
                return team1Id;
            }

            if (team2Id.HasValue && !team1Id.HasValue)
            {
                return team2Id;
            }

            string status = Convert.ToString(sourceMatch["Status"]);
            int team1Score = ReadInt(sourceMatch["Team1Score"], 0);
            int team2Score = ReadInt(sourceMatch["Team2Score"], 0);
            if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase) && team1Id.HasValue && team2Id.HasValue && team1Score != team2Score)
            {
                return team1Score > team2Score ? team1Id : team2Id;
            }

            return null;
        }

        private static bool HasCompatibleWinner(DataRow match, bool isPlayerMode, int? team1Id, int? team2Id)
        {
            int? winnerTeamId = GetParticipantId(match, isPlayerMode, 3);
            return !winnerTeamId.HasValue || winnerTeamId == team1Id || winnerTeamId == team2Id;
        }

        private static int? NormalizeBracketTeamId(int? teamId, ICollection<int> availableTeams)
        {
            if (!teamId.HasValue)
            {
                return null;
            }

            if (!availableTeams.Contains(teamId.Value))
            {
                throw new InvalidOperationException("В сетке можно использовать только участников выбранного турнира.");
            }

            return teamId;
        }

        private static int? NormalizeWinnerTeamId(int? winnerTeamId, int? team1Id, int? team2Id, string status, int team1Score, int team2Score)
        {
            if (winnerTeamId.HasValue)
            {
                if (winnerTeamId != team1Id && winnerTeamId != team2Id)
                {
                    throw new InvalidOperationException("Победитель должен совпадать с одним из участников матча.");
                }

                return winnerTeamId;
            }

            if (team1Id.HasValue && !team2Id.HasValue)
            {
                return team1Id;
            }

            if (team2Id.HasValue && !team1Id.HasValue)
            {
                return team2Id;
            }

            if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase) && team1Id.HasValue && team2Id.HasValue && team1Score != team2Score)
            {
                return team1Score > team2Score ? team1Id : team2Id;
            }

            return null;
        }

        private static int? ResolveAutoAdvanceParticipantId(int? team1Id, int? team2Id)
        {
            if (team1Id.HasValue && !team2Id.HasValue)
            {
                return team1Id;
            }

            if (team2Id.HasValue && !team1Id.HasValue)
            {
                return team2Id;
            }

            return null;
        }

        private static int? GetParticipantId(DataRow row, bool isPlayerMode, int slotIndex)
        {
            string preferredColumn = GetParticipantColumnName(isPlayerMode, slotIndex);
            if (row.Table.Columns.Contains(preferredColumn) && row[preferredColumn] != DBNull.Value)
            {
                return Convert.ToInt32(row[preferredColumn]);
            }

            string fallbackColumn = GetParticipantColumnName(!isPlayerMode, slotIndex);
            if (row.Table.Columns.Contains(fallbackColumn) && row[fallbackColumn] != DBNull.Value)
            {
                return Convert.ToInt32(row[fallbackColumn]);
            }

            return null;
        }

        private static void SetParticipantId(DataRow row, bool isPlayerMode, int slotIndex, int? participantId)
        {
            string activeColumn = GetParticipantColumnName(isPlayerMode, slotIndex);
            if (row.Table.Columns.Contains(activeColumn))
            {
                row[activeColumn] = participantId.HasValue ? (object)participantId.Value : DBNull.Value;
            }

            string inactiveColumn = GetParticipantColumnName(!isPlayerMode, slotIndex);
            if (row.Table.Columns.Contains(inactiveColumn))
            {
                row[inactiveColumn] = DBNull.Value;
            }
        }

        private static string GetParticipantColumnName(bool isPlayerMode, int slotIndex)
        {
            switch (slotIndex)
            {
                case 1:
                    return isPlayerMode ? Player1IdColumn : "Team1ID";
                case 2:
                    return isPlayerMode ? Player2IdColumn : "Team2ID";
                case 3:
                    return isPlayerMode ? WinnerPlayerIdColumn : "WinnerTeamID";
                default:
                    throw new ArgumentOutOfRangeException(nameof(slotIndex));
            }
        }

        private static string GetTournamentParticipantMode(DataRow tournament)
        {
            if (tournament == null || !tournament.Table.Columns.Contains("ParticipantMode") || tournament["ParticipantMode"] == DBNull.Value)
            {
                return "Команды";
            }

            string mode = Convert.ToString(tournament["ParticipantMode"]);
            return string.IsNullOrWhiteSpace(mode) ? "Команды" : mode;
        }

        private string GetTournamentParticipantMode(int tournamentId, SqlConnection connection, SqlTransaction transaction)
        {
            using (SqlCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT [ParticipantMode] FROM [dbo].[Tournaments] WHERE [TournamentID] = @TournamentID";
                AddParameter(command, "@TournamentID", tournamentId);
                object result = command.ExecuteScalar();
                return result == null || result == DBNull.Value ? "Команды" : Convert.ToString(result);
            }
        }

        private static bool IsPlayerMode(string participantMode)
        {
            return string.Equals(participantMode, "Игроки", StringComparison.CurrentCultureIgnoreCase);
        }

        private static object NormalizeDateValue(object value)
        {
            if (value == null || value == DBNull.Value)
            {
                return DBNull.Value;
            }

            return value is DateTime dateTime ? (object)dateTime : Convert.ToDateTime(value);
        }

        private static bool IsBracketStage(DataRow row)
        {
            string stageName = Convert.ToString(row["StageName"]);
            int stageOrder = ReadInt(row["StageOrder"], 0);
            return stageOrder >= 100 || stageName.StartsWith("Bracket - ", StringComparison.CurrentCultureIgnoreCase);
        }

        private static int ReadInt(object value, int fallback)
        {
            return value == null || value == DBNull.Value ? fallback : Convert.ToInt32(value);
        }

        private static int? ToNullableInt(object value)
        {
            return value == null || value == DBNull.Value ? (int?)null : Convert.ToInt32(value);
        }

        private static int NextPowerOfTwo(int value)
        {
            int result = 2;
            while (result < value)
            {
                result *= 2;
            }

            return result;
        }

        private static int[] BuildSeedPositions(int size)
        {
            List<int> positions = new List<int> { 1, 2 };
            while (positions.Count < size)
            {
                int nextSize = positions.Count * 2 + 1;
                List<int> expanded = new List<int>();
                foreach (int position in positions)
                {
                    expanded.Add(position);
                    expanded.Add(nextSize - position);
                }

                positions = expanded;
            }

            return positions.ToArray();
        }

        private static string GetRoundTitle(int teamsInRound)
        {
            switch (teamsInRound)
            {
                case 2: return "Grand Final";
                case 4: return "Semifinals";
                case 8: return "Quarterfinals";
                case 16: return "Round of 16";
                case 32: return "Round of 32";
                default: return "Round of " + teamsInRound;
            }
        }

        private static string EscapeIdentifier(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                throw new InvalidOperationException("Имя таблицы или столбца не может быть пустым.");
            }

            foreach (char symbol in identifier)
            {
                if (!char.IsLetterOrDigit(symbol) && symbol != '_')
                {
                    throw new InvalidOperationException("Недопустимое имя таблицы или столбца: " + identifier);
                }
            }

            return identifier;
        }

        private static bool ContainsColumn(IReadOnlyCollection<string> columns, string columnName)
        {
            return columns.Any(column => string.Equals(column, columnName, StringComparison.OrdinalIgnoreCase));
        }

        private static Dictionary<string, DataRow> BuildKeyMap(DataTable table, string tableName, IReadOnlyList<string> keyColumns)
        {
            Dictionary<string, DataRow> rows = new Dictionary<string, DataRow>(StringComparer.OrdinalIgnoreCase);
            foreach (DataRow row in table.Rows)
            {
                rows.Add(BuildKeySignature(tableName, row, keyColumns), row);
            }

            return rows;
        }

        private static string BuildKeySignature(string tableName, DataRow row, IEnumerable<string> keyColumns)
        {
            List<string> parts = new List<string>();
            foreach (string keyColumn in keyColumns)
            {
                if (!row.Table.Columns.Contains(keyColumn))
                {
                    throw new InvalidOperationException("В таблице \"" + tableName + "\" отсутствует ключевой столбец \"" + keyColumn + "\".");
                }

                object value = row[keyColumn];
                if (value == null || value == DBNull.Value)
                {
                    throw new InvalidOperationException("В таблице \"" + tableName + "\" ключевой столбец \"" + keyColumn + "\" содержит пустое значение.");
                }

                parts.Add(FormatKeyValue(value));
            }

            return string.Join("|", parts);
        }

        private static string FormatKeyValue(object value)
        {
            if (value is DateTime dateTime)
            {
                return dateTime.ToString("O", CultureInfo.InvariantCulture);
            }

            if (value is decimal || value is double || value is float)
            {
                return Convert.ToDecimal(value).ToString(CultureInfo.InvariantCulture);
            }

            if (value is bool booleanValue)
            {
                return booleanValue ? "1" : "0";
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static Dictionary<string, object> ToDictionary(DataRow row, Func<string, bool> shouldSkipColumn = null)
        {
            Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (DataColumn column in row.Table.Columns)
            {
                if (shouldSkipColumn != null && shouldSkipColumn(column.ColumnName))
                {
                    continue;
                }

                object value = row[column.ColumnName];
                values[column.ColumnName] = value == DBNull.Value ? null : value;
            }

            return values;
        }

        private static bool IsIdentityColumn(string tableName, string logicalColumnName, string physicalIdentityColumnName)
        {
            if (string.IsNullOrWhiteSpace(physicalIdentityColumnName))
            {
                return false;
            }

            return string.Equals(
                SqlSchemaMap.GetPhysicalColumnName(tableName, logicalColumnName),
                physicalIdentityColumnName,
                StringComparison.OrdinalIgnoreCase);
        }

        private static void AddParameter(SqlCommand command, string parameterName, object value)
        {
            command.Parameters.AddWithValue(parameterName, value ?? DBNull.Value);
        }

        private static bool AreEqual(object left, object right)
        {
            if (left == DBNull.Value) left = null;
            if (right == DBNull.Value) right = null;
            if (left == null && right == null) return true;
            if (left == null || right == null) return false;
            if (left is string || right is string) return string.Equals(Convert.ToString(left), Convert.ToString(right), StringComparison.CurrentCultureIgnoreCase);
            if (left is DateTime || right is DateTime) return Convert.ToDateTime(left).Date == Convert.ToDateTime(right).Date;
            if (left is bool || right is bool) return Convert.ToBoolean(left) == Convert.ToBoolean(right);
            if (left is decimal || right is decimal || left is double || right is double || left is float || right is float) return Convert.ToDecimal(left) == Convert.ToDecimal(right);
            return Convert.ToInt64(left) == Convert.ToInt64(right);
        }

        private sealed class ColumnMetadata
        {
            public ColumnMetadata(int maxLength, bool isNullable)
            {
                MaxLength = maxLength;
                IsNullable = isNullable;
            }

            public int MaxLength { get; }

            public bool IsNullable { get; }
        }

        private sealed class PasswordColumnTarget
        {
            public PasswordColumnTarget(string tableName, string columnName, ColumnMetadata metadata)
            {
                TableName = tableName;
                ColumnName = columnName;
                Metadata = metadata;
            }

            public string TableName { get; }

            public string ColumnName { get; }

            public ColumnMetadata Metadata { get; }
        }

        private sealed class PasswordMigrationRow
        {
            public PasswordMigrationRow(object key, string password)
            {
                Key = key;
                Password = password;
            }

            public object Key { get; }

            public string Password { get; }
        }

        private sealed class SqlBracketRoundState
        {
            public int RoundIndex { get; set; }

            public DataRow Stage { get; set; }

            public bool IsPlayerMode { get; set; }

            public List<DataRow> Matches { get; } = new List<DataRow>();
        }
    }
}
