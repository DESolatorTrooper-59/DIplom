using System.Collections.Generic;

namespace Tournaments.WPF.Services
{
    internal static class SqlSchemaMap
    {
        private static readonly IReadOnlyDictionary<string, string> TableNames =
            new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["Organizer"] = "Организаторы",
                ["GameTitles"] = "Игры",
                ["Teams"] = "Команды",
                ["Players"] = "Игроки",
                ["Sponsors"] = "Спонсоры",
                ["Tournaments"] = "Турниры",
                ["TournamentStages"] = "ЭтапыТурниров",
                ["TeamPlayers"] = "СоставыКоманд",
                ["TournamentParticipants"] = "УчастникиТурниров",
                ["Matches"] = "Матчи",
                ["Streams"] = "Трансляции",
                ["TournamentSponsors"] = "СпонсорыТурниров"
            };

        private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ColumnNames =
            new Dictionary<string, IReadOnlyDictionary<string, string>>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["Organizer"] = CreateColumns(
                    ("Login", "Логин"),
                    ("Password", "Пароль")),
                ["GameTitles"] = CreateColumns(
                    ("GameID", "IDИгры"),
                    ("GameName", "НазваниеИгры"),
                    ("Developer", "Разработчик"),
                    ("ReleaseYear", "ГодВыпуска"),
                    ("MaxPlayersPerTeam", "МаксИгроковВКоманде")),
                ["Teams"] = CreateColumns(
                    ("TeamID", "IDКоманды"),
                    ("TeamName", "НазваниеКоманды"),
                    ("FoundedDate", "ДатаОснования"),
                    ("Country", "Страна"),
                    ("CoachName", "ИмяТренера"),
                    ("CreatedDate", "ДатаСоздания")),
                ["Players"] = CreateColumns(
                    ("PlayerID", "IDИгрока"),
                    ("Nickname", "Никнейм"),
                    ("RealName", "НастоящееИмя"),
                    ("Country", "Страна"),
                    ("BirthDate", "ДатаРождения"),
                    ("Password", "Пароль")),
                ["Sponsors"] = CreateColumns(
                    ("SponsorID", "IDСпонсора"),
                    ("SponsorName", "НазваниеСпонсора"),
                    ("Industry", "Индустрия")),
                ["Tournaments"] = CreateColumns(
                    ("TournamentID", "IDТурнира"),
                    ("TournamentName", "НазваниеТурнира"),
                    ("GameID", "IDИгры"),
                    ("StartDate", "ДатаНачала"),
                    ("EndDate", "ДатаОкончания"),
                    ("PrizePool", "ПризовойФонд"),
                    ("Organizer", "Организатор"),
                    ("Location", "МестоПроведения"),
                    ("FormatType", "ТипФормата"),
                    ("MaxTeams", "МаксУчастников"),
                    ("ParticipantMode", "ТипУчастников")),
                ["TournamentStages"] = CreateColumns(
                    ("StageID", "IDЭтапа"),
                    ("TournamentID", "IDТурнира"),
                    ("StageName", "НазваниеЭтапа"),
                    ("StageOrder", "ПорядокЭтапа"),
                    ("BracketType", "ТипСетки")),
                ["TeamPlayers"] = CreateColumns(
                    ("TeamPlayerID", "IDСоставаКоманды"),
                    ("TeamID", "IDКоманды"),
                    ("PlayerID", "IDИгрока"),
                    ("JoinDate", "ДатаПрисоединения"),
                    ("LeaveDate", "ДатаУхода"),
                    ("IsActive", "Активен"),
                    ("Role", "Роль")),
                ["TournamentParticipants"] = CreateColumns(
                    ("ParticipationID", "IDУчастия"),
                    ("TournamentID", "IDТурнира"),
                    ("TeamID", "IDКоманды"),
                    ("PlayerID", "IDИгрока"),
                    ("Seed", "Посев"),
                    ("FinalPlace", "ИтоговоеМесто")),
                ["Matches"] = CreateColumns(
                    ("MatchID", "IDМатча"),
                    ("TournamentID", "IDТурнира"),
                    ("StageID", "IDЭтапа"),
                    ("MatchNumber", "НомерМатча"),
                    ("Team1ID", "IDКоманды1"),
                    ("Team2ID", "IDКоманды2"),
                    ("WinnerTeamID", "IDПобедившейКоманды"),
                    ("Player1ID", "IDИгрока1"),
                    ("Player2ID", "IDИгрока2"),
                    ("WinnerPlayerID", "IDПобедившегоИгрока"),
                    ("Team1Score", "СчетКоманды1"),
                    ("Team2Score", "СчетКоманды2"),
                    ("MatchDate", "ДатаМатча"),
                    ("BestOf", "ДоСколькихПобед"),
                    ("Status", "Статус")),
                ["Streams"] = CreateColumns(
                    ("StreamID", "IDТрансляции"),
                    ("TournamentID", "IDТурнира"),
                    ("MatchID", "IDМатча"),
                    ("Platform", "Платформа"),
                    ("StreamURL", "СсылкаТрансляции")),
                ["TournamentSponsors"] = CreateColumns(
                    ("TournamentID", "IDТурнира"),
                    ("SponsorID", "IDСпонсора"),
                    ("SponsorshipAmount", "СуммаСпонсорства"),
                    ("Currency", "Валюта"))
            };

        public static string GetPhysicalTableName(string logicalTableName)
        {
            return TableNames.TryGetValue(logicalTableName, out string physicalTableName)
                ? physicalTableName
                : logicalTableName;
        }

        public static string GetPhysicalColumnName(string logicalTableName, string logicalColumnName)
        {
            if (ColumnNames.TryGetValue(logicalTableName, out IReadOnlyDictionary<string, string> columns) &&
                columns.TryGetValue(logicalColumnName, out string physicalColumnName))
            {
                return physicalColumnName;
            }

            return logicalColumnName;
        }

        private static IReadOnlyDictionary<string, string> CreateColumns(params (string Logical, string Physical)[] pairs)
        {
            Dictionary<string, string> columns = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            foreach ((string logical, string physical) in pairs)
            {
                columns[logical] = physical;
            }

            return columns;
        }
    }
}
