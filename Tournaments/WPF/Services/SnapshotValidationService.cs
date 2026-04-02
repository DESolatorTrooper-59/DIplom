using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using Tournaments.WPF.Models;

namespace Tournaments.WPF.Services
{
    public static class SnapshotValidationService
    {
        public static void Validate(IReadOnlyList<EntityDefinition> definitions, DatabaseService database)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            foreach (EntityDefinition definition in definitions)
            {
                DataTable table = database.GetTable(definition.TableName);
                ValidateKeys(definition, table);

                for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
                {
                    DataRow row = table.Rows[rowIndex];
                    Dictionary<string, object> values = ToDictionary(row);

                    ValidateRequiredFields(definition, values, rowIndex + 1);
                    ValidateAllowedValues(definition, values, rowIndex + 1);
                    ValidateRow(definition, database, values, rowIndex + 1);
                }
            }
        }

        private static void ValidateKeys(EntityDefinition definition, DataTable table)
        {
            if (definition.KeyColumns == null || definition.KeyColumns.Length == 0)
            {
                return;
            }

            HashSet<string> seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
            {
                DataRow row = table.Rows[rowIndex];
                string signature = BuildKeySignature(definition, row, rowIndex + 1);
                if (!seenKeys.Add(signature))
                {
                    throw new InvalidOperationException($"Таблица \"{definition.Title}\": обнаружен дубликат ключа на строке {rowIndex + 1}.");
                }
            }
        }

        private static string BuildKeySignature(EntityDefinition definition, DataRow row, int rowNumber)
        {
            List<string> parts = new List<string>();
            foreach (string keyColumn in definition.KeyColumns)
            {
                object value = row[keyColumn];
                if (value == null || value == DBNull.Value)
                {
                    throw new InvalidOperationException($"Таблица \"{definition.Title}\": ключевое поле \"{keyColumn}\" не заполнено на строке {rowNumber}.");
                }

                if (value is DateTime dateTime)
                {
                    parts.Add(dateTime.ToString("O", CultureInfo.InvariantCulture));
                }
                else if (value is decimal || value is double || value is float)
                {
                    parts.Add(Convert.ToDecimal(value).ToString(CultureInfo.InvariantCulture));
                }
                else if (value is bool booleanValue)
                {
                    parts.Add(booleanValue ? "1" : "0");
                }
                else
                {
                    parts.Add(Convert.ToString(value, CultureInfo.InvariantCulture));
                }
            }

            return string.Join("|", parts);
        }

        private static void ValidateRequiredFields(EntityDefinition definition, IDictionary<string, object> values, int rowNumber)
        {
            foreach (FieldDefinition field in definition.Fields.Where(item => item.IsRequired))
            {
                object value = values.ContainsKey(field.Name) ? values[field.Name] : null;
                if (IsRequiredValueMissing(field, value))
                {
                    throw new InvalidOperationException($"Таблица \"{definition.Title}\": обязательное поле \"{field.Label}\" не заполнено на строке {rowNumber}.");
                }
            }
        }

        private static void ValidateAllowedValues(EntityDefinition definition, IDictionary<string, object> values, int rowNumber)
        {
            foreach (FieldDefinition field in definition.Fields.Where(item => item.Type == FieldType.Choice && item.AllowedValues.Count > 0))
            {
                object value = values.ContainsKey(field.Name) ? values[field.Name] : null;
                if (value == null)
                {
                    continue;
                }

                string text = Convert.ToString(value);
                bool isAllowed = field.AllowedValues.Any(item => string.Equals(item, text, StringComparison.CurrentCultureIgnoreCase));
                if (!isAllowed)
                {
                    throw new InvalidOperationException($"Таблица \"{definition.Title}\": значение \"{text}\" недопустимо для поля \"{field.Label}\" на строке {rowNumber}.");
                }
            }
        }

        private static void ValidateRow(EntityDefinition definition, DatabaseService database, IDictionary<string, object> values, int rowNumber)
        {
            try
            {
                EntityEditContext context = new EntityEditContext(false, values, values, database);
                EntityValidationResult result = definition.SaveValidator == null
                    ? EntityValidationResult.Success()
                    : definition.SaveValidator(context);

                if (!result.IsValid)
                {
                    throw new InvalidOperationException($"Таблица \"{definition.Title}\": {result.Message} Строка {rowNumber}.");
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Таблица \"{definition.Title}\": ошибка проверки строки {rowNumber}: {ex.Message}", ex);
            }
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

        private static bool IsRequiredValueMissing(FieldDefinition field, object value)
        {
            if (field.Type == FieldType.Boolean)
            {
                return value == null;
            }

            if (value == null)
            {
                return true;
            }

            string text = value as string;
            return text != null && string.IsNullOrWhiteSpace(text);
        }
    }
}
