using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Tournaments.WPF.Models;
using Tournaments.WPF.Services;

namespace Tournaments.WPF.Views
{
    public partial class TeamCatalogPage : UserControl
    {
        private readonly DatabaseService _database;
        private readonly EntityCrudService _crud;
        private readonly UserRole _currentRole;
        private readonly EntityDefinition _teamsDefinition;
        private readonly EntityDefinition _teamPlayersDefinition;
        private List<TeamCardViewModel> _allCards = new List<TeamCardViewModel>();

        public TeamCatalogPage(DatabaseService database, EntityCrudService crud, UserRole currentRole)
        {
            InitializeComponent();
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _crud = crud ?? throw new ArgumentNullException(nameof(crud));
            _currentRole = currentRole;
            _teamsDefinition = _database.GetEffectiveDefinition(EntityRegistry.All.First(definition => string.Equals(definition.TableName, "Teams", StringComparison.OrdinalIgnoreCase)));
            _teamPlayersDefinition = _database.GetEffectiveDefinition(EntityRegistry.All.First(definition => string.Equals(definition.TableName, "TeamPlayers", StringComparison.OrdinalIgnoreCase)));

            CreateButton.Visibility = CanManageData ? Visibility.Visible : Visibility.Collapsed;
            SubtitleText.Text = BuildSubtitle();
            Loaded += TeamCatalogPage_Loaded;
        }

        private bool CanManageData
        {
            get { return AccessPolicy.CanManageData(_currentRole); }
        }

        private void TeamCatalogPage_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= TeamCatalogPage_Loaded;
            LoadCards();
        }

