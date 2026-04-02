using System.Collections.Generic;

namespace Tournaments.WPF.Models
{
    public sealed class TournamentBracketSnapshot
    {
        public TournamentBracketSnapshot()
        {
            Participants = new List<BracketParticipantViewModel>();
            Rounds = new List<BracketRoundViewModel>();
        }

        public int TournamentId { get; set; }

        public string TournamentName { get; set; }

        public int ParticipantCount { get; set; }

        public int BracketSize { get; set; }

        public int MatchCount { get; set; }

        public bool HasGeneratedBracket { get; set; }

        public string ChampionName { get; set; }

        public List<BracketParticipantViewModel> Participants { get; }

        public List<BracketRoundViewModel> Rounds { get; }
    }
}
