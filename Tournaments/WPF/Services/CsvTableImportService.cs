using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Tournaments.WPF.Models;

namespace Tournaments.WPF.Services
{
    public static class CsvTableImportService
    {
        private static readonly string[] SupportedDateFormats =
        {
            "dd.MM.yyyy",
            "d.M.yyyy",
            "dd.MM.yyyy HH:mm:ss",
            "d.M.yyyy H:mm:ss",
            "dd.MM.yyyy HH:mm",
            "d.M.yyyy H:mm",
            "yyyy-MM-dd",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-ddTHH:mm:ss",
            "O",
            "s",
            "G",
            "g"
        };

        public static DataTable Load(string filePath, EntityDefinition definition, DataTable schemaTable)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (schemaTable == null)
            {
                throw new ArgumentNullException(nameof(schemaTable));
            }

            string content = File.ReadAllText(filePath, Encoding.UTF8);
            List<List<string>> rows = ParseCsv(content);
            if (rows.Count == 0)
            {
                throw new InvalidOperationException("CSV-файл пуст.");
            }

            List<string> headers = rows[0];
            Dictionary<int, FieldDefinition> headerMap = MapHeaders(headers, definition);
            DataTable result = schemaTable.Clone();
            result.TableName = definition.TableName;

            for (int rowIndex = 1; rowIndex < rows.Count; rowIndex++)
            {
                List<string> csvRow = rows[rowIndex];
                if (csvRow.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                if (csvRow.Count != headers.Count)
                {
                    throw new InvalidOperationException($"Строка {rowIndex + 1} содержит {csvRow.Count} столбцов, ожидалось {headers.Count}.");
                }

                DataRow row = result.NewRow();
                foreach (DataColumn column in result.Columns)
                {
                    row[column.ColumnName] = DBNull.Value;
                }

                for (int columnIndex = 0; columnIndex < headers.Count; columnIndex++)
                {
                    FieldDefinition field = headerMap[columnIndex];
                    DataColumn column = result.Columns[field.Name];
                    row[column.ColumnName] = ParseValue(csvRow[columnIndex], column.DataType, field, rowIndex + 1);
                }

                result.Rows.Add(row);
            }

            return result;
        }

        private static Dictionary<int, FieldDefinition> MapHeaders(IReadOnlyList<string> headers, EntityDefinition definition)
        {
            Dictionary<string, FieldDefinition> availableFields = new Dictionary<string, FieldDefinition>(StringComparer.CurrentCultureIgnoreCase);
            foreach (FieldDefinition field in definition.Fields)
            {
                availableFields[field.Name.Trim()] = field;
                availableFields[field.Label.Trim()] = field;
            }

            Dictionary<int, FieldDefinition> headerMap = new Dictionary<int, FieldDefinition>();
            HashSet<string> mappedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < headers.Count; index++)
            {
                string header = (headers[index] ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(header))
                {
                    throw new InvalidOperationException($"Заголовок столбца {index + 1} пуст.");
                }

                if (!availableFields.TryGetValue(header, out FieldDefinition field))
                {
                    throw new InvalidOperationException($"Неизвестный столбец \"{header}\" в CSV-файле.");
                }

                if (!mappedFields.Add(field.Name))
                {
                    throw new InvalidOperationException($"Столбец \"{header}\" указан в CSV больше одного раза.");
                }

                headerMap[index] = field;
            }

            List<string> missingFields = definition.Fields
                .Where(field => !mappedFields.Contains(field.Name))
                .Select(field => field.Label)
                .ToList();

            if (missingFields.Count > 0)
            {
                throw new InvalidOperationException("В CSV отсутствуют столбцы: " + string.Join(", ", missingFields) + ".");
            }

            return headerMap;
        }

        private static object ParseValue(string rawValue, Type targetType, FieldDefinition field, int rowNumber)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return DBNull.Value;
            }

            string value = rawValue.Trim();
            if (field.Type == FieldType.Choice && field.AllowedValues.Count > 0)
            {
                string allowedValue = field.AllowedValues.FirstOrDefault(item => string.Equals(item, value, StringComparison.CurrentCultureIgnoreCase));
                if (allowedValue != null)
                {
                    value = allowedValue;
                }
            }

            try
            {
                if (targetType == typeof(string))
                {
                    return value;
                }

                if (targetType == typeof(int))
                {
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.CurrentCulture, out int intValue) ||
                        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
                    {
                        return intValue;
                    }

                    throw new InvalidOperationException();
                }

                if (targetType == typeof(decimal))
                {
                    if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal decimalValue) ||
                        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimalValue))
                    {
                        return decimalValue;
                    }

                    throw new InvalidOperationException();
                }

                if (targetType == typeof(bool))
                {
                    if (bool.TryParse(value, out bool boolValue))
                    {
                        return boolValue;
                    }

                    switch (value.Trim().ToLowerInvariant())
                    {
                        case "1":
                        case "да":
                        case "yes":
                        case "y":
                            return true;
                        case "0":
                        case "нет":
                        case "no":
                        case "n":
                            return false;
                    }

                    throw new InvalidOperationException();
                }

                if (targetType == typeof(DateTime))
                {
                    CultureInfo[] cultures =
                    {
                        CultureInfo.GetCultureInfo("ru-RU"),
                        CultureInfo.CurrentCulture,
                        CultureInfo.InvariantCulture
                    };

                    foreach (CultureInfo culture in cultures)
                    {
                        if (DateTime.TryParseExact(value, SupportedDateFormats, culture, DateTimeStyles.AllowWhiteSpaces, out DateTime exactDate))
                        {
                            return exactDate;
                        }

                        if (DateTime.TryParse(value, culture, DateTimeStyles.AllowWhiteSpaces, out DateTime parsedDate))
                        {
                            return parsedDate;
                        }
                    }

                    throw new InvalidOperationException();
                }

                return value;
            }
            catch (InvalidOperationException)
            {
                throw new InvalidOperationException($"Не удалось преобразовать значение \"{rawValue}\" в поле \"{field.Label}\" на строке {rowNumber}.");
            }
        }

        private static List<List<string>> ParseCsv(string content)
        {
            List<List<string>> rows = new List<List<string>>();
            List<string> currentRow = new List<string>();
            StringBuilder currentField = new StringBuilder();
            bool inQuotes = false;

            for (int index = 0; index < content.Length; index++)
            {
                char symbol = content[index];

                if (symbol == '"')
                {
                    if (inQuotes && index + 1 < content.Length && content[index + 1] == '"')
                    {
                        currentField.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }

                    continue;
                }

                if (symbol == ';' && !inQuotes)
                {
                    currentRow.Add(currentField.ToString());
                    currentField.Clear();
                    continue;
                }

                if ((symbol == '\r' || symbol == '\n') && !inQuotes)
                {
                    currentRow.Add(currentField.ToString());
                    currentField.Clear();
                    rows.Add(currentRow);
                    currentRow = new List<string>();

                    if (symbol == '\r' && index + 1 < content.Length && content[index + 1] == '\n')
                    {
                        index++;
                    }

                    continue;
                }

                currentField.Append(symbol);
            }

            if (inQuotes)
            {
                throw new InvalidOperationException("CSV-файл содержит незакрытые кавычки.");
            }

            if (currentField.Length > 0 || currentRow.Count > 0)
            {
                currentRow.Add(currentField.ToString());
                rows.Add(currentRow);
            }

            return rows;
        }
    }
}
