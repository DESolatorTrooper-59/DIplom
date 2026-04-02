using Tournaments.WPF.Models;

namespace Tournaments.WPF.Services
{
    public static partial class EntityRegistry
    {
        private static EntityDefinition CreateTeamsDefinition()
        {
            EntityDefinition definition = new EntityDefinition(
                "Teams",
                "Команды",
                new[] { "TeamID" },
                new[]
                {
                    new FieldDefinition("TeamID", "ID команды", FieldType.Integer) { IsIdentity = true, IsReadOnly = true, IsKey = true },
                    new FieldDefinition("TeamName", "Название команды", FieldType.Text) { IsRequired = true },
                    new FieldDefinition("FoundedDate", "Дата основания", FieldType.Date) { IsRequired = true },
                    new FieldDefinition("Country", "Страна", FieldType.Text) { IsRequired = true },
                    new FieldDefinition("CoachName", "Тренер", FieldType.Text) { IsRequired = true },
                    new FieldDefinition("CreatedDate", "Дата создания", FieldType.Text) { IsReadOnly = true }
                });

            definition.SaveValidator = context =>
            {
                string teamName = GetString(context.Values, "TeamName");
                int currentId = GetOriginalInt(context, "TeamID");
                if (IsDuplicate(context.Database, "Teams", "TeamName", teamName, "TeamID", currentId, context.IsInsert))
                {
                    return EntityValidationResult.Fail("Команда с таким названием уже существует.");
                }

                return EntityValidationResult.Success();
            };

            definition.DeleteValidator = context =>
            {
                int teamId = GetOriginalInt(context, "TeamID");
                int participants = Count(context.Database, "TournamentParticipants", row => ValuesEqual(row["TeamID"], teamId));
                int players = Count(context.Database, "TeamPlayers", row => ValuesEqual(row["TeamID"], teamId));
                int matches = Count(context.Database, "Matches", row => ValuesEqual(row["Team1ID"], teamId) || ValuesEqual(row["Team2ID"], teamId));

                if (participants > 0 || players > 0 || matches > 0)
                {
                    return EntityValidationResult.Fail("Нельзя удалить команду: есть связанные участники турниров, составы команд или матчи.");
                }

                return EntityValidationResult.Success();
            };

            return definition;
        }

        private static EntityDefinition CreatePlayersDefinition()
        {
            EntityDefinition definition = new EntityDefinition(
                "Players",
                "Игроки",
                new[] { "PlayerID" },
                new[]
                {
                    new FieldDefinition("PlayerID", "ID игрока", FieldType.Integer) { IsIdentity = true, IsReadOnly = true, IsKey = true },
                    new FieldDefinition("Nickname", "Никнейм", FieldType.Text) { IsRequired = true },
                    new FieldDefinition("RealName", "Реальное имя", FieldType.Text) { IsRequired = true },
                    new FieldDefinition("Country", "Страна", FieldType.Text) { IsRequired = true },
                    new FieldDefinition("BirthDate", "Дата рождения", FieldType.Date) { IsRequired = true }
                });

            definition.SaveValidator = context =>
            {
                string nickname = GetString(context.Values, "Nickname");
                int currentId = GetOriginalInt(context, "PlayerID");
                if (IsDuplicate(context.Database, "Players", "Nickname", nickname, "PlayerID", currentId, context.IsInsert))
                {
                    return EntityValidationResult.Fail("Игрок с таким никнеймом уже существует.");
                }

                return EntityValidationResult.Success();
            };

            definition.DeleteValidator = context =>
            {
                int playerId = GetOriginalInt(context, "PlayerID");
                int teamPlayers = Count(context.Database, "TeamPlayers", row => ValuesEqual(row["PlayerID"], playerId));
                if (teamPlayers > 0)
                {
                    return EntityValidationResult.Fail("Нельзя удалить игрока, пока он связан с составами команд.");
                }

                return EntityValidationResult.Success();
            };

            return definition;
        }

        private static EntityDefinition CreateGamesDefinition()
        {
            EntityDefinition definition = new EntityDefinition(
                "GameTitles",
                "Игры",
                new[] { "GameID" },
                new[]
                {
                    new FieldDefinition("GameID", "ID игры", FieldType.Integer) { IsIdentity = true, IsReadOnly = true, IsKey = true },
                    new FieldDefinition("GameName", "Название игры", FieldType.Text) { IsRequired = true },
                    new FieldDefinition("Developer", "Разработчик", FieldType.Text) { IsRequired = true },
                    new FieldDefinition("ReleaseYear", "Год выпуска", FieldType.Integer) { IsRequired = true },
                    new FieldDefinition("MaxPlayersPerTeam", "Макс. игроков в команде", FieldType.Integer) { IsRequired = true }
                });

            definition.SaveValidator = context =>
            {
                string gameName = GetString(context.Values, "GameName");
                int currentId = GetOriginalInt(context, "GameID");
                if (IsDuplicate(context.Database, "GameTitles", "GameName", gameName, "GameID", currentId, context.IsInsert))
                {
                    return EntityValidationResult.Fail("Игра с таким названием уже существует.");
                }

                return EntityValidationResult.Success();
            };

            definition.DeleteValidator = context =>
            {
                int gameId = GetOriginalInt(context, "GameID");
                int tournaments = Count(context.Database, "Tournaments", row => ValuesEqual(row["GameID"], gameId));
                if (tournaments > 0)
                {
                    return EntityValidationResult.Fail("Нельзя удалить игру, пока на неё ссылаются турниры.");
                }

                return EntityValidationResult.Success();
            };

            return definition;
        }

        private static EntityDefinition CreateSponsorsDefinition()
        {
            EntityDefinition definition = new EntityDefinition(
                "Sponsors",
                "Спонсоры",
                new[] { "SponsorID" },
                new[]
                {
                    new FieldDefinition("SponsorID", "ID спонсора", FieldType.Integer) { IsIdentity = true, IsReadOnly = true, IsKey = true },
                    new FieldDefinition("SponsorName", "Название спонсора", FieldType.Text) { IsRequired = true },
                    new FieldDefinition("Industry", "Индустрия", FieldType.Text) { IsRequired = true }
                });

            definition.SaveValidator = context =>
            {
                string sponsorName = GetString(context.Values, "SponsorName");
                int currentId = GetOriginalInt(context, "SponsorID");
                if (IsDuplicate(context.Database, "Sponsors", "SponsorName", sponsorName, "SponsorID", currentId, context.IsInsert))
                {
                    return EntityValidationResult.Fail("Спонсор с таким названием уже существует.");
                }

                return EntityValidationResult.Success();
            };

            definition.DeleteValidator = context =>
            {
                int sponsorId = GetOriginalInt(context, "SponsorID");
                int count = Count(context.Database, "TournamentSponsors", row => ValuesEqual(row["SponsorID"], sponsorId));
                if (count > 0)
                {
                    return EntityValidationResult.Fail("Нельзя удалить спонсора, пока он связан с турнирами.");
                }

                return EntityValidationResult.Success();
            };

            return definition;
        }
    }
}
