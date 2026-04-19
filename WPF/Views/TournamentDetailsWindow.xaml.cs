using System;
using System.Windows;
using Tournaments.WPF.Models;
using Tournaments.WPF.Services;

namespace Tournaments.WPF.Views
{
    public partial class TournamentDetailsWindow : Window
    {
        private readonly DatabaseService _database;
        private readonly TournamentCardViewModel _card;

        public TournamentDetailsWindow(DatabaseService database, TournamentCardViewModel card)
        {
            InitializeComponent();
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _card = card ?? throw new ArgumentNullException(nameof(card));
            DataContext = _card;
            Title = string.IsNullOrWhiteSpace(_card.TournamentName) ? "Турнир" : _card.TournamentName;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OpenBracket_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BracketPreviewWindow window = new BracketPreviewWindow(_database, _card.TournamentId)
                {
                    Owner = this
                };
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось открыть сетку турнира: " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
