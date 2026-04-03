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
    internal static class TournamentBracketRenderer
    {
        private const double ColumnWidth = 220;
        private const double ColumnGap = 52;
        private const double MatchWidth = 184;
        private const double MatchHeight = 88;
        private const double ChampionHeight = 46;
        private const double CanvasPadding = 18;
        private const double ColumnTop = 42;
        private const double HeaderHeight = 28;
        private const double BinarySectionTopPadding = 48;
        private const double LinearSectionTopPadding = 48;
        private const double FirstRoundStep = 118;
        private const double LinearStep = 104;
        private const double SectionGap = 92;
        private const double FinalSectionHeight = 180;

        public static void Render(Canvas canvas, TournamentBracketSnapshot snapshot, int? selectedMatchId, MouseButtonEventHandler matchClick)
        {
            canvas.Children.Clear();
            if (snapshot == null || snapshot.Rounds.Count == 0)
            {
                return;
            }

            List<BracketRoundViewModel> mainRounds = snapshot.Rounds.Where(round => string.Equals(round.SectionKey, "Main", System.StringComparison.OrdinalIgnoreCase)).OrderBy(round => round.LayoutColumn).ToList();
            List<BracketRoundViewModel> upperRounds = snapshot.Rounds.Where(round => string.Equals(round.SectionKey, "Upper", System.StringComparison.OrdinalIgnoreCase)).OrderBy(round => round.LayoutColumn).ToList();
            List<BracketRoundViewModel> lowerRounds = snapshot.Rounds.Where(round => string.Equals(round.SectionKey, "Lower", System.StringComparison.OrdinalIgnoreCase)).OrderBy(round => round.LayoutColumn).ToList();
            List<BracketRoundViewModel> finalRounds = snapshot.Rounds.Where(round => string.Equals(round.SectionKey, "Final", System.StringComparison.OrdinalIgnoreCase)).OrderBy(round => round.LayoutColumn).ToList();
            List<BracketRoundViewModel> leagueRounds = snapshot.Rounds.Where(round => string.Equals(round.SectionKey, "League", System.StringComparison.OrdinalIgnoreCase)).OrderBy(round => round.LayoutColumn).ToList();

            bool isDouble = upperRounds.Count > 0 || lowerRounds.Count > 0 || finalRounds.Count > 0;
            bool isLeague = leagueRounds.Count > 0;
            List<BracketRoundViewModel> primaryRounds = isLeague ? leagueRounds : (upperRounds.Count > 0 ? upperRounds : mainRounds);
            int firstRoundMatches = primaryRounds.Count == 0 ? 0 : primaryRounds[0].Matches.Count;
            double upperHeight = isLeague
                ? CalculateLinearSectionHeight(primaryRounds)
                : CalculateBinarySectionHeight(firstRoundMatches);
            double upperTop = ColumnTop;
            double lowerTop = upperTop + upperHeight + SectionGap;
            double lowerHeight = lowerRounds.Count == 0 ? 0 : CalculateLinearSectionHeight(lowerRounds);
            double finalTop = isDouble
                ? upperTop + ((lowerTop + lowerHeight) - upperTop - FinalSectionHeight) / 2
                : upperTop;
            double totalHeight = isDouble
                ? lowerTop + lowerHeight + 32
                : upperTop + upperHeight + 32;

            int maxLayoutColumn = snapshot.Rounds.Max(round => round.LayoutColumn);
            int totalColumns = maxLayoutColumn + 1 + (isLeague ? 0 : 1);
            canvas.Width = CanvasPadding * 2 + totalColumns * ColumnWidth + System.Math.Max(0, totalColumns - 1) * ColumnGap;
            canvas.Height = System.Math.Max(460, totalHeight);

            AddElement(canvas, CreateCaption(snapshot.TournamentName), CanvasPadding, 0);

            foreach (BracketRoundViewModel round in snapshot.Rounds.OrderBy(round => round.LayoutColumn))
            {
                double sectionTop = ResolveSectionTop(round.SectionKey, upperTop, lowerTop, finalTop);
                double sectionHeight = ResolveSectionHeight(round.SectionKey, upperHeight, lowerHeight, isLeague ? upperHeight : FinalSectionHeight);
                double columnX = GetColumnX(round.LayoutColumn);
                AddElement(canvas, CreateColumnFrame(round.Title, sectionHeight), columnX, sectionTop);
            }

            Dictionary<string, CardBounds> cardBounds = new Dictionary<string, CardBounds>(System.StringComparer.OrdinalIgnoreCase);
            foreach (BracketRoundViewModel round in snapshot.Rounds)
            {
                double cardLeft = GetCardX(round.LayoutColumn);
                for (int matchIndex = 0; matchIndex < round.Matches.Count; matchIndex++)
                {
                    BracketMatchViewModel match = round.Matches[matchIndex];
                    double cardTop = ResolveCardTop(round, matchIndex, upperTop, lowerTop, finalTop);
                    Border card = CreateMatchCard(match, selectedMatchId);
                    if (match.IsEditable && matchClick != null)
                    {
                        card.Tag = match;
                        card.Cursor = Cursors.Hand;
                        card.ToolTip = "Нажмите, чтобы отредактировать матч";
                        card.MouseLeftButtonUp += matchClick;
                    }

                    AddElement(canvas, card, cardLeft, cardTop);
                    if (!string.IsNullOrWhiteSpace(match.MatchKey))
                    {
                        cardBounds[match.MatchKey] = new CardBounds(cardLeft, cardTop);
                    }
                }
            }

            foreach (BracketRoundViewModel round in snapshot.Rounds)
            {
                foreach (BracketMatchViewModel match in round.Matches)
                {
                    if (!string.IsNullOrWhiteSpace(match.SourceMatchKey1) && cardBounds.ContainsKey(match.SourceMatchKey1) && cardBounds.ContainsKey(match.MatchKey))
                    {
                        DrawConnector(canvas, cardBounds[match.SourceMatchKey1], cardBounds[match.MatchKey], true);
                    }

                    if (!string.IsNullOrWhiteSpace(match.SourceMatchKey2) && cardBounds.ContainsKey(match.SourceMatchKey2) && cardBounds.ContainsKey(match.MatchKey))
                    {
                        DrawConnector(canvas, cardBounds[match.SourceMatchKey2], cardBounds[match.MatchKey], false);
                    }
                }
            }

            if (!isLeague)
            {
                int championColumn = maxLayoutColumn + 1;
                double championCenterY = ResolveChampionCenterY(snapshot.Rounds, cardBounds, upperTop, upperHeight, lowerTop, lowerHeight, finalTop);
                double championTop = championCenterY - ChampionHeight / 2;
                AddElement(canvas, CreateColumnFrame("Чемпион", isDouble ? FinalSectionHeight : upperHeight), GetColumnX(championColumn), isDouble ? finalTop : upperTop);

                Border championCard = CreateChampionCard(snapshot.ChampionName);
                AddElement(canvas, championCard, GetCardX(championColumn), championTop);

                BracketMatchViewModel sourceMatch = finalRounds.SelectMany(round => round.Matches).FirstOrDefault() ??
                                                   upperRounds.LastOrDefault()?.Matches.FirstOrDefault() ??
                                                   mainRounds.LastOrDefault()?.Matches.FirstOrDefault();
                if (sourceMatch != null && !string.IsNullOrWhiteSpace(sourceMatch.MatchKey) && cardBounds.ContainsKey(sourceMatch.MatchKey))
                {
                    CardBounds sourceBounds = cardBounds[sourceMatch.MatchKey];
                    DrawHorizontalConnector(canvas, sourceBounds.Right, sourceBounds.CenterY, GetCardX(championColumn), championCenterY);
                }
            }
        }

        private static double ResolveSectionTop(string sectionKey, double upperTop, double lowerTop, double finalTop)
        {
            if (string.Equals(sectionKey, "Lower", System.StringComparison.OrdinalIgnoreCase))
            {
                return lowerTop;
            }

            if (string.Equals(sectionKey, "Final", System.StringComparison.OrdinalIgnoreCase))
            {
                return finalTop;
            }

            return upperTop;
        }

        private static double ResolveSectionHeight(string sectionKey, double upperHeight, double lowerHeight, double finalHeight)
        {
            if (string.Equals(sectionKey, "Lower", System.StringComparison.OrdinalIgnoreCase))
            {
                return lowerHeight;
            }

            if (string.Equals(sectionKey, "Final", System.StringComparison.OrdinalIgnoreCase))
            {
                return finalHeight;
            }

            return upperHeight;
        }

        private static double ResolveCardTop(BracketRoundViewModel round, int matchIndex, double upperTop, double lowerTop, double finalTop)
        {
            if (string.Equals(round.SectionKey, "Lower", System.StringComparison.OrdinalIgnoreCase) || string.Equals(round.SectionKey, "League", System.StringComparison.OrdinalIgnoreCase))
            {
                return ResolveSectionTop(round.SectionKey, upperTop, lowerTop, finalTop) + LinearSectionTopPadding + matchIndex * LinearStep;
            }

            if (string.Equals(round.SectionKey, "Final", System.StringComparison.OrdinalIgnoreCase))
            {
                return finalTop + HeaderHeight + 30;
            }

            return ResolveSectionTop(round.SectionKey, upperTop, lowerTop, finalTop) + GetBinaryCardTop(round.SectionIndex, matchIndex);
        }

        private static double GetBinaryCardTop(int roundIndex, int matchIndex)
        {
            return BinarySectionTopPadding + GetBinaryMatchCenterY(roundIndex, matchIndex) - MatchHeight / 2;
        }

        private static double GetBinaryMatchCenterY(int roundIndex, int matchIndex)
        {
            return (matchIndex + 0.5) * FirstRoundStep * System.Math.Pow(2, roundIndex);
        }

        private static double CalculateBinarySectionHeight(int firstRoundMatches)
        {
            return HeaderHeight + BinarySectionTopPadding + firstRoundMatches * FirstRoundStep + 18;
        }

        private static double CalculateLinearSectionHeight(IList<BracketRoundViewModel> rounds)
        {
            int maxMatches = rounds.Count == 0 ? 1 : rounds.Max(round => System.Math.Max(1, round.Matches.Count));
            return HeaderHeight + LinearSectionTopPadding + maxMatches * LinearStep + 12;
        }

        private static Border CreateColumnFrame(string title, double height)
        {
            Grid grid = new Grid
            {
                Width = ColumnWidth,
                Height = height
            };

            Border panel = new Border
            {
                Width = ColumnWidth,
                Height = height,
                Background = ThemeBrush("BracketColumnBackgroundBrush", new SolidColorBrush(Color.FromRgb(240, 240, 250))),
                BorderBrush = ThemeBrush("BracketColumnBorderBrush", new SolidColorBrush(Color.FromRgb(223, 223, 238))),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4)
            };
            grid.Children.Add(panel);

            grid.Children.Add(new Border
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
            });

            return new Border { Child = grid };
        }

        private static Border CreateCaption(string tournamentName)
        {
            return new Border
            {
                Child = new TextBlock
                {
                    Text = tournamentName,
                    FontSize = 24,
                    FontWeight = FontWeights.Bold,
                    Foreground = ThemeBrush("TextPrimaryBrush", new SolidColorBrush(Color.FromRgb(31, 41, 55)))
                }
            };
        }

        private static Border CreateChampionCard(string championName)
        {
            return new Border
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
        }

        private static Border CreateMatchCard(BracketMatchViewModel match, int? selectedMatchId)
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

            bool isSelected = selectedMatchId.HasValue && match.MatchId != 0 && selectedMatchId.Value == match.MatchId;
            return new Border
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
                Child = grid
            };
        }

        private static Border CreateTeamRow(string teamName, string scoreText, bool isTop, bool isWinner)
        {
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            grid.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(teamName) ? "TBD" : teamName,
                Margin = new Thickness(8, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = ThemeBrush("BracketMatchTextBrush", new SolidColorBrush(Color.FromRgb(17, 24, 39)))
            });

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

            Brush background = string.Equals(teamName, "BYE", System.StringComparison.CurrentCultureIgnoreCase)
                ? ThemeBrush("BracketByeBackgroundBrush", new SolidColorBrush(Color.FromRgb(232, 236, 244)))
                : isWinner
                    ? ThemeGradient("BracketWinnerStartBrush", "BracketWinnerEndBrush", Color.FromRgb(240, 253, 244), Color.FromRgb(134, 239, 172))
                    : ThemeGradient("BracketDefaultStartBrush", "BracketDefaultEndBrush", Color.FromRgb(236, 245, 255), Color.FromRgb(122, 171, 242));

            return new Border
            {
                Background = background,
                BorderBrush = ThemeBrush("BracketRowBorderBrush", new SolidColorBrush(Color.FromRgb(91, 143, 231))),
                BorderThickness = isTop ? new Thickness(0, 0, 0, 1) : new Thickness(0, 1, 0, 0),
                Child = grid
            };
        }

        private static void DrawConnector(Canvas canvas, CardBounds source, CardBounds target, bool isTopSlot)
        {
            DrawHorizontalConnector(canvas, source.Right, source.CenterY, target.Left, isTopSlot ? target.TopSlotY : target.BottomSlotY);
        }

        private static void DrawHorizontalConnector(Canvas canvas, double sourceX, double sourceY, double targetX, double targetY)
        {
            double middleX = sourceX + (targetX - sourceX) / 2;
            DrawLine(canvas, sourceX, sourceY, middleX, sourceY);
            DrawLine(canvas, middleX, sourceY, middleX, targetY);
            DrawLine(canvas, middleX, targetY, targetX, targetY);
        }

        private static void DrawLine(Canvas canvas, double x1, double y1, double x2, double y2)
        {
            canvas.Children.Add(new Line
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

        private static double ResolveChampionCenterY(IList<BracketRoundViewModel> rounds, IDictionary<string, CardBounds> cardBounds, double upperTop, double upperHeight, double lowerTop, double lowerHeight, double finalTop)
        {
            BracketMatchViewModel sourceMatch = rounds.Where(round => string.Equals(round.SectionKey, "Final", System.StringComparison.OrdinalIgnoreCase)).SelectMany(round => round.Matches).FirstOrDefault() ??
                                               rounds.Where(round => string.Equals(round.SectionKey, "Main", System.StringComparison.OrdinalIgnoreCase) || string.Equals(round.SectionKey, "Upper", System.StringComparison.OrdinalIgnoreCase)).LastOrDefault()?.Matches.FirstOrDefault();
            if (sourceMatch != null && !string.IsNullOrWhiteSpace(sourceMatch.MatchKey) && cardBounds.ContainsKey(sourceMatch.MatchKey))
            {
                return cardBounds[sourceMatch.MatchKey].CenterY;
            }

            if (lowerHeight > 0)
            {
                return upperTop + (lowerTop + lowerHeight - upperTop) / 2;
            }

            return upperTop + upperHeight / 2;
        }

        private static string GetScoreText(int? teamId, string teamName, int score)
        {
            if (!teamId.HasValue || string.Equals(teamName, "BYE", System.StringComparison.CurrentCultureIgnoreCase))
            {
                return string.Empty;
            }

            return score.ToString(CultureInfo.InvariantCulture);
        }

        private static double GetColumnX(int roundIndex)
        {
            return CanvasPadding + roundIndex * (ColumnWidth + ColumnGap);
        }

        private static double GetCardX(int roundIndex)
        {
            return GetColumnX(roundIndex) + (ColumnWidth - MatchWidth) / 2;
        }

        private static void AddElement(Canvas canvas, UIElement element, double left, double top)
        {
            Canvas.SetLeft(element, left);
            Canvas.SetTop(element, top);
            canvas.Children.Add(element);
        }

        private static Brush ThemeBrush(string key, Brush fallback)
        {
            return ThemeManager.GetBrush(key, fallback);
        }

        private static LinearGradientBrush ThemeGradient(string startKey, string endKey, Color fallbackStart, Color fallbackEnd)
        {
            return ThemeManager.CreateVerticalGradientBrush(startKey, endKey, fallbackStart, fallbackEnd);
        }

        private sealed class CardBounds
        {
            public CardBounds(double left, double top)
            {
                Left = left;
                Top = top;
            }

            public double Left { get; }

            public double Top { get; }

            public double Right => Left + MatchWidth;

            public double CenterY => Top + MatchHeight / 2;

            public double TopSlotY => Top + 14;

            public double BottomSlotY => Top + 74;
        }
    }
}
