using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Tournaments.WPF.Models;
using Tournaments.WPF.Services;

namespace Tournaments.WPF.Views
{
    public partial class BracketPreviewWindow : Window
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

        private readonly TournamentBracketService _bracketService;
        private readonly int _tournamentId;
        private int _currentRoundCount;

        public BracketPreviewWindow(DatabaseService database, int tournamentId)
        {
            InitializeComponent();
            _bracketService = new TournamentBracketService(database);
            _tournamentId = tournamentId;
            Loaded += BracketPreviewWindow_Loaded;
        }

        private void BracketPreviewWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= BracketPreviewWindow_Loaded;
            LoadSnapshot();
        }

        private void LoadSnapshot()
        {
            TournamentBracketSnapshot snapshot = _bracketService.BuildPreview(_tournamentId);
            Title = string.IsNullOrWhiteSpace(snapshot.TournamentName) ? "Турнирная сетка" : snapshot.TournamentName + " - турнирная сетка";
            TitleText.Text = string.IsNullOrWhiteSpace(snapshot.TournamentName) ? "Турнирная сетка" : snapshot.TournamentName;
            SubtitleText.Text = snapshot.HasGeneratedBracket
                ? "Открыта сохраненная сетка турнира в отдельном окне просмотра."
                : "Открыт предпросмотр сетки. Чтобы начать полноценное редактирование матчей, сначала создайте сетку на основной вкладке.";
            SummaryText.Text = BuildSummary(snapshot);

            bool hasEnoughParticipants = snapshot.ParticipantCount >= 2;
            BracketScrollViewer.Visibility = hasEnoughParticipants ? Visibility.Visible : Visibility.Collapsed;
            EmptyStateText.Visibility = hasEnoughParticipants ? Visibility.Collapsed : Visibility.Visible;
            EmptyStateText.Text = snapshot.ParticipantCount == 0
                ? "У выбранного турнира пока нет участников."
                : "Для построения сетки нужно минимум 2 участника.";

            if (hasEnoughParticipants && snapshot.Rounds.Count > 0)
            {
                RenderBracket(snapshot);
                return;
            }

            BracketCanvas.Children.Clear();
        }

        private void RenderBracket(TournamentBracketSnapshot snapshot)
        {
            BracketCanvas.Children.Clear();
            if (snapshot.Rounds.Count == 0)
            {
                return;
            }

            int roundCount = snapshot.Rounds.Count;
            _currentRoundCount = roundCount;
            int firstRoundMatches = snapshot.Rounds[0].Matches.Count;
            double totalWidth = CanvasPadding * 2 + (roundCount + 1) * ColumnWidth + roundCount * ColumnGap;
            double totalHeight = Math.Max(460, MatchesTop + firstRoundMatches * FirstRoundStep + 32);

            BracketCanvas.Width = totalWidth;
            BracketCanvas.Height = totalHeight;

            DrawTournamentCaption(snapshot.TournamentName);
            for (int roundIndex = 0; roundIndex < roundCount; roundIndex++)
            {
                DrawRoundColumnFrame(roundIndex, snapshot.Rounds[roundIndex].Title, totalHeight);
            }

            DrawChampionColumnFrame(roundCount, totalHeight);
            for (int roundIndex = 1; roundIndex < roundCount; roundIndex++)
            {
                DrawRoundConnectors(roundIndex);
            }

            DrawChampionConnector(roundCount);
            for (int roundIndex = 0; roundIndex < roundCount; roundIndex++)
            {
                DrawRoundMatches(roundIndex, snapshot.Rounds[roundIndex]);
            }

            DrawChampionCard(roundCount, snapshot.ChampionName);
        }

        private void DrawRoundMatches(int roundIndex, BracketRoundViewModel round)
        {
            for (int matchIndex = 0; matchIndex < round.Matches.Count; matchIndex++)
            {
                AddElement(CreateMatchCard(round.Matches[matchIndex]), GetCardX(roundIndex), GetCardTop(roundIndex, matchIndex));
            }
        }

        private void DrawTournamentCaption(string tournamentName)
        {
            TextBlock title = new TextBlock
            {
                Text = tournamentName,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(31, 41, 55))
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
                Background = new SolidColorBrush(Color.FromRgb(240, 240, 250)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(223, 223, 238)),
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

        private void DrawChampionCard(int roundCount, string championName)
        {
            Border card = new Border
            {
                Width = MatchWidth,
                Height = ChampionHeight,
                CornerRadius = new CornerRadius(4),
                BorderBrush = new SolidColorBrush(Color.FromRgb(116, 116, 116)),
                BorderThickness = new Thickness(1),
                Background = new LinearGradientBrush(Color.FromRgb(245, 245, 245), Color.FromRgb(178, 178, 178), new Point(0.5, 0), new Point(0.5, 1)),
                Child = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(championName) ? "Будет определен" : championName,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = new SolidColorBrush(Color.FromRgb(31, 41, 55))
                }
            };

            AddElement(card, GetCardX(roundCount), GetMatchCenterY(roundCount - 1, 0) - ChampionHeight / 2);
        }

        private Border CreateHeader(string title)
        {
            return new Border
            {
                Width = ColumnWidth,
                Height = HeaderHeight,
                Background = new LinearGradientBrush(Color.FromRgb(252, 252, 252), Color.FromRgb(183, 183, 183), new Point(0.5, 0), new Point(0.5, 1)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(118, 118, 118)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = new TextBlock
                {
                    Text = title,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39))
                }
            };
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
                Foreground = new SolidColorBrush(Color.FromRgb(70, 85, 105)),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            metaPanel.Children.Add(new TextBlock
            {
                Text = match.StatusText,
                FontSize = 10,
                Margin = new Thickness(0, 2, 0, 0),
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetRow(metaPanel, 1);
            grid.Children.Add(metaPanel);

            return new Border
            {
                Width = MatchWidth,
                Height = MatchHeight,
                CornerRadius = new CornerRadius(4),
                BorderBrush = new SolidColorBrush(Color.FromRgb(64, 117, 217)),
                BorderThickness = new Thickness(1),
                Background = Brushes.White,
                Child = grid
            };
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
                Foreground = new SolidColorBrush(Color.FromRgb(17, 24, 39))
            };
            grid.Children.Add(teamText);

            TextBlock score = new TextBlock
            {
                Text = scoreText,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 64, 175))
            };
            Grid.SetColumn(score, 1);
            grid.Children.Add(score);

            Brush background;
            if (string.Equals(teamName, "BYE", StringComparison.CurrentCultureIgnoreCase))
            {
                background = new SolidColorBrush(Color.FromRgb(232, 236, 244));
            }
            else if (isWinner)
            {
                background = new LinearGradientBrush(Color.FromRgb(240, 253, 244), Color.FromRgb(134, 239, 172), new Point(0.5, 0), new Point(0.5, 1));
            }
            else
            {
                background = new LinearGradientBrush(Color.FromRgb(236, 245, 255), Color.FromRgb(122, 171, 242), new Point(0.5, 0), new Point(0.5, 1));
            }

            return new Border
            {
                Background = background,
                BorderBrush = new SolidColorBrush(Color.FromRgb(91, 143, 231)),
                BorderThickness = isTop ? new Thickness(0, 0, 0, 1) : new Thickness(0, 1, 0, 0),
                Child = grid
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
                Stroke = new SolidColorBrush(Color.FromRgb(58, 106, 214)),
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

        private static string BuildSummary(TournamentBracketSnapshot snapshot)
        {
            if (string.IsNullOrWhiteSpace(snapshot.TournamentName))
            {
                return string.Empty;
            }

            string modeText = snapshot.HasGeneratedBracket
                ? "Показывается сохраненная сетка турнира."
                : "Показывается расчетный предпросмотр сетки без сохранения.";

            return snapshot.TournamentName + ": участников " + snapshot.ParticipantCount + ", размер сетки " + snapshot.BracketSize + ", матчей в сетке " + snapshot.MatchCount + ". " + modeText;
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
    }
}
