using System;
using System.Linq;
using Tournaments.WPF.Models;

namespace Tournaments.WPF.Services
{
    public static partial class EntityRegistry
    {
        private static EntityDefinition CreateTournamentsDefinition()
        {
            FieldDefinition participantMode = new FieldDefinition("ParticipantMode", "Тип участников", FieldType.Choice) { IsRequired = true };
            participantMode.AllowedValues.Add("Команды");
            participantMode.AllowedValues.Add("Игроки");

            FieldDefinition formatType = new FieldDefinition("FormatType", "Формат", FieldType.Choice) { IsRequired = true };
            formatType.AllowedValues.Add("Single Elimination");
            formatType.AllowedValues.Add("Double Elimination");
            formatType.AllowedValues.Add("League");

            EntityDefinition definition = new EntityDefinition(
                "Tournaments",
                "Турниры",
                new[] { "TournamentID" },
                new FieldDefinition[]
                {
                    new FieldDefinition("TournamentID", "ID турнира", FieldType.Integer) { IsIdentity = true, IsReadOnly = true, IsKey = true },
                    new FieldDefinition("TournamentName", "Название турнира", FieldType.Text) { IsRequired = true },
                    CreateLookupField("GameID", "ID игры", "GameTitles", "GameID", "GameName", isRequired: true),
                    new FieldDefinition("StartDate", "Дата начала", FieldType.Date) { IsRequired = true },
                    new FieldDefinition("EndDate", "Дата окончания", FieldType.Date),
                    new FieldDefinition("PrizePool", "Призовой фонд", FieldType.Decimal),
                    new FieldDefinition("Organizer", "Организатор", FieldType.Text),
                    new FieldDefinition("Location", "Место проведения", FieldType.Text),
                    formatType,
                    new FieldDefinition("MaxTeams", "Макс. участников", FieldType.Integer) { IsRequired = true },
                    participantMode
                });

            definition.SaveValidator = context =>
            {
                int gameId = GetInt(context.Values, "GameID");
                if (!context.Database.RecordExists("GameTitles", "GameID", gameId))
                {
                    return EntityValidationResult.Fail("Игры с таким ID не существует.");
                }

                DateTime startDate = GetDate(context.Values, "StartDate");
                DateTime? endDate = GetNullableDate(context.Values, "EndDate");
                if (endDate.HasValue && endDate.Value < startDate)
                {
                    return EntityValidationResult.Fail("Дата окончания должна быть позже или равна дате начала.");
                }

                int maxTeams = GetInt(context.Values, "MaxTeams");
                if (maxTeams < 2)
                {
                    return EntityValidationResult.Fail("Количество участников должно быть не меньше 2.");
                }

                string formatValue = Convert.ToString(context.Values["FormatType"]);
                if (!formatType.AllowedValues.Contains(formatValue))
                {
                    return EntityValidationResult.Fail("Выберите один из поддерживаемых форматов турнира.");
                }

                return EntityValidationResult.Success();
            };

            definition.DeleteValidator = context =>
            {
                int tournamentId = GetOriginalInt(context, "TournamentID");
                int stages = Count(context.Database, "TournamentStages", row => ValuesEqual(row["TournamentID"], tournamentId));
                int participants = Count(context.Database, "TournamentParticipants", row => ValuesEqual(row["TournamentID"], tournamentId));
                int matches = Count(context.Database, "Matches", row => ValuesEqual(row["TournamentID"], tournamentId));
                int streams = Count(context.Database, "Streams", row => ValuesEqual(row["TournamentID"], tournamentId));
                int sponsors = Count(context.Database, "TournamentSponsors", row => ValuesEqual(row["TournamentID"], tournamentId));

                if (stages > 0 || participants > 0 || matches > 0 || sponsors > 0 || streams > 0)
                {
                    return EntityValidationResult.Fail("Нельзя удалить турнир: существуют связанные этапы, участники, матчи, трансляции или спонсоры.");
                }

                return EntityValidationResult.Success();
            };

            return definition;
        }

