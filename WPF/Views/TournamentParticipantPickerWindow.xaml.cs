using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Tournaments.WPF.Models;
using Tournaments.WPF.Services;

namespace Tournaments.WPF.Views
{
    public partial class TournamentParticipantPickerWindow : Window
    {
        private readonly DatabaseService _database;
        private readonly EntityDefinition _participantsDefinition;
        private readonly TournamentCardViewModel _card;
        private List<AvailablePlayerViewModel> _allPlayers = new List<AvailablePlayerViewModel>();
        private List<RegisteredPlayerViewModel> _registeredPlayers = new List<RegisteredPlayerViewModel>();

        public TournamentParticipantPickerWindow(DatabaseService database, EntityDefinition participantsDefinition, TournamentCardViewModel card)
        {
            InitializeComponent();
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _participantsDefinition = participantsDefinition ?? throw new ArgumentNullException(nameof(participantsDefinition));
            _card = card ?? throw new ArgumentNullException(nameof(card));

            Title = "Управление участниками";
            TitleText.Text = "Управление участниками";
            SubtitleText.Text = "Добавляйте свободных игроков и удаляйте текущих участников турнира \"" + _card.TournamentName + "\" прямо из этого окна.";
            Loaded += TournamentParticipantPickerWindow_Loaded;
        }

        public bool HasChanges { get; private set; }

        private void TournamentParticipantPickerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= TournamentParticipantPickerWindow_Loaded;
            LoadPlayers();
        }

        private void LoadPlayers()
        {
            try
            {
                if (!string.Equals(_card.ParticipantMode, "Игроки", StringComparison.CurrentCultureIgnoreCase))
                {
                    _registeredPlayers = new List<RegisteredPlayerViewModel>();
                    _allPlayers = new List<AvailablePlayerViewModel>();
                    ApplyFilter();
                    return;
                }

                DataTable players = _database.GetTable("Players");
                DataTable participants = _database.GetTable("TournamentParticipants");
                Dictionary<int, DataRow> playersById = players.Rows
                    .Cast<DataRow>()
                    .Where(row => row["PlayerID"] != DBNull.Value)
                    .GroupBy(row => Convert.ToInt32(row["PlayerID"]))
                    .ToDictionary(group => group.Key, group => group.First());

                List<DataRow> participantRows = participants.Rows
                    .Cast<DataRow>()
                    .Where(row =>
                        row["TournamentID"] != DBNull.Value &&
                        Convert.ToInt32(row["TournamentID"]) == _card.TournamentId &&
                        row.Table.Columns.Contains("PlayerID") &&
                        row["PlayerID"] != DBNull.Value)
                    .ToList();

                _registeredPlayers = participantRows
                    .Select(row => BuildRegisteredPlayer(row, playersById))
                    .OrderBy(player => player.Seed.HasValue ? 0 : 1)
                    .ThenBy(player => player.Seed ?? int.MaxValue)
                    .ThenBy(player => player.Nickname)
                    .ToList();

                HashSet<int> registeredPlayerIds = new HashSet<int>(_registeredPlayers.Select(player => player.PlayerId));

                int registeredCount = _registeredPlayers.Count;
                bool limitReached = _card.MaxParticipants > 0 && registeredCount >= _card.MaxParticipants;

                _allPlayers = limitReached
                    ? new List<AvailablePlayerViewModel>()
                    : players.Rows
                        .Cast<DataRow>()
                        .Where(row =>
                            row["PlayerID"] != DBNull.Value &&
                            !registeredPlayerIds.Contains(Convert.ToInt32(row["PlayerID"])))
                        .Select(row => new AvailablePlayerViewModel
                        {
                            PlayerId = Convert.ToInt32(row["PlayerID"]),
                            Nickname = Convert.ToString(row["Nickname"]),
                            Details = BuildPlayerDetails(row)
                        })
                        .OrderBy(player => player.Nickname)
                        .ToList();

                ApplyFilter();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось загрузить список игроков: " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
                _registeredPlayers = new List<RegisteredPlayerViewModel>();
                _allPlayers = new List<AvailablePlayerViewModel>();
                ApplyFilter();
            }
        }

