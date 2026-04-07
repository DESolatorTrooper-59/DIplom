using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Tournaments.WPF.Models;
using Tournaments.WPF.Services;

namespace Tournaments.WPF.Views
{
    public partial class TournamentBracketPage : UserControl
    {
        private const double ColumnWidth = 220;
        private const double ColumnGap = 52;
        private const double MatchWidth = 184;
        private const double MatchHeight = 88;
        private const double ChampionHeight = 46;
        private const double CanvasPadding = 18;
        private const double ColumnTop = 42;
        private const double MatchesTop = 98;
        private const double HeaderHeight = 28;
        private const double FirstRoundStep = 118;

        private static readonly List<StatusOption> StatusOptions = new List<StatusOption>
        {
            new StatusOption("Scheduled", "Запланирован"),
            new StatusOption("Completed", "Завершен")
        };

        private static readonly List<int> BestOfOptions = new List<int> { 1, 3, 5, 7 };

        private readonly DatabaseService _database;
        private readonly TournamentBracketService _bracketService;
        private readonly UserRole _currentRole;
        private bool _isLoaded;
        private int _currentRoundCount;
        private TournamentBracketSnapshot _currentSnapshot;
        private BracketMatchViewModel _selectedMatch;
        private List<BracketTeamOption> _teamOptions = new List<BracketTeamOption>();
        private bool _isUpdatingEditor;

        public TournamentBracketPage(DatabaseService database, UserRole currentRole)
        {
            InitializeComponent();
            _database = database;
            _bracketService = new TournamentBracketService(database);
            _currentRole = currentRole;
            Loaded += TournamentBracketPage_Loaded;

            StatusComboBox.ItemsSource = StatusOptions;
            StatusComboBox.DisplayMemberPath = "DisplayName";
            StatusComboBox.SelectedValuePath = "Value";
            BestOfComboBox.ItemsSource = BestOfOptions;
            EditorSelectionText.Text = "Матч не выбран";
            ApplyRoleAccess();
        }

        private void TournamentBracketPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isLoaded)
            {
                return;
            }