        private static EntityDefinition CreateTournamentStagesDefinition()
        {
            FieldDefinition bracketType = new FieldDefinition("BracketType", "Тип сетки", FieldType.Choice);
            bracketType.AllowedValues.Add("Winner");
            bracketType.AllowedValues.Add("Loser");
            bracketType.AllowedValues.Add("Final");

            EntityDefinition definition = new EntityDefinition(
                "TournamentStages",
                "Этапы турниров",
                new[] { "StageID" },
                new FieldDefinition[]
                {
                    new FieldDefinition("StageID", "ID этапа", FieldType.Integer) { IsIdentity = true, IsReadOnly = true, IsKey = true },
                    CreateLookupField("TournamentID", "ID турнира", "Tournaments", "TournamentID", "TournamentName", isRequired: true),
                    new FieldDefinition("StageName", "Название этапа", FieldType.Text) { IsRequired = true },
                    new FieldDefinition("StageOrder", "Порядок этапа", FieldType.Integer) { IsRequired = true },
                    bracketType
                });

            definition.SaveValidator = context =>
            {
                int tournamentId = GetInt(context.Values, "TournamentID");
                if (!context.Database.RecordExists("Tournaments", "TournamentID", tournamentId))
                {
                    return EntityValidationResult.Fail("Турнира с таким ID не существует.");
                }

                return EntityValidationResult.Success();
            };

            definition.DeleteValidator = context =>
            {
                int stageId = GetOriginalInt(context, "StageID");
                int matches = Count(context.Database, "Matches", row => ValuesEqual(row["StageID"], stageId));
                if (matches > 0)
                {
                    return EntityValidationResult.Fail("Нельзя удалить этап, пока к нему привязаны матчи.");
                }

                return EntityValidationResult.Success();
            };

            return definition;
        }

        private static EntityDefinition CreateTournamentParticipantsDefinition()
        {
            EntityDefinition definition = new EntityDefinition(
                "TournamentParticipants",
                "Участники турниров",
                new[] { "ParticipationID" },
                new FieldDefinition[]
                {
                    new FieldDefinition("ParticipationID", "ID участия", FieldType.Integer) { IsIdentity = true, IsReadOnly = true, IsKey = true },
                    CreateLookupField("TournamentID", "ID турнира", "Tournaments", "TournamentID", "TournamentName", isRequired: true),
                    CreateLookupField("TeamID", "ID команды", "Teams", "TeamID", "TeamName"),
                    CreateLookupField("PlayerID", "ID игрока", "Players", "PlayerID", "Nickname"),
                    new FieldDefinition("Seed", "Seed", FieldType.Integer),
                    new FieldDefinition("FinalPlace", "Итоговое место", FieldType.Integer)
                });

            definition.SaveValidator = context =>
            {
                int tournamentId = GetInt(context.Values, "TournamentID");
                int currentId = GetOriginalInt(context, "ParticipationID");
                int? teamId = GetNullableInt(context.Values, "TeamID");
                int? playerId = GetNullableInt(context.Values, "PlayerID");

                if (!context.Database.RecordExists("Tournaments", "TournamentID", tournamentId))
                {
                    return EntityValidationResult.Fail("Турнира с таким ID не существует.");
                }

                string participantMode = GetTournamentParticipantMode(context.Database, tournamentId);
                if (string.Equals(participantMode, "Игроки", StringComparison.CurrentCultureIgnoreCase))
                {
                    if (!context.Database.GetAvailableColumns("TournamentParticipants").Any(column => string.Equals(column, "PlayerID", StringComparison.OrdinalIgnoreCase)))
                    {
                        return EntityValidationResult.Fail("Текущее хранилище не поддерживает участников-игроков для турниров.");
                    }

                    if (!playerId.HasValue)
                    {
                        return EntityValidationResult.Fail("Для турнира игроков нужно указать ID игрока.");
                    }

                    if (teamId.HasValue)
                    {
                        return EntityValidationResult.Fail("Для турнира игроков указывайте только ID игрока.");
                    }

                    if (!context.Database.RecordExists("Players", "PlayerID", playerId.Value))
                    {
                        return EntityValidationResult.Fail("Игрока с таким ID не существует.");
                    }

                    int duplicates = Count(context.Database, "TournamentParticipants", row =>
                        ValuesEqual(row["TournamentID"], tournamentId) &&
                        ValuesEqual(row["PlayerID"], playerId.Value) &&
                        (context.IsInsert || !ValuesEqual(row["ParticipationID"], currentId)));

                    if (duplicates > 0)
                    {
                        return EntityValidationResult.Fail("Этот игрок уже добавлен в турнир.");
                    }

                    return EntityValidationResult.Success();
                }

                if (!teamId.HasValue)
                {
                    return EntityValidationResult.Fail("Для командного турнира нужно указать ID команды.");
                }

                if (playerId.HasValue)
                {
                    return EntityValidationResult.Fail("Для командного турнира указывайте только ID команды.");
                }

                if (!context.Database.RecordExists("Teams", "TeamID", teamId.Value))
                {
                    return EntityValidationResult.Fail("Команды с таким ID не существует.");
                }

                int teamDuplicates = Count(context.Database, "TournamentParticipants", row =>
                    ValuesEqual(row["TournamentID"], tournamentId) &&
                    ValuesEqual(row["TeamID"], teamId.Value) &&
                    (context.IsInsert || !ValuesEqual(row["ParticipationID"], currentId)));

                if (teamDuplicates > 0)
                {
                    return EntityValidationResult.Fail("Эта команда уже добавлена в турнир.");
                }

                return EntityValidationResult.Success();
            };

            definition.DeleteValidator = context => EntityValidationResult.Success();
            return definition;
        }

