using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using Tournaments.WPF.Models;

namespace Tournaments.WPF.Services
{
    public sealed class TournamentBracketService
    {
        private readonly DatabaseService _database;

        public TournamentBracketService(DatabaseService database)
        {
            _database = database;
        }

        public List<TournamentOption> GetTournaments()
        {
            return _database.GetTable("Tournaments")
                .Rows
                .Cast<DataRow>()
                .OrderBy(row => Convert.ToDateTime(row["StartDate"]))
                .Select(row => new TournamentOption
                {
                    TournamentId = Convert.ToInt32(row["TournamentID"]),
                    TournamentName = Convert.ToString(row["TournamentName"]),
                    StartDate = Convert.ToDateTime(row["StartDate"])
                })
                .ToList();
        }

        public List<BracketTeamOption> GetTeamOptions(int tournamentId)
        {
            DataTable participants = _database.GetTable("TournamentParticipants");
            DataTable teams = _database.GetTable("Teams");
            Dictionary<int, DataRow> teamsById = teams.Rows.Cast<DataRow>().ToDictionary(row => Convert.ToInt32(row["TeamID"]));

            return participants.Rows
                .Cast<DataRow>()
                .Where(row => Convert.ToInt32(row["TournamentID"]) == tournamentId)
                .OrderBy(row => row["Seed"] == DBNull.Value ? 1 : 0)
                .ThenBy(row => row["Seed"] == DBNull.Value ? int.MaxValue : Convert.ToInt32(row["Seed"]))
                .ThenBy(row => GetTeamName(teamsById, Convert.ToInt32(row["TeamID"])))
                .Select(row =>
                {
                    int teamId = Convert.ToInt32(row["TeamID"]);
                    DataRow team = teamsById[teamId];
                    return new BracketTeamOption
                    {
                        TeamId = teamId,
                        TeamName = Convert.ToString(team["TeamName"]),
                        SecondaryText = Convert.ToString(team["Country"])
                    };
                })
                .ToList();
        }

        public TournamentBracketSnapshot BuildPreview(int tournamentId)
        {
            DataTable tournaments = _database.GetTable("Tournaments");
            DataRow tournament = tournaments.Rows.Cast<DataRow>().FirstOrDefault(row => Convert.ToInt32(row["TournamentID"]) == tournamentId);
            if (tournament == null)
            {
                return new TournamentBracketSnapshot();
            }

            TournamentBracketSnapshot snapshot = new TournamentBracketSnapshot
            {
                TournamentId = tournamentId,
                TournamentName = Convert.ToString(tournament["TournamentName"])
            };

            List<BracketParticipantViewModel> participants = GetParticipants(tournamentId);
            foreach (BracketParticipantViewModel participant in participants)
            {
                snapshot.Participants.Add(participant);
            }

            snapshot.ParticipantCount = participants.Count;
            if (participants.Count < 2)
            {
                return snapshot;
            }

            snapshot.BracketSize = NextPowerOfTwo(participants.Count);
            DateTime tournamentDate = Convert.ToDateTime(tournament["StartDate"]);
            List<BracketRoundViewModel> rounds = BuildPersistedRounds(tournamentId);
            snapshot.HasGeneratedBracket = rounds.Count > 0;

            if (!snapshot.HasGeneratedBracket)
            {
                rounds = BuildPreviewRounds(participants, tournamentDate);
            }

            foreach (BracketRoundViewModel round in rounds)
            {
                snapshot.Rounds.Add(round);
            }

            snapshot.MatchCount = rounds.Sum(round => round.Matches.Count);
            snapshot.ChampionName = ResolveChampionName(rounds);
            return snapshot;
        }

        public int GenerateBracket(int tournamentId)
        {
            return _database.GenerateTournamentBracket(tournamentId);
        }

        public void UpdateMatch(int tournamentId, BracketMatchUpdateRequest request)
        {
            _database.UpdateBracketMatch(tournamentId, request);
        }

