using System;

namespace Tournaments.WPF.Models
{
    public sealed class BracketMatchUpdateRequest
    {
        public int MatchId { get; set; }

        public int? Team1Id { get; set; }

        public int? Team2Id { get; set; }

        public int? WinnerTeamId { get; set; }

        public int Team1Score { get; set; }

        public int Team2Score { get; set; }

        public int BestOf { get; set; }

        public DateTime MatchDate { get; set; }

        public string Status { get; set; }
    }
}