        private static EntityDefinition CreateTournamentSponsorsDefinition()
        {
            EntityDefinition definition = new EntityDefinition(
                "TournamentSponsors",
                "Спонсоры турниров",
                new[] { "TournamentID", "SponsorID" },
                new FieldDefinition[]
                {
                    CreateLookupField("TournamentID", "ID турнира", "Tournaments", "TournamentID", "TournamentName", isRequired: true, isKey: true),
                    CreateLookupField("SponsorID", "ID спонсора", "Sponsors", "SponsorID", "SponsorName", isRequired: true, isKey: true),
                    new FieldDefinition("SponsorshipAmount", "Сумма спонсорства", FieldType.Decimal),
                    new FieldDefinition("Currency", "Валюта", FieldType.Text) { IsRequired = true }
                });

            definition.SaveValidator = context =>
            {
                int tournamentId = GetInt(context.Values, "TournamentID");
                int sponsorId = GetInt(context.Values, "SponsorID");
                int originalTournamentId = GetOriginalInt(context, "TournamentID");
                int originalSponsorId = GetOriginalInt(context, "SponsorID");

                if (!context.Database.RecordExists("Tournaments", "TournamentID", tournamentId))
                {
                    return EntityValidationResult.Fail("Турнира с таким ID не существует.");
                }

                if (!context.Database.RecordExists("Sponsors", "SponsorID", sponsorId))
                {
                    return EntityValidationResult.Fail("Спонсора с таким ID не существует.");
                }

                int duplicates = Count(context.Database, "TournamentSponsors", row =>
                    ValuesEqual(row["TournamentID"], tournamentId) &&
                    ValuesEqual(row["SponsorID"], sponsorId) &&
                    (context.IsInsert || !(ValuesEqual(row["TournamentID"], originalTournamentId) && ValuesEqual(row["SponsorID"], originalSponsorId))));

                if (duplicates > 0)
                {
                    return EntityValidationResult.Fail("Этот спонсор уже добавлен к выбранному турниру.");
                }

                return EntityValidationResult.Success();
            };

            definition.DeleteValidator = context => EntityValidationResult.Success();
            return definition;
        }
    }
}