        private void ApplyFilter()
        {
            string query = SearchTextBox.Text == null ? string.Empty : SearchTextBox.Text.Trim();
            List<RegisteredPlayerViewModel> filteredRegistered = string.IsNullOrWhiteSpace(query)
                ? _registeredPlayers
                : _registeredPlayers
                    .Where(player =>
                        Contains(player.Nickname, query) ||
                        Contains(player.Details, query) ||
                        Contains(player.SeedText, query))
                    .ToList();

            List<AvailablePlayerViewModel> filteredAvailable = string.IsNullOrWhiteSpace(query)
                ? _allPlayers
                : _allPlayers
                    .Where(player =>
                        Contains(player.Nickname, query) ||
                        Contains(player.Details, query))
                    .ToList();

            RegisteredPlayersItemsControl.ItemsSource = filteredRegistered;
            PlayersItemsControl.ItemsSource = filteredAvailable;

            SummaryText.Text = BuildSummaryText(filteredRegistered.Count, filteredAvailable.Count);
            RegisteredCountText.Text = filteredRegistered.Count.ToString();
            AvailableCountText.Text = filteredAvailable.Count.ToString();

            RegisteredEmptyText.Visibility = filteredRegistered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RegisteredEmptyText.Text = BuildRegisteredEmptyText(query);

            AvailableEmptyText.Visibility = filteredAvailable.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            AvailableEmptyText.Text = BuildAvailableEmptyText(query);
        }

        private void Player_Click(object sender, RoutedEventArgs e)
        {
            AvailablePlayerViewModel player = (sender as FrameworkElement)?.DataContext as AvailablePlayerViewModel;
            if (player == null)
            {
                return;
            }

            try
            {
                Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["TournamentID"] = _card.TournamentId,
                    ["PlayerID"] = player.PlayerId,
                    ["Seed"] = GetNextSeed(_card.TournamentId)
                };

                EntityEditContext context = new EntityEditContext(true, values, null, _database);
                EntityValidationResult validation = _participantsDefinition.SaveValidator == null
                    ? EntityValidationResult.Success()
                    : _participantsDefinition.SaveValidator(context);

                if (!validation.IsValid)
                {
                    MessageBox.Show(validation.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
                    LoadPlayers();
                    return;
                }

                _database.Insert("TournamentParticipants", values);
                HasChanges = true;
                LoadPlayers();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось добавить игрока: " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
                LoadPlayers();
            }
        }

        private void RemovePlayer_Click(object sender, RoutedEventArgs e)
        {
            RegisteredPlayerViewModel player = (sender as FrameworkElement)?.DataContext as RegisteredPlayerViewModel;
            if (player == null)
            {
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                "Удалить игрока \"" + player.Nickname + "\" из турнира \"" + _card.TournamentName + "\"?",
                "Tournaments WPF",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                _database.Delete("TournamentParticipants", _participantsDefinition.KeyColumns, player.OriginalValues);
                HasChanges = true;
                LoadPlayers();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось удалить участника: " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
                LoadPlayers();
            }
        }