        private List<BracketParticipantViewModel> GetParticipants(int tournamentId)
        {
            DataTable participants = _database.GetTable("TournamentParticipants");
            DataTable teams = _database.GetTable("Teams");
            Dictionary<int, DataRow> teamsById = teams.Rows.Cast<DataRow>().ToDictionary(row => Convert.ToInt32(row["TeamID"]));

            List<DataRow> orderedRows = participants.Rows
                .Cast<DataRow>()
                .Where(row => Convert.ToInt32(row["TournamentID"]) == tournamentId)
                .OrderBy(row => row["Seed"] == DBNull.Value ? 1 : 0)
                .ThenBy(row => row["Seed"] == DBNull.Value ? int.MaxValue : Convert.ToInt32(row["Seed"]))
                .ThenBy(row => GetTeamName(teamsById, Convert.ToInt32(row["TeamID"])))
                .ToList();

            List<BracketParticipantViewModel> result = new List<BracketParticipantViewModel>();
            for (int index = 0; index < orderedRows.Count; index++)
            {
                DataRow row = orderedRows[index];
                int teamId = Convert.ToInt32(row["TeamID"]);
                DataRow team = teamsById[teamId];
                result.Add(new BracketParticipantViewModel
                {
                    TeamId = teamId,
                    Seed = index + 1,
                    TeamName = Convert.ToString(team["TeamName"]),
                    Country = Convert.ToString(team["Country"])
                });
            }

            return result;
        }

        private List<BracketRoundViewModel> BuildPersistedRounds(int tournamentId)
        {
            DataTable stages = _database.GetTable("TournamentStages");
            DataTable matches = _database.GetTable("Matches");
            DataTable teams = _database.GetTable("Teams");
            Dictionary<int, DataRow> teamsById = teams.Rows.Cast<DataRow>().ToDictionary(row => Convert.ToInt32(row["TeamID"]));

            List<DataRow> bracketStages = stages.Rows
                .Cast<DataRow>()
                .Where(row => Convert.ToInt32(row["TournamentID"]) == tournamentId && IsBracketStage(row))
                .OrderBy(row => Convert.ToInt32(row["StageOrder"]))
                .ThenBy(row => Convert.ToInt32(row["StageID"]))
                .ToList();

            if (bracketStages.Count == 0)
            {
                return new List<BracketRoundViewModel>();
            }

            List<BracketRoundViewModel> rounds = new List<BracketRoundViewModel>();
            List<BracketMatchViewModel> previousRound = new List<BracketMatchViewModel>();

            for (int roundIndex = 0; roundIndex < bracketStages.Count; roundIndex++)
            {
                DataRow stage = bracketStages[roundIndex];
                List<DataRow> roundMatches = matches.Rows
                    .Cast<DataRow>()
                    .Where(row => Convert.ToInt32(row["TournamentID"]) == tournamentId && Convert.ToInt32(row["StageID"]) == Convert.ToInt32(stage["StageID"]))
                    .OrderBy(row => Convert.ToInt32(row["MatchNumber"]))
                    .ToList();

                if (roundMatches.Count == 0)
                {
                    continue;
                }

                BracketRoundViewModel round = new BracketRoundViewModel
                {
                    Title = LocalizeRoundTitle(Convert.ToString(stage["StageName"]))
                };

                for (int matchIndex = 0; matchIndex < roundMatches.Count; matchIndex++)
                {
                    DataRow row = roundMatches[matchIndex];
                    int? team1Id = ToNullableInt(row["Team1ID"]);
                    int? team2Id = ToNullableInt(row["Team2ID"]);
                    int? winnerTeamId = ResolveWinnerTeamId(row, team1Id, team2Id);
                    string team1Name = ResolveStoredTeamName(team1Id, roundIndex, matchIndex * 2, previousRound, teamsById, true);
                    string team2Name = ResolveStoredTeamName(team2Id, roundIndex, matchIndex * 2 + 1, previousRound, teamsById, false);
                    string winnerName = winnerTeamId.HasValue ? ResolveTeamName(teamsById, winnerTeamId.Value) : string.Empty;
                    int bestOf = ReadInt(row["BestOf"], 3);
                    DateTime? matchDate = ParseDate(row["MatchDate"]);
                    string status = Convert.ToString(row["Status"]);

                    round.Matches.Add(new BracketMatchViewModel
                    {
                        MatchId = Convert.ToInt32(row["MatchID"]),
                        RoundIndex = roundIndex,
                        MatchIndex = matchIndex,
                        MatchCode = "M" + Convert.ToInt32(row["MatchNumber"]),
                        Team1Id = team1Id,
                        Team2Id = team2Id,
                        WinnerTeamId = winnerTeamId,
                        Team1Name = team1Name,
                        Team2Name = team2Name,
                        Team1Score = ReadInt(row["Team1Score"], 0),
                        Team2Score = ReadInt(row["Team2Score"], 0),
                        BestOf = bestOf,
                        MatchDate = matchDate,
                        MetaText = BuildMetaText(bestOf, matchDate),
                        Status = status,
                        StatusText = BuildStatusText(status, winnerName),
                        WinnerName = winnerName,
                        IsEditable = true,
                        CanEditTeams = roundIndex == 0
                    });
                }

                rounds.Add(round);
                previousRound = round.Matches.ToList();
            }

            return rounds;
        }