            _isLoaded = true;
            LoadTournaments(null);
        }

        private void TournamentComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoaded)
            {
                _selectedMatch = null;
                RefreshPreview();
            }
        }

        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            int? tournamentId = GetSelectedTournamentId();
            if (!tournamentId.HasValue)
            {
                MessageBox.Show("Сначала выберите турнир.", "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            RefreshPreview(_selectedMatch == null ? (int?)null : _selectedMatch.MatchId);

            BracketPreviewWindow previewWindow = new BracketPreviewWindow(_database, tournamentId.Value)
            {
                Owner = Window.GetWindow(this)
            };

            previewWindow.Show();
        }

        private void Generate_Click(object sender, RoutedEventArgs e)
        {
            if (!CanManageBracket())
            {
                return;
            }

            int? tournamentId = GetSelectedTournamentId();
            if (!tournamentId.HasValue)
            {
                MessageBox.Show("Сначала выберите турнир.", "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                int createdMatches = _bracketService.GenerateBracket(tournamentId.Value);
                RefreshPreview();
                if (_selectedMatch == null)
                {
                    SelectMatch(_currentSnapshot?.Rounds.SelectMany(round => round.Matches).FirstOrDefault(match => match.IsEditable));
                }

                string noun = _currentSnapshot != null && string.Equals(_currentSnapshot.FormatType, "League", StringComparison.OrdinalIgnoreCase)
                    ? "Расписание"
                    : "Сетка";
                MessageBox.Show(noun + " создано. В хранилище добавлено матчей: " + createdMatches + ".", "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось создать сетку: " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RefreshTournaments_Click(object sender, RoutedEventArgs e)
        {
            _selectedMatch = null;
            LoadTournaments(GetSelectedTournamentId());
        }

        private void OpenParticipants_Click(object sender, RoutedEventArgs e)
        {
            if (!CanManageBracket())
            {
                return;
            }

            MainWindow mainWindow = Window.GetWindow(this) as MainWindow;
            if (mainWindow != null && mainWindow.OpenEntityPage("TournamentParticipants"))
            {
                return;
            }

            MessageBox.Show("Не удалось открыть страницу \"Участники турниров\".", "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void SaveMatch_Click(object sender, RoutedEventArgs e)
        {
            if (!CanManageBracket())
            {
                return;
            }

            if (_selectedMatch == null)
            {
                return;
            }

            int? tournamentId = GetSelectedTournamentId();
            if (!tournamentId.HasValue)
            {
                return;
            }

            if (!TryParseNonNegativeInt(Team1ScoreTextBox.Text, out int team1Score) || !TryParseNonNegativeInt(Team2ScoreTextBox.Text, out int team2Score))
            {
                MessageBox.Show("Счет должен быть целым неотрицательным числом.", "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!(BestOfComboBox.SelectedItem is int bestOf))
            {
                MessageBox.Show("Выберите корректный формат Best Of.", "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!MatchDatePicker.SelectedDate.HasValue)
            {
                MessageBox.Show("Укажите дату матча.", "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int? team1Id = _selectedMatch.CanEditTeams ? GetSelectedTeamId(Team1ComboBox) : _selectedMatch.Team1Id;
            int? team2Id = _selectedMatch.CanEditTeams ? GetSelectedTeamId(Team2ComboBox) : _selectedMatch.Team2Id;
            if (_selectedMatch.CanEditTeams && team1Id.HasValue && team2Id.HasValue && team1Id.Value == team2Id.Value)
            {
                MessageBox.Show("В одном матче нельзя выбрать одного и того же участника дважды.", "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                BracketMatchUpdateRequest request = new BracketMatchUpdateRequest
                {
                    MatchId = _selectedMatch.MatchId,
                    Team1Id = team1Id,
                    Team2Id = team2Id,
                    WinnerTeamId = GetSelectedTeamId(WinnerComboBox),
                    Team1Score = team1Score,
                    Team2Score = team2Score,
                    BestOf = bestOf,
                    MatchDate = MatchDatePicker.SelectedDate.Value.Date,
                    Status = Convert.ToString(StatusComboBox.SelectedValue)
                };

                int selectedMatchId = _selectedMatch.MatchId;
                _bracketService.UpdateMatch(tournamentId.Value, request);
                RefreshPreview(selectedMatchId);
                EditorStateText.Text = "Изменения сохранены, сетка пересчитана.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось сохранить матч: " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CancelEdit_Click(object sender, RoutedEventArgs e)
        {
            if (!CanManageBracket())
            {
                return;
            }

            if (_selectedMatch == null)
            {
                return;
            }

            PopulateEditor(_selectedMatch);
            EditorStateText.Text = "Несохраненные изменения отменены.";
        }

        private void TeamSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingEditor || _selectedMatch == null)
            {
                return;
            }

            RefreshWinnerOptions(GetSelectedTeamId(WinnerComboBox));
        }

        private void MatchCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!CanManageBracket())
            {
                return;
            }

            if (!(sender is Border border) || !(border.Tag is BracketMatchViewModel match))
            {
                return;
            }

            SelectMatch(match);
        }

        private void LoadTournaments(int? selectedTournamentId)
        {
            List<TournamentOption> tournaments = _bracketService.GetTournaments();
            TournamentComboBox.ItemsSource = tournaments;
            TournamentComboBox.DisplayMemberPath = "DisplayName";
            TournamentComboBox.SelectedValuePath = "TournamentId";

            if (tournaments.Count == 0)
            {
                SummaryText.Text = "Нет доступных турниров для построения сетки.";
                ParticipantsList.ItemsSource = null;
                BracketCanvas.Children.Clear();
                BracketScrollViewer.Visibility = Visibility.Collapsed;
                EmptyStateText.Text = "Добавьте хотя бы один турнир, чтобы увидеть сетку.";
                EmptyStateText.Visibility = Visibility.Visible;
                _currentSnapshot = null;
                _selectedMatch = null;
                _teamOptions = new List<BracketTeamOption>();
                ClearEditor("Турнирная сетка пока недоступна.");
                return;
            }

            TournamentOption target = null;
            if (selectedTournamentId.HasValue)
            {
                target = tournaments.Find(item => item.TournamentId == selectedTournamentId.Value);
            }

            TournamentComboBox.SelectedItem = target ?? tournaments[0];
            RefreshPreview();
        }

        private void RefreshPreview(int? preferredMatchId = null)
        {
            int? tournamentId = GetSelectedTournamentId();
            if (!tournamentId.HasValue)
            {
                return;
            }

            _teamOptions = _bracketService.GetTeamOptions(tournamentId.Value);
            TournamentBracketSnapshot snapshot = _bracketService.BuildPreview(tournamentId.Value);
            _currentSnapshot = snapshot;
            _selectedMatch = ResolveSelectedMatch(snapshot, preferredMatchId);

            ParticipantsList.ItemsSource = snapshot.Participants;
            SummaryText.Text = BuildSummary(snapshot);

            bool hasEnoughParticipants = snapshot.ParticipantCount >= 2;
            BracketScrollViewer.Visibility = hasEnoughParticipants ? Visibility.Visible : Visibility.Collapsed;
            EmptyStateText.Visibility = hasEnoughParticipants ? Visibility.Collapsed : Visibility.Visible;
            EmptyStateText.Text = snapshot.ParticipantCount == 0
                ? "У выбранного турнира пока нет участников. Добавьте участников во вкладке участников турниров."
                : "Для построения формата турнира нужно минимум 2 участника.";

            if (hasEnoughParticipants)
            {
                RenderBracket(snapshot);
            }
            else
            {
                BracketCanvas.Children.Clear();
            }

            UpdateEditorState();
        }

        private void RenderBracket(TournamentBracketSnapshot snapshot)
        {
            _currentRoundCount = snapshot.Rounds.Count;
            TournamentBracketRenderer.Render(BracketCanvas, snapshot, _selectedMatch == null ? (int?)null : _selectedMatch.MatchId, MatchCard_MouseLeftButtonUp);
        }

        private void DrawRoundMatches(int roundIndex, BracketRoundViewModel round)
        {
            for (int matchIndex = 0; matchIndex < round.Matches.Count; matchIndex++)
            {
                AddElement(CreateMatchCard(round.Matches[matchIndex]), GetCardX(roundIndex), GetCardTop(roundIndex, matchIndex));
            }
        }

        private void DrawChampionCard(int roundCount, string championName)
        {
            Border card = new Border
            {
                Width = MatchWidth,
                Height = ChampionHeight,
                CornerRadius = new CornerRadius(4),
                BorderBrush = ThemeBrush("ChampionBorderBrush", new SolidColorBrush(Color.FromRgb(116, 116, 116))),
                BorderThickness = new Thickness(1),
                Background = ThemeGradient("ChampionStartBrush", "ChampionEndBrush", Color.FromRgb(245, 245, 245), Color.FromRgb(178, 178, 178)),
                Child = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(championName) ? "Будет определен" : championName,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = ThemeBrush("ChampionTextBrush", new SolidColorBrush(Color.FromRgb(31, 41, 55)))
                }
            };

            AddElement(card, GetCardX(roundCount), GetMatchCenterY(roundCount - 1, 0) - ChampionHeight / 2);
        }

        private Border CreateMatchCard(BracketMatchViewModel match)
        {
            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });

            Border topRow = CreateTeamRow(match.Team1Name, GetScoreText(match.Team1Id, match.Team1Name, match.Team1Score), true, match.WinnerTeamId.HasValue && match.Team1Id == match.WinnerTeamId);
            Border bottomRow = CreateTeamRow(match.Team2Name, GetScoreText(match.Team2Id, match.Team2Name, match.Team2Score), false, match.WinnerTeamId.HasValue && match.Team2Id == match.WinnerTeamId);
            Grid.SetRow(topRow, 0);
            Grid.SetRow(bottomRow, 2);
            grid.Children.Add(topRow);
            grid.Children.Add(bottomRow);

            StackPanel metaPanel = new StackPanel { Margin = new Thickness(8, 5, 8, 5) };
            metaPanel.Children.Add(new TextBlock
            {
                Text = match.MatchCode + " • " + match.MetaText,
                FontSize = 10,
                Foreground = ThemeBrush("BracketMetaBrush", new SolidColorBrush(Color.FromRgb(70, 85, 105))),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            metaPanel.Children.Add(new TextBlock
            {
                Text = match.StatusText,
                FontSize = 10,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = ThemeBrush("BracketStatusBrush", new SolidColorBrush(Color.FromRgb(100, 116, 139))),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetRow(metaPanel, 1);
            grid.Children.Add(metaPanel);

            bool canManageBracket = CanManageBracket();
            bool isSelected = canManageBracket && _selectedMatch != null && match.MatchId != 0 && _selectedMatch.MatchId == match.MatchId;
            Border card = new Border
            {
                Width = MatchWidth,
                Height = MatchHeight,
                CornerRadius = new CornerRadius(4),
                BorderBrush = isSelected
                    ? ThemeBrush("BracketMatchSelectedBorderBrush", new SolidColorBrush(Color.FromRgb(245, 158, 11)))
                    : ThemeBrush("BracketMatchCardBorderBrush", new SolidColorBrush(Color.FromRgb(64, 117, 217))),
                BorderThickness = isSelected ? new Thickness(2) : new Thickness(1),
                Background = isSelected
                    ? ThemeBrush("BracketMatchSelectedBackgroundBrush", new SolidColorBrush(Color.FromRgb(255, 251, 235)))
                    : ThemeBrush("BracketMatchCardBackgroundBrush", Brushes.White),
                Child = grid,
                Tag = match,
                Cursor = canManageBracket && match.IsEditable ? Cursors.Hand : Cursors.Arrow,
                ToolTip = canManageBracket
                    ? (match.IsEditable
                        ? "Нажмите, чтобы отредактировать матч"
                        : "Редактирование станет доступно после создания сетки")
                    : "Доступен только просмотр сетки"
            };

            if (canManageBracket && match.IsEditable)
            {
                card.MouseLeftButtonUp += MatchCard_MouseLeftButtonUp;
            }

            return card;
        }

        private Border CreateTeamRow(string teamName, string scoreText, bool isTop, bool isWinner)
        {
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });

            TextBlock teamText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(teamName) ? "TBD" : teamName,
                Margin = new Thickness(8, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = ThemeBrush("BracketMatchTextBrush", new SolidColorBrush(Color.FromRgb(17, 24, 39)))
            };
            grid.Children.Add(teamText);

            TextBlock score = new TextBlock
            {
                Text = scoreText,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold,
                Foreground = ThemeBrush("BracketScoreBrush", new SolidColorBrush(Color.FromRgb(30, 64, 175)))
            };
            Grid.SetColumn(score, 1);
            grid.Children.Add(score);

            Brush background;
            if (string.Equals(teamName, "BYE", StringComparison.CurrentCultureIgnoreCase))
            {
                background = ThemeBrush("BracketByeBackgroundBrush", new SolidColorBrush(Color.FromRgb(232, 236, 244)));
            }
            else if (isWinner)
            {
                background = ThemeGradient("BracketWinnerStartBrush", "BracketWinnerEndBrush", Color.FromRgb(240, 253, 244), Color.FromRgb(134, 239, 172));
            }
            else
            {
                background = ThemeGradient("BracketDefaultStartBrush", "BracketDefaultEndBrush", Color.FromRgb(236, 245, 255), Color.FromRgb(122, 171, 242));
            }

            return new Border
            {
                Background = background,
                BorderBrush = ThemeBrush("BracketRowBorderBrush", new SolidColorBrush(Color.FromRgb(91, 143, 231))),
                BorderThickness = isTop ? new Thickness(0, 0, 0, 1) : new Thickness(0, 1, 0, 0),
                Child = grid
            };
        }

        private void DrawTournamentCaption(string tournamentName)
        {
            TextBlock title = new TextBlock
            {
                Text = tournamentName,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = ThemeBrush("TextPrimaryBrush", new SolidColorBrush(Color.FromRgb(31, 41, 55)))
            };

            AddElement(title, CanvasPadding, 0);
        }

        private void DrawRoundColumnFrame(int roundIndex, string title, double totalHeight)
        {
            double columnX = GetColumnX(roundIndex);
            Border panel = new Border
            {
                Width = ColumnWidth,
                Height = totalHeight - ColumnTop,
                Background = ThemeBrush("BracketColumnBackgroundBrush", new SolidColorBrush(Color.FromRgb(240, 240, 250))),
                BorderBrush = ThemeBrush("BracketColumnBorderBrush", new SolidColorBrush(Color.FromRgb(223, 223, 238))),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4)
            };

            AddElement(panel, columnX, ColumnTop);
            AddElement(CreateHeader(title), columnX, ColumnTop);
        }

        private void DrawChampionColumnFrame(int roundCount, double totalHeight)
        {
            DrawRoundColumnFrame(roundCount, "Чемпион", totalHeight);
        }

        private void DrawRoundConnectors(int roundIndex)
        {
            double previousRight = GetCardX(roundIndex - 1) + MatchWidth;
            double currentLeft = GetCardX(roundIndex);
            double middleX = previousRight + (currentLeft - previousRight) / 2;
            int matchesInRound = (int)Math.Pow(2, Math.Max(0, GetFinalRoundIndex() - roundIndex));

            for (int matchIndex = 0; matchIndex < matchesInRound; matchIndex++)
            {
                double upperY = GetMatchCenterY(roundIndex - 1, matchIndex * 2);
                double lowerY = GetMatchCenterY(roundIndex - 1, matchIndex * 2 + 1);
                double currentY = GetMatchCenterY(roundIndex, matchIndex);
                DrawConnectorLine(previousRight, upperY, middleX, upperY);
                DrawConnectorLine(previousRight, lowerY, middleX, lowerY);
                DrawConnectorLine(middleX, upperY, middleX, lowerY);
                DrawConnectorLine(middleX, currentY, currentLeft, currentY);
            }
        }

        private void DrawChampionConnector(int roundCount)
        {
            double finalRight = GetCardX(roundCount - 1) + MatchWidth;
            double championLeft = GetCardX(roundCount);
            double finalY = GetMatchCenterY(roundCount - 1, 0);
            double middleX = finalRight + (championLeft - finalRight) / 2;
            DrawConnectorLine(finalRight, finalY, middleX, finalY);
            DrawConnectorLine(middleX, finalY, championLeft, finalY);
        }

        private Border CreateHeader(string title)
        {
            return new Border
            {
                Width = ColumnWidth,
                Height = HeaderHeight,
                Background = ThemeGradient("BracketHeaderStartBrush", "BracketHeaderEndBrush", Color.FromRgb(252, 252, 252), Color.FromRgb(183, 183, 183)),
                BorderBrush = ThemeBrush("BracketHeaderBorderBrush", new SolidColorBrush(Color.FromRgb(118, 118, 118))),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = new TextBlock
                {
                    Text = title,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = ThemeBrush("BracketHeaderTextBrush", new SolidColorBrush(Color.FromRgb(17, 24, 39)))
                }
            };
        }

        private void DrawConnectorLine(double x1, double y1, double x2, double y2)
        {
            BracketCanvas.Children.Add(new Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Stroke = ThemeBrush("BracketConnectorBrush", new SolidColorBrush(Color.FromRgb(58, 106, 214))),
                StrokeThickness = 2,
                SnapsToDevicePixels = true
            });
        }

        private void AddElement(UIElement element, double left, double top)
        {
            Canvas.SetLeft(element, left);
            Canvas.SetTop(element, top);
            BracketCanvas.Children.Add(element);
        }

        private void SelectMatch(BracketMatchViewModel match)
        {
            _selectedMatch = match;
            if (_currentSnapshot != null && _currentSnapshot.Rounds.Count > 0)
            {
                RenderBracket(_currentSnapshot);
            }

            UpdateEditorState();
        }

        private void UpdateEditorState()
        {
            if (!CanManageBracket())
            {
                ClearEditor("Доступен режим просмотра. Создание сетки, управление участниками и редактирование матчей доступны только администратору.");
                return;
            }

            if (_currentSnapshot == null || !_currentSnapshot.HasGeneratedBracket)
            {
                ClearEditor("Редактирование станет доступно после создания формата для выбранного турнира.");
                return;
            }

            if (_selectedMatch == null)
            {
                ClearEditor("Выберите матч на сетке, чтобы изменить его параметры.");
                return;
            }

            PopulateEditor(_selectedMatch);
        }

        private void PopulateEditor(BracketMatchViewModel match)
        {
            _isUpdatingEditor = true;
            try
            {
                EditorFieldsGrid.IsEnabled = true;
                SaveMatchButton.IsEnabled = true;
                CancelEditButton.IsEnabled = true;
                EditorSelectionText.Text = match.MatchCode + " • " + GetRoundTitle(match.RoundIndex);
                EditorStateText.Text = match.CanEditTeams
                    ? "Для этого матча можно менять участников, счет, дату, статус и победителя."
                    : "Состав этого матча заполняется автоматически по правилам выбранного формата турнира.";
                TeamEditHintText.Text = match.CanEditTeams
                    ? "Ручное изменение участников доступно только для стартовых матчей формата."
                    : "Если вы измените результат зависимого матча, участники этой встречи обновятся автоматически.";

                List<BracketTeamOption> availableTeams = BuildTeamOptions("Не назначена");
                ConfigureTeamComboBox(Team1ComboBox, availableTeams, match.Team1Id, match.CanEditTeams);
                ConfigureTeamComboBox(Team2ComboBox, availableTeams, match.Team2Id, match.CanEditTeams);
                Team1ScoreTextBox.Text = match.Team1Score.ToString(CultureInfo.InvariantCulture);
                Team2ScoreTextBox.Text = match.Team2Score.ToString(CultureInfo.InvariantCulture);
                MatchDatePicker.SelectedDate = match.MatchDate ?? DateTime.Today;
                StatusComboBox.SelectedValue = NormalizeStatusValue(match.Status);
                BestOfComboBox.ItemsSource = BestOfOptions.Contains(match.BestOf)
                    ? BestOfOptions
                    : BestOfOptions.Concat(new[] { match.BestOf }).Distinct().OrderBy(value => value).ToList();
                BestOfComboBox.SelectedItem = match.BestOf;
                RefreshWinnerOptions(match.WinnerTeamId);
            }
            finally
            {
                _isUpdatingEditor = false;
            }
        }

        private void ClearEditor(string message)
        {
            _isUpdatingEditor = true;
            try
            {
                EditorFieldsGrid.IsEnabled = false;
                SaveMatchButton.IsEnabled = false;
                CancelEditButton.IsEnabled = false;
                EditorSelectionText.Text = "Матч не выбран";
                EditorStateText.Text = message;
                TeamEditHintText.Text = string.Empty;
                Team1ComboBox.ItemsSource = null;
                Team2ComboBox.ItemsSource = null;
                WinnerComboBox.ItemsSource = null;
                Team1ScoreTextBox.Text = string.Empty;
                Team2ScoreTextBox.Text = string.Empty;
                MatchDatePicker.SelectedDate = null;
                StatusComboBox.SelectedIndex = -1;
                BestOfComboBox.SelectedIndex = -1;
            }
            finally
            {
                _isUpdatingEditor = false;
            }
        }

        private void ConfigureTeamComboBox(ComboBox comboBox, List<BracketTeamOption> options, int? selectedTeamId, bool isEnabled)
        {
            comboBox.ItemsSource = options;
            comboBox.DisplayMemberPath = "DisplayName";
            comboBox.SelectedValuePath = "TeamId";
            comboBox.SelectedValue = selectedTeamId;
            comboBox.IsEnabled = isEnabled;
        }

        private void RefreshWinnerOptions(int? preferredWinnerId)
        {
            int? team1Id = GetSelectedTeamId(Team1ComboBox);
            int? team2Id = GetSelectedTeamId(Team2ComboBox);

            List<BracketTeamOption> options = new List<BracketTeamOption>
            {
                new BracketTeamOption { EmptyLabel = "Не выбран" }
            };

            AddWinnerOption(options, team1Id);
            AddWinnerOption(options, team2Id);

            WinnerComboBox.ItemsSource = options;
            WinnerComboBox.DisplayMemberPath = "DisplayName";
            WinnerComboBox.SelectedValuePath = "TeamId";
            WinnerComboBox.SelectedValue = options.Any(option => option.TeamId == preferredWinnerId) ? preferredWinnerId : null;
        }

        private void AddWinnerOption(List<BracketTeamOption> options, int? teamId)
        {
            if (!teamId.HasValue)
            {
                return;
            }

            BracketTeamOption source = _teamOptions.FirstOrDefault(option => option.TeamId == teamId.Value);
            if (source == null || options.Any(option => option.TeamId == teamId.Value))
            {
                return;
            }

            options.Add(new BracketTeamOption
            {
                TeamId = source.TeamId,
                TeamName = source.TeamName,
                SecondaryText = source.SecondaryText
            });
        }

        private List<BracketTeamOption> BuildTeamOptions(string emptyLabel)
        {
            List<BracketTeamOption> options = new List<BracketTeamOption>
            {
                new BracketTeamOption { EmptyLabel = emptyLabel }
            };

            options.AddRange(_teamOptions.Select(option => new BracketTeamOption
            {
                TeamId = option.TeamId,
                TeamName = option.TeamName,
                SecondaryText = option.SecondaryText
            }));

            return options;
        }

        private BracketMatchViewModel ResolveSelectedMatch(TournamentBracketSnapshot snapshot, int? preferredMatchId)
        {
            if (snapshot == null || !snapshot.HasGeneratedBracket)
            {
                return null;
            }

            List<BracketMatchViewModel> matches = snapshot.Rounds.SelectMany(round => round.Matches).ToList();
            if (preferredMatchId.HasValue)
            {
                return matches.FirstOrDefault(match => match.MatchId == preferredMatchId.Value);
            }

            if (_selectedMatch != null)
            {
                return matches.FirstOrDefault(match => match.MatchId == _selectedMatch.MatchId);
            }

            return null;
        }

        private int? GetSelectedTournamentId()
        {
            TournamentOption option = TournamentComboBox.SelectedItem as TournamentOption;
            return option == null ? (int?)null : option.TournamentId;
        }

        private string GetRoundTitle(int roundIndex)
        {
            if (_currentSnapshot == null || roundIndex < 0 || roundIndex >= _currentSnapshot.Rounds.Count)
            {
                return "Раунд";
            }

            return _currentSnapshot.Rounds[roundIndex].Title;
        }

        private string BuildSummary(TournamentBracketSnapshot snapshot)
        {
            if (string.IsNullOrWhiteSpace(snapshot.TournamentName))
            {
                return string.Empty;
            }

            bool isLeague = string.Equals(snapshot.FormatType, "League", StringComparison.OrdinalIgnoreCase);
            bool canManageBracket = CanManageBracket();
            string modeText = snapshot.HasGeneratedBracket
                ? (canManageBracket
                    ? (isLeague
                        ? "Расписание сохранено. Можно редактировать результаты матчей и автоматически пересчитывать таблицу."
                        : "Сетка сохранена. Кликните по матчу, чтобы изменить его и автоматически обновить зависимые встречи.")
                    : "Сетка сохранена и доступна только для просмотра.")
                : (canManageBracket
                    ? (isLeague
                        ? "Пока показан предпросмотр расписания. Нажмите «Создать сетку», чтобы сохранить матчи и включить редактирование."
                        : "Пока показан предпросмотр сетки. Нажмите «Создать сетку», чтобы сохранить ее и включить редактирование.")
                    : "Пока показан предпросмотр сетки. Создание и редактирование доступны только администратору.");

            if (isLeague)
            {
                return snapshot.TournamentName + ": участников " + snapshot.ParticipantCount + ", туров " + snapshot.Rounds.Count + ", матчей " + snapshot.MatchCount + ". " + modeText;
            }

            return snapshot.TournamentName + ": формат " + snapshot.FormatType + ", участников " + snapshot.ParticipantCount + ", размер сетки " + snapshot.BracketSize + ", матчей " + snapshot.MatchCount + ". " + modeText;
        }

        private bool CanManageBracket()
        {
            return AccessPolicy.CanManageBracket(_currentRole);
        }

        private void ApplyRoleAccess()
        {
            bool canManageBracket = CanManageBracket();
            HeaderDescriptionText.Text = canManageBracket
                ? "Просмотр и редактирование сетки по раундам: выберите матч, измените счет, статус и параметры встречи."
                : "Просмотр сетки по раундам и состава участников выбранного турнира.";
            EditorTitleText.Text = canManageBracket ? "Редактирование матча" : "Режим просмотра";
            GenerateButton.Visibility = canManageBracket ? Visibility.Visible : Visibility.Collapsed;
            OpenParticipantsButton.Visibility = canManageBracket ? Visibility.Visible : Visibility.Collapsed;
            EditorFieldsGrid.Visibility = canManageBracket ? Visibility.Visible : Visibility.Collapsed;
            TeamEditHintText.Visibility = canManageBracket ? Visibility.Visible : Visibility.Collapsed;
        }

        private static bool TryParseNonNegativeInt(string text, out int value)
        {
            bool parsed = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            return parsed && value >= 0;
        }

        private static string NormalizeStatusValue(string status)
        {
            string value = (status ?? string.Empty).Trim();
            switch (value.ToLowerInvariant())
            {
                case "completed":
                    return "Completed";
                default:
                    return "Scheduled";
            }
        }

        private static int? GetSelectedTeamId(ComboBox comboBox)
        {
            return comboBox.SelectedValue == null ? (int?)null : Convert.ToInt32(comboBox.SelectedValue);
        }

        private static string GetScoreText(int? teamId, string teamName, int score)
        {
            if (!teamId.HasValue || string.Equals(teamName, "BYE", StringComparison.CurrentCultureIgnoreCase))
            {
                return string.Empty;
            }

            return score.ToString(CultureInfo.InvariantCulture);
        }

        private static double GetMatchCenterY(int roundIndex, int matchIndex)
        {
            return MatchesTop + (matchIndex + 0.5) * FirstRoundStep * Math.Pow(2, roundIndex);
        }

        private static double GetCardTop(int roundIndex, int matchIndex)
        {
            return GetMatchCenterY(roundIndex, matchIndex) - MatchHeight / 2;
        }

        private static double GetColumnX(int roundIndex)
        {
            return CanvasPadding + roundIndex * (ColumnWidth + ColumnGap);
        }

        private static double GetCardX(int roundIndex)
        {
            return GetColumnX(roundIndex) + (ColumnWidth - MatchWidth) / 2;
        }

        private int GetFinalRoundIndex()
        {
            return Math.Max(0, _currentRoundCount - 1);
        }

        private static Brush ThemeBrush(string key, Brush fallback)
        {
            return ThemeManager.GetBrush(key, fallback);
        }

        private static LinearGradientBrush ThemeGradient(string startKey, string endKey, Color fallbackStart, Color fallbackEnd)
        {
            return ThemeManager.CreateVerticalGradientBrush(startKey, endKey, fallbackStart, fallbackEnd);
        }

        private sealed class StatusOption
        {
            public StatusOption(string value, string displayName)
            {
                Value = value;
                DisplayName = displayName;
            }

            public string Value { get; }

            public string DisplayName { get; }
        }
    }
}



