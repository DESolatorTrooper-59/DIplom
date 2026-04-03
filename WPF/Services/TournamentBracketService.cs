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
        private const string Player1IdColumn = "Player1ID";
        private const string Player2IdColumn = "Player2ID";
        private const string WinnerPlayerIdColumn = "WinnerPlayerID";

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
            DataRow tournament = GetTournamentRow(tournamentId);
            if (tournament == null)
            {
                return new List<BracketTeamOption>();
            }

            DataTable participants = _database.GetTable("TournamentParticipants");
            string participantMode = GetTournamentParticipantMode(tournament);
            bool isPlayerMode = IsPlayerMode(participantMode);
            Dictionary<int, DataRow> participantsById = GetParticipantLookup(participantMode);

            return participants.Rows
                .Cast<DataRow>()
                .Where(row => Convert.ToInt32(row["TournamentID"]) == tournamentId)
                .Select(row => new
                {
                    Row = row,
                    ParticipantId = GetParticipantId(row, isPlayerMode)
                })
                .Where(item => item.ParticipantId.HasValue && participantsById.ContainsKey(item.ParticipantId.Value))
                .OrderBy(item => item.Row["Seed"] == DBNull.Value ? 1 : 0)
                .ThenBy(item => item.Row["Seed"] == DBNull.Value ? int.MaxValue : Convert.ToInt32(item.Row["Seed"]))
                .ThenBy(item => GetParticipantName(participantsById, item.ParticipantId.Value, isPlayerMode))
                .Select(item =>
                {
                    DataRow participant = participantsById[item.ParticipantId.Value];
                    return new BracketTeamOption
                    {
                        TeamId = item.ParticipantId.Value,
                        TeamName = GetParticipantName(participantsById, item.ParticipantId.Value, isPlayerMode),
                        SecondaryText = GetParticipantSecondaryText(participant, isPlayerMode)
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

        private DataRow GetTournamentRow(int tournamentId)
        {
            return _database.GetTable("Tournaments")
                .Rows
                .Cast<DataRow>()
                .FirstOrDefault(row => Convert.ToInt32(row["TournamentID"]) == tournamentId);
        }

        private Dictionary<int, DataRow> GetParticipantLookup(string participantMode)
        {
            bool isPlayerMode = IsPlayerMode(participantMode);
            string tableName = isPlayerMode ? "Players" : "Teams";
            string keyColumn = isPlayerMode ? "PlayerID" : "TeamID";

            return _database.GetTable(tableName)
                .Rows
                .Cast<DataRow>()
                .Where(row => row[keyColumn] != DBNull.Value)
                .ToDictionary(row => Convert.ToInt32(row[keyColumn]));
        }

        private List<BracketParticipantViewModel> GetParticipants(int tournamentId)
        {
            DataTable participants = _database.GetTable("TournamentParticipants");
            DataRow tournament = GetTournamentRow(tournamentId);
            if (tournament == null)
            {
                return new List<BracketParticipantViewModel>();
            }

            string participantMode = GetTournamentParticipantMode(tournament);
            bool isPlayerMode = IsPlayerMode(participantMode);
            Dictionary<int, DataRow> participantsById = GetParticipantLookup(participantMode);

            List<(DataRow Row, int ParticipantId)> orderedRows = participants.Rows
                .Cast<DataRow>()
                .Where(row => Convert.ToInt32(row["TournamentID"]) == tournamentId)
                .Select(row => new
                {
                    Row = row,
                    ParticipantId = GetParticipantId(row, isPlayerMode)
                })
                .Where(item => item.ParticipantId.HasValue && participantsById.ContainsKey(item.ParticipantId.Value))
                .OrderBy(item => item.Row["Seed"] == DBNull.Value ? 1 : 0)
                .ThenBy(item => item.Row["Seed"] == DBNull.Value ? int.MaxValue : Convert.ToInt32(item.Row["Seed"]))
                .ThenBy(item => GetParticipantName(participantsById, item.ParticipantId.Value, isPlayerMode))
                .Select(item => (item.Row, item.ParticipantId.Value))
                .ToList();

            List<BracketParticipantViewModel> result = new List<BracketParticipantViewModel>();
            for (int index = 0; index < orderedRows.Count; index++)
            {
                DataRow row = orderedRows[index].Row;
                int participantId = orderedRows[index].ParticipantId;
                DataRow participant = participantsById[participantId];
                result.Add(new BracketParticipantViewModel
                {
                    TeamId = participantId,
                    Seed = index + 1,
                    TeamName = GetParticipantName(participantsById, participantId, isPlayerMode),
                    Country = GetParticipantSecondaryText(participant, isPlayerMode)
                });
            }

            return result;
        }

        private List<BracketRoundViewModel> BuildPersistedRounds(int tournamentId)
        {
            DataRow tournament = GetTournamentRow(tournamentId);
            string participantMode = GetTournamentParticipantMode(tournament);
            bool isPlayerMode = IsPlayerMode(participantMode);
            DataTable stages = _database.GetTable("TournamentStages");
            DataTable matches = _database.GetTable("Matches");
            Dictionary<int, DataRow> participantsById = GetParticipantLookup(participantMode);

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
                    int? team1Id = GetPersistedParticipantId(row, isPlayerMode, 1);
                    int? team2Id = GetPersistedParticipantId(row, isPlayerMode, 2);
                    int? winnerTeamId = ResolveWinnerParticipantId(row, isPlayerMode, team1Id, team2Id);
                    string team1Name = ResolveStoredParticipantName(team1Id, roundIndex, matchIndex * 2, previousRound, participantsById, isPlayerMode, true);
                    string team2Name = ResolveStoredParticipantName(team2Id, roundIndex, matchIndex * 2 + 1, previousRound, participantsById, isPlayerMode, false);
                    string winnerName = winnerTeamId.HasValue ? ResolveParticipantName(participantsById, winnerTeamId.Value, isPlayerMode) : string.Empty;
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
                        StatusText = BuildStatusText(status, winnerName, team1Id, team2Id),
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
                        WinnerName = string.Empty,
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
                        ApplyAutoAdvancePreviewState(match);
                    }
                    else
                    {
                        BracketMatchViewModel source1 = previousRound[matchIndex * 2];
                        BracketMatchViewModel source2 = previousRound[matchIndex * 2 + 1];
                        if (source1.WinnerTeamId.HasValue && !string.IsNullOrWhiteSpace(source1.WinnerName))
                        {
                            match.Team1Id = source1.WinnerTeamId;
                            match.Team1Name = source1.WinnerName;
                        }
                        else
                        {
                            match.Team1Name = "Победитель " + source1.MatchCode;
                        }

                        if (source2.WinnerTeamId.HasValue && !string.IsNullOrWhiteSpace(source2.WinnerName))
                        {
                            match.Team2Id = source2.WinnerTeamId;
                            match.Team2Name = source2.WinnerName;
                        }
                        else
                        {
                            match.Team2Name = "Победитель " + source2.MatchCode;
                        }
                    }

                    match.MetaText = BuildMetaText(match.BestOf, match.MatchDate);
                    match.StatusText = BuildStatusText(match.Status, match.WinnerName, match.Team1Id, match.Team2Id);
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

        private static string ResolveStoredParticipantName(int? teamId, int roundIndex, int sourceMatchIndex, List<BracketMatchViewModel> previousRound, Dictionary<int, DataRow> participantsById, bool isPlayerMode, bool isTopSlot)
        {
            if (teamId.HasValue)
            {
                return ResolveParticipantName(participantsById, teamId.Value, isPlayerMode);
            }

            if (roundIndex == 0)
            {
                return "BYE";
            }

            if (sourceMatchIndex >= 0 && sourceMatchIndex < previousRound.Count)
            {
                BracketMatchViewModel sourceMatch = previousRound[sourceMatchIndex];
                if (sourceMatch.WinnerTeamId.HasValue && !string.IsNullOrWhiteSpace(sourceMatch.WinnerName))
                {
                    return sourceMatch.WinnerName;
                }

                return "Победитель " + sourceMatch.MatchCode;
            }

            return isTopSlot ? "TBD" : "TBD";
        }

        private static string ResolveParticipantName(Dictionary<int, DataRow> participantsById, int participantId, bool isPlayerMode)
        {
            if (!participantsById.ContainsKey(participantId))
            {
                return isPlayerMode ? "Unknown Player" : "Unknown Team";
            }

            DataRow participant = participantsById[participantId];
            return isPlayerMode ? Convert.ToString(participant["Nickname"]) : Convert.ToString(participant["TeamName"]);
        }

        private static int? ResolveWinnerParticipantId(DataRow matchRow, bool isPlayerMode, int? team1Id, int? team2Id)
        {
            int? winnerTeamId = GetPersistedParticipantId(matchRow, isPlayerMode, 3);
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

        private static int? GetPersistedParticipantId(DataRow matchRow, bool isPlayerMode, int slotIndex)
        {
            string preferredColumn = GetParticipantColumnName(isPlayerMode, slotIndex);
            if (matchRow.Table.Columns.Contains(preferredColumn) && matchRow[preferredColumn] != DBNull.Value)
            {
                return Convert.ToInt32(matchRow[preferredColumn]);
            }

            string fallbackColumn = GetParticipantColumnName(!isPlayerMode, slotIndex);
            if (matchRow.Table.Columns.Contains(fallbackColumn) && matchRow[fallbackColumn] != DBNull.Value)
            {
                return Convert.ToInt32(matchRow[fallbackColumn]);
            }

            return null;
        }

        private static string GetParticipantColumnName(bool isPlayerMode, int slotIndex)
        {
            switch (slotIndex)
            {
                case 1:
                    return isPlayerMode ? Player1IdColumn : "Team1ID";
                case 2:
                    return isPlayerMode ? Player2IdColumn : "Team2ID";
                case 3:
                    return isPlayerMode ? WinnerPlayerIdColumn : "WinnerTeamID";
                default:
                    throw new ArgumentOutOfRangeException(nameof(slotIndex));
            }
        }

        private static string BuildMetaText(int bestOf, DateTime? matchDate)
        {
            string datePart = matchDate.HasValue ? matchDate.Value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : "Дата TBD";
            return datePart + " • BO" + bestOf;
        }

        private static string BuildStatusText(string status, string winnerName, int? team1Id, int? team2Id)
        {
            if (team1Id.HasValue != team2Id.HasValue && !string.IsNullOrWhiteSpace(winnerName))
            {
                return "Автопроход • Победитель: " + winnerName;
            }

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

        private static string GetTournamentParticipantMode(DataRow tournament)
        {
            if (tournament == null || !tournament.Table.Columns.Contains("ParticipantMode") || tournament["ParticipantMode"] == DBNull.Value)
            {
                return "Команды";
            }

            string mode = Convert.ToString(tournament["ParticipantMode"]);
            return string.IsNullOrWhiteSpace(mode) ? "Команды" : mode;
        }

        private static bool IsPlayerMode(string participantMode)
        {
            return string.Equals(participantMode, "Игроки", StringComparison.CurrentCultureIgnoreCase);
        }

        private static int? GetParticipantId(DataRow participantRow, bool isPlayerMode)
        {
            string columnName = isPlayerMode ? "PlayerID" : "TeamID";
            if (!participantRow.Table.Columns.Contains(columnName) || participantRow[columnName] == DBNull.Value)
            {
                return null;
            }

            return Convert.ToInt32(participantRow[columnName]);
        }

        private static string GetParticipantName(Dictionary<int, DataRow> participantsById, int participantId, bool isPlayerMode)
        {
            return ResolveParticipantName(participantsById, participantId, isPlayerMode);
        }

        private static string GetParticipantSecondaryText(DataRow participant, bool isPlayerMode)
        {
            if (participant == null)
            {
                return string.Empty;
            }

            if (!isPlayerMode)
            {
                return participant.Table.Columns.Contains("Country") ? Convert.ToString(participant["Country"]) : string.Empty;
            }

            string realName = participant.Table.Columns.Contains("RealName") ? Convert.ToString(participant["RealName"]) : string.Empty;
            string country = participant.Table.Columns.Contains("Country") ? Convert.ToString(participant["Country"]) : string.Empty;
            if (string.IsNullOrWhiteSpace(realName))
            {
                return country;
            }

            return string.IsNullOrWhiteSpace(country) ? realName : realName + " • " + country;
        }

        private static void ApplyAutoAdvancePreviewState(BracketMatchViewModel match)
        {
            if (match.Team1Id.HasValue && !match.Team2Id.HasValue)
            {
                match.WinnerTeamId = match.Team1Id;
                match.WinnerName = match.Team1Name;
                return;
            }

            if (match.Team2Id.HasValue && !match.Team1Id.HasValue)
            {
                match.WinnerTeamId = match.Team2Id;
                match.WinnerName = match.Team2Name;
            }
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
