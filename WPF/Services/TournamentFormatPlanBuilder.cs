using System.Collections.Generic;

namespace Tournaments.WPF.Services
{
    internal enum TournamentFormatKind
    {
        SingleElimination,
        DoubleElimination,
        League
    }

    internal enum TournamentMatchFeedKind
    {
        DirectSeed,
        Winner,
        Loser
    }

    internal sealed class TournamentFormatPlan
    {
        public TournamentFormatPlan(TournamentFormatKind formatKind)
        {
            FormatKind = formatKind;
            Stages = new List<TournamentStagePlan>();
        }

        public TournamentFormatKind FormatKind { get; }

        public List<TournamentStagePlan> Stages { get; }

        public bool HasChampionColumn
        {
            get { return FormatKind != TournamentFormatKind.League; }
        }
    }

    internal sealed class TournamentStagePlan
    {
        public TournamentStagePlan()
        {
            Matches = new List<TournamentMatchPlan>();
        }

        public string StageName { get; set; }

        public string Title { get; set; }

        public int StageOrder { get; set; }

        public string BracketType { get; set; }

        public string SectionKey { get; set; }

        public int LayoutColumn { get; set; }

        public int SectionIndex { get; set; }

        public List<TournamentMatchPlan> Matches { get; }
    }

    internal sealed class TournamentMatchPlan
    {
        public string MatchKey { get; set; }

        public int PlannedMatchNumber { get; set; }

        public int MatchIndex { get; set; }

        public int BestOf { get; set; }

        public int DayOffset { get; set; }

        public bool CanEditParticipants { get; set; }

        public TournamentMatchSlotPlan Slot1 { get; set; }

        public TournamentMatchSlotPlan Slot2 { get; set; }
    }

    internal sealed class TournamentMatchSlotPlan
    {
        public int? SeedIndex { get; set; }

        public string SourceMatchKey { get; set; }

        public TournamentMatchFeedKind FeedKind { get; set; }

        public bool IsDirectSeed
        {
            get { return SeedIndex.HasValue; }
        }
    }

    internal static class TournamentFormatPlanBuilder
    {
        public static TournamentFormatKind ParseKind(string formatType)
        {
            string value = (formatType ?? string.Empty).Trim();
            switch (value)
            {
                case "Double Elimination":
                    return TournamentFormatKind.DoubleElimination;
                case "League":
                    return TournamentFormatKind.League;
                default:
                    return TournamentFormatKind.SingleElimination;
            }
        }

        public static TournamentFormatPlan Build(TournamentFormatKind formatKind, int participantCount)
        {
            switch (formatKind)
            {
                case TournamentFormatKind.DoubleElimination:
                    return BuildDoubleEliminationPlan(participantCount);
                case TournamentFormatKind.League:
                    return BuildLeaguePlan(participantCount);
                default:
                    return BuildSingleEliminationPlan(participantCount);
            }
        }

        private static TournamentFormatPlan BuildSingleEliminationPlan(int participantCount)
        {
            TournamentFormatPlan plan = new TournamentFormatPlan(TournamentFormatKind.SingleElimination);
            int bracketSize = NextPowerOfTwo(participantCount);
            int roundCount = Log2(bracketSize);
            int[] seedPositions = BuildSeedPositions(bracketSize);
            int?[] seededSlots = BuildSeedSlots(seedPositions, participantCount);
            int matchNumber = 1;

            for (int roundIndex = 0; roundIndex < roundCount; roundIndex++)
            {
                int teamsInRound = bracketSize / (1 << roundIndex);
                int matchesInRound = teamsInRound / 2;
                TournamentStagePlan stage = new TournamentStagePlan
                {
                    StageName = "Bracket - " + GetSingleStoredRoundTitle(teamsInRound),
                    Title = GetSingleDisplayTitle(teamsInRound),
                    StageOrder = 100 + roundIndex,
                    BracketType = roundIndex == roundCount - 1 ? "Final" : "Winner",
                    SectionKey = "Main",
                    LayoutColumn = roundIndex,
                    SectionIndex = roundIndex
                };

                for (int matchIndex = 0; matchIndex < matchesInRound; matchIndex++)
                {
                    TournamentMatchPlan match = new TournamentMatchPlan
                    {
                        MatchKey = BuildMatchKey(stage.StageName, matchIndex),
                        MatchIndex = matchIndex,
                        PlannedMatchNumber = matchNumber++,
                        BestOf = roundIndex == roundCount - 1 ? 5 : 3,
                        DayOffset = roundIndex,
                        CanEditParticipants = roundIndex == 0
                    };

                    if (roundIndex == 0)
                    {
                        match.Slot1 = DirectSeed(seededSlots[matchIndex * 2]);
                        match.Slot2 = DirectSeed(seededSlots[matchIndex * 2 + 1]);
                    }
                    else
                    {
                        match.Slot1 = WinnerOf(plan.Stages[roundIndex - 1].Matches[matchIndex * 2].MatchKey);
                        match.Slot2 = WinnerOf(plan.Stages[roundIndex - 1].Matches[matchIndex * 2 + 1].MatchKey);
                    }

                    stage.Matches.Add(match);
                }

                plan.Stages.Add(stage);
            }

            return plan;
        }

