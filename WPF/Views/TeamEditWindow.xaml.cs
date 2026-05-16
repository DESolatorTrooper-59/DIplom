using System;
using System.Collections.Generic;
using System.Windows;

namespace Tournaments.WPF.Views
{
    public partial class TeamEditWindow : Window
    {
        public TeamEditWindow()
        {
            InitializeComponent();
            CountryTextBox.Text = "Online";
            FoundedDatePicker.SelectedDate = DateTime.Today;
            Loaded += TeamEditWindow_Loaded;
        }

        public Dictionary<string, object> ResultValues { get; private set; }

        private void TeamEditWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= TeamEditWindow_Loaded;
            TeamNameTextBox.Focus();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string teamName = Normalize(TeamNameTextBox.Text);
            string country = Normalize(CountryTextBox.Text);
            string coachName = Normalize(CoachNameTextBox.Text);

            if (string.IsNullOrWhiteSpace(teamName))
            {
                MessageBox.Show("Заполните поле \"Название команды\".", "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(country))
            {
                country = "Online";
            }

            if (string.IsNullOrWhiteSpace(coachName))
            {
                MessageBox.Show("Заполните поле \"Тренер\".", "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!FoundedDatePicker.SelectedDate.HasValue)
            {
                MessageBox.Show("Укажите дату основания.", "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ResultValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                ["TeamName"] = teamName,
                ["Country"] = country,
                ["CoachName"] = coachName,
                ["FoundedDate"] = FoundedDatePicker.SelectedDate.Value.Date
            };

            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
