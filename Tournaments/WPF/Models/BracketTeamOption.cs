namespace Tournaments.WPF.Models
{
    public sealed class BracketTeamOption
    {
        public int? TeamId { get; set; }

        public string TeamName { get; set; }

        public string SecondaryText { get; set; }

        public string EmptyLabel { get; set; }

        public string DisplayName
        {
            get
            {
                if (!TeamId.HasValue)
                {
                    return string.IsNullOrWhiteSpace(EmptyLabel) ? "Не выбрано" : EmptyLabel;
                }

                return string.IsNullOrWhiteSpace(SecondaryText)
                    ? TeamName
                    : TeamName + " (" + SecondaryText + ")";
            }
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