        private static TournamentFormatPlan BuildDoubleEliminationPlan(int participantCount)
        {
            TournamentFormatPlan plan = new TournamentFormatPlan(TournamentFormatKind.DoubleElimination);
            int bracketSize = NextPowerOfTwo(participantCount);
            int upperRoundCount = Log2(bracketSize);
            int[] seedPositions = BuildSeedPositions(bracketSize);
            int?[] seededSlots = BuildSeedSlots(seedPositions, participantCount);
            int matchNumber = 1;

            List<TournamentStagePlan> upperStages = new List<TournamentStagePlan>();
            for (int roundIndex = 0; roundIndex < upperRoundCount; roundIndex++)
            {
                int teamsInRound = bracketSize / (1 << roundIndex);
                int matchesInRound = teamsInRound / 2;
                TournamentStagePlan stage = new TournamentStagePlan
                {
                    StageName = "Bracket - Upper - " + GetUpperStoredRoundTitle(teamsInRound),
                    Title = GetUpperDisplayTitle(teamsInRound),
                    StageOrder = 100 + roundIndex,
                    BracketType = "Winner",
                    SectionKey = "Upper",
                    LayoutColumn = roundIndex,
                    SectionIndex = roundIndex
                };

                for (int matchIndex = 0; matchIndex < matchesInRound; matchIndex++)
                {
                    TournamentMatchPlan match = new TournamentMatchPlan
                    {
                        MatchKey = BuildMatchKey(stage.StageName, matchIndex),
                        MatchIndex = matchIndex,
                        PlannedMatchNumber = matchNumber++,
                        BestOf = roundIndex == upperRoundCount - 1 ? 5 : 3,
                        DayOffset = roundIndex,
                        CanEditParticipants = roundIndex == 0
                    };

                    if (roundIndex == 0)
                    {
                        match.Slot1 = DirectSeed(seededSlots[matchIndex * 2]);
                        match.Slot2 = DirectSeed(seededSlots[matchIndex * 2 + 1]);
                    }
                    else
                    {
                        match.Slot1 = WinnerOf(upperStages[roundIndex - 1].Matches[matchIndex * 2].MatchKey);
                        match.Slot2 = WinnerOf(upperStages[roundIndex - 1].Matches[matchIndex * 2 + 1].MatchKey);
                    }

                    stage.Matches.Add(match);
                }

                upperStages.Add(stage);
                plan.Stages.Add(stage);
            }

            if (upperRoundCount == 1)
            {
                TournamentStagePlan grandFinalStage = new TournamentStagePlan
                {
                    StageName = "Bracket - Grand Final",
                    Title = "Гранд-финал",
                    StageOrder = 300,
                    BracketType = "Final",
                    SectionKey = "Final",
                    LayoutColumn = 1,
                    SectionIndex = 0
                };

                grandFinalStage.Matches.Add(new TournamentMatchPlan
                {
                    MatchKey = BuildMatchKey(grandFinalStage.StageName, 0),
                    MatchIndex = 0,
                    PlannedMatchNumber = matchNumber++,
                    BestOf = 5,
                    DayOffset = 1,
                    CanEditParticipants = false,
                    Slot1 = WinnerOf(upperStages[0].Matches[0].MatchKey),
                    Slot2 = LoserOf(upperStages[0].Matches[0].MatchKey)
                });

                plan.Stages.Add(grandFinalStage);
                return plan;
            }

            List<TournamentStagePlan> lowerStages = new List<TournamentStagePlan>();
            TournamentStagePlan firstLowerStage = new TournamentStagePlan
            {
                StageName = "Bracket - Lower - Round 1",
                Title = "Нижняя сетка • Раунд 1",
                StageOrder = 200,
                BracketType = "Loser",
                SectionKey = "Lower",
                LayoutColumn = 0,
                SectionIndex = 0
            };

            for (int matchIndex = 0; matchIndex < upperStages[0].Matches.Count / 2; matchIndex++)
            {
                firstLowerStage.Matches.Add(new TournamentMatchPlan
                {
                    MatchKey = BuildMatchKey(firstLowerStage.StageName, matchIndex),
                    MatchIndex = matchIndex,
                    PlannedMatchNumber = matchNumber++,
                    BestOf = 3,
                    DayOffset = 1,
                    CanEditParticipants = false,
                    Slot1 = LoserOf(upperStages[0].Matches[matchIndex * 2].MatchKey),
                    Slot2 = LoserOf(upperStages[0].Matches[matchIndex * 2 + 1].MatchKey)
                });
            }

            lowerStages.Add(firstLowerStage);
            plan.Stages.Add(firstLowerStage);
            TournamentStagePlan previousLowerStage = firstLowerStage;
            int lowerStageNumber = 2;
            int lowerLayoutColumn = 1;

            for (int upperRoundIndex = 1; upperRoundIndex < upperStages.Count; upperRoundIndex++)
            {
                TournamentStagePlan mergeStage = new TournamentStagePlan
                {
                    StageName = upperRoundIndex == upperStages.Count - 1 ? "Bracket - Lower - Final" : "Bracket - Lower - Round " + lowerStageNumber,
                    Title = upperRoundIndex == upperStages.Count - 1 ? "Нижняя сетка • Финал" : "Нижняя сетка • Раунд " + lowerStageNumber,
                    StageOrder = 200 + lowerStages.Count,
                    BracketType = "Loser",
                    SectionKey = "Lower",
                    LayoutColumn = lowerLayoutColumn++,
                    SectionIndex = lowerStages.Count
                };

                for (int matchIndex = 0; matchIndex < upperStages[upperRoundIndex].Matches.Count; matchIndex++)
                {
                    mergeStage.Matches.Add(new TournamentMatchPlan
                    {
                        MatchKey = BuildMatchKey(mergeStage.StageName, matchIndex),
                        MatchIndex = matchIndex,
                        PlannedMatchNumber = matchNumber++,
                        BestOf = upperRoundIndex == upperStages.Count - 1 ? 5 : 3,
                        DayOffset = upperRoundIndex + lowerStages.Count,
                        CanEditParticipants = false,
                        Slot1 = WinnerOf(previousLowerStage.Matches[matchIndex].MatchKey),
                        Slot2 = LoserOf(upperStages[upperRoundIndex].Matches[matchIndex].MatchKey)
                    });
                }

                lowerStages.Add(mergeStage);
                plan.Stages.Add(mergeStage);
                previousLowerStage = mergeStage;
                lowerStageNumber++;

                if (upperRoundIndex == upperStages.Count - 1)
                {
                    continue;
                }

                TournamentStagePlan consolidationStage = new TournamentStagePlan
                {
                    StageName = "Bracket - Lower - Round " + lowerStageNumber,
                    Title = "Нижняя сетка • Раунд " + lowerStageNumber,
                    StageOrder = 200 + lowerStages.Count,
                    BracketType = "Loser",
                    SectionKey = "Lower",
                    LayoutColumn = lowerLayoutColumn++,
                    SectionIndex = lowerStages.Count
                };

                for (int matchIndex = 0; matchIndex < mergeStage.Matches.Count / 2; matchIndex++)
                {
                    consolidationStage.Matches.Add(new TournamentMatchPlan
                    {
                        MatchKey = BuildMatchKey(consolidationStage.StageName, matchIndex),
                        MatchIndex = matchIndex,
                        PlannedMatchNumber = matchNumber++,
                        BestOf = 3,
                        DayOffset = upperRoundIndex + lowerStages.Count,
                        CanEditParticipants = false,
                        Slot1 = WinnerOf(mergeStage.Matches[matchIndex * 2].MatchKey),
                        Slot2 = WinnerOf(mergeStage.Matches[matchIndex * 2 + 1].MatchKey)
                    });
                }

                lowerStages.Add(consolidationStage);
                plan.Stages.Add(consolidationStage);
                previousLowerStage = consolidationStage;
                lowerStageNumber++;
            }

            TournamentStagePlan grandFinal = new TournamentStagePlan
            {
                StageName = "Bracket - Grand Final",
                Title = "Гранд-финал",
                StageOrder = 300,
                BracketType = "Final",
                SectionKey = "Final",
                LayoutColumn = lowerLayoutColumn,
                SectionIndex = 0
            };

            grandFinal.Matches.Add(new TournamentMatchPlan
            {
                MatchKey = BuildMatchKey(grandFinal.StageName, 0),
                MatchIndex = 0,
                PlannedMatchNumber = matchNumber++,
                BestOf = 5,
                DayOffset = upperStages.Count + lowerStages.Count,
                CanEditParticipants = false,
                Slot1 = WinnerOf(upperStages[upperStages.Count - 1].Matches[0].MatchKey),
                Slot2 = WinnerOf(previousLowerStage.Matches[0].MatchKey)
            });

            plan.Stages.Add(grandFinal);
            return plan;
        }

