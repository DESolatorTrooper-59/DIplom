using System.Collections.Generic;

namespace Tournaments.WPF.Models
{
    public sealed class FieldDefinition
    {
        public FieldDefinition(string name, string label, FieldType type)
        {
            Name = name;
            Label = label;
            Type = type;
            AllowedValues = new List<string>();
        }

        public string Name { get; }

        public string Label { get; }

        public FieldType Type { get; }

        public bool IsRequired { get; set; }

        public bool IsIdentity { get; set; }

        public bool IsReadOnly { get; set; }

        public bool IsKey { get; set; }

        public IList<string> AllowedValues { get; }
    }
}
