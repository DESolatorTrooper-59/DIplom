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

        public void DeleteTournamentCascade(int tournamentId)
        {
            _backend.DeleteTournamentCascade(tournamentId);
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
