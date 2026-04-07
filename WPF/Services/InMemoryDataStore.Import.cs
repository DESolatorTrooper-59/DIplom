using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace Tournaments.WPF.Services
{
    public sealed partial class InMemoryDataStore
    {
        private InMemoryDataStore(bool initializeData)
        {
            if (initializeData)
            {
                InitializeSchema();
                SeedData();
            }
        }

        internal InMemoryDataStore CloneStore()
        {
            lock (_syncRoot)
            {
                InMemoryDataStore clone = new InMemoryDataStore(false);
                clone.InitializeSchema();

                foreach (KeyValuePair<string, DataTable> tableEntry in _tables)
                {
                    clone._tables[tableEntry.Key] = clone.PrepareTableCopy(tableEntry.Key, tableEntry.Value);
                    clone.RecalculateIdentity(tableEntry.Key);
                }

                return clone;
            }
        }

        internal void ReplaceTableContents(string tableName, DataTable source)
        {
            lock (_syncRoot)
            {
                _tables[tableName] = PrepareTableCopy(tableName, source);
                RecalculateIdentity(tableName);
            }
        }

        private DataTable PrepareTableCopy(string tableName, DataTable source)
        {
            DataTable schemaTable = GetRequiredTable(tableName);
            DataTable result = schemaTable.Clone();
            result.TableName = tableName;

            foreach (DataRow sourceRow in source.Rows)
            {
                DataRow newRow = result.NewRow();
                foreach (DataColumn column in result.Columns)
                {
                    object value = source.Columns.Contains(column.ColumnName) ? sourceRow[column.ColumnName] : null;
                    newRow[column.ColumnName] = NormalizeValue(column.DataType, value);
                }

                ApplyDefaults(tableName, newRow);
                result.Rows.Add(newRow);
            }

            return result;
        }

        private void RecalculateIdentity(string tableName)
        {
            if (!_identityColumns.TryGetValue(tableName, out string identityColumn))
            {
                return;
            }

            DataTable table = GetRequiredTable(tableName);
            int maxValue = table.Rows
                .Cast<DataRow>()
                .Where(row => row[identityColumn] != DBNull.Value)
                .Select(row => Convert.ToInt32(row[identityColumn]))
                .DefaultIfEmpty(0)
                .Max();

            _nextIdentities[tableName] = maxValue + 1;
        }
    }
}
