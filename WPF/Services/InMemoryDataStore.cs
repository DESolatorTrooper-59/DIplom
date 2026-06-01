using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Tournaments.WPF.Models;

namespace Tournaments.WPF.Services
{
    public sealed partial class InMemoryDataStore
    {
        private static readonly Lazy<InMemoryDataStore> _instance = new Lazy<InMemoryDataStore>(() => new InMemoryDataStore());

        private readonly object _syncRoot = new object();
        private readonly Dictionary<string, DataTable> _tables = new Dictionary<string, DataTable>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _identityColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _nextIdentities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private InMemoryDataStore()
        {
            InitializeSchema();
            SeedData();
        }

        public static InMemoryDataStore Instance => _instance.Value;

        public void EnsureUser(string login, string password, int roleId)
        {
            lock (_syncRoot)
            {
                DataTable players = GetRequiredTable("Players");
                bool exists = players.Rows
                    .Cast<DataRow>()
                    .Any(row =>
                        !IsNull(row["Nickname"]) &&
                        string.Equals(Convert.ToString(row["Nickname"]), login, StringComparison.OrdinalIgnoreCase));

                if (exists)
                {
                    return;
                }

                SeedPlayer(NextIdentity("Players"), login, login, "Online", new DateTime(1970, 1, 1), PasswordHasher.HashPassword(password), roleId);
            }
        }

        public bool ValidateUser(string login, string password)
        {
            lock (_syncRoot)
            {
                DataTable players = GetRequiredTable("Players");
                return players.Rows
                    .Cast<DataRow>()
                    .Any(row =>
                        !IsNull(row["Nickname"]) &&
                        !IsNull(row["Password"]) &&
                        string.Equals(Convert.ToString(row["Nickname"]), login, StringComparison.OrdinalIgnoreCase) &&
                        PasswordHasher.VerifyPassword(password, Convert.ToString(row["Password"])));
            }
        }

        public DataTable GetTableCopy(string tableName)
        {
            lock (_syncRoot)
            {
                return GetRequiredTable(tableName).Copy();
            }
        }

        public bool RecordExists(string tableName, string columnName, object value)
        {
            lock (_syncRoot)
            {
                return GetRequiredTable(tableName)
                    .Rows
                    .Cast<DataRow>()
                    .Any(row => AreEqual(row[columnName], value));
            }
        }

        public int CountRows(string tableName, Func<DataRow, bool> predicate)
        {
            lock (_syncRoot)
            {
                return GetRequiredTable(tableName)
                    .Rows
                    .Cast<DataRow>()
                    .Count(predicate);
            }
        }

        public int? PeekNextIdentityValue(string tableName)
        {
            lock (_syncRoot)
            {
                return _identityColumns.ContainsKey(tableName) ? _nextIdentities[tableName] : (int?)null;
            }
        }

        public int? Insert(string tableName, IDictionary<string, object> values)
        {
            lock (_syncRoot)
            {
                DataTable table = GetRequiredTable(tableName);
                DataRow row = table.NewRow();
                int? createdIdentity = null;

                foreach (DataColumn column in table.Columns)
                {
                    if (_identityColumns.TryGetValue(tableName, out string identityColumn) && string.Equals(identityColumn, column.ColumnName, StringComparison.OrdinalIgnoreCase))
                    {
                        int identityValue = _nextIdentities[tableName]++;
                        row[column.ColumnName] = identityValue;
                        createdIdentity = identityValue;
                        continue;
                    }

                    object value = values.ContainsKey(column.ColumnName) ? values[column.ColumnName] : null;
                    row[column.ColumnName] = NormalizeValue(column.DataType, value);
                }

                ApplyDefaults(tableName, row);
                table.Rows.Add(row);
                return createdIdentity;
            }
        }

