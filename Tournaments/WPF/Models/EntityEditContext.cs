using System.Collections.Generic;
using Tournaments.WPF.Services;

namespace Tournaments.WPF.Models
{
    public sealed class EntityEditContext
    {
        public EntityEditContext(bool isInsert, IDictionary<string, object> values, IDictionary<string, object> originalValues, DatabaseService database)
        {
            IsInsert = isInsert;
            Values = values;
            OriginalValues = originalValues;
            Database = database;
        }

        public bool IsInsert { get; }

        public IDictionary<string, object> Values { get; }

        public IDictionary<string, object> OriginalValues { get; }

        public DatabaseService Database { get; }
    }
}
