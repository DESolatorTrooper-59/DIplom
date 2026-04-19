using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using Tournaments.WPF.Models;

namespace Tournaments.WPF.Services
{
    public sealed class DatabaseService
    {
        private const string HiddenPlayerName = "Скрыто";
        private const string UnspecifiedCountry = "Не указано";
        private readonly IDataBackend _backend;
        private readonly Dictionary<string, IReadOnlyCollection<string>> _columnsCache = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);

        private DatabaseService(IDataBackend backend)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        public static DatabaseService CreateInMemory()
        {
            return new DatabaseService(new InMemoryDataBackend(InMemoryDataStore.Instance));
        }

        public static DatabaseService CreateSqlServer(string connectionString, string storageLabel)
        {
            return new DatabaseService(new SqlServerDataBackend(connectionString, storageLabel));
        }

        internal static DatabaseService CreateSnapshot(IDictionary<string, DataTable> tables)
        {
            return new DatabaseService(new SnapshotDataBackend(tables));
        }

        public string ModeTitle => _backend.ModeTitle;

        public string StorageLabel => _backend.StorageLabel;

        public bool IsTestMode => _backend.IsTestMode;

        public DataTable GetTable(string tableName)
        {
            return _backend.GetTable(tableName);
        }

        public bool ValidateLogin(string login, string password)
        {
            return _backend.ValidateLogin(login, password);
        }

        public UserRole? AuthenticateUser(string login, string password)
        {
            if (_backend.ValidateOrganizerLogin(login, password))
            {
                return UserRole.Administrator;
            }

            if (_backend.ValidatePlayerLogin(login, password))
            {
                return UserRole.Player;
            }

            return null;
        }

        public void EnsureOrganizerUser(string login, string password)
        {
            _backend.EnsureOrganizerUser(login, password);
        }

