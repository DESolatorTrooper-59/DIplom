using System;
using Tournaments.WPF.Models;

namespace Tournaments.WPF.Services
{
    public static partial class EntityRegistry
    {
        private static EntityDefinition CreateTeamPlayersDefinition()
        {
            EntityDefinition definition = new EntityDefinition(
                "TeamPlayers",
                "Составы команд",
                new[] { "TeamPlayerID" },
                new[]
                {
                    new FieldDefinition("TeamPlayerID", "ID связи", FieldType.Integer) { IsIdentity = true, IsReadOnly = true, IsKey = true },
                    new FieldDefinition("TeamID", "ID команды", FieldType.Integer) { IsRequired = true },
                    new FieldDefinition("PlayerID", "ID игрока", FieldType.Integer) { IsRequired = true },
                    new FieldDefinition("JoinDate", "Дата присоединения", FieldType.Date) { IsRequired = true },
                    new FieldDefinition("LeaveDate", "Дата ухода", FieldType.Date),
                    new FieldDefinition("IsActive", "Активен", FieldType.Boolean) { IsRequired = true },
                    new FieldDefinition("Role", "Роль", FieldType.Text)
                });

            definition.SaveValidator = context =>
            {
                int teamId = GetInt(context.Values, "TeamID");
                int playerId = GetInt(context.Values, "PlayerID");
                int currentId = GetOriginalInt(context, "TeamPlayerID");
                bool isActive = GetBool(context.Values, "IsActive");
                DateTime joinDate = GetDate(context.Values, "JoinDate");
                DateTime? leaveDate = GetNullableDate(context.Values, "LeaveDate");

                if (!context.Database.RecordExists("Teams", "TeamID", teamId))
                {
                    return EntityValidationResult.Fail("Команды с таким ID не существует.");
                }

                if (!context.Database.RecordExists("Players", "PlayerID", playerId))
                {
                    return EntityValidationResult.Fail("Игрока с таким ID не существует.");
                }

                if (leaveDate.HasValue && leaveDate.Value <= joinDate)
                {
                    return EntityValidationResult.Fail("Дата ухода должна быть позже даты присоединения.");
                }

                if (isActive)
                {
                    int activeDuplicates = Count(context.Database, "TeamPlayers", row =>
                        ValuesEqual(row["TeamID"], teamId) &&
                        ValuesEqual(row["PlayerID"], playerId) &&
                        ValuesEqual(row["IsActive"], true) &&
                        (context.IsInsert || !ValuesEqual(row["TeamPlayerID"], currentId)));

                    if (activeDuplicates > 0)
                    {
                        return EntityValidationResult.Fail("Этот игрок уже отмечен как активный в выбранной команде.");
                    }
                }

                return EntityValidationResult.Success();
            };

            definition.DeleteValidator = context => EntityValidationResult.Success();
            return definition;
        }

