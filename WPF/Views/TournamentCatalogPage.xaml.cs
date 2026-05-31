using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tournaments.WPF.Models;
using Tournaments.WPF.Services;

namespace Tournaments.WPF.Views
{
    public partial class TournamentCatalogPage : UserControl
    {
        private readonly DatabaseService _database;
        private readonly EntityCrudService _crud;
        private readonly UserRole _currentRole;
        private readonly string _currentLogin;
        private readonly TournamentPreviewStore _previewStore = new TournamentPreviewStore();
        private readonly EntityDefinition _tournamentsDefinition;
        private readonly EntityDefinition _participantsDefinition;
        private List<TournamentCardViewModel> _allCards = new List<TournamentCardViewModel>();
        private int? _currentPlayerId;

        private bool CanRegisterAsPlayer
        {
            get { return _currentRole == UserRole.Player || _currentRole == UserRole.Organizer; }
        }

        public TournamentCatalogPage(DatabaseService database, EntityCrudService crud, string currentLogin, UserRole currentRole)
        {
            InitializeComponent();
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _crud = crud ?? throw new ArgumentNullException(nameof(crud));
            _currentLogin = currentLogin;
            _currentRole = currentRole;
            _tournamentsDefinition = _database.GetEffectiveDefinition(EntityRegistry.All.First(definition => string.Equals(definition.TableName, "Tournaments", StringComparison.OrdinalIgnoreCase)));
            _participantsDefinition = _database.GetEffectiveDefinition(EntityRegistry.All.First(definition => string.Equals(definition.TableName, "TournamentParticipants", StringComparison.OrdinalIgnoreCase)));

            CreateButton.Visibility = AccessPolicy.CanCreateTournaments(_currentRole) ? Visibility.Visible : Visibility.Collapsed;
            SubtitleText.Text = BuildSubtitle();
            Loaded += TournamentCatalogPage_Loaded;
        }

        private void TournamentCatalogPage_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= TournamentCatalogPage_Loaded;
            LoadCards();
        }

