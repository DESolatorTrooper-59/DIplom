namespace Tournaments.WPF.Models
{
    public sealed class NavigationItem
    {
        public NavigationItem(string title, string pageKey, EntityDefinition entityDefinition)
        {
            Title = title;
            PageKey = pageKey;
            EntityDefinition = entityDefinition;
        }

        public string Title { get; }

        public string PageKey { get; }

        public EntityDefinition EntityDefinition { get; }

        public static NavigationItem ForEntity(EntityDefinition definition)
        {
            return new NavigationItem(definition.Title, "Entity", definition);
        }

        public static NavigationItem ForBracket()
        {
            return new NavigationItem("Турнирная сетка", "Bracket", null);
        }

        public override string ToString()
        {
            return Title;
        }
    }
}