        private static EntityDefinition CreateMatchesDefinition()
        {
            FieldDefinition status = new FieldDefinition("Status", "Статус", FieldType.Choice);
            status.AllowedValues.Add("Scheduled");
            status.AllowedValues.Add("Live");
            status.AllowedValues.Add("Completed");
            status.AllowedValues.Add("Cancelled");

            EntityDefinition definition = new EntityDefinition(
                "Matches",
                "Матчи",
                new[] { "MatchID" },
                new FieldDefinition[]
                {
                    new FieldDefinition("MatchID", "ID матча", FieldType.Integer) { IsIdentity = true, IsReadOnly = true, IsKey = true },
                    new FieldDefinition("TournamentID", "ID турнира", FieldType.Integer) { IsRequired = true },
                    new FieldDefinition("StageID", "ID этапа", FieldType.Integer) { IsRequired = true },
                    new FieldDefinition("MatchNumber", "Номер матча", FieldType.Integer) { IsRequired = true },
                    new FieldDefinition("Team1ID", "Команда 1", FieldType.Integer),
                    new FieldDefinition("Team2ID", "Команда 2", FieldType.Integer),
                    new FieldDefinition("WinnerTeamID", "Победитель", FieldType.Integer),
                    new FieldDefinition("Team1Score", "Счёт команды 1", FieldType.Integer),
                    new FieldDefinition("Team2Score", "Счёт команды 2", FieldType.Integer),
                    new FieldDefinition("MatchDate", "Дата матча", FieldType.Text),
                    new FieldDefinition("BestOf", "Best Of", FieldType.Integer),
                    status
                });

            definition.SaveValidator = context =>
            {
                int tournamentId = GetInt(context.Values, "TournamentID");
                int stageId = GetInt(context.Values, "StageID");
                int? team1Id = GetNullableInt(context.Values, "Team1ID");
                int? team2Id = GetNullableInt(context.Values, "Team2ID");
                int? winnerId = GetNullableInt(context.Values, "WinnerTeamID");

                if (!context.Database.RecordExists("Tournaments", "TournamentID", tournamentId))
                {
                    return EntityValidationResult.Fail("Турнира с таким ID не существует.");
                }

                if (!context.Database.RecordExists("TournamentStages", "StageID", stageId))
                {
                    return EntityValidationResult.Fail("Этапа с таким ID не существует.");
                }

                int matchingStage = Count(context.Database, "TournamentStages", row => ValuesEqual(row["StageID"], stageId) && ValuesEqual(row["TournamentID"], tournamentId));
                if (matchingStage == 0)
                {
                    return EntityValidationResult.Fail("Выбранный этап не принадлежит указанному турниру.");
                }

                if (team1Id.HasValue && !context.Database.RecordExists("Teams", "TeamID", team1Id.Value))
                {
                    return EntityValidationResult.Fail("Команды 1 с таким ID не существует.");
                }

                if (team2Id.HasValue && !context.Database.RecordExists("Teams", "TeamID", team2Id.Value))
                {
                    return EntityValidationResult.Fail("Команды 2 с таким ID не существует.");
                }

                if (team1Id.HasValue && team2Id.HasValue && team1Id.Value == team2Id.Value)
                {
                    return EntityValidationResult.Fail("Команды в матче должны быть разными.");
                }

                if (winnerId.HasValue)
                {
                    if (!context.Database.RecordExists("Teams", "TeamID", winnerId.Value))
                    {
                        return EntityValidationResult.Fail("Команды-победителя с таким ID не существует.");
                    }

                    if ((!team1Id.HasValue || winnerId.Value != team1Id.Value) && (!team2Id.HasValue || winnerId.Value != team2Id.Value))
                    {
                        return EntityValidationResult.Fail("Победитель должен быть одной из команд матча.");
                    }
                }

                return EntityValidationResult.Success();
            };

            definition.DeleteValidator = context =>
            {
                int matchId = GetOriginalInt(context, "MatchID");
                int streams = Count(context.Database, "Streams", row => ValuesEqual(row["MatchID"], matchId));
                if (streams > 0)
                {
                    return EntityValidationResult.Fail("Нельзя удалить матч, пока к нему привязаны трансляции.");
                }

                return EntityValidationResult.Success();
            };

            return definition;
        }

        private static EntityDefinition CreateStreamsDefinition()
        {
            EntityDefinition definition = new EntityDefinition(
                "Streams",
                "Трансляции",
                new[] { "StreamID" },
                new[]
                {
                    new FieldDefinition("StreamID", "ID трансляции", FieldType.Integer) { IsIdentity = true, IsReadOnly = true, IsKey = true },
                    new FieldDefinition("TournamentID", "ID турнира", FieldType.Integer) { IsRequired = true },
                    new FieldDefinition("MatchID", "ID матча", FieldType.Integer),
                    new FieldDefinition("Platform", "Платформа", FieldType.Text),
                    new FieldDefinition("StreamURL", "Ссылка", FieldType.Text)
                });

            definition.SaveValidator = context =>
            {
                int tournamentId = GetInt(context.Values, "TournamentID");
                int? matchId = GetNullableInt(context.Values, "MatchID");

                if (!context.Database.RecordExists("Tournaments", "TournamentID", tournamentId))
                {
                    return EntityValidationResult.Fail("Турнира с таким ID не существует.");
                }

                if (matchId.HasValue && !context.Database.RecordExists("Matches", "MatchID", matchId.Value))
                {
                    return EntityValidationResult.Fail("Матча с таким ID не существует.");
                }

                return EntityValidationResult.Success();
            };

            definition.DeleteValidator = context => EntityValidationResult.Success();
            return definition;
        }
    }
}
