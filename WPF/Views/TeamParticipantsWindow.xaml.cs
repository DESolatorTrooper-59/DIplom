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
    public partial class TeamParticipantsWindow : Window
    {
        private readonly DatabaseService _database;
        private readonly EntityDefinition _teamPlayersDefinition;
        private readonly TeamCardViewModel _card;
        private readonly bool _allowManage;
        private List<CurrentTeamPlayerViewModel> _currentPlayers = new List<CurrentTeamPlayerViewModel>();
        private List<AvailableTeamPlayerViewModel> _availablePlayers = new List<AvailableTeamPlayerViewModel>();

        public TeamParticipantsWindow(DatabaseService database, EntityDefinition teamPlayersDefinition, TeamCardViewModel card, bool allowManage)
        {
            InitializeComponent();
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _teamPlayersDefinition = teamPlayersDefinition ?? throw new ArgumentNullException(nameof(teamPlayersDefinition));
            _card = card ?? throw new ArgumentNullException(nameof(card));
            _allowManage = allowManage;

            Title = allowManage ? "Добавление участников" : "Участники команды";
            TitleText.Text = allowManage ? "Добавление участников" : "Участники команды";
            SubtitleText.Text = allowManage
                ? "Добавляйте игроков в команду \"" + _card.TeamName + "\"."
                : "Текущий состав команды \"" + _card.TeamName + "\".";
            AvailableSectionPanel.Visibility = _allowManage ? Visibility.Visible : Visibility.Collapsed;
            Loaded += TeamParticipantsWindow_Loaded;
        }

        public bool HasChanges { get; private set; }

        private void TeamParticipantsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= TeamParticipantsWindow_Loaded;
            LoadParticipants();
        }

        private void LoadParticipants()
        {
            try
            {
                DataTable teamPlayers = _database.GetTable("TeamPlayers");
                DataTable players = _database.GetTable("Players");

                Dictionary<int, DataRow> playersById = players.Rows
                    .Cast<DataRow>()
                    .Where(row => row.Table.Columns.Contains("PlayerID") && row["PlayerID"] != DBNull.Value)
                    .GroupBy(row => Convert.ToInt32(row["PlayerID"]))
                    .ToDictionary(group => group.Key, group => group.First());

                List<DataRow> currentRows = teamPlayers.Rows
                    .Cast<DataRow>()
                    .Where(row =>
                        row.Table.Columns.Contains("TeamID") &&
                        row.Table.Columns.Contains("PlayerID") &&
                        row["TeamID"] != DBNull.Value &&
                        row["PlayerID"] != DBNull.Value &&
                        Convert.ToInt32(row["TeamID"]) == _card.TeamId &&
                        IsActiveMembership(row))
                    .ToList();

                _currentPlayers = currentRows
                    .Select(row => BuildCurrentPlayer(row, playersById))
                    .OrderBy(item => item.DisplayName)
                    .ToList();

                HashSet<int> activePlayerIds = new HashSet<int>(_currentPlayers.Select(item => item.PlayerId));
                _availablePlayers = _allowManage
                    ? players.Rows
                        .Cast<DataRow>()
                        .Where(row =>
                            row.Table.Columns.Contains("PlayerID") &&
                            row["PlayerID"] != DBNull.Value &&
                            !activePlayerIds.Contains(Convert.ToInt32(row["PlayerID"])))
                        .Select(row => new AvailableTeamPlayerViewModel
                        {
                            PlayerId = Convert.ToInt32(row["PlayerID"]),
                            DisplayName = GetPlayerDisplayName(row),
                            Details = BuildPlayerDetails(row)
                        })
                        .OrderBy(item => item.DisplayName)
                        .ToList()
                    : new List<AvailableTeamPlayerViewModel>();

                ApplyFilter();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось загрузить участников команды: " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
                _currentPlayers = new List<CurrentTeamPlayerViewModel>();
                _availablePlayers = new List<AvailableTeamPlayerViewModel>();
                ApplyFilter();
            }
        }

        private void ApplyFilter()
        {
            string query = SearchTextBox.Text == null ? string.Empty : SearchTextBox.Text.Trim();
            List<CurrentTeamPlayerViewModel> filteredCurrent = string.IsNullOrWhiteSpace(query)
                ? _currentPlayers
                : _currentPlayers
                    .Where(item =>
                        Contains(item.DisplayName, query) ||
                        Contains(item.Details, query) ||
                        Contains(item.RoleText, query))
                    .ToList();

            List<AvailableTeamPlayerViewModel> filteredAvailable = string.IsNullOrWhiteSpace(query)
                ? _availablePlayers
                : _availablePlayers
                    .Where(item =>
                        Contains(item.DisplayName, query) ||
                        Contains(item.Details, query))
                    .ToList();

            CurrentPlayersItemsControl.ItemsSource = filteredCurrent;
            AvailablePlayersItemsControl.ItemsSource = filteredAvailable;

            CurrentCountText.Text = filteredCurrent.Count.ToString();
            AvailableCountText.Text = filteredAvailable.Count.ToString();
            SummaryText.Text = _allowManage
                ? "В составе: " + filteredCurrent.Count + ". Доступно для добавления: " + filteredAvailable.Count + "."
                : "В составе: " + filteredCurrent.Count + ".";

            CurrentEmptyText.Visibility = filteredCurrent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            CurrentEmptyText.Text = string.IsNullOrWhiteSpace(query)
                ? "В команде пока нет активных участников."
                : "По текущему запросу участники команды не найдены.";

            AvailableEmptyText.Visibility = filteredAvailable.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            AvailableEmptyText.Text = string.IsNullOrWhiteSpace(query)
                ? "Свободных игроков для добавления не осталось."
                : "По текущему запросу свободные игроки не найдены.";
        }

        private void AddParticipant_Click(object sender, RoutedEventArgs e)
        {
            if (!_allowManage)
            {
                return;
            }

            AvailableTeamPlayerViewModel player = (sender as FrameworkElement)?.DataContext as AvailableTeamPlayerViewModel;
            if (player == null)
            {
                return;
            }

            try
            {
                Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["TeamID"] = _card.TeamId,
                    ["PlayerID"] = player.PlayerId,
                    ["JoinDate"] = DateTime.Today,
                    ["IsActive"] = true
                };

                EntityEditContext context = new EntityEditContext(true, values, null, _database);
                EntityValidationResult validation = _teamPlayersDefinition.SaveValidator == null
                    ? EntityValidationResult.Success()
                    : _teamPlayersDefinition.SaveValidator(context);

                if (!validation.IsValid)
                {
                    MessageBox.Show(validation.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
                    LoadParticipants();
                    return;
                }

                _database.Insert("TeamPlayers", values);
                HasChanges = true;
                LoadParticipants();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось добавить участника: " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
                LoadParticipants();
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private CurrentTeamPlayerViewModel BuildCurrentPlayer(DataRow teamPlayerRow, IReadOnlyDictionary<int, DataRow> playersById)
        {
            int playerId = Convert.ToInt32(teamPlayerRow["PlayerID"]);
            playersById.TryGetValue(playerId, out DataRow playerRow);
            string role = teamPlayerRow.Table.Columns.Contains("Role") && teamPlayerRow["Role"] != DBNull.Value
                ? Convert.ToString(teamPlayerRow["Role"])
                : null;

            return new CurrentTeamPlayerViewModel
            {
                PlayerId = playerId,
                DisplayName = GetPlayerDisplayName(playerRow, playerId),
                Details = BuildCurrentPlayerDetails(playerRow, teamPlayerRow),
                RoleText = string.IsNullOrWhiteSpace(role) ? "Участник" : role
            };
        }

        private static string BuildCurrentPlayerDetails(DataRow playerRow, DataRow teamPlayerRow)
        {
            List<string> parts = new List<string>();
            string playerDetails = BuildPlayerDetails(playerRow);
            if (!string.IsNullOrWhiteSpace(playerDetails))
            {
                parts.Add(playerDetails);
            }

            if (teamPlayerRow != null && teamPlayerRow.Table.Columns.Contains("JoinDate") && teamPlayerRow["JoinDate"] != DBNull.Value)
            {
                parts.Add("В команде с " + Convert.ToDateTime(teamPlayerRow["JoinDate"]).ToString("dd.MM.yyyy"));
            }

            return parts.Count == 0 ? "Игрок без дополнительных данных" : string.Join(" | ", parts);
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

        private static string GetPlayerDisplayName(DataRow row, int fallbackId = 0)
        {
            if (row != null && row.Table.Columns.Contains("Nickname") && row["Nickname"] != DBNull.Value)
            {
                string nickname = Convert.ToString(row["Nickname"]);
                if (!string.IsNullOrWhiteSpace(nickname))
                {
                    return nickname;
                }
            }

            return "Игрок #" + fallbackId;
        }

        private static bool IsActiveMembership(DataRow row)
        {
            if (row == null || !row.Table.Columns.Contains("IsActive") || row["IsActive"] == DBNull.Value)
            {
                return true;
            }

            return Convert.ToBoolean(row["IsActive"]);
        }

        private static bool Contains(string source, string query)
        {
            return !string.IsNullOrWhiteSpace(source) &&
                   !string.IsNullOrWhiteSpace(query) &&
                   source.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private sealed class CurrentTeamPlayerViewModel
        {
            public int PlayerId { get; set; }

            public string DisplayName { get; set; }

            public string Details { get; set; }

            public string RoleText { get; set; }
        }

        private sealed class AvailableTeamPlayerViewModel
        {
            public int PlayerId { get; set; }

            public string DisplayName { get; set; }

            public string Details { get; set; }
        }
    }
}
