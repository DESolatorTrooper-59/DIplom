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
        private const string Player1IdColumn = "Player1ID";
        private const string Player2IdColumn = "Player2ID";
        private const string WinnerPlayerIdColumn = "WinnerPlayerID";

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
                .ThenBy(item => ResolveParticipantName(participantsById, item.ParticipantId.Value, isPlayerMode))
                .Select(item =>
                {
                    DataRow participant = participantsById[item.ParticipantId.Value];
                    return new BracketTeamOption
                    {
                        TeamId = item.ParticipantId.Value,
                        TeamName = ResolveParticipantName(participantsById, item.ParticipantId.Value, isPlayerMode),
                        SecondaryText = GetParticipantSecondaryText(participant, isPlayerMode)
                    };
                })
                .ToList();
        }

        public TournamentBracketSnapshot BuildPreview(int tournamentId)
        {
            DataRow tournament = GetTournamentRow(tournamentId);
            if (tournament == null)
            {
                return new TournamentBracketSnapshot();
            }

            string formatType = GetTournamentFormatType(tournament);
            TournamentFormatKind formatKind = TournamentFormatPlanBuilder.ParseKind(formatType);
            string participantMode = GetTournamentParticipantMode(tournament);
            bool isPlayerMode = IsPlayerMode(participantMode);
            bool usePlayerColumns = ShouldUsePlayerColumns(isPlayerMode);
            List<BracketParticipantViewModel> participants = GetParticipants(tournamentId);
            TournamentBracketSnapshot snapshot = new TournamentBracketSnapshot
            {
                TournamentId = tournamentId,
                TournamentName = Convert.ToString(tournament["TournamentName"]),
                ParticipantCount = participants.Count,
                BracketSize = formatKind == TournamentFormatKind.League ? participants.Count : NextPowerOfTwo(Math.Max(2, participants.Count)),
                FormatType = NormalizeFormatType(formatKind)
            };

            if (participants.Count < 2)
            {
                foreach (BracketParticipantViewModel participant in participants)
                {
                    snapshot.Participants.Add(participant);
                }

                return snapshot;
            }

            TournamentFormatPlan plan = TournamentFormatPlanBuilder.Build(formatKind, participants.Count);
            PersistedPlanContext persisted = LoadPersistedPlanContext(tournamentId, plan);
            Dictionary<int, DataRow> participantsById = GetParticipantLookup(participantMode);
            Dictionary<string, MatchRuntimeState> statesByKey = CreateRuntimeStates(
                plan,
                participants,
                isPlayerMode,
                usePlayerColumns,
                Convert.ToDateTime(tournament["StartDate"]),
                persisted);

            List<BracketRoundViewModel> rounds = BuildRounds(plan, statesByKey, participantsById, isPlayerMode, persisted.HasGeneratedLayout);
            foreach (BracketRoundViewModel round in rounds)
            {
                snapshot.Rounds.Add(round);
            }

            snapshot.HasGeneratedBracket = persisted.HasGeneratedLayout;
            snapshot.MatchCount = rounds.Sum(round => round.Matches.Count);

            List<BracketParticipantViewModel> displayParticipants = BuildDisplayParticipants(formatKind, participants, statesByKey.Values.ToList());
            foreach (BracketParticipantViewModel participant in displayParticipants)
            {
                snapshot.Participants.Add(participant);
            }

            snapshot.ChampionName = ResolveChampionName(formatKind, rounds, participants, statesByKey.Values.ToList());
            return snapshot;
        }

        public int GenerateBracket(int tournamentId)
        {
            DataRow tournament = GetTournamentRow(tournamentId);
            if (tournament == null)
            {
                throw new InvalidOperationException("Выбранный турнир не найден.");
            }

            string formatType = GetTournamentFormatType(tournament);
            TournamentFormatKind formatKind = TournamentFormatPlanBuilder.ParseKind(formatType);
            string participantMode = GetTournamentParticipantMode(tournament);
            bool isPlayerMode = IsPlayerMode(participantMode);
            bool usePlayerColumns = ShouldUsePlayerColumns(isPlayerMode);
            if (isPlayerMode && !usePlayerColumns && !_database.IsTestMode)
            {
                throw new InvalidOperationException("Текущее хранилище не поддерживает матчи турниров с игроками без столбцов Player1ID/Player2ID/WinnerPlayerID.");
            }

            List<BracketParticipantViewModel> participants = GetParticipants(tournamentId);
            if (participants.Count < 2)
            {
                throw new InvalidOperationException("Для построения формата нужно минимум два участника.");
            }

            TournamentFormatPlan plan = TournamentFormatPlanBuilder.Build(formatKind, participants.Count);
            Dictionary<string, MatchRuntimeState> previewStates = CreateRuntimeStates(
                plan,
                participants,
                isPlayerMode,
                usePlayerColumns,
                Convert.ToDateTime(tournament["StartDate"]),
                PersistedPlanContext.Empty);

            RemoveGeneratedStages(tournamentId);

            Dictionary<string, int> stageIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (TournamentStagePlan stage in plan.Stages)
            {
                int? stageId = _database.PeekNextIdentityValue("TournamentStages");
                if (!stageId.HasValue)
                {
                    throw new InvalidOperationException("Не удалось определить следующий идентификатор этапа турнира.");
                }

                _database.Insert("TournamentStages", new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["TournamentID"] = tournamentId,
                    ["StageName"] = stage.StageName,
                    ["StageOrder"] = stage.StageOrder,
                    ["BracketType"] = stage.BracketType
                });

                stageIds[stage.StageName] = stageId.Value;
            }

            int nextMatchNumber = GetNextMatchNumber(tournamentId);
            int createdMatches = 0;
            foreach (TournamentStagePlan stage in plan.Stages)
            {
                foreach (TournamentMatchPlan matchPlan in stage.Matches)
                {
                    MatchRuntimeState state = previewStates[matchPlan.MatchKey];
                    _database.Insert("Matches", BuildMatchValues(
                        stageIds[stage.StageName],
                        tournamentId,
                        nextMatchNumber++,
                        state.Team1Id,
                        state.Team2Id,
                        ResolveWinnerId(state),
                        state.Team1Score,
                        state.Team2Score,
                        state.MatchDate,
                        state.BestOf,
                        state.Status,
                        isPlayerMode,
                        usePlayerColumns));

                    createdMatches++;
                }
            }

            PersistLeagueFinalPlaces(tournamentId, formatKind, participants, previewStates.Values.ToList());
            return createdMatches;
        }

        public void UpdateMatch(int tournamentId, BracketMatchUpdateRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            DataRow tournament = GetTournamentRow(tournamentId);
            if (tournament == null)
            {
                throw new InvalidOperationException("Выбранный турнир не найден.");
            }

            string formatType = GetTournamentFormatType(tournament);
            TournamentFormatKind formatKind = TournamentFormatPlanBuilder.ParseKind(formatType);
            string participantMode = GetTournamentParticipantMode(tournament);
            bool isPlayerMode = IsPlayerMode(participantMode);
            bool usePlayerColumns = ShouldUsePlayerColumns(isPlayerMode);
            List<BracketParticipantViewModel> participants = GetParticipants(tournamentId);
            TournamentFormatPlan plan = TournamentFormatPlanBuilder.Build(formatKind, participants.Count);
            PersistedPlanContext persisted = LoadPersistedPlanContext(tournamentId, plan);
            if (!persisted.HasGeneratedLayout)
            {
                throw new InvalidOperationException("Для выбранного турнира сетка или расписание еще не созданы.");
            }

            Dictionary<string, MatchRuntimeState> statesByKey = CreateRuntimeStates(
                plan,
                participants,
                isPlayerMode,
                usePlayerColumns,
                Convert.ToDateTime(tournament["StartDate"]),
                persisted);

            MatchRuntimeState targetMatch = statesByKey.Values.FirstOrDefault(state => state.MatchId == request.MatchId);
            if (targetMatch == null)
            {
                throw new InvalidOperationException("Матч турнирного формата не найден.");
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

            List<int> availableParticipants = participants.Select(item => item.TeamId).ToList();
            int? participant1Id = targetMatch.MatchPlan.CanEditParticipants
                ? NormalizeParticipantId(request.Team1Id, availableParticipants)
                : targetMatch.Team1Id;
            int? participant2Id = targetMatch.MatchPlan.CanEditParticipants
                ? NormalizeParticipantId(request.Team2Id, availableParticipants)
                : targetMatch.Team2Id;

            if (participant1Id.HasValue && participant2Id.HasValue && participant1Id.Value == participant2Id.Value)
            {
                throw new InvalidOperationException("В одном матче нельзя выбрать одного и того же участника дважды.");
            }

            targetMatch.Team1Id = participant1Id;
            targetMatch.Team2Id = participant2Id;
            targetMatch.Team1Score = request.Team1Score;
            targetMatch.Team2Score = request.Team2Score;
            targetMatch.BestOf = request.BestOf;
            targetMatch.MatchDate = request.MatchDate.Date;
            targetMatch.Status = NormalizeStatusValue(request.Status);
            targetMatch.WinnerId = NormalizeWinnerId(request.WinnerTeamId, participant1Id, participant2Id, targetMatch.Status, request.Team1Score, request.Team2Score);

            ApplyPlanPropagation(plan, statesByKey, true);
            PersistMatchStates(statesByKey.Values.ToList(), isPlayerMode, usePlayerColumns);
            PersistLeagueFinalPlaces(tournamentId, formatKind, participants, statesByKey.Values.ToList());
        }

        private Dictionary<string, MatchRuntimeState> CreateRuntimeStates(
            TournamentFormatPlan plan,
            IList<BracketParticipantViewModel> participants,
            bool isPlayerMode,
            bool usePlayerColumns,
            DateTime tournamentStartDate,
            PersistedPlanContext persisted)
        {
            Dictionary<string, MatchRuntimeState> statesByKey = new Dictionary<string, MatchRuntimeState>(StringComparer.OrdinalIgnoreCase);

            foreach (TournamentStagePlan stage in plan.Stages)
            {
                List<DataRow> persistedMatches = persisted.HasGeneratedLayout && persisted.MatchesByStageName.ContainsKey(stage.StageName)
                    ? persisted.MatchesByStageName[stage.StageName]
                    : null;

                for (int matchIndex = 0; matchIndex < stage.Matches.Count; matchIndex++)
                {
                    TournamentMatchPlan matchPlan = stage.Matches[matchIndex];
                    DataRow persistedRow = persistedMatches == null ? null : persistedMatches[matchIndex];

                    statesByKey[matchPlan.MatchKey] = new MatchRuntimeState
                    {
                        StagePlan = stage,
                        MatchPlan = matchPlan,
                        MatchId = persistedRow == null ? 0 : Convert.ToInt32(persistedRow["MatchID"]),
                        MatchCode = persistedRow == null ? "M" + matchPlan.PlannedMatchNumber : "M" + Convert.ToInt32(persistedRow["MatchNumber"]),
                        Team1Id = persistedRow == null ? ResolveDirectSeedParticipantId(matchPlan.Slot1, participants) : GetPersistedParticipantId(persistedRow, isPlayerMode, usePlayerColumns, 1),
                        Team2Id = persistedRow == null ? ResolveDirectSeedParticipantId(matchPlan.Slot2, participants) : GetPersistedParticipantId(persistedRow, isPlayerMode, usePlayerColumns, 2),
                        WinnerId = persistedRow == null ? (int?)null : GetPersistedParticipantId(persistedRow, isPlayerMode, usePlayerColumns, 3),
                        Team1Score = persistedRow == null ? 0 : ReadInt(persistedRow["Team1Score"], 0),
                        Team2Score = persistedRow == null ? 0 : ReadInt(persistedRow["Team2Score"], 0),
                        BestOf = persistedRow == null ? matchPlan.BestOf : ReadInt(persistedRow["BestOf"], matchPlan.BestOf),
                        MatchDate = persistedRow == null ? tournamentStartDate.AddDays(matchPlan.DayOffset) : ParseDate(persistedRow["MatchDate"]),
                        Status = persistedRow == null ? "Scheduled" : NormalizeStatusValue(Convert.ToString(persistedRow["Status"]))
                    };
                }
            }

            ApplyPlanPropagation(plan, statesByKey, false);
            return statesByKey;
        }

        private List<BracketRoundViewModel> BuildRounds(
            TournamentFormatPlan plan,
            Dictionary<string, MatchRuntimeState> statesByKey,
            Dictionary<int, DataRow> participantsById,
            bool isPlayerMode,
            bool isEditable)
        {
            List<BracketRoundViewModel> rounds = new List<BracketRoundViewModel>();
            int roundIndex = 0;

            foreach (TournamentStagePlan stage in plan.Stages)
            {
                BracketRoundViewModel round = new BracketRoundViewModel
                {
                    Title = stage.Title,
                    SectionKey = stage.SectionKey,
                    LayoutColumn = stage.LayoutColumn,
                    SectionIndex = stage.SectionIndex
                };

                foreach (TournamentMatchPlan matchPlan in stage.Matches)
                {
                    MatchRuntimeState state = statesByKey[matchPlan.MatchKey];
                    int? winnerId = ResolveWinnerId(state);
                    string winnerName = winnerId.HasValue ? ResolveParticipantName(participantsById, winnerId.Value, isPlayerMode) : string.Empty;

                    round.Matches.Add(new BracketMatchViewModel
                    {
                        MatchId = state.MatchId,
                        MatchKey = matchPlan.MatchKey,
                        RoundIndex = roundIndex,
                        MatchIndex = matchPlan.MatchIndex,
                        MatchCode = state.MatchCode,
                        Team1Id = state.Team1Id,
                        Team2Id = state.Team2Id,
                        WinnerTeamId = winnerId,
                        Team1Name = ResolveSlotDisplayName(matchPlan.Slot1, state.Team1Id, statesByKey, participantsById, isPlayerMode),
                        Team2Name = ResolveSlotDisplayName(matchPlan.Slot2, state.Team2Id, statesByKey, participantsById, isPlayerMode),
                        Team1Score = state.Team1Score,
                        Team2Score = state.Team2Score,
                        BestOf = state.BestOf,
                        MatchDate = state.MatchDate,
                        MetaText = BuildMetaText(state.BestOf, state.MatchDate),
                        Status = state.Status,
                        StatusText = BuildStatusText(state.Status, winnerName, state.Team1Id, state.Team2Id),
                        WinnerName = winnerName,
                        IsEditable = isEditable,
                        CanEditTeams = isEditable && matchPlan.CanEditParticipants,
                        SourceMatchKey1 = matchPlan.Slot1.SourceMatchKey,
                        SourceMatchKey2 = matchPlan.Slot2.SourceMatchKey
                    });
                }

                rounds.Add(round);
                roundIndex++;
            }

            return rounds;
        }

        private void ApplyPlanPropagation(TournamentFormatPlan plan, Dictionary<string, MatchRuntimeState> statesByKey, bool resetDependentMatches)
        {
            foreach (TournamentStagePlan stage in plan.Stages)
            {
                foreach (TournamentMatchPlan matchPlan in stage.Matches)
                {
                    MatchRuntimeState state = statesByKey[matchPlan.MatchKey];
                    bool changed = false;

                    if (!matchPlan.Slot1.IsDirectSeed)
                    {
                        int? expectedTeam1Id = ResolveSlotParticipantId(matchPlan.Slot1, statesByKey);
                        if (state.Team1Id != expectedTeam1Id)
                        {
                            state.Team1Id = expectedTeam1Id;
                            changed = true;
                        }
                    }

                    if (!matchPlan.Slot2.IsDirectSeed)
                    {
                        int? expectedTeam2Id = ResolveSlotParticipantId(matchPlan.Slot2, statesByKey);
                        if (state.Team2Id != expectedTeam2Id)
                        {
                            state.Team2Id = expectedTeam2Id;
                            changed = true;
                        }
                    }

                    if (resetDependentMatches && changed)
                    {
                        state.Team1Score = 0;
                        state.Team2Score = 0;
                        state.Status = "Scheduled";
                        state.WinnerId = null;
                    }

                    state.WinnerId = ResolveWinnerId(state);
                }
            }
        }

        private void PersistMatchStates(IList<MatchRuntimeState> states, bool isPlayerMode, bool usePlayerColumns)
        {
            foreach (MatchRuntimeState state in states.Where(item => item.MatchId > 0))
            {
                Dictionary<string, object> values = BuildMatchValues(
                    0,
                    0,
                    0,
                    state.Team1Id,
                    state.Team2Id,
                    ResolveWinnerId(state),
                    state.Team1Score,
                    state.Team2Score,
                    state.MatchDate,
                    state.BestOf,
                    state.Status,
                    isPlayerMode,
                    usePlayerColumns);

                values.Remove("TournamentID");
                values.Remove("StageID");
                values.Remove("MatchNumber");

                _database.Update("Matches", new[] { "MatchID" }, values, new Dictionary<string, object>
                {
                    ["MatchID"] = state.MatchId
                });
            }
        }

        private void RemoveGeneratedStages(int tournamentId)
        {
            DataTable stages = _database.GetTable("TournamentStages");
            DataTable matches = _database.GetTable("Matches");
            DataTable streams = _database.GetTable("Streams");

            List<DataRow> generatedStages = stages.Rows
                .Cast<DataRow>()
                .Where(row => Convert.ToInt32(row["TournamentID"]) == tournamentId && IsGeneratedStage(row))
                .ToList();

            HashSet<int> generatedStageIds = new HashSet<int>(generatedStages.Select(row => Convert.ToInt32(row["StageID"])));
            List<DataRow> generatedMatches = matches.Rows
                .Cast<DataRow>()
                .Where(row => generatedStageIds.Contains(Convert.ToInt32(row["StageID"])))
                .ToList();

            HashSet<int> generatedMatchIds = new HashSet<int>(generatedMatches.Select(row => Convert.ToInt32(row["MatchID"])));
            List<DataRow> generatedStreams = streams.Rows
                .Cast<DataRow>()
                .Where(row => row["MatchID"] != DBNull.Value && generatedMatchIds.Contains(Convert.ToInt32(row["MatchID"])))
                .ToList();

            foreach (DataRow stream in generatedStreams)
            {
                _database.Delete("Streams", new[] { "StreamID" }, new Dictionary<string, object>
                {
                    ["StreamID"] = Convert.ToInt32(stream["StreamID"])
                });
            }

            foreach (DataRow match in generatedMatches)
            {
                _database.Delete("Matches", new[] { "MatchID" }, new Dictionary<string, object>
                {
                    ["MatchID"] = Convert.ToInt32(match["MatchID"])
                });
            }

            foreach (DataRow stage in generatedStages)
            {
                _database.Delete("TournamentStages", new[] { "StageID" }, new Dictionary<string, object>
                {
                    ["StageID"] = Convert.ToInt32(stage["StageID"])
                });
            }
        }

        private PersistedPlanContext LoadPersistedPlanContext(int tournamentId, TournamentFormatPlan plan)
        {
            DataTable stages = _database.GetTable("TournamentStages");
            DataTable matches = _database.GetTable("Matches");
            Dictionary<string, DataRow> stageByName = stages.Rows
                .Cast<DataRow>()
                .Where(row => Convert.ToInt32(row["TournamentID"]) == tournamentId && IsGeneratedStage(row))
                .GroupBy(row => Convert.ToString(row["StageName"]), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderBy(row => Convert.ToInt32(row["StageID"])).First(), StringComparer.OrdinalIgnoreCase);

            if (!plan.Stages.All(stage => stageByName.ContainsKey(stage.StageName)))
            {
                return PersistedPlanContext.Empty;
            }

            PersistedPlanContext context = new PersistedPlanContext { HasGeneratedLayout = true };
            foreach (TournamentStagePlan stage in plan.Stages)
            {
                int stageId = Convert.ToInt32(stageByName[stage.StageName]["StageID"]);
                List<DataRow> stageMatches = matches.Rows
                    .Cast<DataRow>()
                    .Where(row => Convert.ToInt32(row["StageID"]) == stageId)
                    .OrderBy(row => Convert.ToInt32(row["MatchNumber"]))
                    .ToList();

                if (stageMatches.Count != stage.Matches.Count)
                {
                    return PersistedPlanContext.Empty;
                }

                context.MatchesByStageName[stage.StageName] = stageMatches;
            }

            return context;
        }

        private void PersistLeagueFinalPlaces(int tournamentId, TournamentFormatKind formatKind, IList<BracketParticipantViewModel> participants, IList<MatchRuntimeState> states)
        {
            DataTable participantRows = _database.GetTable("TournamentParticipants");
            List<DataRow> tournamentParticipantRows = participantRows.Rows
                .Cast<DataRow>()
                .Where(row => Convert.ToInt32(row["TournamentID"]) == tournamentId)
                .ToList();

            Dictionary<int, int?> finalPlaces = new Dictionary<int, int?>();
            if (formatKind == TournamentFormatKind.League && AreAllLeagueMatchesCompleted(states))
            {
                int place = 1;
                foreach (LeagueStanding standing in ComputeLeagueStandings(participants, states))
                {
                    finalPlaces[standing.ParticipantId] = place++;
                }
            }

            bool isPlayerMode = IsPlayerMode(GetTournamentParticipantMode(tournamentId));
            foreach (DataRow row in tournamentParticipantRows)
            {
                int? participantId = GetParticipantId(row, isPlayerMode);
                int? finalPlace = participantId.HasValue && finalPlaces.ContainsKey(participantId.Value)
                    ? finalPlaces[participantId.Value]
                    : (int?)null;

                _database.Update("TournamentParticipants", new[] { "ParticipationID" }, new Dictionary<string, object>
                {
                    ["FinalPlace"] = finalPlace
                }, new Dictionary<string, object>
                {
                    ["ParticipationID"] = Convert.ToInt32(row["ParticipationID"])
                });
            }
        }

        private List<BracketParticipantViewModel> BuildDisplayParticipants(TournamentFormatKind formatKind, IList<BracketParticipantViewModel> participants, IList<MatchRuntimeState> states)
        {
            if (formatKind != TournamentFormatKind.League)
            {
                return participants.Select(CloneParticipant).ToList();
            }

            Dictionary<int, LeagueStanding> standings = ComputeLeagueStandings(participants, states)
                .ToDictionary(item => item.ParticipantId);

            return participants
                .Select(CloneParticipant)
                .Select(item =>
                {
                    LeagueStanding standing = standings[item.TeamId];
                    item.Country = BuildLeagueParticipantSummary(item.Country, standing);
                    item.Seed = standing.Place;
                    return item;
                })
                .OrderBy(item => item.Seed)
                .ThenBy(item => item.TeamName)
                .ToList();
        }

        private static List<LeagueStanding> ComputeLeagueStandings(IList<BracketParticipantViewModel> participants, IList<MatchRuntimeState> states)
        {
            Dictionary<int, LeagueStanding> standings = participants
                .ToDictionary(
                    participant => participant.TeamId,
                    participant => new LeagueStanding
                    {
                        ParticipantId = participant.TeamId,
                        Name = participant.TeamName,
                        Seed = participant.Seed
                    });

            foreach (MatchRuntimeState state in states)
            {
                if (!state.Team1Id.HasValue || !state.Team2Id.HasValue)
                {
                    continue;
                }

                if (!string.Equals(state.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                int? winnerId = ResolveWinnerId(state);
                if (!winnerId.HasValue)
                {
                    continue;
                }

                LeagueStanding participant1 = standings[state.Team1Id.Value];
                LeagueStanding participant2 = standings[state.Team2Id.Value];
                participant1.Played++;
                participant2.Played++;

                if (winnerId.Value == state.Team1Id.Value)
                {
                    participant1.Wins++;
                    participant2.Losses++;
                }
                else
                {
                    participant2.Wins++;
                    participant1.Losses++;
                }
            }

            int place = 1;
            foreach (LeagueStanding standing in standings.Values
                .OrderByDescending(item => item.Wins)
                .ThenBy(item => item.Losses)
                .ThenBy(item => item.Seed)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                standing.Place = place++;
            }

            return standings.Values.OrderBy(item => item.Place).ToList();
        }

        private static bool AreAllLeagueMatchesCompleted(IList<MatchRuntimeState> states)
        {
            return states.Count > 0 && states.All(state =>
                string.Equals(state.Status, "Completed", StringComparison.OrdinalIgnoreCase) &&
                ResolveWinnerId(state).HasValue);
        }

        private static string BuildLeagueParticipantSummary(string secondaryText, LeagueStanding standing)
        {
            string baseText = string.IsNullOrWhiteSpace(secondaryText) ? string.Empty : secondaryText + " • ";
            return baseText + "Побед: " + standing.Wins + " • Игр: " + standing.Played + " • Место: " + standing.Place;
        }

        private static string ResolveChampionName(TournamentFormatKind formatKind, IList<BracketRoundViewModel> rounds, IList<BracketParticipantViewModel> participants, IList<MatchRuntimeState> states)
        {
            if (formatKind == TournamentFormatKind.League)
            {
                if (!AreAllLeagueMatchesCompleted(states))
                {
                    return string.Empty;
                }

                LeagueStanding leader = ComputeLeagueStandings(participants, states).FirstOrDefault();
                return leader == null ? string.Empty : leader.Name;
            }

            BracketMatchViewModel finalMatch = rounds.LastOrDefault()?.Matches.FirstOrDefault();
            return finalMatch == null ? string.Empty : finalMatch.WinnerName;
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
                .ThenBy(item => ResolveParticipantName(participantsById, item.ParticipantId.Value, isPlayerMode))
                .Select(item => (item.Row, item.ParticipantId.Value))
                .ToList();

            List<BracketParticipantViewModel> result = new List<BracketParticipantViewModel>();
            for (int index = 0; index < orderedRows.Count; index++)
            {
                DataRow participant = participantsById[orderedRows[index].ParticipantId];
                result.Add(new BracketParticipantViewModel
                {
                    TeamId = orderedRows[index].ParticipantId,
                    Seed = orderedRows[index].Row["Seed"] == DBNull.Value ? index + 1 : Convert.ToInt32(orderedRows[index].Row["Seed"]),
                    TeamName = ResolveParticipantName(participantsById, orderedRows[index].ParticipantId, isPlayerMode),
                    Country = GetParticipantSecondaryText(participant, isPlayerMode)
                });
            }

            return result;
        }

        private int GetNextMatchNumber(int tournamentId)
        {
            return _database.GetTable("Matches")
                .Rows
                .Cast<DataRow>()
                .Where(row => Convert.ToInt32(row["TournamentID"]) == tournamentId)
                .Select(row => Convert.ToInt32(row["MatchNumber"]))
                .DefaultIfEmpty(0)
                .Max() + 1;
        }

        private bool ShouldUsePlayerColumns(bool isPlayerMode)
        {
            if (!isPlayerMode)
            {
                return false;
            }

            IReadOnlyCollection<string> columns = _database.GetAvailableColumns("Matches");
            return ContainsColumn(columns, Player1IdColumn) &&
                   ContainsColumn(columns, Player2IdColumn) &&
                   ContainsColumn(columns, WinnerPlayerIdColumn);
        }

        private static bool ContainsColumn(IReadOnlyCollection<string> columns, string columnName)
        {
            return columns.Any(column => string.Equals(column, columnName, StringComparison.OrdinalIgnoreCase));
        }

        private static Dictionary<string, object> BuildMatchValues(
            int stageId,
            int tournamentId,
            int matchNumber,
            int? team1Id,
            int? team2Id,
            int? winnerId,
            int team1Score,
            int team2Score,
            DateTime? matchDate,
            int bestOf,
            string status,
            bool isPlayerMode,
            bool usePlayerColumns)
        {
            Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["TournamentID"] = tournamentId,
                ["StageID"] = stageId,
                ["MatchNumber"] = matchNumber,
                ["Team1ID"] = !isPlayerMode || !usePlayerColumns ? (object)team1Id : null,
                ["Team2ID"] = !isPlayerMode || !usePlayerColumns ? (object)team2Id : null,
                ["WinnerTeamID"] = !isPlayerMode || !usePlayerColumns ? (object)winnerId : null,
                ["Team1Score"] = team1Score,
                ["Team2Score"] = team2Score,
                ["MatchDate"] = matchDate.HasValue ? matchDate.Value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : null,
                ["BestOf"] = bestOf,
                ["Status"] = NormalizeStatusValue(status)
            };

            if (usePlayerColumns)
            {
                values[Player1IdColumn] = isPlayerMode ? (object)team1Id : null;
                values[Player2IdColumn] = isPlayerMode ? (object)team2Id : null;
                values[WinnerPlayerIdColumn] = isPlayerMode ? (object)winnerId : null;
            }

            return values;
        }

        private static int? ResolveDirectSeedParticipantId(TournamentMatchSlotPlan slot, IList<BracketParticipantViewModel> participants)
        {
            if (slot == null || !slot.SeedIndex.HasValue)
            {
                return null;
            }

            return slot.SeedIndex.Value >= 0 && slot.SeedIndex.Value < participants.Count
                ? participants[slot.SeedIndex.Value].TeamId
                : (int?)null;
        }

        private static int? ResolveSlotParticipantId(TournamentMatchSlotPlan slot, Dictionary<string, MatchRuntimeState> statesByKey)
        {
            if (slot == null || string.IsNullOrWhiteSpace(slot.SourceMatchKey) || !statesByKey.ContainsKey(slot.SourceMatchKey))
            {
                return null;
            }

            return ResolveOutcomeParticipantId(statesByKey[slot.SourceMatchKey], slot.FeedKind);
        }

        private static int? ResolveOutcomeParticipantId(MatchRuntimeState state, TournamentMatchFeedKind feedKind)
        {
            int? winnerId = ResolveWinnerId(state);
            switch (feedKind)
            {
                case TournamentMatchFeedKind.Winner:
                    return winnerId;
                case TournamentMatchFeedKind.Loser:
                    if (!winnerId.HasValue || !state.Team1Id.HasValue || !state.Team2Id.HasValue)
                    {
                        return null;
                    }

                    return winnerId.Value == state.Team1Id.Value ? state.Team2Id : state.Team1Id;
                default:
                    return null;
            }
        }

        private static int? ResolveWinnerId(MatchRuntimeState state)
        {
            if (state == null)
            {
                return null;
            }

            if (state.WinnerId.HasValue && (state.WinnerId == state.Team1Id || state.WinnerId == state.Team2Id))
            {
                return state.WinnerId;
            }

            if (state.Team1Id.HasValue && !state.Team2Id.HasValue)
            {
                return state.Team1Id;
            }

            if (state.Team2Id.HasValue && !state.Team1Id.HasValue)
            {
                return state.Team2Id;
            }

            if (string.Equals(state.Status, "Completed", StringComparison.OrdinalIgnoreCase) &&
                state.Team1Id.HasValue &&
                state.Team2Id.HasValue &&
                state.Team1Score != state.Team2Score)
            {
                return state.Team1Score > state.Team2Score ? state.Team1Id : state.Team2Id;
            }

            return null;
        }

        private static int? NormalizeParticipantId(int? participantId, ICollection<int> availableParticipants)
        {
            if (!participantId.HasValue)
            {
                return null;
            }

            if (!availableParticipants.Contains(participantId.Value))
            {
                throw new InvalidOperationException("В формате турнира можно использовать только участников выбранного турнира.");
            }

            return participantId;
        }

        private static int? NormalizeWinnerId(int? winnerId, int? participant1Id, int? participant2Id, string status, int team1Score, int team2Score)
        {
            if (winnerId.HasValue)
            {
                if (winnerId != participant1Id && winnerId != participant2Id)
                {
                    throw new InvalidOperationException("Победитель должен совпадать с одним из участников матча.");
                }

                return winnerId;
            }

            if (participant1Id.HasValue && !participant2Id.HasValue)
            {
                return participant1Id;
            }

            if (participant2Id.HasValue && !participant1Id.HasValue)
            {
                return participant2Id;
            }

            if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase) &&
                participant1Id.HasValue &&
                participant2Id.HasValue &&
                team1Score != team2Score)
            {
                return team1Score > team2Score ? participant1Id : participant2Id;
            }

            return null;
        }

        private static string ResolveSlotDisplayName(
            TournamentMatchSlotPlan slot,
            int? participantId,
            Dictionary<string, MatchRuntimeState> statesByKey,
            Dictionary<int, DataRow> participantsById,
            bool isPlayerMode)
        {
            if (participantId.HasValue)
            {
                return ResolveParticipantName(participantsById, participantId.Value, isPlayerMode);
            }

            if (slot == null)
            {
                return "TBD";
            }

            if (slot.IsDirectSeed)
            {
                return "BYE";
            }

            if (!string.IsNullOrWhiteSpace(slot.SourceMatchKey) && statesByKey.ContainsKey(slot.SourceMatchKey))
            {
                MatchRuntimeState sourceState = statesByKey[slot.SourceMatchKey];
                int? resolvedParticipantId = ResolveOutcomeParticipantId(sourceState, slot.FeedKind);
                if (resolvedParticipantId.HasValue)
                {
                    return ResolveParticipantName(participantsById, resolvedParticipantId.Value, isPlayerMode);
                }

                return (slot.FeedKind == TournamentMatchFeedKind.Loser ? "Проигравший " : "Победитель ") + sourceState.MatchCode;
            }

            return "TBD";
        }

        private static int? GetPersistedParticipantId(DataRow matchRow, bool isPlayerMode, bool usePlayerColumns, int slotIndex)
        {
            string primaryColumn = GetParticipantColumnName(isPlayerMode && usePlayerColumns, slotIndex);
            if (matchRow.Table.Columns.Contains(primaryColumn) && matchRow[primaryColumn] != DBNull.Value)
            {
                return Convert.ToInt32(matchRow[primaryColumn]);
            }

            string secondaryColumn = GetParticipantColumnName(false, slotIndex);
            if (matchRow.Table.Columns.Contains(secondaryColumn) && matchRow[secondaryColumn] != DBNull.Value)
            {
                return Convert.ToInt32(matchRow[secondaryColumn]);
            }

            string fallbackPlayerColumn = GetParticipantColumnName(true, slotIndex);
            if (matchRow.Table.Columns.Contains(fallbackPlayerColumn) && matchRow[fallbackPlayerColumn] != DBNull.Value)
            {
                return Convert.ToInt32(matchRow[fallbackPlayerColumn]);
            }

            return null;
        }

        private static string GetParticipantColumnName(bool usePlayerColumns, int slotIndex)
        {
            switch (slotIndex)
            {
                case 1:
                    return usePlayerColumns ? Player1IdColumn : "Team1ID";
                case 2:
                    return usePlayerColumns ? Player2IdColumn : "Team2ID";
                case 3:
                    return usePlayerColumns ? WinnerPlayerIdColumn : "WinnerTeamID";
                default:
                    throw new ArgumentOutOfRangeException(nameof(slotIndex));
            }
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

        private static string GetTournamentParticipantMode(DataRow tournament)
        {
            if (tournament == null || !tournament.Table.Columns.Contains("ParticipantMode") || tournament["ParticipantMode"] == DBNull.Value)
            {
                return "Команды";
            }

            string mode = Convert.ToString(tournament["ParticipantMode"]);
            return string.IsNullOrWhiteSpace(mode) ? "Команды" : mode;
        }

        private string GetTournamentParticipantMode(int tournamentId)
        {
            return GetTournamentParticipantMode(GetTournamentRow(tournamentId));
        }

        private static bool IsPlayerMode(string participantMode)
        {
            return string.Equals(participantMode, "Игроки", StringComparison.CurrentCultureIgnoreCase);
        }

        private static string GetTournamentFormatType(DataRow tournament)
        {
            if (tournament == null || !tournament.Table.Columns.Contains("FormatType") || tournament["FormatType"] == DBNull.Value)
            {
                return "Single Elimination";
            }

            string format = Convert.ToString(tournament["FormatType"]);
            return string.IsNullOrWhiteSpace(format) ? "Single Elimination" : format;
        }

        private static string NormalizeFormatType(TournamentFormatKind formatKind)
        {
            switch (formatKind)
            {
                case TournamentFormatKind.DoubleElimination:
                    return "Double Elimination";
                case TournamentFormatKind.League:
                    return "League";
                default:
                    return "Single Elimination";
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
                case "autoadvanced":
                    return "Автопроход";
                default:
                    return string.IsNullOrWhiteSpace(value) ? "Без статуса" : value;
            }
        }

        private static string NormalizeStatusValue(string status)
        {
            string value = (status ?? string.Empty).Trim();
            switch (value.ToLowerInvariant())
            {
                case "completed":
                    return "Completed";
                case "in progress":
                case "inprogress":
                    return "In Progress";
                case "auto advanced":
                case "autoadvanced":
                    return "Auto Advanced";
                default:
                    return "Scheduled";
            }
        }

        private static bool IsGeneratedStage(DataRow row)
        {
            string stageName = Convert.ToString(row["StageName"]);
            return stageName.StartsWith("Bracket - ", StringComparison.CurrentCultureIgnoreCase) ||
                   stageName.StartsWith("League - ", StringComparison.CurrentCultureIgnoreCase);
        }

        private static DateTime? ParseDate(object value)
        {
            if (value == null || value == DBNull.Value)
            {
                return null;
            }

            if (value is DateTime dateTime)
            {
                return dateTime.Date;
            }

            if (DateTime.TryParseExact(Convert.ToString(value), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
            {
                return parsed.Date;
            }

            if (DateTime.TryParse(Convert.ToString(value), out parsed))
            {
                return parsed.Date;
            }

            return null;
        }

        private static int ReadInt(object value, int fallback)
        {
            return value == null || value == DBNull.Value ? fallback : Convert.ToInt32(value);
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

        private static int NextPowerOfTwo(int value)
        {
            int result = 2;
            while (result < value)
            {
                result *= 2;
            }

            return result;
        }

        private static BracketParticipantViewModel CloneParticipant(BracketParticipantViewModel source)
        {
            return new BracketParticipantViewModel
            {
                TeamId = source.TeamId,
                Seed = source.Seed,
                TeamName = source.TeamName,
                Country = source.Country
            };
        }

        private sealed class PersistedPlanContext
        {
            public static PersistedPlanContext Empty { get; } = new PersistedPlanContext();

            public bool HasGeneratedLayout { get; set; }

            public Dictionary<string, List<DataRow>> MatchesByStageName { get; } = new Dictionary<string, List<DataRow>>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class MatchRuntimeState
        {
            public TournamentStagePlan StagePlan { get; set; }

            public TournamentMatchPlan MatchPlan { get; set; }

            public int MatchId { get; set; }

            public string MatchCode { get; set; }

            public int? Team1Id { get; set; }

            public int? Team2Id { get; set; }

            public int? WinnerId { get; set; }

            public int Team1Score { get; set; }

            public int Team2Score { get; set; }

            public int BestOf { get; set; }

            public DateTime? MatchDate { get; set; }

            public string Status { get; set; }
        }

        private sealed class LeagueStanding
        {
            public int ParticipantId { get; set; }

            public string Name { get; set; }

            public int Seed { get; set; }

            public int Wins { get; set; }

            public int Losses { get; set; }

            public int Played { get; set; }

            public int Place { get; set; }
        }
    }
}
