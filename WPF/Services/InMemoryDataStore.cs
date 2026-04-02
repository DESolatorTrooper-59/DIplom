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
        private readonly Dictionary<string, string> _users = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _identityColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _nextIdentities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private InMemoryDataStore()
        {
            InitializeSchema();
            SeedData();
        }

        public static InMemoryDataStore Instance => _instance.Value;

        public void EnsureUser(string login, string password)
        {
            lock (_syncRoot)
            {
                if (_users.ContainsKey(login))
                {
                    return;
                }

                _users[login] = password;
            }
        }

        public bool ValidateUser(string login, string password)
        {
            lock (_syncRoot)
            {
                return _users.TryGetValue(login, out string storedPassword) && string.Equals(storedPassword, password, StringComparison.Ordinal);
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

        public void Insert(string tableName, IDictionary<string, object> values)
        {
            lock (_syncRoot)
            {
                DataTable table = GetRequiredTable(tableName);
                DataRow row = table.NewRow();

                foreach (DataColumn column in table.Columns)
                {
                    if (_identityColumns.TryGetValue(tableName, out string identityColumn) && string.Equals(identityColumn, column.ColumnName, StringComparison.OrdinalIgnoreCase))
                    {
                        row[column.ColumnName] = _nextIdentities[tableName]++;
                        continue;
                    }

                    object value = values.ContainsKey(column.ColumnName) ? values[column.ColumnName] : null;
                    row[column.ColumnName] = NormalizeValue(column.DataType, value);
                }

                ApplyDefaults(tableName, row);
                table.Rows.Add(row);
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

                if (tournament.Table.Columns.Contains("ParticipantMode") &&
                    string.Equals(Convert.ToString(tournament["ParticipantMode"]), "Игроки", StringComparison.CurrentCultureIgnoreCase))
                {
                    throw new InvalidOperationException("Турнирная сетка пока поддерживается только для турниров с командами.");
                }

                List<int> orderedTeamIds = GetOrderedParticipantTeamIds(tournamentId);
                if (orderedTeamIds.Count < 2)
                {
                    throw new InvalidOperationException("Для построения сетки нужно минимум две команды-участницы.");
                }

                RemoveGeneratedBracket(tournamentId);

                int bracketSize = NextPowerOfTwo(orderedTeamIds.Count);
                int roundCount = (int)Math.Log(bracketSize, 2);
                int[] seedPositions = BuildSeedPositions(bracketSize);
                int?[] slots = new int?[bracketSize];
                for (int index = 0; index < seedPositions.Length; index++)
                {
                    int seed = seedPositions[index];
                    if (seed <= orderedTeamIds.Count)
                    {
                        slots[index] = orderedTeamIds[seed - 1];
                    }
                }

                DataTable matches = GetRequiredTable("Matches");
                DateTime tournamentStartDate = Convert.ToDateTime(tournament["StartDate"]);
                int matchNumber = 1;
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
                List<int> availableTeams = GetOrderedParticipantTeamIds(tournamentId);
                int? team1Id = targetRound.RoundIndex == 0 ? NormalizeBracketTeamId(request.Team1Id, availableTeams) : ToNullableInt(match["Team1ID"]);
                int? team2Id = targetRound.RoundIndex == 0 ? NormalizeBracketTeamId(request.Team2Id, availableTeams) : ToNullableInt(match["Team2ID"]);

                if (team1Id.HasValue && team2Id.HasValue && team1Id.Value == team2Id.Value)
                {
                    throw new InvalidOperationException("В одном матче нельзя выбрать одну и ту же команду дважды.");
                }

                if (request.Team1Score < 0 || request.Team2Score < 0)
                {
                    throw new InvalidOperationException("Счет команды не может быть отрицательным.");
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
                throw new InvalidOperationException("В сетке можно использовать только команды-участницы выбранного турнира.");
            }

            return teamId;
        }

        private static int? NormalizeWinnerTeamId(int? winnerTeamId, int? team1Id, int? team2Id, string status, int team1Score, int team2Score)
        {
            if (winnerTeamId.HasValue)
            {
                if (winnerTeamId != team1Id && winnerTeamId != team2Id)
                {
                    throw new InvalidOperationException("Победитель должен совпадать с одной из команд матча.");
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
                Column("BirthDate", typeof(DateTime)));

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
            EnsureUser("admin", "password");
            EnsureUser("organizer", "organizer");

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
                ["GameName"] = "Dota 2",
                ["Developer"] = "Valve",
                ["ReleaseYear"] = 2013,
                ["MaxPlayersPerTeam"] = 5
            });

            SeedRow("GameTitles", new Dictionary<string, object>
            {
                ["GameID"] = 3,
                ["GameName"] = "VALORANT",
                ["Developer"] = "Riot Games",
                ["ReleaseYear"] = 2020,
                ["MaxPlayersPerTeam"] = 5
            });

            SeedTeam(1, "NAVI", new DateTime(2009, 12, 17), "Ukraine", "B1ad3", -60);
            SeedTeam(2, "Team Spirit", new DateTime(2015, 12, 5), "Russia", "hally", -55);
            SeedTeam(3, "Virtus.pro", new DateTime(2003, 11, 1), "Russia", "dastan", -50);
            SeedTeam(4, "G2 Esports", new DateTime(2015, 2, 24), "Germany", "TaZ", -45);
            SeedTeam(5, "FaZe Clan", new DateTime(2010, 5, 30), "United States", "NEO", -40);
            SeedTeam(6, "MOUZ", new DateTime(2002, 8, 1), "Germany", "sycrone", -35);
            SeedTeam(7, "Team Vitality", new DateTime(2013, 8, 5), "France", "XTQZZZ", -30);
            SeedTeam(8, "Cloud9", new DateTime(2013, 1, 9), "United States", "groove", -25);
            SeedTeam(9, "BetBoom Team", new DateTime(2022, 4, 8), "Russia", "boolk", -20);
            SeedTeam(10, "OG", new DateTime(2015, 8, 31), "Europe", "ruggah", -15);

            SeedPlayer(1, "s1mple", "Oleksandr Kostyliev", "Ukraine", new DateTime(1997, 10, 2));
            SeedPlayer(2, "donk", "Danil Kryshkovets", "Russia", new DateTime(2007, 1, 25));
            SeedPlayer(3, "fame", "Pyotr Bolyshev", "Russia", new DateTime(2003, 3, 3));
            SeedPlayer(4, "NiKo", "Nikola Kovac", "Bosnia and Herzegovina", new DateTime(1997, 2, 16));
            SeedPlayer(5, "ropz", "Robin Kool", "Estonia", new DateTime(1999, 12, 22));
            SeedPlayer(6, "xertioN", "Dorian Berman", "Israel", new DateTime(2004, 7, 22));
            SeedPlayer(7, "ZywOo", "Mathieu Herbaut", "France", new DateTime(2000, 11, 9));
            SeedPlayer(8, "electroNic", "Denis Sharipov", "Russia", new DateTime(1998, 9, 2));
            SeedPlayer(9, "kataomi", "Vladislav Semenov", "Russia", new DateTime(1999, 1, 1));
            SeedPlayer(10, "bzm", "Bozhidar Bogdanov", "Bulgaria", new DateTime(2005, 1, 1));

            SeedTeamPlayer(1, 1, 1, "AWPer", -6);
            SeedTeamPlayer(2, 2, 2, "Rifler", -5);
            SeedTeamPlayer(3, 3, 3, "Entry Fragger", -5);
            SeedTeamPlayer(4, 4, 4, "Star Rifler", -4);
            SeedTeamPlayer(5, 5, 5, "Closer", -4);
            SeedTeamPlayer(6, 6, 6, "Lurker", -3);
            SeedTeamPlayer(7, 7, 7, "Sniper", -3);
            SeedTeamPlayer(8, 8, 8, "Captain", -2);
            SeedTeamPlayer(9, 9, 9, "Support", -2);
            SeedTeamPlayer(10, 10, 10, "Midlaner", -1);

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

            SeedRow("Tournaments", new Dictionary<string, object>
            {
                ["TournamentID"] = 1,
                ["TournamentName"] = "Spring Invitational",
                ["GameID"] = 1,
                ["StartDate"] = DateTime.Today.AddDays(-3),
                ["EndDate"] = DateTime.Today.AddDays(1),
                ["PrizePool"] = 250000m,
                ["Organizer"] = "admin",
                ["Location"] = "Moscow",
                ["FormatType"] = "Single Elimination",
                ["MaxTeams"] = 8,
                ["ParticipantMode"] = "Команды"
            });

            SeedRow("Tournaments", new Dictionary<string, object>
            {
                ["TournamentID"] = 2,
                ["TournamentName"] = "Autumn Masters",
                ["GameID"] = 2,
                ["StartDate"] = DateTime.Today.AddDays(20),
                ["EndDate"] = DateTime.Today.AddDays(24),
                ["PrizePool"] = 500000m,
                ["Organizer"] = "admin",
                ["Location"] = "Belgrade",
                ["FormatType"] = "Single Elimination",
                ["MaxTeams"] = 4,
                ["ParticipantMode"] = "Команды"
            });

            SeedRow("TournamentStages", new Dictionary<string, object>
            {
                ["StageID"] = 1,
                ["TournamentID"] = 1,
                ["StageName"] = "Playoffs",
                ["StageOrder"] = 1,
                ["BracketType"] = "Winner"
            });

            SeedRow("TournamentStages", new Dictionary<string, object>
            {
                ["StageID"] = 2,
                ["TournamentID"] = 2,
                ["StageName"] = "Main Event",
                ["StageOrder"] = 1,
                ["BracketType"] = "Winner"
            });

            SeedTournamentParticipant(1, 1, 1, 1);
            SeedTournamentParticipant(2, 1, 2, 2);
            SeedTournamentParticipant(3, 1, 3, 3);
            SeedTournamentParticipant(4, 1, 4, 4);
            SeedTournamentParticipant(5, 1, 5, 5);
            SeedTournamentParticipant(6, 1, 6, 6);
            SeedTournamentParticipant(7, 1, 7, 7);
            SeedTournamentParticipant(8, 1, 8, 8);
            SeedTournamentParticipant(9, 2, 3, 1);
            SeedTournamentParticipant(10, 2, 5, 2);
            SeedTournamentParticipant(11, 2, 9, 3);
            SeedTournamentParticipant(12, 2, 10, 4);

            SeedRow("Streams", new Dictionary<string, object>
            {
                ["StreamID"] = 1,
                ["TournamentID"] = 1,
                ["MatchID"] = DBNull.Value,
                ["Platform"] = "Twitch",
                ["StreamURL"] = "https://twitch.tv/tournamentsdemo"
            });

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

        private void SeedTeam(int teamId, string name, DateTime foundedDate, string country, string coachName, int createdOffsetDays)
        {
            SeedRow("Teams", new Dictionary<string, object>
            {
                ["TeamID"] = teamId,
                ["TeamName"] = name,
                ["FoundedDate"] = foundedDate,
                ["Country"] = country,
                ["CoachName"] = coachName,
                ["CreatedDate"] = DateTime.Today.AddDays(createdOffsetDays)
            });
        }

        private void SeedPlayer(int playerId, string nickname, string realName, string country, DateTime birthDate)
        {
            SeedRow("Players", new Dictionary<string, object>
            {
                ["PlayerID"] = playerId,
                ["Nickname"] = nickname,
                ["RealName"] = realName,
                ["Country"] = country,
                ["BirthDate"] = birthDate
            });
        }

        private void SeedTeamPlayer(int teamPlayerId, int teamId, int playerId, string role, int joinedMonthsAgo)
        {
            SeedRow("TeamPlayers", new Dictionary<string, object>
            {
                ["TeamPlayerID"] = teamPlayerId,
                ["TeamID"] = teamId,
                ["PlayerID"] = playerId,
                ["JoinDate"] = DateTime.Today.AddMonths(joinedMonthsAgo),
                ["LeaveDate"] = DBNull.Value,
                ["IsActive"] = true,
                ["Role"] = role
            });
        }

        private void SeedTournamentParticipant(int participationId, int tournamentId, int? teamId, int seed, int? playerId = null)
        {
            SeedRow("TournamentParticipants", new Dictionary<string, object>
            {
                ["ParticipationID"] = participationId,
                ["TournamentID"] = tournamentId,
                ["TeamID"] = teamId.HasValue ? (object)teamId.Value : DBNull.Value,
                ["PlayerID"] = playerId.HasValue ? (object)playerId.Value : DBNull.Value,
                ["Seed"] = seed,
                ["FinalPlace"] = DBNull.Value
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

        private DataTable GetRequiredTable(string tableName)
        {
            if (!_tables.TryGetValue(tableName, out DataTable table))
            {
                throw new InvalidOperationException("Неизвестная таблица: " + tableName);
            }

            return table;
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

        private List<int> GetOrderedParticipantTeamIds(int tournamentId)
        {
            return GetRequiredTable("TournamentParticipants")
                .Rows
                .Cast<DataRow>()
                .Where(row => AreEqual(row["TournamentID"], tournamentId) && row["TeamID"] != DBNull.Value)
                .OrderBy(row => row["Seed"] == DBNull.Value ? 1 : 0)
                .ThenBy(row => row["Seed"] == DBNull.Value ? int.MaxValue : Convert.ToInt32(row["Seed"]))
                .ThenBy(row => Convert.ToInt32(row["TeamID"]))
                .Select(row => Convert.ToInt32(row["TeamID"]))
                .ToList();
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

            foreach (DataRow match in generatedMatches)
            {
                matches.Rows.Remove(match);
            }

            foreach (DataRow stage in generatedStages)
            {
                stages.Rows.Remove(stage);
            }
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