        public void RegisterPlayer(string nickname, DateTime birthDate, string realName, string password)
        {
            string normalizedNickname = (nickname ?? string.Empty).Trim();
            string normalizedRealName = string.IsNullOrWhiteSpace(realName) ? HiddenPlayerName : realName.Trim();
            string normalizedPassword = password ?? string.Empty;

            if (string.IsNullOrWhiteSpace(normalizedNickname))
            {
                throw new InvalidOperationException("Никнейм не может быть пустым.");
            }

            if (string.IsNullOrWhiteSpace(normalizedPassword))
            {
                throw new InvalidOperationException("Пароль не может быть пустым.");
            }

            if (birthDate.Date > DateTime.Today)
            {
                throw new InvalidOperationException("Дата рождения не может быть больше текущей даты.");
            }

            if (RecordExists("Players", "Nickname", normalizedNickname))
            {
                throw new InvalidOperationException("Игрок с таким никнеймом уже существует.");
            }

            if (RecordExists("Organizer", "Login", normalizedNickname))
            {
                throw new InvalidOperationException("Этот никнейм уже занят учетной записью администратора.");
            }

            Insert("Players", new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["Nickname"] = normalizedNickname,
                ["RealName"] = normalizedRealName,
                ["Country"] = UnspecifiedCountry,
                ["BirthDate"] = birthDate.Date,
                ["Password"] = normalizedPassword
            });
        }

        public UserProfileData GetUserProfile(string login, UserRole role)
        {
            switch (role)
            {
                case UserRole.Player:
                    return GetPlayerProfile(login);
                case UserRole.Administrator:
                    return GetAdministratorProfile(login);
                default:
                    return new UserProfileData(UserRole.Guest, "Гость")
                    {
                        Nickname = "Гость",
                        CanEditExtendedProfile = false,
                        CanChangePassword = false
                    };
            }
        }

        public string UpdateUserProfile(UserRole role, string currentLogin, string nickname, string realName, string country, DateTime? birthDate, string newPassword)
        {
            switch (role)
            {
                case UserRole.Player:
                    return UpdatePlayerProfile(currentLogin, nickname, realName, country, birthDate, newPassword);
                case UserRole.Administrator:
                    UpdateAdministratorPassword(currentLogin, newPassword);
                    return currentLogin;
                default:
                    throw new InvalidOperationException("Гостевой профиль нельзя изменять.");
            }
        }

        public bool RecordExists(string tableName, string columnName, object value)
        {
            return _backend.RecordExists(tableName, columnName, value);
        }

        public int CountRows(string tableName, Func<DataRow, bool> predicate)
        {
            return _backend.CountRows(tableName, predicate);
        }

        public int? PeekNextIdentityValue(string tableName)
        {
            return _backend.PeekNextIdentityValue(tableName);
        }

        public void Insert(string tableName, IDictionary<string, object> values)
        {
            _backend.Insert(tableName, values);
        }

        public void Update(string tableName, string[] keyColumns, IDictionary<string, object> values, IDictionary<string, object> originalValues)
        {
            _backend.Update(tableName, keyColumns, values, originalValues);
        }

        public void Delete(string tableName, string[] keyColumns, IDictionary<string, object> originalValues)
        {
            _backend.Delete(tableName, keyColumns, originalValues);
        }

        public void DeleteCascade(string tableName, string[] keyColumns, IDictionary<string, object> originalValues)
        {
            _backend.DeleteCascade(tableName, keyColumns, originalValues);
        }

        public void DeleteTournamentCascade(int tournamentId)
        {
            _backend.DeleteTournamentCascade(tournamentId);
        }

        public IReadOnlyList<string> GetCascadeDependencyLines(string tableName, IDictionary<string, object> originalValues)
        {
            if (string.IsNullOrWhiteSpace(tableName) || originalValues == null)
            {
                return Array.Empty<string>();
            }

            switch (tableName)
            {
                case "Tournaments":
                    return TryGetInt(originalValues, "TournamentID").HasValue
                        ? BuildTournamentDependencyLines(TryGetInt(originalValues, "TournamentID").Value)
                        : Array.Empty<string>();
                case "Teams":
                    return TryGetInt(originalValues, "TeamID").HasValue
                        ? BuildTeamDependencyLines(TryGetInt(originalValues, "TeamID").Value)
                        : Array.Empty<string>();
                case "Players":
                    return TryGetInt(originalValues, "PlayerID").HasValue
                        ? BuildPlayerDependencyLines(TryGetInt(originalValues, "PlayerID").Value)
                        : Array.Empty<string>();
                case "GameTitles":
                    return TryGetInt(originalValues, "GameID").HasValue
                        ? BuildGameDependencyLines(TryGetInt(originalValues, "GameID").Value)
                        : Array.Empty<string>();
                case "Sponsors":
                    return TryGetInt(originalValues, "SponsorID").HasValue
                        ? BuildSponsorDependencyLines(TryGetInt(originalValues, "SponsorID").Value)
                        : Array.Empty<string>();
                case "TournamentStages":
                    return TryGetInt(originalValues, "StageID").HasValue
                        ? BuildStageDependencyLines(TryGetInt(originalValues, "StageID").Value)
                        : Array.Empty<string>();
                case "Matches":
                    return TryGetInt(originalValues, "MatchID").HasValue
                        ? BuildMatchDependencyLines(TryGetInt(originalValues, "MatchID").Value)
                        : Array.Empty<string>();
                default:
                    return Array.Empty<string>();
            }
        }

        public EntityDefinition GetEffectiveDefinition(EntityDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            IReadOnlyCollection<string> columns = GetAvailableColumns(definition.TableName);
            FieldDefinition[] fields = definition.Fields
                .Where(field => ContainsColumn(columns, field.Name))
                .ToArray();
            string[] keyColumns = definition.KeyColumns
                .Where(keyColumn => ContainsColumn(columns, keyColumn))
                .ToArray();

            EntityDefinition effectiveDefinition = new EntityDefinition(definition.TableName, definition.Title, keyColumns, fields);
            effectiveDefinition.SaveValidator = definition.SaveValidator;
            effectiveDefinition.DeleteValidator = definition.DeleteValidator;
            return effectiveDefinition;
        }

        public IReadOnlyList<EntityDefinition> GetEffectiveDefinitions()
        {
            return EntityRegistry.All
                .Select(GetEffectiveDefinition)
                .ToList();
        }

        public IReadOnlyCollection<string> GetAvailableColumns(string tableName)
        {
            if (_columnsCache.TryGetValue(tableName, out IReadOnlyCollection<string> cachedColumns))
            {
                return cachedColumns;
            }

            IReadOnlyCollection<string> columns = _backend.GetColumns(tableName);
            _columnsCache[tableName] = columns;
            return columns;
        }

        public void ValidateCompatibility()
        {
            if (IsTestMode)
            {
                return;
            }

            ValidateRequiredColumns("Organizer", "Login", "Password");
            ValidateRequiredColumns("Players", "Password");
            foreach (EntityDefinition definition in EntityRegistry.All)
            {
                IReadOnlyCollection<string> columns = GetAvailableColumns(definition.TableName);
                foreach (string keyColumn in definition.KeyColumns)
                {
                    if (!ContainsColumn(columns, keyColumn))
                    {
                        throw new InvalidOperationException("В таблице \"" + definition.TableName + "\" отсутствует ключевой столбец \"" + keyColumn + "\".");
                    }
                }

                if (!definition.Fields.Any(field => ContainsColumn(columns, field.Name)))
                {
                    throw new InvalidOperationException("Таблица \"" + definition.TableName + "\" не содержит ни одного поддерживаемого столбца для WPF-клиента.");
                }
            }
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

            EntityDefinition effectiveDefinition = GetEffectiveDefinition(definition);
            ApplyPrimaryKey(importedTable, effectiveDefinition.KeyColumns);
            Dictionary<string, DataTable> snapshotTables = GetEffectiveDefinitions()
                .ToDictionary(item => item.TableName, item => GetTable(item.TableName), StringComparer.OrdinalIgnoreCase);
            ApplyPrimaryKey(snapshotTables[effectiveDefinition.TableName], effectiveDefinition.KeyColumns);

            DatabaseService currentDatabase = CreateSnapshot(snapshotTables);
            ValidateRemovedRows(effectiveDefinition, snapshotTables[effectiveDefinition.TableName], importedTable, currentDatabase);

            DataTable importedCopy = importedTable.Copy();
            importedCopy.TableName = effectiveDefinition.TableName;
            ApplyPrimaryKey(importedCopy, effectiveDefinition.KeyColumns);
            snapshotTables[effectiveDefinition.TableName] = importedCopy;

            DatabaseService validationDatabase = CreateSnapshot(snapshotTables);
            SnapshotValidationService.Validate(validationDatabase.GetEffectiveDefinitions(), validationDatabase);

            _backend.ReplaceTableContents(effectiveDefinition.TableName, importedTable);
        }

        public int GenerateTournamentBracket(int tournamentId)
        {
            return _backend.GenerateTournamentBracket(tournamentId);
        }

        public void UpdateBracketMatch(int tournamentId, BracketMatchUpdateRequest request)
        {
            _backend.UpdateBracketMatch(tournamentId, request);
        }

        private void ValidateRequiredColumns(string tableName, params string[] columns)
        {
            IReadOnlyCollection<string> availableColumns = GetAvailableColumns(tableName);
            foreach (string column in columns)
            {
                if (!ContainsColumn(availableColumns, column))
                {
                    throw new InvalidOperationException("В таблице \"" + tableName + "\" отсутствует обязательный столбец \"" + column + "\".");
                }
            }
        }

        private static bool ContainsColumn(IReadOnlyCollection<string> columns, string columnName)
        {
            return columns.Any(column => string.Equals(column, columnName, StringComparison.OrdinalIgnoreCase));
        }

        private static void ApplyPrimaryKey(DataTable table, IEnumerable<string> keyColumns)
        {
            if (table == null || keyColumns == null)
            {
                return;
            }

            List<DataColumn> columns = keyColumns
                .Where(keyColumn => table.Columns.Contains(keyColumn))
                .Select(keyColumn => table.Columns[keyColumn])
                .ToList();

            if (columns.Count == 0)
            {
                return;
            }

            table.PrimaryKey = columns.ToArray();
        }

        private List<string> BuildTournamentDependencyLines(int tournamentId)
        {
            DataTable stages = GetTable("TournamentStages");
            DataTable participants = GetTable("TournamentParticipants");
            DataTable matches = GetTable("Matches");
            DataTable streams = GetTable("Streams");
            DataTable sponsors = GetTable("TournamentSponsors");

            int stageCount = stages.Rows.Cast<DataRow>().Count(row => ValuesEqual(row["TournamentID"], tournamentId));
            int participantCount = participants.Rows.Cast<DataRow>().Count(row => ValuesEqual(row["TournamentID"], tournamentId));
            HashSet<int> matchIds = GetMatchIds(matches.Rows.Cast<DataRow>().Where(row => ValuesEqual(row["TournamentID"], tournamentId)));
            int streamCount = streams.Rows.Cast<DataRow>().Count(row =>
                ValuesEqual(row["TournamentID"], tournamentId) ||
                (row.Table.Columns.Contains("MatchID") && row["MatchID"] != DBNull.Value && matchIds.Contains(Convert.ToInt32(row["MatchID"]))));
            int sponsorCount = sponsors.Rows.Cast<DataRow>().Count(row => ValuesEqual(row["TournamentID"], tournamentId));

            List<string> result = new List<string>();
            AddDependencyLine(result, "этапы", stageCount);
            AddDependencyLine(result, "участники", participantCount);
            AddDependencyLine(result, "матчи", matchIds.Count);
            AddDependencyLine(result, "трансляции", streamCount);
            AddDependencyLine(result, "спонсоры турнира", sponsorCount);
            return result;
        }

        private List<string> BuildTeamDependencyLines(int teamId)
        {
            DataTable teamPlayers = GetTable("TeamPlayers");
            DataTable participants = GetTable("TournamentParticipants");
            DataTable matches = GetTable("Matches");
            DataTable streams = GetTable("Streams");

            int teamPlayerCount = teamPlayers.Rows.Cast<DataRow>().Count(row => ValuesEqual(row["TeamID"], teamId));
            int participantCount = participants.Rows.Cast<DataRow>().Count(row => ValuesEqual(row["TeamID"], teamId));
            HashSet<int> matchIds = GetMatchIds(matches.Rows.Cast<DataRow>().Where(row =>
                ValuesEqual(row["Team1ID"], teamId) ||
                ValuesEqual(row["Team2ID"], teamId) ||
                ValuesEqual(row["WinnerTeamID"], teamId)));
            int streamCount = streams.Rows.Cast<DataRow>().Count(row =>
                row.Table.Columns.Contains("MatchID") &&
                row["MatchID"] != DBNull.Value &&
                matchIds.Contains(Convert.ToInt32(row["MatchID"])));

            List<string> result = new List<string>();
            AddDependencyLine(result, "составы команды", teamPlayerCount);
            AddDependencyLine(result, "участия в турнирах", participantCount);
            AddDependencyLine(result, "матчи", matchIds.Count);
            AddDependencyLine(result, "трансляции матчей", streamCount);
            return result;
        }

        private List<string> BuildPlayerDependencyLines(int playerId)
        {
            DataTable teamPlayers = GetTable("TeamPlayers");
            DataTable participants = GetTable("TournamentParticipants");
            DataTable matches = GetTable("Matches");
            DataTable streams = GetTable("Streams");

            int teamPlayerCount = teamPlayers.Rows.Cast<DataRow>().Count(row => ValuesEqual(row["PlayerID"], playerId));
            int participantCount = participants.Rows.Cast<DataRow>().Count(row => ValuesEqual(row["PlayerID"], playerId));
            HashSet<int> matchIds = GetMatchIds(matches.Rows.Cast<DataRow>().Where(row =>
                ValuesEqual(row["Player1ID"], playerId) ||
                ValuesEqual(row["Player2ID"], playerId) ||
                ValuesEqual(row["WinnerPlayerID"], playerId)));
            int streamCount = streams.Rows.Cast<DataRow>().Count(row =>
                row.Table.Columns.Contains("MatchID") &&
                row["MatchID"] != DBNull.Value &&
                matchIds.Contains(Convert.ToInt32(row["MatchID"])));

            List<string> result = new List<string>();
            AddDependencyLine(result, "составы команд", teamPlayerCount);
            AddDependencyLine(result, "участия в турнирах", participantCount);
            AddDependencyLine(result, "матчи", matchIds.Count);
            AddDependencyLine(result, "трансляции матчей", streamCount);
            return result;
        }

        private List<string> BuildGameDependencyLines(int gameId)
        {
            DataTable tournaments = GetTable("Tournaments");
            DataTable stages = GetTable("TournamentStages");
            DataTable participants = GetTable("TournamentParticipants");
            DataTable matches = GetTable("Matches");
            DataTable streams = GetTable("Streams");
            DataTable sponsors = GetTable("TournamentSponsors");

            HashSet<int> tournamentIds = new HashSet<int>(
                tournaments.Rows.Cast<DataRow>()
                    .Where(row => ValuesEqual(row["GameID"], gameId))
                    .Select(row => Convert.ToInt32(row["TournamentID"])));
            HashSet<int> matchIds = GetMatchIds(matches.Rows.Cast<DataRow>().Where(row => ValuesEqual(row["TournamentID"], null) ? false : tournamentIds.Contains(Convert.ToInt32(row["TournamentID"]))));

            int stageCount = stages.Rows.Cast<DataRow>().Count(row => ValuesEqual(row["TournamentID"], null) ? false : tournamentIds.Contains(Convert.ToInt32(row["TournamentID"])));
            int participantCount = participants.Rows.Cast<DataRow>().Count(row => ValuesEqual(row["TournamentID"], null) ? false : tournamentIds.Contains(Convert.ToInt32(row["TournamentID"])));
            int streamCount = streams.Rows.Cast<DataRow>().Count(row =>
                (row.Table.Columns.Contains("TournamentID") && row["TournamentID"] != DBNull.Value && tournamentIds.Contains(Convert.ToInt32(row["TournamentID"]))) ||
                (row.Table.Columns.Contains("MatchID") && row["MatchID"] != DBNull.Value && matchIds.Contains(Convert.ToInt32(row["MatchID"]))));
            int sponsorCount = sponsors.Rows.Cast<DataRow>().Count(row => ValuesEqual(row["TournamentID"], null) ? false : tournamentIds.Contains(Convert.ToInt32(row["TournamentID"])));

            List<string> result = new List<string>();
            AddDependencyLine(result, "турниры", tournamentIds.Count);
            AddDependencyLine(result, "этапы турниров", stageCount);
            AddDependencyLine(result, "участники турниров", participantCount);
            AddDependencyLine(result, "матчи", matchIds.Count);
            AddDependencyLine(result, "трансляции", streamCount);
            AddDependencyLine(result, "связи турниров со спонсорами", sponsorCount);
            return result;
        }

        private List<string> BuildSponsorDependencyLines(int sponsorId)
        {
            DataTable tournamentSponsors = GetTable("TournamentSponsors");
            int tournamentSponsorCount = tournamentSponsors.Rows.Cast<DataRow>().Count(row => ValuesEqual(row["SponsorID"], sponsorId));

            List<string> result = new List<string>();
            AddDependencyLine(result, "привязки к турнирам", tournamentSponsorCount);
            return result;
        }

        private List<string> BuildStageDependencyLines(int stageId)
        {
            DataTable matches = GetTable("Matches");
            DataTable streams = GetTable("Streams");

            HashSet<int> matchIds = GetMatchIds(matches.Rows.Cast<DataRow>().Where(row => ValuesEqual(row["StageID"], stageId)));
            int streamCount = streams.Rows.Cast<DataRow>().Count(row =>
                row.Table.Columns.Contains("MatchID") &&
                row["MatchID"] != DBNull.Value &&
                matchIds.Contains(Convert.ToInt32(row["MatchID"])));

            List<string> result = new List<string>();
            AddDependencyLine(result, "матчи", matchIds.Count);
            AddDependencyLine(result, "трансляции", streamCount);
            return result;
        }

        private List<string> BuildMatchDependencyLines(int matchId)
        {
            DataTable streams = GetTable("Streams");
            int streamCount = streams.Rows.Cast<DataRow>().Count(row => ValuesEqual(row["MatchID"], matchId));

            List<string> result = new List<string>();
            AddDependencyLine(result, "трансляции", streamCount);
            return result;
        }

        private static void ValidateRemovedRows(EntityDefinition definition, DataTable currentTable, DataTable importedTable, DatabaseService database)
        {
            if (definition == null || currentTable == null || importedTable == null || definition.DeleteValidator == null)
            {
                return;
            }

            string[] keyColumns = definition.KeyColumns
                .Where(keyColumn => currentTable.Columns.Contains(keyColumn) && importedTable.Columns.Contains(keyColumn))
                .ToArray();
            if (keyColumns.Length == 0)
            {
                return;
            }

            HashSet<string> importedKeys = new HashSet<string>(
                importedTable.Rows.Cast<DataRow>().Select(row => BuildKeySignature(row, keyColumns)),
                StringComparer.OrdinalIgnoreCase);

            foreach (DataRow currentRow in currentTable.Rows)
            {
                if (importedKeys.Contains(BuildKeySignature(currentRow, keyColumns)))
                {
                    continue;
                }

                Dictionary<string, object> values = ToDictionary(currentRow);
                EntityEditContext context = new EntityEditContext(false, values, values, database);
                EntityValidationResult result = definition.DeleteValidator(context);
                if (result.IsValid)
                {
                    continue;
                }

                throw new InvalidOperationException(
                    "Таблица \"" + definition.Title + "\": импорт удаляет запись (" + DescribeKey(currentRow, keyColumns) + "), но это нельзя сделать. " + result.Message);
            }
        }

        private static string BuildKeySignature(DataRow row, IEnumerable<string> keyColumns)
        {
            return string.Join("|", keyColumns.Select(keyColumn => FormatKeyValue(row[keyColumn])));
        }

        private static string DescribeKey(DataRow row, IEnumerable<string> keyColumns)
        {
            return string.Join(", ", keyColumns.Select(keyColumn => keyColumn + "=" + FormatKeyValue(row[keyColumn])));
        }

        private static string FormatKeyValue(object value)
        {
            if (value == null || value == DBNull.Value)
            {
                return "NULL";
            }

            if (value is DateTime dateTime)
            {
                return dateTime.ToString("O", CultureInfo.InvariantCulture);
            }

            if (value is bool booleanValue)
            {
                return booleanValue ? "1" : "0";
            }

            if (value is decimal || value is double || value is float)
            {
                return Convert.ToDecimal(value).ToString(CultureInfo.InvariantCulture);
            }

            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static int? TryGetInt(IDictionary<string, object> values, string keyName)
        {
            if (values == null || string.IsNullOrWhiteSpace(keyName) || !values.ContainsKey(keyName))
            {
                return null;
            }

            object value = values[keyName];
            return value == null || value == DBNull.Value ? (int?)null : Convert.ToInt32(value);
        }

        private static HashSet<int> GetMatchIds(IEnumerable<DataRow> rows)
        {
            return new HashSet<int>(
                rows.Where(row => row.Table.Columns.Contains("MatchID") && row["MatchID"] != DBNull.Value)
                    .Select(row => Convert.ToInt32(row["MatchID"])));
        }

        private static void AddDependencyLine(ICollection<string> lines, string label, int count)
        {
            if (count > 0)
            {
                lines.Add(label + ": " + count);
            }
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

            if (left is string || right is string)
            {
                return string.Equals(Convert.ToString(left), Convert.ToString(right), StringComparison.CurrentCultureIgnoreCase);
            }

            return Convert.ToInt64(left) == Convert.ToInt64(right);
        }

        private static Dictionary<string, object> ToDictionary(DataRow row)
        {
            Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (DataColumn column in row.Table.Columns)
            {
                object value = row[column.ColumnName];
                values[column.ColumnName] = value == DBNull.Value ? null : value;
            }

            return values;
        }

        private UserProfileData GetPlayerProfile(string login)
        {
            DataRow row = GetPlayerRowByNickname(login);
            if (row == null)
            {
                throw new InvalidOperationException("Профиль игрока не найден.");
            }

            return new UserProfileData(UserRole.Player, login)
            {
                Nickname = Convert.ToString(row["Nickname"]),
                RealName = row.Table.Columns.Contains("RealName") && row["RealName"] != DBNull.Value ? Convert.ToString(row["RealName"]) : string.Empty,
                Country = row.Table.Columns.Contains("Country") && row["Country"] != DBNull.Value ? Convert.ToString(row["Country"]) : string.Empty,
                BirthDate = row.Table.Columns.Contains("BirthDate") && row["BirthDate"] != DBNull.Value ? (DateTime?)Convert.ToDateTime(row["BirthDate"]).Date : null,
                CanEditExtendedProfile = true,
                CanChangePassword = true
            };
        }

        private UserProfileData GetAdministratorProfile(string login)
        {
            DataRow row = GetOrganizerRow(login);
            if (row == null)
            {
                throw new InvalidOperationException("Профиль администратора не найден.");
            }

            return new UserProfileData(UserRole.Administrator, login)
            {
                Nickname = login,
                CanEditExtendedProfile = false,
                CanChangePassword = true
            };
        }

        private string UpdatePlayerProfile(string currentLogin, string nickname, string realName, string country, DateTime? birthDate, string newPassword)
        {
            DataRow row = GetPlayerRowByNickname(currentLogin);
            if (row == null)
            {
                throw new InvalidOperationException("Профиль игрока не найден.");
            }

            string normalizedNickname = (nickname ?? string.Empty).Trim();
            string normalizedRealName = string.IsNullOrWhiteSpace(realName) ? HiddenPlayerName : realName.Trim();
            string normalizedCountry = string.IsNullOrWhiteSpace(country) ? UnspecifiedCountry : country.Trim();

            if (string.IsNullOrWhiteSpace(normalizedNickname))
            {
                throw new InvalidOperationException("Никнейм не может быть пустым.");
            }

            if (!birthDate.HasValue)
            {
                throw new InvalidOperationException("Укажите дату рождения.");
            }

            if (birthDate.Value.Date > DateTime.Today)
            {
                throw new InvalidOperationException("Дата рождения не может быть больше текущей даты.");
            }

            int playerId = Convert.ToInt32(row["PlayerID"]);
            if (NicknameExistsForAnotherPlayer(normalizedNickname, playerId))
            {
                throw new InvalidOperationException("Игрок с таким никнеймом уже существует.");
            }

            if (!string.Equals(currentLogin, normalizedNickname, StringComparison.OrdinalIgnoreCase) &&
                RecordExists("Organizer", "Login", normalizedNickname))
            {
                throw new InvalidOperationException("Этот никнейм уже занят учетной записью администратора.");
            }

            string passwordToSave = string.IsNullOrWhiteSpace(newPassword)
                ? Convert.ToString(row["Password"])
                : newPassword;

            Update("Players",
                new[] { "PlayerID" },
                new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Nickname"] = normalizedNickname,
                    ["RealName"] = normalizedRealName,
                    ["Country"] = normalizedCountry,
                    ["BirthDate"] = birthDate.Value.Date,
                    ["Password"] = passwordToSave
                },
                new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PlayerID"] = playerId
                });

            return normalizedNickname;
        }

        private void UpdateAdministratorPassword(string currentLogin, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(newPassword))
            {
                return;
            }

            DataRow row = GetOrganizerRow(currentLogin);
            if (row == null)
            {
                throw new InvalidOperationException("Профиль администратора не найден.");
            }

            Update("Organizer",
                new[] { "Login" },
                new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Password"] = newPassword
                },
                new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Login"] = currentLogin
                });
        }

        private bool NicknameExistsForAnotherPlayer(string nickname, int currentPlayerId)
        {
            DataTable players = GetTable("Players");
            return players.Rows
                .Cast<DataRow>()
                .Any(row =>
                    row["Nickname"] != DBNull.Value &&
                    string.Equals(Convert.ToString(row["Nickname"]), nickname, StringComparison.OrdinalIgnoreCase) &&
                    Convert.ToInt32(row["PlayerID"]) != currentPlayerId);
        }

        private DataRow GetPlayerRowByNickname(string nickname)
        {
            DataTable players = GetTable("Players");
            return players.Rows
                .Cast<DataRow>()
                .FirstOrDefault(row =>
                    row["Nickname"] != DBNull.Value &&
                    string.Equals(Convert.ToString(row["Nickname"]), nickname, StringComparison.OrdinalIgnoreCase));
        }

        private DataRow GetOrganizerRow(string login)
        {
            DataTable organizers = GetTable("Organizer");
            return organizers.Rows
                .Cast<DataRow>()
                .FirstOrDefault(row =>
                    row["Login"] != DBNull.Value &&
                    string.Equals(Convert.ToString(row["Login"]), login, StringComparison.OrdinalIgnoreCase));
        }
    }
}
