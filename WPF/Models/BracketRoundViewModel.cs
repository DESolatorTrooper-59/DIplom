using System.Collections.Generic;

namespace Tournaments.WPF.Models
{
    public sealed class BracketRoundViewModel
    {
        public BracketRoundViewModel()
        {
            Matches = new List<BracketMatchViewModel>();
        }

        public string Title { get; set; }

        public List<BracketMatchViewModel> Matches { get; }
    }
}
