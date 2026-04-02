namespace Tournaments.WPF.Models
{
    public sealed class BracketParticipantViewModel
    {
        public int TeamId { get; set; }

        public int Seed { get; set; }

        public string TeamName { get; set; }

        public string Country { get; set; }

        public string SeedLabel
        {
            get { return "Seed " + Seed; }
        }
    }
}
