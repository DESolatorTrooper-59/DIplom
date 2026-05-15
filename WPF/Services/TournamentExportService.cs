using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Tournaments.WPF.Services
{
    public sealed class TournamentExportResult
    {
        public TournamentExportResult(int tableCount, int rowCount)
        {
            TableCount = tableCount;
            RowCount = rowCount;
        }

        public int TableCount { get; }

        public int RowCount { get; }
    }

    public static class TournamentExportService
    {
        private const string ExportFormat = "Tournaments.WPF.tournament-export";

        public static TournamentExportResult ExportToFile(DatabaseService database, int tournamentId, string filePath, string previewPath)
        {
            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("Не указан путь для экспорта.", nameof(filePath));
            }

            OrderedDictionary payload = BuildPayload(database, tournamentId, previewPath, out int tableCount, out int rowCount);
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, SerializeJson(payload), new UTF8Encoding(true));
            return new TournamentExportResult(tableCount, rowCount);
        }

        private static OrderedDictionary BuildPayload(DatabaseService database, int tournamentId, string previewPath, out int tableCount, out int rowCount)
        {
            DataTable tournaments = database.GetTable("Tournaments");
            DataRow tournamentRow = tournaments.Rows
                .Cast<DataRow>()
                .FirstOrDefault(row => ValuesEqual(row, "TournamentID", tournamentId));

            if (tournamentRow == null)
            {
                throw new InvalidOperationException("Турнир с таким ID не найден.");
            }

            DataTable stages = database.GetTable("TournamentStages");
            DataTable participants = database.GetTable("TournamentParticipants");
            DataTable matches = database.GetTable("Matches");
            DataTable streams = database.GetTable("Streams");
            DataTable tournamentSponsors = database.GetTable("TournamentSponsors");
            DataTable games = database.GetTable("GameTitles");
            DataTable teams = database.GetTable("Teams");
            DataTable players = database.GetTable("Players");
            DataTable teamPlayers = database.GetTable("TeamPlayers");
            DataTable sponsors = database.GetTable("Sponsors");

            List<DataRow> stageRows = RowsWhere(stages, row => ValuesEqual(row, "TournamentID", tournamentId));
            HashSet<int> stageIds = GetIds(stageRows, "StageID");

            List<DataRow> participantRows = RowsWhere(participants, row => ValuesEqual(row, "TournamentID", tournamentId));
            List<DataRow> matchRows = RowsWhere(matches, row =>
                ValuesEqual(row, "TournamentID", tournamentId) ||
                GetNullableInt(row, "StageID").HasValue && stageIds.Contains(GetNullableInt(row, "StageID").Value));
            HashSet<int> matchIds = GetIds(matchRows, "MatchID");

            List<DataRow> streamRows = RowsWhere(streams, row =>
                ValuesEqual(row, "TournamentID", tournamentId) ||
                GetNullableInt(row, "MatchID").HasValue && matchIds.Contains(GetNullableInt(row, "MatchID").Value));
            List<DataRow> tournamentSponsorRows = RowsWhere(tournamentSponsors, row => ValuesEqual(row, "TournamentID", tournamentId));

            HashSet<int> teamIds = new HashSet<int>();
            AddIds(teamIds, participantRows, "TeamID");
            AddIds(teamIds, matchRows, "Team1ID");
            AddIds(teamIds, matchRows, "Team2ID");
            AddIds(teamIds, matchRows, "WinnerTeamID");

            HashSet<int> playerIds = new HashSet<int>();
            AddIds(playerIds, participantRows, "PlayerID");
            AddIds(playerIds, matchRows, "Player1ID");
            AddIds(playerIds, matchRows, "Player2ID");
            AddIds(playerIds, matchRows, "WinnerPlayerID");

            List<DataRow> teamPlayerRows = RowsWhere(teamPlayers, row =>
                GetNullableInt(row, "TeamID").HasValue && teamIds.Contains(GetNullableInt(row, "TeamID").Value));
            AddIds(playerIds, teamPlayerRows, "PlayerID");

            int? gameId = GetNullableInt(tournamentRow, "GameID");
            List<DataRow> gameRows = RowsWhere(games, row =>
                gameId.HasValue && ValuesEqual(row, "GameID", gameId.Value));
            List<DataRow> teamRows = RowsWhere(teams, row =>
                GetNullableInt(row, "TeamID").HasValue && teamIds.Contains(GetNullableInt(row, "TeamID").Value));
            List<DataRow> playerRows = RowsWhere(players, row =>
                GetNullableInt(row, "PlayerID").HasValue && playerIds.Contains(GetNullableInt(row, "PlayerID").Value));

            HashSet<int> sponsorIds = GetIds(tournamentSponsorRows, "SponsorID");
            List<DataRow> sponsorRows = RowsWhere(sponsors, row =>
                GetNullableInt(row, "SponsorID").HasValue && sponsorIds.Contains(GetNullableInt(row, "SponsorID").Value));

            OrderedDictionary tables = Object(
                "Tournaments", RowsToJson(new[] { tournamentRow }),
                "GameTitles", RowsToJson(gameRows),
                "TournamentStages", RowsToJson(stageRows),
                "TournamentParticipants", RowsToJson(participantRows),
                "Teams", RowsToJson(teamRows),
                "Players", RowsToJson(playerRows),
                "TeamPlayers", RowsToJson(teamPlayerRows),
                "Matches", RowsToJson(matchRows),
                "Streams", RowsToJson(streamRows),
                "TournamentSponsors", RowsToJson(tournamentSponsorRows),
                "Sponsors", RowsToJson(sponsorRows));

            OrderedDictionary counts = BuildCounts(tables);
            tableCount = tables.Count;
            rowCount = counts.Values.Cast<object>().Select(Convert.ToInt32).Sum();

            return Object(
                "format", ExportFormat,
                "version", 1,
                "exportedAt", DateTime.Now,
                "source", Object(
                    "mode", database.ModeTitle,
                    "storage", database.StorageLabel),
                "tournamentId", tournamentId,
                "preview", Object(
                    "path", string.IsNullOrWhiteSpace(previewPath) ? null : previewPath,
                    "fileName", string.IsNullOrWhiteSpace(previewPath) ? null : Path.GetFileName(previewPath)),
                "counts", counts,
                "tables", tables);
        }

        private static OrderedDictionary BuildCounts(OrderedDictionary tables)
        {
            OrderedDictionary counts = new OrderedDictionary();
            foreach (DictionaryEntry entry in tables)
            {
                counts.Add(entry.Key, ((ICollection)entry.Value).Count);
            }

            return counts;
        }

        private static List<OrderedDictionary> RowsToJson(IEnumerable<DataRow> rows)
        {
            return rows.Select(RowToJson).ToList();
        }

        private static OrderedDictionary RowToJson(DataRow row)
        {
            OrderedDictionary result = new OrderedDictionary();
            foreach (DataColumn column in row.Table.Columns)
            {
                object value = row[column];
                result.Add(column.ColumnName, value == DBNull.Value ? null : value);
            }

            return result;
        }

        private static List<DataRow> RowsWhere(DataTable table, Func<DataRow, bool> predicate)
        {
            return table.Rows.Cast<DataRow>().Where(predicate).ToList();
        }

        private static HashSet<int> GetIds(IEnumerable<DataRow> rows, string columnName)
        {
            HashSet<int> result = new HashSet<int>();
            AddIds(result, rows, columnName);
            return result;
        }

        private static void AddIds(ISet<int> target, IEnumerable<DataRow> rows, string columnName)
        {
            foreach (DataRow row in rows)
            {
                int? value = GetNullableInt(row, columnName);
                if (value.HasValue)
                {
                    target.Add(value.Value);
                }
            }
        }

        private static int? GetNullableInt(DataRow row, string columnName)
        {
            if (row == null || row.Table == null || !row.Table.Columns.Contains(columnName) || row[columnName] == DBNull.Value)
            {
                return null;
            }

            return Convert.ToInt32(row[columnName], CultureInfo.InvariantCulture);
        }

        private static bool ValuesEqual(DataRow row, string columnName, int value)
        {
            int? rowValue = GetNullableInt(row, columnName);
            return rowValue.HasValue && rowValue.Value == value;
        }

        private static OrderedDictionary Object(params object[] entries)
        {
            OrderedDictionary result = new OrderedDictionary();
            for (int index = 0; index + 1 < entries.Length; index += 2)
            {
                result.Add((string)entries[index], entries[index + 1]);
            }

            return result;
        }

        private static string SerializeJson(object value)
        {
            StringBuilder builder = new StringBuilder();
            WriteJsonValue(builder, value, 0);
            builder.AppendLine();
            return builder.ToString();
        }

        private static void WriteJsonValue(StringBuilder builder, object value, int indent)
        {
            if (value == null || value == DBNull.Value)
            {
                builder.Append("null");
                return;
            }

            if (value is string text)
            {
                WriteJsonString(builder, text);
                return;
            }

            if (value is DateTime dateTime)
            {
                WriteJsonString(builder, dateTime.ToString("O", CultureInfo.InvariantCulture));
                return;
            }

            if (value is bool boolean)
            {
                builder.Append(boolean ? "true" : "false");
                return;
            }

            if (value is byte || value is short || value is int || value is long ||
                value is float || value is double || value is decimal)
            {
                builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
            }

            if (value is OrderedDictionary orderedDictionary)
            {
                WriteJsonObject(builder, orderedDictionary, indent);
                return;
            }

            if (value is IEnumerable enumerable)
            {
                WriteJsonArray(builder, enumerable, indent);
                return;
            }

            WriteJsonString(builder, Convert.ToString(value, CultureInfo.CurrentCulture));
        }

        private static void WriteJsonObject(StringBuilder builder, OrderedDictionary value, int indent)
        {
            builder.Append("{");
            if (value.Count > 0)
            {
                builder.AppendLine();
                int index = 0;
                foreach (DictionaryEntry entry in value)
                {
                    WriteIndent(builder, indent + 1);
                    WriteJsonString(builder, Convert.ToString(entry.Key, CultureInfo.InvariantCulture));
                    builder.Append(": ");
                    WriteJsonValue(builder, entry.Value, indent + 1);
                    if (index < value.Count - 1)
                    {
                        builder.Append(",");
                    }

                    builder.AppendLine();
                    index++;
                }

                WriteIndent(builder, indent);
            }

            builder.Append("}");
        }

        private static void WriteJsonArray(StringBuilder builder, IEnumerable value, int indent)
        {
            List<object> items = value.Cast<object>().ToList();
            builder.Append("[");
            if (items.Count > 0)
            {
                builder.AppendLine();
                for (int index = 0; index < items.Count; index++)
                {
                    WriteIndent(builder, indent + 1);
                    WriteJsonValue(builder, items[index], indent + 1);
                    if (index < items.Count - 1)
                    {
                        builder.Append(",");
                    }

                    builder.AppendLine();
                }

                WriteIndent(builder, indent);
            }

            builder.Append("]");
        }

        private static void WriteJsonString(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (char symbol in value ?? string.Empty)
            {
                switch (symbol)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\b':
                        builder.Append("\\b");
                        break;
                    case '\f':
                        builder.Append("\\f");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (char.IsControl(symbol))
                        {
                            builder.Append("\\u");
                            builder.Append(((int)symbol).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(symbol);
                        }

                        break;
                }
            }

            builder.Append('"');
        }

        private static void WriteIndent(StringBuilder builder, int indent)
        {
            builder.Append(' ', indent * 2);
        }
    }
}