        private static TournamentFormatPlan BuildLeaguePlan(int participantCount)
        {
            TournamentFormatPlan plan = new TournamentFormatPlan(TournamentFormatKind.League);
            List<int?> rotation = new List<int?>();
            for (int index = 0; index < participantCount; index++)
            {
                rotation.Add(index);
            }

            if (rotation.Count % 2 != 0)
            {
                rotation.Add(null);
            }

            int roundCount = rotation.Count - 1;
            int matchNumber = 1;

            for (int roundIndex = 0; roundIndex < roundCount; roundIndex++)
            {
                TournamentStagePlan stage = new TournamentStagePlan
                {
                    StageName = "League - Round " + (roundIndex + 1),
                    Title = "Тур " + (roundIndex + 1),
                    StageOrder = 400 + roundIndex,
                    BracketType = null,
                    SectionKey = "League",
                    LayoutColumn = roundIndex,
                    SectionIndex = roundIndex
                };

                for (int pairIndex = 0; pairIndex < rotation.Count / 2; pairIndex++)
                {
                    int? left = rotation[pairIndex];
                    int? right = rotation[rotation.Count - 1 - pairIndex];
                    if (!left.HasValue || !right.HasValue)
                    {
                        continue;
                    }

                    stage.Matches.Add(new TournamentMatchPlan
                    {
                        MatchKey = BuildMatchKey(stage.StageName, stage.Matches.Count),
                        MatchIndex = stage.Matches.Count,
                        PlannedMatchNumber = matchNumber++,
                        BestOf = 5,
                        DayOffset = roundIndex,
                        CanEditParticipants = false,
                        Slot1 = DirectSeed(left),
                        Slot2 = DirectSeed(right)
                    });
                }

                plan.Stages.Add(stage);
                RotateLeagueParticipants(rotation);
            }

            return plan;
        }

