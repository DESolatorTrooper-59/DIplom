using System;
using System.Collections.Generic;
using System.Linq;

namespace Tournaments.WPF.Models
{
    public sealed class EntityDefinition
    {
        public EntityDefinition(string tableName, string title, IEnumerable<string> keyColumns, IEnumerable<FieldDefinition> fields)
        {
            TableName = tableName;
            Title = title;
            KeyColumns = keyColumns.ToArray();
            Fields = fields.ToList().AsReadOnly();
            SelectSql = BuildSelectSql();
        }

        public string TableName { get; }

        public string Title { get; }

        public string[] KeyColumns { get; }

        public IReadOnlyList<FieldDefinition> Fields { get; }

        public string SelectSql { get; }

        public Func<EntityEditContext, EntityValidationResult> SaveValidator { get; set; }

        public Func<EntityEditContext, EntityValidationResult> DeleteValidator { get; set; }

        public override string ToString()
        {
            return Title;
        }

        private string BuildSelectSql()
        {
            return "SELECT " +
                   string.Join(", ", Fields.Select(field => "[" + field.Name + "]")) +
                   " FROM [dbo].[" + TableName + "]";
        }
    }
}