        public void Update(string tableName, string[] keyColumns, IDictionary<string, object> values, IDictionary<string, object> originalValues)
        {
            lock (_syncRoot)
            {
                DataTable table = GetRequiredTable(tableName);
                DataRow row = FindRow(table, keyColumns, originalValues);
                if (row == null)
                {
                    throw new InvalidOperationException("Запись для обновления не найдена.");
                }

                foreach (DataColumn column in table.Columns)
                {
                    if (_identityColumns.TryGetValue(tableName, out string identityColumn) && string.Equals(identityColumn, column.ColumnName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!values.ContainsKey(column.ColumnName))
                    {
                        continue;
                    }

                    row[column.ColumnName] = NormalizeValue(column.DataType, values[column.ColumnName]);
                }

                ApplyDefaults(tableName, row);
            }
        }

        public void Delete(string tableName, string[] keyColumns, IDictionary<string, object> originalValues)
        {
            lock (_syncRoot)
            {
                DataTable table = GetRequiredTable(tableName);
                DataRow row = FindRow(table, keyColumns, originalValues);
                if (row == null)
                {
                    throw new InvalidOperationException("Запись для удаления не найдена.");
                }

                table.Rows.Remove(row);
            }
        }

        public void DeleteCascade(string tableName, string[] keyColumns, IDictionary<string, object> originalValues)
        {
            lock (_syncRoot)
            {
                DeleteCascadeInternal(tableName, keyColumns, originalValues);
            }
        }

        public void DeleteTournamentCascade(int tournamentId)
        {
            lock (_syncRoot)
            {
                DeleteTournamentCascadeInternal(tournamentId);
            }
        }

        public int GenerateTournamentBracket(int tournamentId)
        {
            lock (_syncRoot)
            {
                DataRow tournament = GetRequiredTable("Tournaments")
                    .Rows
                    .Cast<DataRow>()
                    .FirstOrDefault(row => AreEqual(row["TournamentID"], tournamentId));

                if (tournament == null)
                {
                    throw new InvalidOperationException("Выбранный турнир не найден.");
                }

                bool isPlayerMode = IsPlayerMode(GetTournamentParticipantMode(tournament));
                List<int> orderedParticipantIds = GetOrderedParticipantIds(tournamentId, isPlayerMode);
                if (orderedParticipantIds.Count < 2)
                {
                    throw new InvalidOperationException("Для построения сетки нужно минимум два участника.");
                }

                RemoveGeneratedBracket(tournamentId);

                int bracketSize = NextPowerOfTwo(orderedParticipantIds.Count);
                int roundCount = (int)Math.Log(bracketSize, 2);
                int[] seedPositions = BuildSeedPositions(bracketSize);
                int?[] slots = new int?[bracketSize];
                for (int index = 0; index < seedPositions.Length; index++)
                {
                    int seed = seedPositions[index];
                    if (seed <= orderedParticipantIds.Count)
                    {
                        slots[index] = orderedParticipantIds[seed - 1];
                    }
                }

                DataTable matches = GetRequiredTable("Matches");
                DateTime tournamentStartDate = Convert.ToDateTime(tournament["StartDate"]);
                int matchNumber = GetNextMatchNumber(tournamentId);
                int matchesInRound = bracketSize / 2;
                int createdMatches = 0;

                for (int roundIndex = 0; roundIndex < roundCount; roundIndex++)
                {
                    int teamsInRound = bracketSize / (int)Math.Pow(2, roundIndex);
                    int stageId = CreateBracketStage(tournamentId, roundIndex + 1, teamsInRound, roundIndex == roundCount - 1);

                    for (int matchIndex = 0; matchIndex < matchesInRound; matchIndex++)
                    {
                        DataRow match = matches.NewRow();
                        match["MatchID"] = NextIdentity("Matches");
                        match["TournamentID"] = tournamentId;
                        match["StageID"] = stageId;
                        match["MatchNumber"] = matchNumber++;
                        match["WinnerTeamID"] = DBNull.Value;
                        match["Team1Score"] = 0;
                        match["Team2Score"] = 0;
                        match["MatchDate"] = tournamentStartDate.AddDays(roundIndex).ToString("dd.MM.yyyy");
                        match["BestOf"] = roundIndex == roundCount - 1 ? 5 : 3;
                        match["Status"] = "Scheduled";

                        if (roundIndex == 0)
                        {
                            match["Team1ID"] = slots[matchIndex * 2].HasValue ? (object)slots[matchIndex * 2].Value : DBNull.Value;
                            match["Team2ID"] = slots[matchIndex * 2 + 1].HasValue ? (object)slots[matchIndex * 2 + 1].Value : DBNull.Value;
                            int? autoWinnerId = ResolveAutoAdvanceParticipantId(ToNullableInt(match["Team1ID"]), ToNullableInt(match["Team2ID"]));
                            match["WinnerTeamID"] = autoWinnerId.HasValue ? (object)autoWinnerId.Value : DBNull.Value;
                        }
                        else
                        {
                            match["Team1ID"] = DBNull.Value;
                            match["Team2ID"] = DBNull.Value;
                        }

                        ApplyDefaults("Matches", match);
                        matches.Rows.Add(match);
                        createdMatches++;
                    }

                    matchesInRound /= 2;
                }

                PropagateBracketState(tournamentId);
                return createdMatches;
            }
        }

        public void UpdateBracketMatch(int tournamentId, BracketMatchUpdateRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            lock (_syncRoot)
            {
                List<BracketRoundState> rounds = GetGeneratedBracketRounds(tournamentId);
                if (rounds.Count == 0)
                {
                    throw new InvalidOperationException("Для выбранного турнира сетка еще не создана.");
                }

                BracketRoundState targetRound = rounds.FirstOrDefault(round => round.Matches.Any(row => AreEqual(row["MatchID"], request.MatchId)));
                if (targetRound == null)
                {
                    throw new InvalidOperationException("Матч турнирной сетки не найден.");
                }

                DataRow match = targetRound.Matches.First(row => AreEqual(row["MatchID"], request.MatchId));
                bool isPlayerMode = IsPlayerMode(GetTournamentParticipantMode(tournamentId));
                List<int> availableParticipants = GetOrderedParticipantIds(tournamentId, isPlayerMode);
                int? team1Id = targetRound.RoundIndex == 0 ? NormalizeBracketTeamId(request.Team1Id, availableParticipants) : ToNullableInt(match["Team1ID"]);
                int? team2Id = targetRound.RoundIndex == 0 ? NormalizeBracketTeamId(request.Team2Id, availableParticipants) : ToNullableInt(match["Team2ID"]);

                if (team1Id.HasValue && team2Id.HasValue && team1Id.Value == team2Id.Value)
                {
                    throw new InvalidOperationException("В одном матче нельзя выбрать одного и того же участника дважды.");
                }

                if (request.Team1Score < 0 || request.Team2Score < 0)
                {
                    throw new InvalidOperationException("Счет участника не может быть отрицательным.");
                }

                if (request.BestOf <= 0 || request.BestOf % 2 == 0)
                {
                    throw new InvalidOperationException("Best Of должен быть положительным нечетным числом.");
                }

                if (request.MatchDate == default(DateTime))
                {
                    throw new InvalidOperationException("Укажите корректную дату матча.");
                }

                string status = string.IsNullOrWhiteSpace(request.Status) ? "Scheduled" : request.Status.Trim();
                match["Team1ID"] = team1Id.HasValue ? (object)team1Id.Value : DBNull.Value;
                match["Team2ID"] = team2Id.HasValue ? (object)team2Id.Value : DBNull.Value;
                match["Team1Score"] = request.Team1Score;
                match["Team2Score"] = request.Team2Score;
                match["BestOf"] = request.BestOf;
                match["MatchDate"] = request.MatchDate.ToString("dd.MM.yyyy");
                match["Status"] = status;

                int? winnerTeamId = NormalizeWinnerTeamId(request.WinnerTeamId, team1Id, team2Id, status, request.Team1Score, request.Team2Score);
                match["WinnerTeamID"] = winnerTeamId.HasValue ? (object)winnerTeamId.Value : DBNull.Value;

                PropagateBracketState(tournamentId);
            }
        }

        private List<BracketRoundState> GetGeneratedBracketRounds(int tournamentId)
        {
            DataTable stages = GetRequiredTable("TournamentStages");
            DataTable matches = GetRequiredTable("Matches");

            return stages.Rows
                .Cast<DataRow>()
                .Where(row => AreEqual(row["TournamentID"], tournamentId) && IsBracketStage(row))
                .OrderBy(row => ReadInt(row["StageOrder"], 0))
                .ThenBy(row => ReadInt(row["StageID"], 0))
                .Select((stage, index) =>
                {
                    BracketRoundState state = new BracketRoundState
                    {
                        RoundIndex = index,
                        Stage = stage
                    };

                    foreach (DataRow match in matches.Rows
                        .Cast<DataRow>()
                        .Where(row => AreEqual(row["TournamentID"], tournamentId) && AreEqual(row["StageID"], stage["StageID"]))
                        .OrderBy(row => ReadInt(row["MatchNumber"], 0)))
                    {
                        state.Matches.Add(match);
                    }

                    return state;
                })
                .Where(state => state.Matches.Count > 0)
                .ToList();
        }

        private void PropagateBracketState(int tournamentId)
        {
            List<BracketRoundState> rounds = GetGeneratedBracketRounds(tournamentId);
            for (int roundIndex = 1; roundIndex < rounds.Count; roundIndex++)
            {
                List<DataRow> previousRoundMatches = rounds[roundIndex - 1].Matches;
                List<DataRow> currentRoundMatches = rounds[roundIndex].Matches;
                for (int matchIndex = 0; matchIndex < currentRoundMatches.Count; matchIndex++)
                {
                    int? team1Id = matchIndex * 2 < previousRoundMatches.Count
                        ? ResolveAdvancingTeamId(previousRoundMatches[matchIndex * 2])
                        : (int?)null;
                    int? team2Id = matchIndex * 2 + 1 < previousRoundMatches.Count
                        ? ResolveAdvancingTeamId(previousRoundMatches[matchIndex * 2 + 1])
                        : (int?)null;

                    ApplyPropagatedTeams(currentRoundMatches[matchIndex], team1Id, team2Id);
                }
            }
        }

        private static void ApplyPropagatedTeams(DataRow match, int? team1Id, int? team2Id)
        {
            int? currentTeam1Id = ToNullableInt(match["Team1ID"]);
            int? currentTeam2Id = ToNullableInt(match["Team2ID"]);
            bool changed = currentTeam1Id != team1Id || currentTeam2Id != team2Id;

            match["Team1ID"] = team1Id.HasValue ? (object)team1Id.Value : DBNull.Value;
            match["Team2ID"] = team2Id.HasValue ? (object)team2Id.Value : DBNull.Value;

            if (changed)
            {
                match["WinnerTeamID"] = DBNull.Value;
                match["Team1Score"] = 0;
                match["Team2Score"] = 0;
                match["Status"] = "Scheduled";
                return;
            }

            if (!HasCompatibleWinner(match, team1Id, team2Id))
            {
                match["WinnerTeamID"] = DBNull.Value;
            }
        }

        private static int? ResolveAdvancingTeamId(DataRow sourceMatch)
        {
            int? team1Id = ToNullableInt(sourceMatch["Team1ID"]);
            int? team2Id = ToNullableInt(sourceMatch["Team2ID"]);
            int? winnerTeamId = ToNullableInt(sourceMatch["WinnerTeamID"]);

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

            string status = Convert.ToString(sourceMatch["Status"]);
            int team1Score = ReadInt(sourceMatch["Team1Score"], 0);
            int team2Score = ReadInt(sourceMatch["Team2Score"], 0);
            if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase) && team1Id.HasValue && team2Id.HasValue && team1Score != team2Score)
            {
                return team1Score > team2Score ? team1Id : team2Id;
            }