        private static void RotateLeagueParticipants(List<int?> rotation)
        {
            int? last = rotation[rotation.Count - 1];
            rotation.RemoveAt(rotation.Count - 1);
            rotation.Insert(1, last);
        }

        private static TournamentMatchSlotPlan DirectSeed(int? seedIndex)
        {
            return new TournamentMatchSlotPlan
            {
                SeedIndex = seedIndex,
                FeedKind = TournamentMatchFeedKind.DirectSeed
            };
        }

        private static TournamentMatchSlotPlan WinnerOf(string sourceMatchKey)
        {
            return new TournamentMatchSlotPlan
            {
                SourceMatchKey = sourceMatchKey,
                FeedKind = TournamentMatchFeedKind.Winner
            };
        }

        private static TournamentMatchSlotPlan LoserOf(string sourceMatchKey)
        {
            return new TournamentMatchSlotPlan
            {
                SourceMatchKey = sourceMatchKey,
                FeedKind = TournamentMatchFeedKind.Loser
            };
        }

        private static int?[] BuildSeedSlots(int[] seedPositions, int participantCount)
        {
            int?[] result = new int?[seedPositions.Length];
            for (int index = 0; index < seedPositions.Length; index++)
            {
                int seed = seedPositions[index];
                if (seed <= participantCount)
                {
                    result[index] = seed - 1;
                }
            }

            return result;
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

        private static int Log2(int value)
        {
            int result = 0;
            while (value > 1)
            {
                value /= 2;
                result++;
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

        private static string BuildMatchKey(string stageName, int matchIndex)
        {
            return stageName + "::" + matchIndex;
        }

        private static string GetSingleStoredRoundTitle(int teamsInRound)
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

        private static string GetUpperStoredRoundTitle(int teamsInRound)
        {
            switch (teamsInRound)
            {
                case 2:
                    return "Upper Final";
                case 4:
                    return "Upper Semifinals";
                case 8:
                    return "Upper Quarterfinals";
                case 16:
                    return "Upper Round of 16";
                default:
                    return "Upper Round of " + teamsInRound;
            }
        }

        private static string GetSingleDisplayTitle(int teamsInRound)
        {
            switch (teamsInRound)
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
                    return "Раунд " + teamsInRound;
            }
        }

        private static string GetUpperDisplayTitle(int teamsInRound)
        {
            switch (teamsInRound)
            {
                case 2:
                    return "Верхняя сетка • Финал";
                case 4:
                    return "Верхняя сетка • Полуфинал";
                case 8:
                    return "Верхняя сетка • Четвертьфинал";
                case 16:
                    return "Верхняя сетка • 1/8 финала";
                default:
                    return "Верхняя сетка • Раунд " + teamsInRound;
            }
        }
    }
}
