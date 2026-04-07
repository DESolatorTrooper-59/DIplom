using System;
using System.Collections.Generic;
using System.Data;
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

            Insert("Players", new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["Nickname"] = normalizedNickname,
                ["RealName"] = normalizedRealName,
                ["Country"] = UnspecifiedCountry,
                ["BirthDate"] = birthDate.Date,
                ["Password"] = normalizedPassword
            });
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
            Dictionary<string, DataTable> snapshotTables = GetEffectiveDefinitions()
                .ToDictionary(item => item.TableName, item => GetTable(item.TableName), StringComparer.OrdinalIgnoreCase);

            DataTable importedCopy = importedTable.Copy();
            importedCopy.TableName = effectiveDefinition.TableName;
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
    }
}