            return null;
        }

        private static bool HasCompatibleWinner(DataRow match, int? team1Id, int? team2Id)
        {
            int? winnerTeamId = ToNullableInt(match["WinnerTeamID"]);
            return !winnerTeamId.HasValue || winnerTeamId == team1Id || winnerTeamId == team2Id;
        }

        private static int? NormalizeBracketTeamId(int? teamId, ICollection<int> availableTeams)
        {
            if (!teamId.HasValue)
            {
                return null;
            }

            if (!availableTeams.Contains(teamId.Value))
            {
                throw new InvalidOperationException("В сетке можно использовать только участников выбранного турнира.");
            }

            return teamId;
        }

        private static int? NormalizeWinnerTeamId(int? winnerTeamId, int? team1Id, int? team2Id, string status, int team1Score, int team2Score)
        {
            if (winnerTeamId.HasValue)
            {
                if (winnerTeamId != team1Id && winnerTeamId != team2Id)
                {
                    throw new InvalidOperationException("Победитель должен совпадать с одним из участников матча.");
                }

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

            if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase) && team1Id.HasValue && team2Id.HasValue && team1Score != team2Score)
            {
                return team1Score > team2Score ? team1Id : team2Id;
            }

            return null;
        }

        private static bool IsBracketStage(DataRow row)
        {
            string stageName = Convert.ToString(row["StageName"]);
            int stageOrder = ReadInt(row["StageOrder"], 0);
            return stageOrder >= 100 || stageName.StartsWith("Bracket - ", StringComparison.CurrentCultureIgnoreCase);
        }

        private static int ReadInt(object value, int fallback)
        {
            return value == null || value == DBNull.Value ? fallback : Convert.ToInt32(value);
        }

        private static int? ToNullableInt(object value)
        {
            return value == null || value == DBNull.Value ? (int?)null : Convert.ToInt32(value);
        }

        private sealed class BracketRoundState
        {
            public int RoundIndex { get; set; }

            public DataRow Stage { get; set; }

            public List<DataRow> Matches { get; } = new List<DataRow>();
        }
        private void InitializeSchema()
        {
            CreateTable("Roles", "RoleID",
                Column("RoleID", typeof(int)),
                Column("RoleName", typeof(string)));

            CreateTable("GameTitles", "GameID",
                Column("GameID", typeof(int)),
                Column("GameName", typeof(string)),
                Column("Developer", typeof(string)),
                Column("ReleaseYear", typeof(int)),
                Column("MaxPlayersPerTeam", typeof(int)));

            CreateTable("Matches", "MatchID",
                Column("MatchID", typeof(int)),
                Column("TournamentID", typeof(int)),
                Column("StageID", typeof(int)),
                Column("MatchNumber", typeof(int)),
                Column("Team1ID", typeof(int)),
                Column("Team2ID", typeof(int)),
                Column("WinnerTeamID", typeof(int)),
                Column("Player1ID", typeof(int)),
                Column("Player2ID", typeof(int)),
                Column("WinnerPlayerID", typeof(int)),
                Column("Team1Score", typeof(int)),
                Column("Team2Score", typeof(int)),
                Column("MatchDate", typeof(string)),
                Column("BestOf", typeof(int)),
                Column("Status", typeof(string)));

            CreateTable("Players", "PlayerID",
                Column("PlayerID", typeof(int)),
                Column("Nickname", typeof(string)),
                Column("RealName", typeof(string)),
                Column("Country", typeof(string)),
                Column("BirthDate", typeof(DateTime)),
                Column("Password", typeof(string)),
                Column("RoleID", typeof(int)));

            CreateTable("Sponsors", "SponsorID",
                Column("SponsorID", typeof(int)),
                Column("SponsorName", typeof(string)),
                Column("Industry", typeof(string)));

            CreateTable("Streams", "StreamID",
                Column("StreamID", typeof(int)),
                Column("TournamentID", typeof(int)),
                Column("MatchID", typeof(int)),
                Column("Platform", typeof(string)),
                Column("StreamURL", typeof(string)));

            CreateTable("TeamPlayers", "TeamPlayerID",
                Column("TeamPlayerID", typeof(int)),
                Column("TeamID", typeof(int)),
                Column("PlayerID", typeof(int)),
                Column("JoinDate", typeof(DateTime)),
                Column("LeaveDate", typeof(DateTime)),
                Column("IsActive", typeof(bool)),
                Column("Role", typeof(string)));

            CreateTable("Teams", "TeamID",
                Column("TeamID", typeof(int)),
                Column("TeamName", typeof(string)),
                Column("FoundedDate", typeof(DateTime)),
                Column("Country", typeof(string)),
                Column("CoachName", typeof(string)),
                Column("CreatedDate", typeof(DateTime)));

            CreateTable("TournamentParticipants", "ParticipationID",
                Column("ParticipationID", typeof(int)),
                Column("TournamentID", typeof(int)),
                Column("TeamID", typeof(int)),
                Column("PlayerID", typeof(int)),
                Column("Seed", typeof(int)),
                Column("FinalPlace", typeof(int)));

            CreateTable("Tournaments", "TournamentID",
                Column("TournamentID", typeof(int)),
                Column("TournamentName", typeof(string)),
                Column("GameID", typeof(int)),
                Column("StartDate", typeof(DateTime)),
                Column("EndDate", typeof(DateTime)),
                Column("PrizePool", typeof(decimal)),
                Column("Organizer", typeof(string)),
                Column("Location", typeof(string)),
                Column("FormatType", typeof(string)),
                Column("MaxTeams", typeof(int)),
                Column("ParticipantMode", typeof(string)));

            CreateTable("TournamentSponsors", null,
                Column("TournamentID", typeof(int)),
                Column("SponsorID", typeof(int)),
                Column("SponsorshipAmount", typeof(decimal)),
                Column("Currency", typeof(string)));

            CreateTable("TournamentStages", "StageID",
                Column("StageID", typeof(int)),
                Column("TournamentID", typeof(int)),
                Column("StageName", typeof(string)),
                Column("StageOrder", typeof(int)),
                Column("BracketType", typeof(string)));
        }

        private void SeedData()
        {
            SeedRole(1, "Игрок");
            SeedRole(2, "Организатор");
            SeedRole(3, "Администратор");
            EnsureUser("admin", "password", 3);
            EnsureUser("organizer", "organizer", 2);

            SeedRow("GameTitles", new Dictionary<string, object>
            {
                ["GameID"] = 1,
                ["GameName"] = "Counter-Strike 2",
                ["Developer"] = "Valve",
                ["ReleaseYear"] = 2023,
                ["MaxPlayersPerTeam"] = 5
            });

            SeedRow("GameTitles", new Dictionary<string, object>
            {
                ["GameID"] = 2,
                ["GameName"] = "Tiberium Wars",
                ["Developer"] = "EA LA",
                ["ReleaseYear"] = 2007,
                ["MaxPlayersPerTeam"] = 4
            });

            SeedRow("GameTitles", new Dictionary<string, object>
            {
                ["GameID"] = 3,
                ["GameName"] = "Kane's Wrath",
                ["Developer"] = "EA LA",
                ["ReleaseYear"] = 2008,
                ["MaxPlayersPerTeam"] = 4
            });

            SeedRow("GameTitles", new Dictionary<string, object>
            {
                ["GameID"] = 4,
                ["GameName"] = "Red Alert 2",
                ["Developer"] = "Westwood Studios, EA Pacific",
                ["ReleaseYear"] = 2000,
                ["MaxPlayersPerTeam"] = 4
            });

            SeedRow("GameTitles", new Dictionary<string, object>
            {
                ["GameID"] = 5,
                ["GameName"] = "Red Alert 3",
                ["Developer"] = "EA LA",
                ["ReleaseYear"] = 2008,
                ["MaxPlayersPerTeam"] = 4
            });

            SeedTeam(1, "Bikerush team 01", new DateTime(2009, 12, 17), "Online", "Bikerush", new DateTime(2026, 5, 14));
            SeedTeam(2, "Zaneki team", new DateTime(2015, 12, 5), "Online", "Zaneki", new DateTime(2026, 5, 14));
            SeedTeam(3, "Bartjones team", new DateTime(2026, 4, 28), "Online", "Bartjones", new DateTime(2026, 5, 15));
            SeedTeam(4, "Bookuha team", new DateTime(2026, 4, 27), "Online", "Bookuha", new DateTime(2026, 5, 15));
            SeedTeam(5, "DavidZD team", new DateTime(2026, 5, 1), "Online", "DavidZD", new DateTime(2026, 5, 15));
            SeedTeam(6, "Neiroslop", new DateTime(2026, 5, 16), "Online", "AI", new DateTime(2026, 5, 16, 21, 16, 23));

            SeedPlayer(3, "DESolatorTrooper", "Sergey Kornev", "Russia", new DateTime(2005, 6, 21));
            SeedPlayer(4, "Bookuha", "Andrey", "Ukraine", new DateTime(2000, 1, 1));
            SeedPlayer(5, "Bikerushownz", "Скрыто", "United Kingdom", new DateTime(2000, 1, 1));
            SeedPlayer(6, "Hulk", "Ivan", "Russia", new DateTime(2000, 1, 1));
            SeedPlayer(7, "Mah_Boi", "Скрыто", "Blocked", new DateTime(2000, 1, 1));
            SeedPlayer(8, "Lamas", "Скрыто", "USA", new DateTime(2026, 4, 1));
            SeedPlayer(9, "Rildcom", "Скрыто", "Australia", new DateTime(2000, 1, 1));
            SeedPlayer(10, "Svenson", "Скрыто", "Online", new DateTime(2000, 1, 1));
            SeedPlayer(11, "Redeemer", "Dmitry", "Russia", new DateTime(2026, 4, 29));
            SeedPlayer(12, "Lumos", "Скрыто", "Russia", new DateTime(2026, 1, 13));
            SeedPlayer(13, "FateZero", "Скрыто", "Pakistan", new DateTime(2019, 2, 13));
            SeedPlayer(14, "Player 1", "Player", "Online", new DateTime(2026, 4, 29));
            SeedPlayer(15, "UnderworldFox", "Скрыто", "South Africa", new DateTime(2025, 10, 14));
            SeedPlayer(16, "Aquatech", "Скрыто", "Unknown", new DateTime(2025, 11, 13));
            SeedPlayer(17, "Iluhan", "Илья", "Russia", new DateTime(1970, 3, 1));
            SeedPlayer(18, "YourHorse", "Скрыто", "Europe", new DateTime(2011, 2, 1));
            SeedPlayer(19, "MrNoSweat", "Скрыто", "Europe", new DateTime(1981, 3, 18));
            SeedPlayer(20, "GreeeeeeenAlert", "Скрыто", "Russia", new DateTime(2003, 5, 30));
            SeedPlayer(21, "Player 21", "Скрыто", "Unknown", new DateTime(1970, 1, 1));

            SeedRow("Sponsors", new Dictionary<string, object>
            {
                ["SponsorID"] = 1,
                ["SponsorName"] = "Red Bull",
                ["Industry"] = "Energy Drinks"
            });

            SeedRow("Sponsors", new Dictionary<string, object>
            {
                ["SponsorID"] = 2,
                ["SponsorName"] = "Intel",
                ["Industry"] = "Hardware"
            });

            SeedRow("Sponsors", new Dictionary<string, object>
            {
                ["SponsorID"] = 3,
                ["SponsorName"] = "Logitech G",
                ["Industry"] = "Peripherals"
            });

            SeedTeamPlayer(3, 3, 20, new DateTime(2026, 5, 16));
            SeedTeamPlayer(4, 3, 13, new DateTime(2026, 5, 16));
            SeedTeamPlayer(5, 3, 8, new DateTime(2026, 5, 16));
            SeedTeamPlayer(6, 2, 19, new DateTime(2026, 5, 16));
            SeedTeamPlayer(7, 2, 9, new DateTime(2026, 5, 16));
            SeedTeamPlayer(8, 2, 3, new DateTime(2026, 5, 16));
            SeedTeamPlayer(9, 2, 21, new DateTime(2026, 5, 16));
            SeedTeamPlayer(10, 6, 16, new DateTime(2026, 5, 16));
            SeedTeamPlayer(11, 6, 17, new DateTime(2026, 5, 16));
            SeedTeamPlayer(12, 6, 6, new DateTime(2026, 5, 16));
            SeedTeamPlayer(13, 6, 20, new DateTime(2026, 5, 16));
            SeedTeamPlayer(14, 6, 7, new DateTime(2026, 5, 16));
            SeedTeamPlayer(15, 6, 12, new DateTime(2026, 5, 16));
            SeedTeamPlayer(16, 6, 8, new DateTime(2026, 5, 16));
            SeedTeamPlayer(17, 6, 13, new DateTime(2026, 5, 16));
            SeedTeamPlayer(18, 6, 3, new DateTime(2026, 5, 16));

            SeedTournament(1, "Spring Invitational 2026", 1, new DateTime(2026, 5, 10), new DateTime(2026, 5, 12), 150000m, "admin", "Moscow", "Double Elimination", 10, "Игроки");
            SeedTournament(2, "WEC Season 1", 3, new DateTime(2026, 2, 1), null, 1000m, "Bikerushownz", "Online", "League", 8, "Игроки");
            SeedTournament(3, "WEC Season 2", 3, new DateTime(2026, 3, 1), null, 1000m, "Bikerushownz", "Online", "League", 24, "Игроки");
            SeedTournament(4, "Red Champions", 5, new DateTime(2024, 7, 5), new DateTime(2024, 8, 16), 1m, "MoscowCypersports", "Online", "Single Elimination", 24, "Игроки");
            SeedTournament(5, "4v4 ZanekiPrivateTournament", 3, new DateTime(2026, 2, 5), new DateTime(2026, 2, 7), 100m, "Zaneki", "Online", "Single Elimination", 4, "Команды");

            SeedTournamentStage(1, 1, "Playoffs", 1, "Winner");
            SeedTournamentStage(7, 2, "League - Round 1", 400, DBNull.Value);
            SeedTournamentStage(8, 2, "League - Round 2", 401, DBNull.Value);
            SeedTournamentStage(9, 2, "League - Round 3", 402, DBNull.Value);
            SeedTournamentStage(10, 2, "League - Round 4", 403, DBNull.Value);
            SeedTournamentStage(11, 2, "League - Round 5", 404, DBNull.Value);
            SeedTournamentStage(12, 5, "Bracket - Semifinals", 100, "Winner");
            SeedTournamentStage(13, 5, "Bracket - Grand Final", 101, "Final");
            SeedTournamentStage(14, 3, "League - Round 1", 400, DBNull.Value);
            SeedTournamentStage(15, 3, "League - Round 2", 401, DBNull.Value);
            SeedTournamentStage(16, 3, "League - Round 3", 402, DBNull.Value);
            SeedTournamentStage(17, 3, "League - Round 4", 403, DBNull.Value);
            SeedTournamentStage(18, 3, "League - Round 5", 404, DBNull.Value);
            SeedTournamentStage(19, 3, "League - Round 6", 405, DBNull.Value);
            SeedTournamentStage(20, 3, "League - Round 7", 406, DBNull.Value);
            SeedTournamentStage(21, 3, "League - Round 8", 407, DBNull.Value);
            SeedTournamentStage(22, 3, "League - Round 9", 408, DBNull.Value);
            SeedTournamentStage(23, 3, "League - Round 10", 409, DBNull.Value);
            SeedTournamentStage(24, 3, "League - Round 11", 410, DBNull.Value);
            SeedTournamentStage(25, 3, "League - Round 12", 411, DBNull.Value);
            SeedTournamentStage(26, 3, "League - Round 13", 412, DBNull.Value);
            SeedTournamentStage(27, 3, "League - Round 14", 413, DBNull.Value);
            SeedTournamentStage(28, 3, "League - Round 15", 414, DBNull.Value);
            SeedTournamentStage(29, 3, "League - Round 16", 415, DBNull.Value);
            SeedTournamentStage(30, 3, "League - Round 17", 416, DBNull.Value);
            SeedTournamentStage(31, 3, "League - Round 18", 417, DBNull.Value);
            SeedTournamentStage(32, 3, "League - Round 19", 418, DBNull.Value);
            SeedTournamentStage(33, 1, "Bracket - Upper - Upper Quarterfinals", 100, "Winner");
            SeedTournamentStage(34, 1, "Bracket - Upper - Upper Semifinals", 101, "Winner");
            SeedTournamentStage(35, 1, "Bracket - Upper - Upper Final", 102, "Winner");
            SeedTournamentStage(36, 1, "Bracket - Lower - Round 1", 200, "Loser");
            SeedTournamentStage(37, 1, "Bracket - Lower - Round 2", 201, "Loser");
            SeedTournamentStage(38, 1, "Bracket - Lower - Round 3", 202, "Loser");
            SeedTournamentStage(39, 1, "Bracket - Lower - Final", 203, "Loser");
            SeedTournamentStage(40, 1, "Bracket - Grand Final", 300, "Final");
            SeedTournamentStage(41, 4, "Bracket - Round of 32", 100, "Winner");
            SeedTournamentStage(42, 4, "Bracket - Round of 16", 101, "Winner");
            SeedTournamentStage(43, 4, "Bracket - Quarterfinals", 102, "Winner");
            SeedTournamentStage(44, 4, "Bracket - Semifinals", 103, "Winner");
            SeedTournamentStage(45, 4, "Bracket - Grand Final", 104, "Final");

            SeedTournamentParticipant(1, 1, 1, 1, null, null);
            SeedTournamentParticipant(2, 1, 2, 2, null, null);
            SeedTournamentParticipant(3, 2, null, 1, 3, null);
            SeedTournamentParticipant(4, 2, null, 2, 4, null);
            SeedTournamentParticipant(6, 2, null, 3, 6, null);
            SeedTournamentParticipant(7, 2, null, 4, 8, null);
            SeedTournamentParticipant(8, 2, null, 5, 9, null);
            SeedTournamentParticipant(9, 2, null, 6, 10, null);
            SeedTournamentParticipant(13, 3, null, 3, 4, null);
            SeedTournamentParticipant(14, 4, null, 1, 5, null);
            SeedTournamentParticipant(15, 4, null, 2, 3, null);
            SeedTournamentParticipant(17, 5, 2, 2, null, null);
            SeedTournamentParticipant(18, 5, 3, 3, null, null);
            SeedTournamentParticipant(19, 5, 4, 4, null, null);
            SeedTournamentParticipant(20, 5, 5, 5, null, null);
            SeedTournamentParticipant(21, 3, null, 4, 16, null);
            SeedTournamentParticipant(22, 3, null, 5, 17, null);
            SeedTournamentParticipant(23, 3, null, 6, 6, null);
            SeedTournamentParticipant(24, 3, null, 7, 20, null);
            SeedTournamentParticipant(25, 3, null, 8, 13, null);
            SeedTournamentParticipant(26, 3, null, 9, 3, null);
            SeedTournamentParticipant(27, 3, null, 10, 5, null);
            SeedTournamentParticipant(28, 3, null, 11, 7, null);
            SeedTournamentParticipant(29, 3, null, 12, 12, null);
            SeedTournamentParticipant(30, 3, null, 13, 8, null);
            SeedTournamentParticipant(31, 3, null, 14, 11, null);
            SeedTournamentParticipant(32, 3, null, 15, 14, null);
            SeedTournamentParticipant(33, 3, null, 16, 19, null);
            SeedTournamentParticipant(34, 3, null, 17, 10, null);
            SeedTournamentParticipant(35, 3, null, 18, 15, null);
            SeedTournamentParticipant(36, 3, null, 19, 9, null);
            SeedTournamentParticipant(37, 3, null, 20, 21, null);
            SeedTournamentParticipant(38, 3, null, 21, 18, null);
            SeedTournamentParticipant(39, 1, null, 3, 5, null);
            SeedTournamentParticipant(40, 1, null, 4, 4, null);
            SeedTournamentParticipant(41, 1, null, 5, 16, null);
            SeedTournamentParticipant(42, 1, null, 6, 17, null);
            SeedTournamentParticipant(43, 1, null, 7, 3, null);
            SeedTournamentParticipant(44, 4, null, 3, 4, null);
            SeedTournamentParticipant(45, 4, null, 4, 16, null);
            SeedTournamentParticipant(46, 4, null, 5, 20, null);
            SeedTournamentParticipant(47, 4, null, 6, 6, null);
            SeedTournamentParticipant(48, 4, null, 7, 13, null);
            SeedTournamentParticipant(49, 4, null, 8, 12, null);
            SeedTournamentParticipant(50, 4, null, 9, 8, null);
            SeedTournamentParticipant(51, 4, null, 10, 17, null);
            SeedTournamentParticipant(52, 4, null, 11, 11, null);
            SeedTournamentParticipant(53, 4, null, 12, 14, null);
            SeedTournamentParticipant(54, 4, null, 13, 19, null);
            SeedTournamentParticipant(55, 4, null, 14, 7, null);
            SeedTournamentParticipant(56, 4, null, 15, 10, null);
            SeedTournamentParticipant(57, 4, null, 16, 9, null);
            SeedTournamentParticipant(58, 4, null, 17, 21, null);
            SeedTournamentParticipant(59, 4, null, 18, 18, null);
            SeedTournamentParticipant(60, 4, null, 19, 15, null);

            SeedMatch(48, 5, 12, 1, 2, 5, null, null, null, null, 0, 0, "05.02.2026", 3, "Scheduled");
            SeedMatch(49, 5, 12, 2, 3, 4, null, null, null, null, 0, 0, "05.02.2026", 3, "Scheduled");
            SeedMatch(50, 5, 13, 3, null, null, null, null, null, null, 0, 0, "06.02.2026", 5, "Scheduled");

            SeedRow("TournamentSponsors", new Dictionary<string, object>
            {
                ["TournamentID"] = 1,
                ["SponsorID"] = 1,
                ["SponsorshipAmount"] = 100000m,
                ["Currency"] = "USD"
            });

            SeedRow("TournamentSponsors", new Dictionary<string, object>
            {
                ["TournamentID"] = 1,
                ["SponsorID"] = 2,
                ["SponsorshipAmount"] = 75000m,
                ["Currency"] = "USD"
            });

            SeedRow("TournamentSponsors", new Dictionary<string, object>
            {
                ["TournamentID"] = 2,
                ["SponsorID"] = 3,
                ["SponsorshipAmount"] = 50000m,
                ["Currency"] = "USD"
            });
        }

        private void SeedTeam(int teamId, string name, DateTime foundedDate, string country, string coachName, DateTime createdDate)
        {
            SeedRow("Teams", new Dictionary<string, object>
            {
                ["TeamID"] = teamId,
                ["TeamName"] = name,
                ["FoundedDate"] = foundedDate,
                ["Country"] = country,
                ["CoachName"] = coachName,
                ["CreatedDate"] = createdDate
            });
        }

        private void SeedRole(int roleId, string roleName)
        {
            SeedRow("Roles", new Dictionary<string, object>
            {
                ["RoleID"] = roleId,
                ["RoleName"] = roleName
            });
        }

        private void SeedPlayer(int playerId, string nickname, string realName, string country, DateTime birthDate, string passwordHash = null, int roleId = 1)
        {
            SeedRow("Players", new Dictionary<string, object>
            {
                ["PlayerID"] = playerId,
                ["Nickname"] = nickname,
                ["RealName"] = realName,
                ["Country"] = country,
                ["BirthDate"] = birthDate,
                ["Password"] = passwordHash,
                ["RoleID"] = roleId
            });
        }

        private void SeedTournament(
            int tournamentId,
            string name,
            int gameId,
            DateTime startDate,
            DateTime? endDate,
            decimal prizePool,
            string organizer,
            string location,
            string formatType,
            int maxParticipants,
            string participantMode)
        {
            SeedRow("Tournaments", new Dictionary<string, object>
            {
                ["TournamentID"] = tournamentId,
                ["TournamentName"] = name,
                ["GameID"] = gameId,
                ["StartDate"] = startDate,
                ["EndDate"] = endDate.HasValue ? (object)endDate.Value : DBNull.Value,
                ["PrizePool"] = prizePool,
                ["Organizer"] = organizer,
                ["Location"] = location,
                ["FormatType"] = formatType,
                ["MaxTeams"] = maxParticipants,
                ["ParticipantMode"] = participantMode
            });
        }

        private void SeedTournamentStage(int stageId, int tournamentId, string stageName, int stageOrder, object bracketType)
        {
            SeedRow("TournamentStages", new Dictionary<string, object>
            {
                ["StageID"] = stageId,
                ["TournamentID"] = tournamentId,
                ["StageName"] = stageName,
                ["StageOrder"] = stageOrder,
                ["BracketType"] = bracketType
            });
        }

        private void SeedTeamPlayer(int teamPlayerId, int teamId, int playerId, DateTime joinDate, string role = null)
        {
            SeedRow("TeamPlayers", new Dictionary<string, object>
            {
                ["TeamPlayerID"] = teamPlayerId,
                ["TeamID"] = teamId,
                ["PlayerID"] = playerId,
                ["JoinDate"] = joinDate,
                ["LeaveDate"] = DBNull.Value,
                ["IsActive"] = true,
                ["Role"] = string.IsNullOrWhiteSpace(role) ? (object)DBNull.Value : role
            });
        }

        private void SeedTournamentParticipant(int participationId, int tournamentId, int? teamId, int seed, int? playerId = null, int? finalPlace = null)
        {
            SeedRow("TournamentParticipants", new Dictionary<string, object>
            {
                ["ParticipationID"] = participationId,
                ["TournamentID"] = tournamentId,
                ["TeamID"] = teamId.HasValue ? (object)teamId.Value : DBNull.Value,
                ["PlayerID"] = playerId.HasValue ? (object)playerId.Value : DBNull.Value,
                ["Seed"] = seed,
                ["FinalPlace"] = finalPlace.HasValue ? (object)finalPlace.Value : DBNull.Value
            });
        }

        private void SeedMatch(
            int matchId,
            int tournamentId,
            int stageId,
            int matchNumber,
            int? team1Id,
            int? team2Id,
            int? winnerTeamId,
            int? player1Id,
            int? player2Id,
            int? winnerPlayerId,
            int team1Score,
            int team2Score,
            string matchDate,
            int bestOf,
            string status)
        {
            SeedRow("Matches", new Dictionary<string, object>
            {
                ["MatchID"] = matchId,
                ["TournamentID"] = tournamentId,
                ["StageID"] = stageId,
                ["MatchNumber"] = matchNumber,
                ["Team1ID"] = team1Id.HasValue ? (object)team1Id.Value : DBNull.Value,
                ["Team2ID"] = team2Id.HasValue ? (object)team2Id.Value : DBNull.Value,
                ["WinnerTeamID"] = winnerTeamId.HasValue ? (object)winnerTeamId.Value : DBNull.Value,
                ["Player1ID"] = player1Id.HasValue ? (object)player1Id.Value : DBNull.Value,
                ["Player2ID"] = player2Id.HasValue ? (object)player2Id.Value : DBNull.Value,
                ["WinnerPlayerID"] = winnerPlayerId.HasValue ? (object)winnerPlayerId.Value : DBNull.Value,
                ["Team1Score"] = team1Score,
                ["Team2Score"] = team2Score,
                ["MatchDate"] = matchDate,
                ["BestOf"] = bestOf,
                ["Status"] = status
            });
        }

        private void CreateTable(string tableName, string identityColumn, params DataColumn[] columns)
        {
            DataTable table = new DataTable(tableName);
            foreach (DataColumn column in columns)
            {
                table.Columns.Add(column);
            }

            _tables[tableName] = table;
            if (!string.IsNullOrWhiteSpace(identityColumn))
            {
                _identityColumns[tableName] = identityColumn;
                _nextIdentities[tableName] = 1;
            }
        }

        private void SeedRow(string tableName, IDictionary<string, object> values)
        {
            DataTable table = GetRequiredTable(tableName);
            DataRow row = table.NewRow();
            foreach (DataColumn column in table.Columns)
            {
                object value = values.ContainsKey(column.ColumnName) ? values[column.ColumnName] : null;
                row[column.ColumnName] = NormalizeValue(column.DataType, value);
            }

            ApplyDefaults(tableName, row);
            table.Rows.Add(row);

            if (_identityColumns.TryGetValue(tableName, out string identityColumn))
            {
                int identityValue = Convert.ToInt32(row[identityColumn]);
                if (_nextIdentities[tableName] <= identityValue)
                {
                    _nextIdentities[tableName] = identityValue + 1;
                }
            }
        }

        private void ApplyDefaults(string tableName, DataRow row)
        {
            if (string.Equals(tableName, "Teams", StringComparison.OrdinalIgnoreCase) && IsNull(row["CreatedDate"]))
            {
                row["CreatedDate"] = DateTime.Now;
            }

            if (string.Equals(tableName, "TeamPlayers", StringComparison.OrdinalIgnoreCase) && IsNull(row["IsActive"]))
            {
                row["IsActive"] = true;
            }

            if (string.Equals(tableName, "Matches", StringComparison.OrdinalIgnoreCase))
            {
                if (IsNull(row["Team1Score"]))
                {
                    row["Team1Score"] = 0;
                }

                if (IsNull(row["Team2Score"]))
                {
                    row["Team2Score"] = 0;
                }

                if (IsNull(row["BestOf"]))
                {
                    row["BestOf"] = 3;
                }

                if (IsNull(row["Status"]))
                {
                    row["Status"] = "Scheduled";
                }
            }

            if (string.Equals(tableName, "TournamentSponsors", StringComparison.OrdinalIgnoreCase) && IsNull(row["Currency"]))
            {
                row["Currency"] = "USD";
            }

            if (string.Equals(tableName, "Players", StringComparison.OrdinalIgnoreCase) && IsNull(row["RealName"]))
            {
                row["RealName"] = "Скрыто";
            }

            if (string.Equals(tableName, "Players", StringComparison.OrdinalIgnoreCase) &&
                row.Table.Columns.Contains("RoleID") &&
                IsNull(row["RoleID"]))
            {
                row["RoleID"] = 1;
            }

            if (string.Equals(tableName, "Players", StringComparison.OrdinalIgnoreCase) &&
                row.Table.Columns.Contains("Country") &&
                IsNull(row["Country"]))
            {
                row["Country"] = "Не указано";
            }

            if (string.Equals(tableName, "Tournaments", StringComparison.OrdinalIgnoreCase) && row.Table.Columns.Contains("ParticipantMode") && IsNull(row["ParticipantMode"]))
            {
                row["ParticipantMode"] = "Команды";
            }
        }

        private DataRow FindRow(DataTable table, string[] keyColumns, IDictionary<string, object> originalValues)
        {
            return table.Rows
                .Cast<DataRow>()
                .FirstOrDefault(row => keyColumns.All(key => originalValues.ContainsKey(key) && AreEqual(row[key], originalValues[key])));
        }

        private void DeleteCascadeInternal(string tableName, string[] keyColumns, IDictionary<string, object> originalValues)
        {
            switch (tableName)
            {
                case "Tournaments":
                    DeleteTournamentCascadeInternal(GetRequiredInt(originalValues, "TournamentID"));
                    return;
                case "Teams":
                    DeleteTeamCascadeInternal(GetRequiredInt(originalValues, "TeamID"));
                    return;
                case "Players":
                    DeletePlayerCascadeInternal(GetRequiredInt(originalValues, "PlayerID"));
                    return;
                case "GameTitles":
                    DeleteGameCascadeInternal(GetRequiredInt(originalValues, "GameID"));
                    return;
                case "Sponsors":
                    DeleteSponsorCascadeInternal(GetRequiredInt(originalValues, "SponsorID"));
                    return;
                case "TournamentStages":
                    DeleteStageCascadeInternal(GetRequiredInt(originalValues, "StageID"));
                    return;
                case "Matches":
                    DeleteMatchCascadeInternal(GetRequiredInt(originalValues, "MatchID"));
                    return;
                default:
                    Delete(tableName, keyColumns, originalValues);
                    return;
            }
        }

        private void DeleteTournamentCascadeInternal(int tournamentId)
        {
            DataTable matches = GetRequiredTable("Matches");
            HashSet<int> matchIds = new HashSet<int>(
                matches.Rows
                    .Cast<DataRow>()
                    .Where(row => AreEqual(row["TournamentID"], tournamentId))
                    .Select(row => Convert.ToInt32(row["MatchID"])));

            RemoveRows(GetRequiredTable("Streams"), row =>
                AreEqual(row["TournamentID"], tournamentId) ||
                (row["MatchID"] != DBNull.Value && matchIds.Contains(Convert.ToInt32(row["MatchID"]))));
            RemoveRows(matches, row => AreEqual(row["TournamentID"], tournamentId));
            RemoveRows(GetRequiredTable("TournamentStages"), row => AreEqual(row["TournamentID"], tournamentId));
            RemoveRows(GetRequiredTable("TournamentParticipants"), row => AreEqual(row["TournamentID"], tournamentId));
            RemoveRows(GetRequiredTable("TournamentSponsors"), row => AreEqual(row["TournamentID"], tournamentId));
            RemoveRows(GetRequiredTable("Tournaments"), row => AreEqual(row["TournamentID"], tournamentId));
        }

        private void DeleteTeamCascadeInternal(int teamId)
        {
            DataTable matches = GetRequiredTable("Matches");
            HashSet<int> matchIds = new HashSet<int>(
                matches.Rows
                    .Cast<DataRow>()
                    .Where(row =>
                        AreEqual(row["Team1ID"], teamId) ||
                        AreEqual(row["Team2ID"], teamId) ||
                        AreEqual(row["WinnerTeamID"], teamId))
                    .Select(row => Convert.ToInt32(row["MatchID"])));

            RemoveRows(GetRequiredTable("Streams"), row =>
                row["MatchID"] != DBNull.Value &&
                matchIds.Contains(Convert.ToInt32(row["MatchID"])));
            RemoveRows(matches, row =>
                AreEqual(row["Team1ID"], teamId) ||
                AreEqual(row["Team2ID"], teamId) ||
                AreEqual(row["WinnerTeamID"], teamId));
            RemoveRows(GetRequiredTable("TournamentParticipants"), row => AreEqual(row["TeamID"], teamId));
            RemoveRows(GetRequiredTable("TeamPlayers"), row => AreEqual(row["TeamID"], teamId));
            RemoveRows(GetRequiredTable("Teams"), row => AreEqual(row["TeamID"], teamId));
        }

        private void DeletePlayerCascadeInternal(int playerId)
        {
            DataTable matches = GetRequiredTable("Matches");
            HashSet<int> matchIds = new HashSet<int>(
                matches.Rows
                    .Cast<DataRow>()
                    .Where(row =>
                        AreEqual(row["Player1ID"], playerId) ||
                        AreEqual(row["Player2ID"], playerId) ||
                        AreEqual(row["WinnerPlayerID"], playerId))
                    .Select(row => Convert.ToInt32(row["MatchID"])));

            RemoveRows(GetRequiredTable("Streams"), row =>
                row["MatchID"] != DBNull.Value &&
                matchIds.Contains(Convert.ToInt32(row["MatchID"])));
            RemoveRows(matches, row =>
                AreEqual(row["Player1ID"], playerId) ||
                AreEqual(row["Player2ID"], playerId) ||
                AreEqual(row["WinnerPlayerID"], playerId));
            RemoveRows(GetRequiredTable("TournamentParticipants"), row => AreEqual(row["PlayerID"], playerId));
            RemoveRows(GetRequiredTable("TeamPlayers"), row => AreEqual(row["PlayerID"], playerId));
            RemoveRows(GetRequiredTable("Players"), row => AreEqual(row["PlayerID"], playerId));
        }

        private void DeleteGameCascadeInternal(int gameId)
        {
            List<int> tournamentIds = GetRequiredTable("Tournaments")
                .Rows
                .Cast<DataRow>()
                .Where(row => AreEqual(row["GameID"], gameId))
                .Select(row => Convert.ToInt32(row["TournamentID"]))
                .ToList();

            foreach (int tournamentId in tournamentIds)
            {
                DeleteTournamentCascadeInternal(tournamentId);
            }

            RemoveRows(GetRequiredTable("GameTitles"), row => AreEqual(row["GameID"], gameId));
        }

        private void DeleteSponsorCascadeInternal(int sponsorId)
        {
            RemoveRows(GetRequiredTable("TournamentSponsors"), row => AreEqual(row["SponsorID"], sponsorId));
            RemoveRows(GetRequiredTable("Sponsors"), row => AreEqual(row["SponsorID"], sponsorId));
        }

        private void DeleteStageCascadeInternal(int stageId)
        {
            DataTable matches = GetRequiredTable("Matches");
            HashSet<int> matchIds = new HashSet<int>(
                matches.Rows
                    .Cast<DataRow>()
                    .Where(row => AreEqual(row["StageID"], stageId))
                    .Select(row => Convert.ToInt32(row["MatchID"])));

            RemoveRows(GetRequiredTable("Streams"), row =>
                row["MatchID"] != DBNull.Value &&
                matchIds.Contains(Convert.ToInt32(row["MatchID"])));
            RemoveRows(matches, row => AreEqual(row["StageID"], stageId));
            RemoveRows(GetRequiredTable("TournamentStages"), row => AreEqual(row["StageID"], stageId));
        }

        private void DeleteMatchCascadeInternal(int matchId)
        {
            RemoveRows(GetRequiredTable("Streams"), row => AreEqual(row["MatchID"], matchId));
            RemoveRows(GetRequiredTable("Matches"), row => AreEqual(row["MatchID"], matchId));
        }

        private static int GetRequiredInt(IDictionary<string, object> values, string keyName)
        {
            if (values == null || string.IsNullOrWhiteSpace(keyName) || !values.ContainsKey(keyName) || values[keyName] == null || values[keyName] == DBNull.Value)
            {
                throw new InvalidOperationException("Не удалось определить ключ для каскадного удаления: " + keyName);
            }

            return Convert.ToInt32(values[keyName]);
        }

        private DataTable GetRequiredTable(string tableName)
        {
            if (!_tables.TryGetValue(tableName, out DataTable table))
            {
                throw new InvalidOperationException("Неизвестная таблица: " + tableName);
            }

            return table;
        }

        private static void RemoveRows(DataTable table, Func<DataRow, bool> predicate)
        {
            List<DataRow> rowsToDelete = table.Rows
                .Cast<DataRow>()
                .Where(predicate)
                .ToList();

            foreach (DataRow row in rowsToDelete)
            {
                table.Rows.Remove(row);
            }
        }

        private string GetTournamentParticipantMode(int tournamentId)
        {
            DataRow tournament = GetRequiredTable("Tournaments")
                .Rows
                .Cast<DataRow>()
                .FirstOrDefault(row => AreEqual(row["TournamentID"], tournamentId));
            return GetTournamentParticipantMode(tournament);
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

        private int CreateBracketStage(int tournamentId, int stageOrder, int teamsInRound, bool isFinal)
        {
            DataTable stages = GetRequiredTable("TournamentStages");
            DataRow stage = stages.NewRow();
            int stageId = NextIdentity("TournamentStages");
            stage["StageID"] = stageId;
            stage["TournamentID"] = tournamentId;
            stage["StageName"] = "Bracket - " + GetRoundTitle(teamsInRound);
            stage["StageOrder"] = 100 + stageOrder;
            stage["BracketType"] = isFinal ? "Final" : "Winner";
            stages.Rows.Add(stage);
            return stageId;
        }

        private List<int> GetOrderedParticipantIds(int tournamentId, bool isPlayerMode)
        {
            string columnName = isPlayerMode ? "PlayerID" : "TeamID";
            return GetRequiredTable("TournamentParticipants")
                .Rows
                .Cast<DataRow>()
                .Where(row => AreEqual(row["TournamentID"], tournamentId) &&
                    row.Table.Columns.Contains(columnName) &&
                    row[columnName] != DBNull.Value)
                .OrderBy(row => row["Seed"] == DBNull.Value ? 1 : 0)
                .ThenBy(row => row["Seed"] == DBNull.Value ? int.MaxValue : Convert.ToInt32(row["Seed"]))
                .ThenBy(row => Convert.ToInt32(row[columnName]))
                .Select(row => Convert.ToInt32(row[columnName]))
                .ToList();
        }

        private static int? ResolveAutoAdvanceParticipantId(int? team1Id, int? team2Id)
        {
            if (team1Id.HasValue && !team2Id.HasValue)
            {
                return team1Id;
            }

            if (team2Id.HasValue && !team1Id.HasValue)
            {
                return team2Id;
            }

            return null;
        }

        private void RemoveGeneratedBracket(int tournamentId)
        {
            DataTable stages = GetRequiredTable("TournamentStages");
            List<DataRow> generatedStages = stages.Rows
                .Cast<DataRow>()
                .Where(row => AreEqual(row["TournamentID"], tournamentId) && Convert.ToString(row["StageName"]).StartsWith("Bracket - ", StringComparison.CurrentCultureIgnoreCase))
                .ToList();

            HashSet<int> stageIds = new HashSet<int>(generatedStages.Select(row => Convert.ToInt32(row["StageID"])));
            DataTable matches = GetRequiredTable("Matches");
            List<DataRow> generatedMatches = matches.Rows
                .Cast<DataRow>()
                .Where(row => stageIds.Contains(Convert.ToInt32(row["StageID"])))
                .ToList();

            HashSet<int> generatedMatchIds = new HashSet<int>(generatedMatches.Select(row => Convert.ToInt32(row["MatchID"])));
            RemoveRows(GetRequiredTable("Streams"), row =>
                row.Table.Columns.Contains("MatchID") &&
                row["MatchID"] != DBNull.Value &&
                generatedMatchIds.Contains(Convert.ToInt32(row["MatchID"])));

            foreach (DataRow match in generatedMatches)
            {
                matches.Rows.Remove(match);
            }

            foreach (DataRow stage in generatedStages)
            {
                stages.Rows.Remove(stage);
            }
        }

        private int GetNextMatchNumber(int tournamentId)
        {
            DataTable matches = GetRequiredTable("Matches");
            List<int> existingNumbers = matches.Rows
                .Cast<DataRow>()
                .Where(row => AreEqual(row["TournamentID"], tournamentId) && row["MatchNumber"] != DBNull.Value)
                .Select(row => Convert.ToInt32(row["MatchNumber"]))
                .ToList();

            return existingNumbers.Count == 0 ? 1 : existingNumbers.Max() + 1;
        }

        private int NextIdentity(string tableName)
        {
            int value = _nextIdentities[tableName];
            _nextIdentities[tableName] = value + 1;
            return value;
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

        private static string GetRoundTitle(int teamsInRound)
        {
            switch (teamsInRound)
            {
                case 2:
                    return "Grand Final";
                case 4:
                    return "Semifinals";
                case 8:
                    return "Quarterfinals";
                case 16:
                    return "Round of 16";
                case 32:
                    return "Round of 32";
                default:
                    return "Round of " + teamsInRound;
            }
        }

        private static DataColumn Column(string name, Type type)
        {
            DataColumn column = new DataColumn(name, type)
            {
                AllowDBNull = true
            };
            return column;
        }

        private static object NormalizeValue(Type targetType, object value)
        {
            if (value == null || value == DBNull.Value)
            {
                return DBNull.Value;
            }

            if (targetType == typeof(string))
            {
                return Convert.ToString(value);
            }

            if (targetType == typeof(int))
            {
                return Convert.ToInt32(value);
            }

            if (targetType == typeof(decimal))
            {
                return Convert.ToDecimal(value);
            }

            if (targetType == typeof(bool))
            {
                return Convert.ToBoolean(value);
            }

            if (targetType == typeof(DateTime))
            {
                return Convert.ToDateTime(value);
            }

            return value;
        }

        private static bool AreEqual(object left, object right)
        {
            if (left == DBNull.Value)
            {
                left = null;
            }

            if (right == DBNull.Value)
            {
                right = null;
            }

            if (left == null && right == null)
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            if (left is string || right is string)
            {
                return string.Equals(Convert.ToString(left), Convert.ToString(right), StringComparison.CurrentCultureIgnoreCase);
            }

            if (left is DateTime || right is DateTime)
            {
                return Convert.ToDateTime(left).Date == Convert.ToDateTime(right).Date;
            }

            if (left is bool || right is bool)
            {
                return Convert.ToBoolean(left) == Convert.ToBoolean(right);
            }

            if (left is decimal || right is decimal || left is double || right is double || left is float || right is float)
            {
                return Convert.ToDecimal(left) == Convert.ToDecimal(right);
            }

            return Convert.ToInt64(left) == Convert.ToInt64(right);
        }

        private static bool IsNull(object value)
        {
            return value == null || value == DBNull.Value;
        }
    }
}






