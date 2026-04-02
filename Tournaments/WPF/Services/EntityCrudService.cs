using System.Collections.Generic;
using System.Data;
using System.Linq;
using Tournaments.WPF.Models;

namespace Tournaments.WPF.Services
{
    public sealed class EntityCrudService
    {
        private readonly DatabaseService _database;

        public EntityCrudService(DatabaseService database)
        {
            _database = database;
        }

        public DataTable Load(EntityDefinition definition)
        {
            return _database.GetTable(definition.TableName);
        }

        public void Insert(EntityDefinition definition, IDictionary<string, object> values)
        {
            Dictionary<string, object> payload = GetWritableFields(definition)
                .ToDictionary(field => field.Name, field => values.ContainsKey(field.Name) ? values[field.Name] : null);

            _database.Insert(definition.TableName, payload);
        }

        public void Update(EntityDefinition definition, IDictionary<string, object> values, IDictionary<string, object> originalValues)
        {
            Dictionary<string, object> payload = GetWritableFields(definition)
                .Where(field => values.ContainsKey(field.Name))
                .ToDictionary(field => field.Name, field => values[field.Name]);

            _database.Update(definition.TableName, definition.KeyColumns, payload, originalValues);
        }

        public void Delete(EntityDefinition definition, IDictionary<string, object> originalValues)
        {
            _database.Delete(definition.TableName, definition.KeyColumns, originalValues);
        }

        private static List<FieldDefinition> GetWritableFields(EntityDefinition definition)
        {
            return definition.Fields
                .Where(field => !field.IsIdentity && !field.IsReadOnly)
                .ToList();
        }
    }
}
