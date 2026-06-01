using System;

namespace Tournaments.WPF.Models
{
    public sealed class TournamentOption
    {
        public int TournamentId { get; set; }

        public string TournamentName { get; set; }

        public string Organizer { get; set; }

        public DateTime StartDate { get; set; }

        public string DisplayName
        {
            get { return TournamentName + " (" + StartDate.ToString("dd.MM.yyyy") + ")"; }
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