        private static List<BracketRoundViewModel> BuildPreviewRounds(List<BracketParticipantViewModel> participants, DateTime tournamentStartDate)
        {
            int bracketSize = NextPowerOfTwo(participants.Count);
            int[] seedPositions = BuildSeedPositions(bracketSize);
            BracketParticipantViewModel[] slots = new BracketParticipantViewModel[bracketSize];
            for (int index = 0; index < seedPositions.Length; index++)
            {
                int seed = seedPositions[index];
                if (seed <= participants.Count)
                {
                    slots[index] = participants[seed - 1];
                }
            }

            List<BracketRoundViewModel> rounds = new List<BracketRoundViewModel>();
            List<BracketMatchViewModel> previousRound = new List<BracketMatchViewModel>();
            int roundCount = (int)Math.Log(bracketSize, 2);
            int globalMatchNumber = 1;

            for (int roundIndex = 0; roundIndex < roundCount; roundIndex++)
            {
                int matchesInRound = bracketSize / (int)Math.Pow(2, roundIndex + 1);
                BracketRoundViewModel round = new BracketRoundViewModel
                {
                    Title = GetRoundTitle(bracketSize / (int)Math.Pow(2, roundIndex))
                };

                for (int matchIndex = 0; matchIndex < matchesInRound; matchIndex++)
                {
                    BracketMatchViewModel match = new BracketMatchViewModel
                    {
                        MatchId = 0,
                        RoundIndex = roundIndex,
                        MatchIndex = matchIndex,
                        MatchCode = "M" + globalMatchNumber++,
                        Team1Score = 0,
                        Team2Score = 0,
                        BestOf = roundIndex == roundCount - 1 ? 5 : 3,
                        MatchDate = tournamentStartDate.AddDays(roundIndex),
                        Status = "Scheduled",
                        StatusText = BuildStatusText("Scheduled", string.Empty),
                        IsEditable = false,
                        CanEditTeams = false
                    };

                    if (roundIndex == 0)
                    {
                        BracketParticipantViewModel team1 = slots[matchIndex * 2];
                        BracketParticipantViewModel team2 = slots[matchIndex * 2 + 1];
                        match.Team1Id = team1 == null ? (int?)null : team1.TeamId;
                        match.Team2Id = team2 == null ? (int?)null : team2.TeamId;
                        match.Team1Name = team1 == null ? "BYE" : team1.TeamName;
                        match.Team2Name = team2 == null ? "BYE" : team2.TeamName;
                    }
                    else
                    {
                        BracketMatchViewModel source1 = previousRound[matchIndex * 2];
                        BracketMatchViewModel source2 = previousRound[matchIndex * 2 + 1];
                        match.Team1Name = "Победитель " + source1.MatchCode;
                        match.Team2Name = "Победитель " + source2.MatchCode;
                    }

                    match.MetaText = BuildMetaText(match.BestOf, match.MatchDate);
                    round.Matches.Add(match);
                }

                rounds.Add(round);
                previousRound = round.Matches.ToList();
            }

            return rounds;
        }

        private static int NextPowerOfTwo(int value)
        {
            int result = 2;
            while (result < value)
            {
                result *= 2;
            }

            return result;
        }

        private static int[] BuildSeedPositions(int size)
        {
            List<int> positions = new List<int> { 1, 2 };
            while (positions.Count < size)
            {
                int nextSize = positions.Count * 2 + 1;
                List<int> expanded = new List<int>();
                foreach (int position in positions)
                {
                    expanded.Add(position);
                    expanded.Add(nextSize - position);
                }

                positions = expanded;
            }

            return positions.ToArray();
        }

        private static string GetRoundTitle(double teamsInRound)
        {
            switch ((int)teamsInRound)
            {
                case 2:
                    return "Финал";
                case 4:
                    return "Полуфинал";
                case 8:
                    return "Четвертьфинал";
                case 16:
                    return "1/8 финала";
                case 32:
                    return "1/16 финала";
                default:
                    return "Раунд " + Convert.ToInt32(teamsInRound);
            }
        }

        private static bool IsBracketStage(DataRow row)
        {
            string stageName = Convert.ToString(row["StageName"]);
            int stageOrder = ReadInt(row["StageOrder"], 0);
            return stageOrder >= 100 || stageName.StartsWith("Bracket - ", StringComparison.CurrentCultureIgnoreCase);
        }