        private void LoadCards()
        {
            try
            {
                DataTable teams = _database.GetTable("Teams");
                DataTable teamPlayers = _database.GetTable("TeamPlayers");

                Dictionary<int, int> activePlayerCounts = teamPlayers.Rows
                    .Cast<DataRow>()
                    .Where(row =>
                        row.Table.Columns.Contains("TeamID") &&
                        row["TeamID"] != DBNull.Value &&
                        IsActiveMembership(row))
                    .GroupBy(row => Convert.ToInt32(row["TeamID"]))
                    .ToDictionary(group => group.Key, group => group.Count());

                _allCards = teams.Rows
                    .Cast<DataRow>()
                    .OrderBy(row => GetString(row, "TeamName"))
                    .Select(row => BuildCard(row, activePlayerCounts))
                    .ToList();

                ApplyFilter();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось загрузить команды: " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private TeamCardViewModel BuildCard(DataRow row, IReadOnlyDictionary<int, int> activePlayerCounts)
        {
            int teamId = Convert.ToInt32(row["TeamID"]);
            int activePlayers = activePlayerCounts.TryGetValue(teamId, out int count) ? count : 0;
            bool canManage = CanManageData;

            return new TeamCardViewModel
            {
                TeamId = teamId,
                TeamName = GetString(row, "TeamName", "Команда #" + teamId),
                Country = GetString(row, "Country", "Online"),
                CoachName = GetString(row, "CoachName", "Не указан"),
                FoundedDate = GetDate(row, "FoundedDate"),
                FoundedDateText = GetDate(row, "FoundedDate").HasValue
                    ? GetDate(row, "FoundedDate").Value.ToString("dd.MM.yyyy", CultureInfo.CurrentCulture)
                    : "Не указана",
                ActivePlayers = activePlayers,
                ParticipantCountText = activePlayers + " " + GetPlayersWord(activePlayers),
                CanManageData = canManage,
                ManageActionsToolTip = canManage ? string.Empty : "Действие доступно только администратору.",
                OriginalValues = ToDictionary(row)
            };
        }

        private void ApplyFilter()
        {
            string query = SearchTextBox.Text == null ? string.Empty : SearchTextBox.Text.Trim();
            List<TeamCardViewModel> filtered = string.IsNullOrWhiteSpace(query)
                ? _allCards
                : _allCards
                    .Where(card =>
                        Contains(card.TeamName, query) ||
                        Contains(card.Country, query) ||
                        Contains(card.CoachName, query))
                    .ToList();

            CardsItemsControl.ItemsSource = filtered;
            EmptyStatePanel.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            CardsScrollViewer.Visibility = filtered.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            SummaryText.Text = "Показано команд: " + filtered.Count + " из " + _allCards.Count;
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadCards();
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            if (!CanManageData)
            {
                return;
            }

            try
            {
                TeamEditWindow window = new TeamEditWindow
                {
                    Owner = Window.GetWindow(this)
                };

                if (window.ShowDialog() != true)
                {
                    return;
                }

                Dictionary<string, object> values = new Dictionary<string, object>(window.ResultValues, StringComparer.OrdinalIgnoreCase);
                ValidateTeamSave(values, null, true);
                _crud.Insert(_teamsDefinition, values);
                LoadCards();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось создать команду: " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ViewParticipants_Click(object sender, RoutedEventArgs e)
        {
            TeamCardViewModel card = GetCard(sender);
            if (card == null)
            {
                return;
            }

            TeamParticipantsWindow window = new TeamParticipantsWindow(_database, _teamPlayersDefinition, card, false)
            {
                Owner = Window.GetWindow(this)
            };
            window.ShowDialog();
        }

        private void AddParticipants_Click(object sender, RoutedEventArgs e)
        {
            if (!CanManageData)
            {
                return;
            }

            TeamCardViewModel card = GetCard(sender);
            if (card == null)
            {
                return;
            }

            TeamParticipantsWindow window = new TeamParticipantsWindow(_database, _teamPlayersDefinition, card, true)
            {
                Owner = Window.GetWindow(this)
            };

            window.ShowDialog();
            if (window.HasChanges)
            {
                LoadCards();
            }
        }

        private void DeleteTeam_Click(object sender, RoutedEventArgs e)
        {
            if (!CanManageData)
            {
                return;
            }

            TeamCardViewModel card = GetCard(sender);
            if (card == null)
            {
                return;
            }

            try
            {
                EntityEditContext context = new EntityEditContext(false, card.OriginalValues, card.OriginalValues, _database);
                EntityValidationResult validation = _teamsDefinition.DeleteValidator == null
                    ? EntityValidationResult.Success()
                    : _teamsDefinition.DeleteValidator(context);

                if (validation.IsValid)
                {
                    if (MessageBox.Show("Удалить команду \"" + card.TeamName + "\"?", "Tournaments WPF", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    {
                        return;
                    }

                    _crud.Delete(_teamsDefinition, card.OriginalValues);
                    LoadCards();
                    return;
                }

                IReadOnlyList<string> dependencyLines = _database.GetCascadeDependencyLines("Teams", card.OriginalValues);
                if (dependencyLines.Count == 0)
                {
                    MessageBox.Show(validation.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!ConfirmCascadeDelete("У выбранной команды есть связанные данные:", dependencyLines, "Удалить команду вместе со всеми этими данными?"))
                {
                    return;
                }

                _database.DeleteCascade("Teams", _teamsDefinition.KeyColumns, card.OriginalValues);
                LoadCards();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось удалить команду: " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RenameTeam_Click(object sender, RoutedEventArgs e)
        {
            if (!CanManageData)
            {
                return;
            }

            TeamCardViewModel card = GetCard(sender);
            if (card == null)
            {
                return;
            }

            try
            {
                TeamRenameWindow window = new TeamRenameWindow(card.TeamName)
                {
                    Owner = Window.GetWindow(this)
                };

                if (window.ShowDialog() != true)
                {
                    return;
                }

                Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                {
                    ["TeamName"] = window.TeamName
                };

                ValidateTeamSave(values, card.OriginalValues, false);
                _crud.Update(_teamsDefinition, values, card.OriginalValues);
                LoadCards();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось изменить название команды: " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ValidateTeamSave(IDictionary<string, object> values, IDictionary<string, object> originalValues, bool isInsert)
        {
            EntityEditContext context = new EntityEditContext(isInsert, values, originalValues, _database);
            EntityValidationResult validation = _teamsDefinition.SaveValidator == null
                ? EntityValidationResult.Success()
                : _teamsDefinition.SaveValidator(context);

            if (!validation.IsValid)
            {
                throw new InvalidOperationException(validation.Message);
            }
        }

        private string BuildSubtitle()
        {
            switch (_currentRole)
            {
                case UserRole.Administrator:
                    return "Карточки команд с быстрым управлением составом, удалением и переименованием.";
                default:
                    return "Просматривайте команды и их участников в карточном формате.";
            }
        }

        private static TeamCardViewModel GetCard(object sender)
        {
            return (sender as FrameworkElement)?.DataContext as TeamCardViewModel;
        }

        private static bool ConfirmCascadeDelete(string header, IEnumerable<string> dependencyLines, string question)
        {
            List<string> lines = dependencyLines == null ? new List<string>() : dependencyLines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
            if (lines.Count == 0)
            {
                return false;
            }

            StringBuilder message = new StringBuilder();
            message.AppendLine(header);
            foreach (string line in lines)
            {
                message.AppendLine("- " + line);
            }

            message.AppendLine();
            message.Append(question);
            return MessageBox.Show(message.ToString(), "Tournaments WPF", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }

        private static bool IsActiveMembership(DataRow row)
        {
            if (row == null || !row.Table.Columns.Contains("IsActive") || row["IsActive"] == DBNull.Value)
            {
                return true;
            }

            return Convert.ToBoolean(row["IsActive"]);
        }

        private static string GetString(DataRow row, string columnName, string fallback = "")
        {
            if (row == null || !row.Table.Columns.Contains(columnName) || row[columnName] == DBNull.Value)
            {
                return fallback;
            }

            string value = Convert.ToString(row[columnName]);
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static DateTime? GetDate(DataRow row, string columnName)
        {
            if (row == null || !row.Table.Columns.Contains(columnName) || row[columnName] == DBNull.Value)
            {
                return null;
            }

            return Convert.ToDateTime(row[columnName]).Date;
        }

        private static bool Contains(string source, string query)
        {
            return !string.IsNullOrWhiteSpace(source) &&
                   !string.IsNullOrWhiteSpace(query) &&
                   source.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private static string GetPlayersWord(int count)
        {
            int value = Math.Abs(count) % 100;
            int lastDigit = value % 10;
            if (value > 10 && value < 20)
            {
                return "участников";
            }

            if (lastDigit == 1)
            {
                return "участник";
            }

            if (lastDigit >= 2 && lastDigit <= 4)
            {
                return "участника";
            }

            return "участников";
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
    }
}
