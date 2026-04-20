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
        private List<AvailableParticipantViewModel> _allAvailableParticipants = new List<AvailableParticipantViewModel>();
        private List<RegisteredParticipantViewModel> _registeredParticipants = new List<RegisteredParticipantViewModel>();

        public TournamentParticipantPickerWindow(DatabaseService database, EntityDefinition participantsDefinition, TournamentCardViewModel card)
        {
            InitializeComponent();
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _participantsDefinition = participantsDefinition ?? throw new ArgumentNullException(nameof(participantsDefinition));
            _card = card ?? throw new ArgumentNullException(nameof(card));

            Title = IsPlayerMode ? "Управление участниками" : "Управление командами";
            TitleText.Text = IsPlayerMode ? "Управление участниками" : "Управление командами";
            SubtitleText.Text = IsPlayerMode
                ? "Добавляйте свободных игроков и удаляйте текущих участников турнира \"" + _card.TournamentName + "\" прямо из этого окна."
                : "Добавляйте свободные команды и удаляйте текущих участников турнира \"" + _card.TournamentName + "\" прямо из этого окна.";
            SearchHintText.Text = IsPlayerMode
                ? "Поиск по нику, имени или стране"
                : "Поиск по названию, тренеру или стране";
            RegisteredSectionTitleText.Text = IsPlayerMode ? "Участники турнира" : "Команды турнира";
            RegisteredHintText.Text = IsPlayerMode
                ? "Удаление выполняется по крестику справа от участника."
                : "Удаление выполняется по крестику справа от команды.";
            AvailableSectionTitleText.Text = IsPlayerMode ? "Можно добавить" : "Можно добавить команды";
            Loaded += TournamentParticipantPickerWindow_Loaded;
        }

        public bool HasChanges { get; private set; }

        private bool IsPlayerMode
        {
            get
            {
                return string.Equals(_card.ParticipantMode, "Игроки", StringComparison.CurrentCultureIgnoreCase);
            }
        }

        private void TournamentParticipantPickerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= TournamentParticipantPickerWindow_Loaded;
            LoadParticipants();
        }

        private void LoadParticipants()
        {
            try
            {
                DataTable participants = _database.GetTable("TournamentParticipants");
                DataTable sourceTable = _database.GetTable(IsPlayerMode ? "Players" : "Teams");
                string sourceIdColumn = IsPlayerMode ? "PlayerID" : "TeamID";
                string participantColumn = IsPlayerMode ? "PlayerID" : "TeamID";

                Dictionary<int, DataRow> participantsById = sourceTable.Rows
                    .Cast<DataRow>()
                    .Where(row => row[sourceIdColumn] != DBNull.Value)
                    .GroupBy(row => Convert.ToInt32(row[sourceIdColumn]))
                    .ToDictionary(group => group.Key, group => group.First());

                List<DataRow> participantRows = participants.Rows
                    .Cast<DataRow>()
                    .Where(row =>
                        row["TournamentID"] != DBNull.Value &&
                        Convert.ToInt32(row["TournamentID"]) == _card.TournamentId &&
                        row.Table.Columns.Contains(participantColumn) &&
                        row[participantColumn] != DBNull.Value)
                    .ToList();

                _registeredParticipants = participantRows
                    .Select(row => BuildRegisteredParticipant(row, participantsById))
                    .OrderBy(item => item.Seed.HasValue ? 0 : 1)
                    .ThenBy(item => item.Seed ?? int.MaxValue)
                    .ThenBy(item => item.DisplayName)
                    .ToList();

                HashSet<int> registeredParticipantIds = new HashSet<int>(_registeredParticipants.Select(item => item.ParticipantId));

                int registeredCount = _registeredParticipants.Count;
                bool limitReached = _card.MaxParticipants > 0 && registeredCount >= _card.MaxParticipants;

                _allAvailableParticipants = limitReached
                    ? new List<AvailableParticipantViewModel>()
                    : sourceTable.Rows
                        .Cast<DataRow>()
                        .Where(row =>
                            row[sourceIdColumn] != DBNull.Value &&
                            !registeredParticipantIds.Contains(Convert.ToInt32(row[sourceIdColumn])))
                        .Select(row => new AvailableParticipantViewModel
                        {
                            ParticipantId = Convert.ToInt32(row[sourceIdColumn]),
                            DisplayName = GetParticipantDisplayName(row),
                            Details = BuildParticipantDetails(row),
                            AddButtonText = IsPlayerMode ? "Добавить" : "Добавить команду"
                        })
                        .OrderBy(item => item.DisplayName)
                        .ToList();

                ApplyFilter();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось загрузить список " + GetParticipantsLabelGenitive() + ": " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
                _registeredParticipants = new List<RegisteredParticipantViewModel>();
                _allAvailableParticipants = new List<AvailableParticipantViewModel>();
                ApplyFilter();
            }
        }

        private void ApplyFilter()
        {
            string query = SearchTextBox.Text == null ? string.Empty : SearchTextBox.Text.Trim();
            List<RegisteredParticipantViewModel> filteredRegistered = string.IsNullOrWhiteSpace(query)
                ? _registeredParticipants
                : _registeredParticipants
                    .Where(item =>
                        Contains(item.DisplayName, query) ||
                        Contains(item.Details, query) ||
                        Contains(item.SeedText, query))
                    .ToList();

            List<AvailableParticipantViewModel> filteredAvailable = string.IsNullOrWhiteSpace(query)
                ? _allAvailableParticipants
                : _allAvailableParticipants
                    .Where(item =>
                        Contains(item.DisplayName, query) ||
                        Contains(item.Details, query))
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

        private void AddParticipant_Click(object sender, RoutedEventArgs e)
        {
            AvailableParticipantViewModel participant = (sender as FrameworkElement)?.DataContext as AvailableParticipantViewModel;
            if (participant == null)
            {
                return;
            }

            try
            {
                Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["TournamentID"] = _card.TournamentId,
                    ["Seed"] = GetNextSeed(_card.TournamentId)
                };
                values[IsPlayerMode ? "PlayerID" : "TeamID"] = participant.ParticipantId;

                EntityEditContext context = new EntityEditContext(true, values, null, _database);
                EntityValidationResult validation = _participantsDefinition.SaveValidator == null
                    ? EntityValidationResult.Success()
                    : _participantsDefinition.SaveValidator(context);

                if (!validation.IsValid)
                {
                    MessageBox.Show(validation.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
                    LoadParticipants();
                    return;
                }

                _database.Insert("TournamentParticipants", values);
                HasChanges = true;
                LoadParticipants();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось добавить " + GetParticipantNameAccusative() + ": " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
                LoadParticipants();
            }
        }

        private void RemoveParticipant_Click(object sender, RoutedEventArgs e)
        {
            RegisteredParticipantViewModel participant = (sender as FrameworkElement)?.DataContext as RegisteredParticipantViewModel;
            if (participant == null)
            {
                return;
            }

            MessageBoxResult result = MessageBox.Show(
                "Удалить " + GetParticipantNameAccusative() + " \"" + participant.DisplayName + "\" из турнира \"" + _card.TournamentName + "\"?",
                "Tournaments WPF",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                _database.Delete("TournamentParticipants", _participantsDefinition.KeyColumns, participant.OriginalValues);
                HasChanges = true;
                LoadParticipants();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось удалить " + GetParticipantNameAccusative() + ": " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
                LoadParticipants();
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
            if (!string.IsNullOrWhiteSpace(query) && _registeredParticipants.Count > 0)
            {
                return "По текущему запросу " + GetParticipantsLabelPlural() + " не найдены.";
            }

            return "В турнире пока нет добавленных " + GetParticipantsLabelGenitive() + ".";
        }

        private string BuildAvailableEmptyText(string query)
        {
            if (_card.MaxParticipants > 0 && _registeredParticipants.Count >= _card.MaxParticipants)
            {
                return "Лимит турнира достигнут. Чтобы добавить нового " + GetParticipantNameAccusative() + ", сначала удалите одного из текущих участников.";
            }

            if (!string.IsNullOrWhiteSpace(query) && _allAvailableParticipants.Count > 0)
            {
                return "По текущему запросу свободные " + GetParticipantsLabelPlural() + " не найдены.";
            }

            return "Свободных " + GetParticipantsLabelGenitive() + " для добавления не осталось.";
        }

        private string BuildSummaryText(int visibleRegisteredCount, int visibleAvailableCount)
        {
            string participantsLabel = IsPlayerMode ? "игроков" : "команд";
            if (_card.MaxParticipants > 0)
            {
                return "Участников в турнире: " + visibleRegisteredCount + " " + participantsLabel + ". Доступно для добавления: " + visibleAvailableCount + ". Лимит: " + _card.MaxParticipants + ".";
            }

            return "Участников в турнире: " + visibleRegisteredCount + " " + participantsLabel + ". Доступно для добавления: " + visibleAvailableCount + ".";
        }

        private string BuildParticipantDetails(DataRow row)
        {
            if (row == null)
            {
                return IsPlayerMode ? "Игрок без дополнительных данных" : "Команда без дополнительных данных";
            }

            List<string> parts = new List<string>();

            if (IsPlayerMode && row.Table.Columns.Contains("RealName") && row["RealName"] != DBNull.Value)
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

            if (!IsPlayerMode && row.Table.Columns.Contains("CoachName") && row["CoachName"] != DBNull.Value)
            {
                string coachName = Convert.ToString(row["CoachName"]);
                if (!string.IsNullOrWhiteSpace(coachName))
                {
                    parts.Add("Тренер: " + coachName);
                }
            }

            return parts.Count == 0
                ? (IsPlayerMode ? "Игрок без дополнительных данных" : "Команда без дополнительных данных")
                : string.Join(" | ", parts);
        }

        private static bool Contains(string source, string query)
        {
            return !string.IsNullOrWhiteSpace(source) &&
                   !string.IsNullOrWhiteSpace(query) &&
                   source.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private RegisteredParticipantViewModel BuildRegisteredParticipant(DataRow participantRow, IReadOnlyDictionary<int, DataRow> participantsById)
        {
            string participantColumn = IsPlayerMode ? "PlayerID" : "TeamID";
            int participantId = Convert.ToInt32(participantRow[participantColumn]);
            participantsById.TryGetValue(participantId, out DataRow participantRowData);

            int? seed = participantRow.Table.Columns.Contains("Seed") && participantRow["Seed"] != DBNull.Value
                ? (int?)Convert.ToInt32(participantRow["Seed"])
                : null;

            return new RegisteredParticipantViewModel
            {
                ParticipationId = Convert.ToInt32(participantRow["ParticipationID"]),
                ParticipantId = participantId,
                DisplayName = GetParticipantDisplayName(participantRowData, participantId),
                Details = BuildParticipantDetails(participantRowData),
                Seed = seed,
                RemoveToolTip = "Удалить " + GetParticipantNameAccusative() + " из турнира",
                OriginalValues = ToDictionary(participantRow)
            };
        }

        private string GetParticipantDisplayName(DataRow row, int fallbackId = 0)
        {
            if (row == null)
            {
                return (IsPlayerMode ? "Игрок #" : "Команда #") + fallbackId;
            }

            string preferredColumn = IsPlayerMode ? "Nickname" : "TeamName";
            if (row.Table.Columns.Contains(preferredColumn) && row[preferredColumn] != DBNull.Value)
            {
                string value = Convert.ToString(row[preferredColumn]);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return (IsPlayerMode ? "Игрок #" : "Команда #") + fallbackId;
        }

        private string GetParticipantNameAccusative()
        {
            return IsPlayerMode ? "игрока" : "команду";
        }

        private string GetParticipantsLabelPlural()
        {
            return IsPlayerMode ? "игроки" : "команды";
        }

        private string GetParticipantsLabelGenitive()
        {
            return IsPlayerMode ? "игроков" : "команд";
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

        private sealed class AvailableParticipantViewModel
        {
            public int ParticipantId { get; set; }

            public string DisplayName { get; set; }

            public string Details { get; set; }

            public string AddButtonText { get; set; }
        }

        private sealed class RegisteredParticipantViewModel
        {
            public int ParticipationId { get; set; }

            public int ParticipantId { get; set; }

            public string DisplayName { get; set; }

            public string Details { get; set; }

            public int? Seed { get; set; }

            public string RemoveToolTip { get; set; }

            public string SeedText
            {
                get { return Seed.HasValue ? "Seed " + Seed.Value : "Без seed"; }
            }

            public IDictionary<string, object> OriginalValues { get; set; }
        }
    }
}
