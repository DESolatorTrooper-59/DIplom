using System;
using Tournaments.WPF.Models;

namespace Tournaments.WPF.Services
{
    public static partial class EntityRegistry
    {
        private static EntityDefinition CreateTournamentsDefinition()
        {
            EntityDefinition definition = new EntityDefinition(
                "Tournaments",
                "Турниры",
                new[] { "TournamentID" },
                new[]
                {
                    new FieldDefinition("TournamentID", "ID турнира", FieldType.Integer) { IsIdentity = true, IsReadOnly = true, IsKey = true },
                    new FieldDefinition("TournamentName", "Название турнира", FieldType.Text) { IsRequired = true },
                    new FieldDefinition("GameID", "ID игры", FieldType.Integer) { IsRequired = true },
                    new FieldDefinition("StartDate", "Дата начала", FieldType.Date) { IsRequired = true },
                    new FieldDefinition("EndDate", "Дата окончания", FieldType.Date) { IsRequired = true },
                    new FieldDefinition("PrizePool", "Призовой фонд", FieldType.Decimal),
                    new FieldDefinition("Organizer", "Организатор", FieldType.Text),
                    new FieldDefinition("Location", "Место проведения", FieldType.Text),
                    new FieldDefinition("FormatType", "Формат", FieldType.Text) { IsRequired = true },
                    new FieldDefinition("MaxTeams", "Макс. команд", FieldType.Integer) { IsRequired = true }
                });

            definition.SaveValidator = context =>
            {
                int gameId = GetInt(context.Values, "GameID");
                if (!context.Database.RecordExists("GameTitles", "GameID", gameId))
                {
                    return EntityValidationResult.Fail("Игры с таким ID не существует.");
                }

                DateTime startDate = GetDate(context.Values, "StartDate");
                DateTime endDate = GetDate(context.Values, "EndDate");
                if (endDate < startDate)
                {
                    return EntityValidationResult.Fail("Дата окончания должна быть позже или равна дате начала.");
                }

                int maxTeams = GetInt(context.Values, "MaxTeams");
                if (maxTeams % 2 != 0)
                {
                    return EntityValidationResult.Fail("Количество команд должно быть чётным.");
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

                if (stages > 0 || participants > 0 || matches > 0 || streams > 0 || sponsors > 0)
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
                    new FieldDefinition("TournamentID", "ID турнира", FieldType.Integer) { IsRequired = true },
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
                new[]
                {
                    new FieldDefinition("ParticipationID", "ID участия", FieldType.Integer) { IsIdentity = true, IsReadOnly = true, IsKey = true },
                    new FieldDefinition("TournamentID", "ID турнира", FieldType.Integer) { IsRequired = true },
                    new FieldDefinition("TeamID", "ID команды", FieldType.Integer) { IsRequired = true },
                    new FieldDefinition("Seed", "Seed", FieldType.Integer),
                    new FieldDefinition("FinalPlace", "Итоговое место", FieldType.Integer)
                });

            definition.SaveValidator = context =>
            {
                int tournamentId = GetInt(context.Values, "TournamentID");
                int teamId = GetInt(context.Values, "TeamID");
                int currentId = GetOriginalInt(context, "ParticipationID");

                if (!context.Database.RecordExists("Tournaments", "TournamentID", tournamentId))
                {
                    return EntityValidationResult.Fail("Турнира с таким ID не существует.");
                }

                if (!context.Database.RecordExists("Teams", "TeamID", teamId))
                {
                    return EntityValidationResult.Fail("Команды с таким ID не существует.");
                }

                int duplicates = Count(context.Database, "TournamentParticipants", row =>
                    ValuesEqual(row["TournamentID"], tournamentId) &&
                    ValuesEqual(row["TeamID"], teamId) &&
                    (context.IsInsert || !ValuesEqual(row["ParticipationID"], currentId)));

                if (duplicates > 0)
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
                new[]
                {
                    new FieldDefinition("TournamentID", "ID турнира", FieldType.Integer) { IsRequired = true, IsKey = true },
                    new FieldDefinition("SponsorID", "ID спонсора", FieldType.Integer) { IsRequired = true, IsKey = true },
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


