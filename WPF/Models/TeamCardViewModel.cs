using System;
using System.Collections.Generic;

namespace Tournaments.WPF.Models
{
    public sealed class TeamCardViewModel
    {
        public int TeamId { get; set; }

        public string TeamName { get; set; }

        public string Country { get; set; }

        public string CoachName { get; set; }

        public DateTime? FoundedDate { get; set; }

        public string FoundedDateText { get; set; }

        public int ActivePlayers { get; set; }

        public string ParticipantCountText { get; set; }

        public bool CanManageData { get; set; }

        public string ManageActionsToolTip { get; set; }

        public IDictionary<string, object> OriginalValues { get; set; }
    }
}
