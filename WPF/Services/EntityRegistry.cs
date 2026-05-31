using System.Collections.Generic;
using Tournaments.WPF.Models;

namespace Tournaments.WPF.Services
{
    public static partial class EntityRegistry
    {
        public static IReadOnlyList<EntityDefinition> All { get; } = BuildDefinitions();

        private static IReadOnlyList<EntityDefinition> BuildDefinitions()
        {
            List<EntityDefinition> definitions = new List<EntityDefinition>
            {
                CreateRolesDefinition(),
                CreateTeamsDefinition(),
                CreatePlayersDefinition(),
                CreateGamesDefinition(),
                CreateTournamentsDefinition(),
                CreateTournamentStagesDefinition(),
                CreateTournamentParticipantsDefinition(),
                CreateTeamPlayersDefinition(),
                CreateMatchesDefinition(),
                CreateStreamsDefinition(),
                CreateSponsorsDefinition(),
                CreateTournamentSponsorsDefinition()
            };

            return definitions.AsReadOnly();
        }
    }
}