        private void LoadCards()
        {
            try
            {
                DataTable tournaments = _database.GetTable("Tournaments");
                DataTable participants = _database.GetTable("TournamentParticipants");
                DataTable games = _database.GetTable("GameTitles");

                _currentPlayerId = ResolveCurrentPlayerId();

                Dictionary<int, string> gameNames = games.Rows
                    .Cast<DataRow>()
                    .Where(row => row["GameID"] != DBNull.Value)
                    .GroupBy(row => Convert.ToInt32(row["GameID"]))
                    .ToDictionary(group => group.Key, group => Convert.ToString(group.First()["GameName"]));

                Dictionary<int, int> participantCounts = participants.Rows
                    .Cast<DataRow>()
                    .Where(row => row["TournamentID"] != DBNull.Value)
                    .GroupBy(row => Convert.ToInt32(row["TournamentID"]))
                    .ToDictionary(group => group.Key, group => group.Count());

                HashSet<int> registeredTournamentIds = new HashSet<int>();
                if (_currentPlayerId.HasValue && participants.Columns.Contains("PlayerID"))
                {
                    registeredTournamentIds = new HashSet<int>(
                        participants.Rows
                            .Cast<DataRow>()
                            .Where(row =>
                                row["TournamentID"] != DBNull.Value &&
                                row["PlayerID"] != DBNull.Value &&
                                Convert.ToInt32(row["PlayerID"]) == _currentPlayerId.Value)
                            .Select(row => Convert.ToInt32(row["TournamentID"])));
                }

                _allCards = tournaments.Rows
                    .Cast<DataRow>()
                    .OrderBy(row => row["StartDate"] == DBNull.Value ? DateTime.MaxValue : Convert.ToDateTime(row["StartDate"]))
                    .ThenBy(row => Convert.ToString(row["TournamentName"]))
                    .Select(row => BuildCard(row, gameNames, participantCounts, registeredTournamentIds))
                    .ToList();

                ApplyFilter();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось загрузить турниры: " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private TournamentCardViewModel BuildCard(
            DataRow row,
            IReadOnlyDictionary<int, string> gameNames,
            IReadOnlyDictionary<int, int> participantCounts,
            ISet<int> registeredTournamentIds)
        {
            int tournamentId = Convert.ToInt32(row["TournamentID"]);
            string tournamentName = Convert.ToString(row["TournamentName"]);
            int gameId = row.Table.Columns.Contains("GameID") && row["GameID"] != DBNull.Value ? Convert.ToInt32(row["GameID"]) : 0;
            string gameName = gameNames.TryGetValue(gameId, out string resolvedGameName) ? resolvedGameName : "Неизвестная игра";
            string participantMode = GetParticipantMode(row);
            int registeredParticipants = participantCounts.TryGetValue(tournamentId, out int count) ? count : 0;
            int maxParticipants = row.Table.Columns.Contains("MaxTeams") && row["MaxTeams"] != DBNull.Value ? Convert.ToInt32(row["MaxTeams"]) : 0;
            bool isRegistered = registeredTournamentIds.Contains(tournamentId);
            string previewPath = _previewStore.GetPreviewPath(tournamentId);
            ImageSource previewImage = LoadPreviewImage(previewPath);

            TournamentCardViewModel card = new TournamentCardViewModel
            {
                TournamentId = tournamentId,
                TournamentName = tournamentName,
                GameName = gameName,
                FormatType = row.Table.Columns.Contains("FormatType") ? Convert.ToString(row["FormatType"]) : string.Empty,
                FormatDisplay = FormatTournamentFormat(row.Table.Columns.Contains("FormatType") ? Convert.ToString(row["FormatType"]) : string.Empty),
                ParticipantMode = participantMode,
                ParticipantModeBadgeText = string.Equals(participantMode, "Игроки", StringComparison.CurrentCultureIgnoreCase) ? "Игроки" : "Команды",
                RegisteredParticipants = registeredParticipants,
                MaxParticipants = maxParticipants,
                ParticipantCountText = BuildParticipantCountText(participantMode, registeredParticipants, maxParticipants),
                StartDate = row.Table.Columns.Contains("StartDate") && row["StartDate"] != DBNull.Value ? Convert.ToDateTime(row["StartDate"]) : DateTime.MinValue,
                StartDateText = row.Table.Columns.Contains("StartDate") && row["StartDate"] != DBNull.Value
                    ? Convert.ToDateTime(row["StartDate"]).ToString("dd.MM.yyyy", CultureInfo.CurrentCulture)
                    : "Не указана",
                PrizePool = row.Table.Columns.Contains("PrizePool") && row["PrizePool"] != DBNull.Value ? (decimal?)Convert.ToDecimal(row["PrizePool"]) : null,
                PrizePoolText = row.Table.Columns.Contains("PrizePool") && row["PrizePool"] != DBNull.Value
                    ? Convert.ToDecimal(row["PrizePool"]).ToString("N0", CultureInfo.CurrentCulture)
                    : "Не указан",
                Organizer = row.Table.Columns.Contains("Organizer") && row["Organizer"] != DBNull.Value ? Convert.ToString(row["Organizer"]) : "Не указан",
                Location = row.Table.Columns.Contains("Location") && row["Location"] != DBNull.Value ? Convert.ToString(row["Location"]) : "Не указана",
                PreviewPlaceholderText = tournamentName,
                PreviewPath = previewPath,
                PreviewImage = previewImage,
                PreviewImageVisibility = previewImage == null ? Visibility.Collapsed : Visibility.Visible,
                PreviewPlaceholderVisibility = previewImage == null ? Visibility.Visible : Visibility.Collapsed,
                RegisterButtonVisibility = CanRegisterAsPlayer ? Visibility.Visible : Visibility.Collapsed,
                SettingsButtonVisibility = CanManageTournament(cardOrganizer: row.Table.Columns.Contains("Organizer") && row["Organizer"] != DBNull.Value ? Convert.ToString(row["Organizer"]) : null)
                    ? Visibility.Visible
                    : Visibility.Collapsed,
                AdminManageParticipantsVisibility = CanManageTournament(cardOrganizer: row.Table.Columns.Contains("Organizer") && row["Organizer"] != DBNull.Value ? Convert.ToString(row["Organizer"]) : null)
                    ? Visibility.Visible
                    : Visibility.Collapsed,
                AdminManageParticipantsButtonText = string.Equals(participantMode, "Игроки", StringComparison.CurrentCultureIgnoreCase)
                    ? "Добавить участников"
                    : "Добавить команды",
                IsRegistered = isRegistered,
                OriginalValues = ToDictionary(row)
            };

            ConfigureRegistrationState(card);
            ConfigureAdminActions(card);
            return card;
        }

        private void ConfigureRegistrationState(TournamentCardViewModel card)
        {
            if (!CanRegisterAsPlayer)
            {
                card.IsRegisterEnabled = false;
                card.RegisterButtonText = "Зарегистрироваться";
                card.RegisterButtonToolTip = string.Empty;
                return;
            }

            if (!_currentPlayerId.HasValue)
            {
                card.IsRegisterEnabled = false;
                card.RegisterButtonText = "Профиль недоступен";
                card.RegisterButtonToolTip = "Не удалось определить профиль игрока для регистрации.";
                return;
            }

            if (!string.Equals(card.ParticipantMode, "Игроки", StringComparison.CurrentCultureIgnoreCase))
            {
                card.IsRegisterEnabled = false;
                card.RegisterButtonText = "Только через команду";
                card.RegisterButtonToolTip = "Самостоятельная регистрация доступна только для турниров с участниками-игроками.";
                return;
            }

            if (card.IsRegistered)
            {
                card.IsRegisterEnabled = false;
                card.RegisterButtonText = "Вы уже в списке";
                card.RegisterButtonToolTip = "Этот профиль уже зарегистрирован в выбранном турнире.";
                return;
            }

            if (card.MaxParticipants > 0 && card.RegisteredParticipants >= card.MaxParticipants)
            {
                card.IsRegisterEnabled = false;
                card.RegisterButtonText = "Набор завершён";
                card.RegisterButtonToolTip = "В турнире уже достигнут лимит участников.";
                return;
            }

            card.IsRegisterEnabled = true;
            card.RegisterButtonText = "Зарегистрироваться";
            card.RegisterButtonToolTip = "Добавить текущий профиль игрока в список участников турнира.";
        }

        private void ConfigureAdminActions(TournamentCardViewModel card)
        {
            if (!CanManageTournament(card.Organizer))
            {
                card.IsAdminManageParticipantsEnabled = false;
                card.AdminManageParticipantsToolTip = string.Empty;
                return;
            }

            card.IsAdminManageParticipantsEnabled = true;
            card.AdminManageParticipantsToolTip = string.Equals(card.ParticipantMode, "Игроки", StringComparison.CurrentCultureIgnoreCase)
                ? "Открыть быстрый список игроков, которых можно добавить в турнир."
                : "Открыть быстрый список команд, которые можно добавить в турнир.";
        }

        private void ApplyFilter()
        {
            string query = SearchTextBox.Text == null ? string.Empty : SearchTextBox.Text.Trim();
            List<TournamentCardViewModel> filtered = string.IsNullOrWhiteSpace(query)
                ? _allCards
                : _allCards
                    .Where(card =>
                        Contains(card.TournamentName, query) ||
                        Contains(card.GameName, query) ||
                        Contains(card.FormatDisplay, query))
                    .ToList();

            CardsItemsControl.ItemsSource = filtered;
            EmptyStatePanel.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            CardsScrollViewer.Visibility = filtered.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            SummaryText.Text = "Показано турниров: " + filtered.Count + " из " + _allCards.Count;
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
            if (!AccessPolicy.CanCreateTournaments(_currentRole))
            {
                return;
            }

            try
            {
                EditEntityWindow window = new EditEntityWindow(GetTournamentWindowDefinition(), null, _database)
                {
                    Owner = Window.GetWindow(this)
                };

                if (window.ShowDialog() != true)
                {
                    return;
                }

                Dictionary<string, object> values = new Dictionary<string, object>(window.ResultValues, StringComparer.OrdinalIgnoreCase);
                ApplyTournamentDefaults(values, null, true);
                ValidateTournamentSave(values, null, true);

                _crud.Insert(_tournamentsDefinition, values);
                LoadCards();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось создать турнир: " + NormalizeTournamentErrorMessage(ex.Message), "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Register_Click(object sender, RoutedEventArgs e)
        {
            TournamentCardViewModel card = GetCard(sender);
            if (card == null)
            {
                return;
            }

            try
            {
                RegisterCurrentPlayer(card);
                LoadCards();
                MessageBox.Show("Регистрация выполнена.", "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось зарегистрироваться: " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Details_Click(object sender, RoutedEventArgs e)
        {
            TournamentCardViewModel card = GetCard(sender);
            if (card == null)
            {
                return;
            }

            TournamentDetailsWindow window = new TournamentDetailsWindow(_database, card)
            {
                Owner = Window.GetWindow(this)
            };
            window.ShowDialog();
        }

        private void AddParticipants_Click(object sender, RoutedEventArgs e)
        {
            TournamentCardViewModel card = GetCard(sender);
            if (card == null)
            {
                return;
            }

            if (!CanManageTournament(card.Organizer))
            {
                return;
            }

            TournamentParticipantPickerWindow window = new TournamentParticipantPickerWindow(_database, _participantsDefinition, card)
            {
                Owner = Window.GetWindow(this)
            };

            window.ShowDialog();
            if (window.HasChanges)
            {
                LoadCards();
            }
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            TournamentCardViewModel card = GetCard(sender);
            Button button = sender as Button;
            if (card == null || button == null)
            {
                return;
            }

            ContextMenu menu = BuildSettingsMenu(card);
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }

        private ContextMenu BuildSettingsMenu(TournamentCardViewModel card)
        {
            ContextMenu menu = new ContextMenu();

            MenuItem editItem = new MenuItem { Header = "Изменить турнир" };
            editItem.Click += (sender, e) => EditTournament(card);
            menu.Items.Add(editItem);

            MenuItem participantsItem = new MenuItem { Header = "Список участников" };
            participantsItem.Click += (sender, e) => OpenParticipantsPage();
            menu.Items.Add(participantsItem);

            MenuItem choosePreviewItem = new MenuItem { Header = "Выбрать превью" };
            choosePreviewItem.Click += (sender, e) => ChoosePreview(card);
            menu.Items.Add(choosePreviewItem);

            MenuItem clearPreviewItem = new MenuItem { Header = "Сбросить превью" };
            clearPreviewItem.IsEnabled = !string.IsNullOrWhiteSpace(card.PreviewPath);
            clearPreviewItem.Click += (sender, e) => ClearPreview(card);
            menu.Items.Add(clearPreviewItem);

            MenuItem exportItem = new MenuItem { Header = "Экспорт данных" };
            exportItem.Click += (sender, e) => ExportTournament(card);
            menu.Items.Add(exportItem);

            menu.Items.Add(new Separator());

            MenuItem deleteItem = new MenuItem { Header = "Удалить турнир" };
            deleteItem.Click += (sender, e) => DeleteTournament(card);
            if (_currentRole == UserRole.Administrator)
            {
                menu.Items.Add(deleteItem);
            }

            return menu;
        }

        private void EditTournament(TournamentCardViewModel card)
        {
            if (card == null || !CanManageTournament(card.Organizer))
            {
                return;
            }

            try
            {
                EditEntityWindow window = new EditEntityWindow(GetTournamentWindowDefinition(), card.OriginalValues, _database)
                {
                    Owner = Window.GetWindow(this)
                };

                if (window.ShowDialog() != true)
                {
                    return;
                }

                Dictionary<string, object> values = new Dictionary<string, object>(window.ResultValues, StringComparer.OrdinalIgnoreCase);
                ApplyTournamentDefaults(values, card.OriginalValues, false);
                ValidateTournamentSave(values, card.OriginalValues, false);

                _crud.Update(_tournamentsDefinition, values, card.OriginalValues);
                LoadCards();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось обновить турнир: " + NormalizeTournamentErrorMessage(ex.Message), "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void DeleteTournament(TournamentCardViewModel card)
        {
            if (_currentRole != UserRole.Administrator)
            {
                return;
            }

            try
            {
                EntityEditContext context = new EntityEditContext(false, card.OriginalValues, card.OriginalValues, _database);
                EntityValidationResult validation = _tournamentsDefinition.DeleteValidator == null
                    ? EntityValidationResult.Success()
                    : _tournamentsDefinition.DeleteValidator(context);

                bool deleted = false;
                if (validation.IsValid)
                {
                    if (MessageBox.Show("Удалить выбранный турнир?", "Tournaments WPF", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    {
                        return;
                    }

                    _crud.Delete(_tournamentsDefinition, card.OriginalValues);
                    deleted = true;
                }
                else
                {
                    IReadOnlyList<string> dependencyLines = _database.GetCascadeDependencyLines("Tournaments", card.OriginalValues);
                    if (dependencyLines.Count == 0)
                    {
                        MessageBox.Show(validation.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (!ConfirmCascadeDelete("У выбранного турнира есть связанные данные:", dependencyLines, "Удалить турнир вместе со всеми этими данными?"))
                    {
                        return;
                    }

                    _database.DeleteCascade("Tournaments", _tournamentsDefinition.KeyColumns, card.OriginalValues);
                    deleted = true;
                }

                if (deleted)
                {
                    _previewStore.RemovePreviewPath(card.TournamentId);
                    LoadCards();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось удалить турнир: " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ChoosePreview(TournamentCardViewModel card)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*",
                Title = "Выберите изображение турнира"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                _previewStore.SetPreviewPath(card.TournamentId, dialog.FileName);
                LoadCards();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось сохранить превью: " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ClearPreview(TournamentCardViewModel card)
        {
            try
            {
                _previewStore.RemovePreviewPath(card.TournamentId);
                LoadCards();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось сбросить превью: " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ExportTournament(TournamentCardViewModel card)
        {
            if (card == null)
            {
                return;
            }

            SaveFileDialog dialog = new SaveFileDialog
            {
                AddExtension = true,
                DefaultExt = ".json",
                Filter = "JSON files (*.json)|*.json",
                FileName = BuildTournamentExportFileName(card),
                Title = "Экспорт данных турнира"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                TournamentExportResult result = TournamentExportService.ExportToFile(_database, card.TournamentId, dialog.FileName, card.PreviewPath);
                MessageBox.Show(
                    "Экспорт данных турнира завершён. Таблиц: " + result.TableCount + ", записей: " + result.RowCount + ".",
                    "Tournaments WPF",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось экспортировать данные турнира: " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenParticipantsPage()
        {
            MainWindow mainWindow = Window.GetWindow(this) as MainWindow;
            if (mainWindow != null && mainWindow.OpenEntityPage("TournamentParticipants"))
            {
                return;
            }

            MessageBox.Show("Не удалось открыть страницу \"Участники турниров\".", "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void RegisterCurrentPlayer(TournamentCardViewModel card)
        {
            if (!CanRegisterAsPlayer)
            {
                throw new InvalidOperationException("Регистрация доступна только игрокам и организаторам.");
            }

            if (!_currentPlayerId.HasValue)
            {
                throw new InvalidOperationException("Не удалось определить текущий профиль игрока.");
            }

            if (!card.IsRegisterEnabled)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(card.RegisterButtonToolTip) ? "Регистрация сейчас недоступна." : card.RegisterButtonToolTip);
            }

            int nextSeed = GetNextSeed(card.TournamentId);
            Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["TournamentID"] = card.TournamentId,
                ["PlayerID"] = _currentPlayerId.Value,
                ["Seed"] = nextSeed
            };

            EntityEditContext context = new EntityEditContext(true, values, null, _database);
            EntityValidationResult validation = _participantsDefinition.SaveValidator == null
                ? EntityValidationResult.Success()
                : _participantsDefinition.SaveValidator(context);

            if (!validation.IsValid)
            {
                throw new InvalidOperationException(validation.Message);
            }

            _database.Insert("TournamentParticipants", values);
        }

        private int? ResolveCurrentPlayerId()
        {
            if (!CanRegisterAsPlayer || string.IsNullOrWhiteSpace(_currentLogin))
            {
                return null;
            }

            DataTable players = _database.GetTable("Players");
            DataRow row = players.Rows
                .Cast<DataRow>()
                .FirstOrDefault(item =>
                    item.Table.Columns.Contains("Nickname") &&
                    item["Nickname"] != DBNull.Value &&
                    string.Equals(Convert.ToString(item["Nickname"]), _currentLogin, StringComparison.OrdinalIgnoreCase));

            return row == null || row["PlayerID"] == DBNull.Value ? (int?)null : Convert.ToInt32(row["PlayerID"]);
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

        private void ValidateTournamentSave(IDictionary<string, object> values, IDictionary<string, object> originalValues, bool isInsert)
        {
            EntityEditContext context = new EntityEditContext(isInsert, values, originalValues, _database);
            EntityValidationResult validation = _tournamentsDefinition.SaveValidator == null
                ? EntityValidationResult.Success()
                : _tournamentsDefinition.SaveValidator(context);

            if (!validation.IsValid)
            {
                throw new InvalidOperationException(validation.Message);
            }
        }

        private void ApplyTournamentDefaults(IDictionary<string, object> values, IDictionary<string, object> originalValues, bool isInsert)
        {
            if (values == null)
            {
                return;
            }

            if (isInsert && !string.IsNullOrWhiteSpace(_currentLogin))
            {
                values["Organizer"] = _currentLogin;
            }
            else if (_currentRole != UserRole.Administrator && originalValues != null && originalValues.ContainsKey("Organizer"))
            {
                values["Organizer"] = originalValues["Organizer"];
            }
            else if ((!values.ContainsKey("Organizer") || values["Organizer"] == null || string.IsNullOrWhiteSpace(Convert.ToString(values["Organizer"]))) &&
                !string.IsNullOrWhiteSpace(_currentLogin))
            {
                values["Organizer"] = _currentLogin;
            }

            if (!values.ContainsKey("ParticipantMode") || values["ParticipantMode"] == null || string.IsNullOrWhiteSpace(Convert.ToString(values["ParticipantMode"])))
            {
                values["ParticipantMode"] = originalValues != null && originalValues.ContainsKey("ParticipantMode") && originalValues["ParticipantMode"] != null
                    ? originalValues["ParticipantMode"]
                    : "Команды";
            }
        }

        private static TournamentCardViewModel GetCard(object sender)
        {
            return (sender as FrameworkElement)?.DataContext as TournamentCardViewModel;
        }

        private bool CanManageTournament(string cardOrganizer)
        {
            if (_currentRole == UserRole.Administrator)
            {
                return true;
            }

            return _currentRole == UserRole.Organizer &&
                   !string.IsNullOrWhiteSpace(_currentLogin) &&
                   !string.IsNullOrWhiteSpace(cardOrganizer) &&
                   string.Equals(cardOrganizer, _currentLogin, StringComparison.OrdinalIgnoreCase);
        }

        private EntityDefinition GetTournamentWindowDefinition()
        {
            if (_currentRole == UserRole.Administrator)
            {
                return _tournamentsDefinition;
            }

            List<FieldDefinition> fields = _tournamentsDefinition.Fields
                .Select(field => CloneField(field, string.Equals(field.Name, "Organizer", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            EntityDefinition definition = new EntityDefinition(_tournamentsDefinition.TableName, _tournamentsDefinition.Title, _tournamentsDefinition.KeyColumns, fields);
            definition.SaveValidator = _tournamentsDefinition.SaveValidator;
            definition.DeleteValidator = _tournamentsDefinition.DeleteValidator;
            return definition;
        }

        private static FieldDefinition CloneField(FieldDefinition source, bool forceReadOnly)
        {
            FieldDefinition clone = new FieldDefinition(source.Name, source.Label, source.Type)
            {
                IsRequired = source.IsRequired,
                IsIdentity = source.IsIdentity,
                IsReadOnly = source.IsReadOnly || forceReadOnly,
                IsKey = source.IsKey,
                LookupTableName = source.LookupTableName,
                LookupColumnName = source.LookupColumnName,
                LookupDisplayColumnName = source.LookupDisplayColumnName
            };

            foreach (string value in source.AllowedValues)
            {
                clone.AllowedValues.Add(value);
            }

            return clone;
        }

        private static bool ConfirmCascadeDelete(string header, IEnumerable<string> dependencyLines, string question)
        {
            List<string> lines = dependencyLines == null ? new List<string>() : dependencyLines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
            if (lines.Count == 0)
            {
                return false;
            }

            System.Text.StringBuilder message = new System.Text.StringBuilder();
            message.AppendLine(header);
            foreach (string line in lines)
            {
                message.AppendLine("- " + line);
            }

            message.AppendLine();
            message.Append(question);
            return MessageBox.Show(message.ToString(), "Tournaments WPF", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }

        private static string BuildTournamentExportFileName(TournamentCardViewModel card)
        {
            string name = string.IsNullOrWhiteSpace(card.TournamentName) ? "tournament" : card.TournamentName.Trim();
            char[] invalidChars = Path.GetInvalidFileNameChars();
            string safeName = new string(name.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());
            if (string.IsNullOrWhiteSpace(safeName))
            {
                safeName = "tournament";
            }

            return "tournament-" + card.TournamentId.ToString(CultureInfo.InvariantCulture) + "-" + safeName + ".json";
        }

        private static bool Contains(string source, string query)
        {
            return !string.IsNullOrWhiteSpace(source) &&
                   !string.IsNullOrWhiteSpace(query) &&
                   source.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        private static string BuildParticipantCountText(string participantMode, int registeredParticipants, int maxParticipants)
        {
            string noun = string.Equals(participantMode, "Игроки", StringComparison.CurrentCultureIgnoreCase) ? "игроков" : "команд";
            if (maxParticipants <= 0)
            {
                return registeredParticipants + " " + noun;
            }

            return registeredParticipants + " / " + maxParticipants + " " + noun;
        }

        private static string FormatTournamentFormat(string formatType)
        {
            switch ((formatType ?? string.Empty).Trim())
            {
                case "Single Elimination":
                    return "Single Elimination";
                case "Double Elimination":
                    return "Double Elimination";
                case "League":
                    return "Лига";
                default:
                    return string.IsNullOrWhiteSpace(formatType) ? "Не указан" : formatType;
            }
        }

        private static string GetParticipantMode(DataRow row)
        {
            if (row == null || !row.Table.Columns.Contains("ParticipantMode") || row["ParticipantMode"] == DBNull.Value)
            {
                return "Команды";
            }

            string mode = Convert.ToString(row["ParticipantMode"]);
            return string.IsNullOrWhiteSpace(mode) ? "Команды" : mode.Trim();
        }

        private static ImageSource LoadPreviewImage(string previewPath)
        {
            if (string.IsNullOrWhiteSpace(previewPath) || !File.Exists(previewPath))
            {
                return null;
            }

            try
            {
                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                image.UriSource = new Uri(previewPath, UriKind.Absolute);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
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

        private string BuildSubtitle()
        {
            switch (_currentRole)
            {
                case UserRole.Administrator:
                    return "Галерея турниров с карточками, подробностями, настройками и быстрым управлением превью.";
                case UserRole.Organizer:
                    return "Выбирайте турниры, регистрируйтесь и управляйте турнирами, где вы указаны организатором.";
                case UserRole.Player:
                    return "Выбирайте турнир, смотрите детали и регистрируйтесь прямо с карточки.";
                default:
                    return "Просматривайте турниры в формате карточной галереи.";
            }
        }

        private static string NormalizeTournamentErrorMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return "Неизвестная ошибка.";
            }

            if (message.IndexOf("CK__Tournamen__MaxTe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("CK_Tournaments_MaxTeams", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "Текущая SQL-база данных всё ещё использует старое ограничение на MaxTeams. Для нечётного количества участников нужно обновить constraint в таблице Tournaments.";
            }

            return message;
        }
    }
}