        private static string ResolveStoredTeamName(int? teamId, int roundIndex, int sourceMatchIndex, List<BracketMatchViewModel> previousRound, Dictionary<int, DataRow> teamsById, bool isTopSlot)
        {
            if (teamId.HasValue)
            {
                return ResolveTeamName(teamsById, teamId.Value);
            }

            if (roundIndex == 0)
            {
                return "BYE";
            }

            if (sourceMatchIndex >= 0 && sourceMatchIndex < previousRound.Count)
            {
                return "Победитель " + previousRound[sourceMatchIndex].MatchCode;
            }

            return isTopSlot ? "TBD" : "TBD";
        }

        private static string ResolveTeamName(Dictionary<int, DataRow> teamsById, int teamId)
        {
            return teamsById.ContainsKey(teamId) ? Convert.ToString(teamsById[teamId]["TeamName"]) : "Unknown Team";
        }

        private static int? ResolveWinnerTeamId(DataRow matchRow, int? team1Id, int? team2Id)
        {
            int? winnerTeamId = ToNullableInt(matchRow["WinnerTeamID"]);
            if (winnerTeamId.HasValue && (winnerTeamId == team1Id || winnerTeamId == team2Id))
            {
                return winnerTeamId;
            }

            if (team1Id.HasValue && !team2Id.HasValue)
            {
                return team1Id;
            }

            if (team2Id.HasValue && !team1Id.HasValue)
            {
                return team2Id;
            }

            int team1Score = ReadInt(matchRow["Team1Score"], 0);
            int team2Score = ReadInt(matchRow["Team2Score"], 0);
            string status = Convert.ToString(matchRow["Status"]);
            if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase) && team1Id.HasValue && team2Id.HasValue && team1Score != team2Score)
            {
                return team1Score > team2Score ? team1Id : team2Id;
            }

            return null;
        }

        private static string BuildMetaText(int bestOf, DateTime? matchDate)
        {
            string datePart = matchDate.HasValue ? matchDate.Value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : "Дата TBD";
            return datePart + " • BO" + bestOf;
        }

        private static string BuildStatusText(string status, string winnerName)
        {
            string translated = TranslateStatus(status);
            return string.IsNullOrWhiteSpace(winnerName)
                ? translated
                : translated + " • Победитель: " + winnerName;
        }

        private static string TranslateStatus(string status)
        {
            string value = (status ?? string.Empty).Trim();
            switch (value.ToLowerInvariant())
            {
                case "scheduled":
                    return "Запланирован";
                case "in progress":
                case "inprogress":
                    return "Идет";
                case "completed":
                    return "Завершен";
                case "auto advanced":
                    return "Автопроход";
                default:
                    return string.IsNullOrWhiteSpace(value) ? "Без статуса" : value;
            }
        }

        private static string LocalizeRoundTitle(string stageName)
        {
            string value = (stageName ?? string.Empty).Trim();
            if (value.StartsWith("Bracket - ", StringComparison.CurrentCultureIgnoreCase))
            {
                value = value.Substring("Bracket - ".Length).Trim();
            }

            switch (value)
            {
                case "Grand Final":
                    return "Финал";
                case "Semifinals":
                    return "Полуфинал";
                case "Quarterfinals":
                    return "Четвертьфинал";
                case "Round of 16":
                    return "1/8 финала";
                case "Round of 32":
                    return "1/16 финала";
                default:
                    return string.IsNullOrWhiteSpace(value) ? "Раунд" : value;
            }
        }

        private static string ResolveChampionName(List<BracketRoundViewModel> rounds)
        {
            BracketMatchViewModel finalMatch = rounds.LastOrDefault()?.Matches.FirstOrDefault();
            return finalMatch == null ? string.Empty : finalMatch.WinnerName;
        }

        private static string GetTeamName(Dictionary<int, DataRow> teamsById, int teamId)
        {
            return teamsById.ContainsKey(teamId) ? Convert.ToString(teamsById[teamId]["TeamName"]) : "Unknown Team";
        }

        private static DateTime? ParseDate(object value)
        {
            if (value == null || value == DBNull.Value)
            {
                return null;
            }

            if (value is DateTime dateTime)
            {
                return dateTime;
            }

            if (DateTime.TryParseExact(Convert.ToString(value), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
            {
                return parsed;
            }

            if (DateTime.TryParse(Convert.ToString(value), out parsed))
            {
                return parsed;
            }

            return null;
        }

        private static int ReadInt(object value, int fallback)
        {
            if (value == null || value == DBNull.Value)
            {
                return fallback;
            }

            return Convert.ToInt32(value);
        }

        private static int? ToNullableInt(object value)
        {
            return value == null || value == DBNull.Value ? (int?)null : Convert.ToInt32(value);
        }
    }
}