        private int GetNextSeed(int tournamentId)
        {
            DataTable participants = _database.GetTable("TournamentParticipants");
            List<int> seeds = participants.Rows
                .Cast<DataRow>()
                .Where(row =>
                    row["TournamentID"] != DBNull.Value &&
                    Convert.ToInt32(row["TournamentID"]) == tournamentId &&
                    row.Table.Columns.Contains("Seed") &&
                    row["Seed"] != DBNull.Value)
                .Select(row => Convert.ToInt32(row["Seed"]))
                .ToList();

            return seeds.Count == 0 ? 1 : seeds.Max() + 1;
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private string BuildRegisteredEmptyText(string query)
        {
            if (!string.Equals(_card.ParticipantMode, "Игроки", StringComparison.CurrentCultureIgnoreCase))
            {
                return "Быстрое управление доступно только для турниров с участниками-игроками.";
            }

            if (!string.IsNullOrWhiteSpace(query) && _registeredPlayers.Count > 0)
            {
                return "По текущему запросу участники не найдены.";
            }

            return "В турнире пока нет добавленных игроков.";
        }

        private string BuildAvailableEmptyText(string query)
        {
            if (!string.Equals(_card.ParticipantMode, "Игроки", StringComparison.CurrentCultureIgnoreCase))
            {
                return "Для командных турниров используйте общий список участников.";
            }

            if (_card.MaxParticipants > 0 && _registeredPlayers.Count >= _card.MaxParticipants)
            {
                return "Лимит турнира достигнут. Чтобы добавить нового игрока, сначала удалите одного из текущих участников.";
            }

            if (!string.IsNullOrWhiteSpace(query) && _allPlayers.Count > 0)
            {
                return "По текущему запросу свободные игроки не найдены.";
            }

            return "Свободных игроков для добавления не осталось.";
        }

        private string BuildSummaryText(int visibleRegisteredCount, int visibleAvailableCount)
        {
            if (_card.MaxParticipants > 0)
            {
                return "Участников в турнире: " + visibleRegisteredCount + ". Доступно для добавления: " + visibleAvailableCount + ". Лимит: " + _card.MaxParticipants + ".";
            }

            return "Участников в турнире: " + visibleRegisteredCount + ". Доступно для добавления: " + visibleAvailableCount + ".";
        }

        private static string BuildPlayerDetails(DataRow row)
        {
            if (row == null)
            {
                return "Игрок без дополнительных данных";
            }

            List<string> parts = new List<string>();

            if (row.Table.Columns.Contains("RealName") && row["RealName"] != DBNull.Value)
            {
                string realName = Convert.ToString(row["RealName"]);
                if (!string.IsNullOrWhiteSpace(realName))
                {
                    parts.Add(realName);
                }
            }

            if (row.Table.Columns.Contains("Country") && row["Country"] != DBNull.Value)
            {
                string country = Convert.ToString(row["Country"]);
                if (!string.IsNullOrWhiteSpace(country))
                {
                    parts.Add(country);
                }
            }

            return parts.Count == 0 ? "Игрок без дополнительных данных" : string.Join(" | ", parts);
        }

        private static bool Contains(string source, string query)
        {
            return !string.IsNullOrWhiteSpace(source) &&
                   !string.IsNullOrWhiteSpace(query) &&
                   source.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private RegisteredPlayerViewModel BuildRegisteredPlayer(DataRow participantRow, IReadOnlyDictionary<int, DataRow> playersById)
        {
            int playerId = Convert.ToInt32(participantRow["PlayerID"]);
            playersById.TryGetValue(playerId, out DataRow playerRow);

            string nickname = playerRow != null && playerRow.Table.Columns.Contains("Nickname") && playerRow["Nickname"] != DBNull.Value
                ? Convert.ToString(playerRow["Nickname"])
                : "Игрок #" + playerId;

            int? seed = participantRow.Table.Columns.Contains("Seed") && participantRow["Seed"] != DBNull.Value
                ? (int?)Convert.ToInt32(participantRow["Seed"])
                : null;

            return new RegisteredPlayerViewModel
            {
                ParticipationId = Convert.ToInt32(participantRow["ParticipationID"]),
                PlayerId = playerId,
                Nickname = nickname,
                Details = BuildPlayerDetails(playerRow),
                Seed = seed,
                OriginalValues = ToDictionary(participantRow)
            };
        }

        private static Dictionary<string, object> ToDictionary(DataRow row)
        {
            Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (DataColumn column in row.Table.Columns)
            {
                object value = row[column.ColumnName];
                values[column.ColumnName] = value == DBNull.Value ? null : value;
            }

            return values;
        }

        private sealed class AvailablePlayerViewModel
        {
            public int PlayerId { get; set; }

            public string Nickname { get; set; }

            public string Details { get; set; }
        }

        private sealed class RegisteredPlayerViewModel
        {
            public int ParticipationId { get; set; }

            public int PlayerId { get; set; }

            public string Nickname { get; set; }

            public string Details { get; set; }

            public int? Seed { get; set; }

            public string SeedText
            {
                get { return Seed.HasValue ? "Seed " + Seed.Value : "Без seed"; }
            }

            public IDictionary<string, object> OriginalValues { get; set; }
        }
    }
}
