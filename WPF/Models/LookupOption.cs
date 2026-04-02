namespace Tournaments.WPF.Models
{
    public sealed class LookupOption
    {
        public LookupOption(object value, string display)
        {
            Value = value;
            Display = display;
        }

        public object Value { get; }

        public string Display { get; }
    }
}
