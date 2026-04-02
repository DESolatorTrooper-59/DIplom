namespace Tournaments.WPF.Models
{
    public sealed class BracketMatchViewModel
    {
        public int MatchId { get; set; }

        public int RoundIndex { get; set; }

        public int MatchIndex { get; set; }

        public string MatchCode { get; set; }

        public int? Team1Id { get; set; }

        public int? Team2Id { get; set; }

        public int? WinnerTeamId { get; set; }

        public string Team1Name { get; set; }

        public string Team2Name { get; set; }

        public int Team1Score { get; set; }

        public int Team2Score { get; set; }

        public int BestOf { get; set; }

        public System.DateTime? MatchDate { get; set; }

        public string MetaText { get; set; }

        public string Status { get; set; }

        public string StatusText { get; set; }

        public string WinnerName { get; set; }

        public bool IsEditable { get; set; }

        public bool CanEditTeams { get; set; }
    }
}
