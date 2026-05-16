using System.Windows;

namespace Tournaments.WPF.Views
{
    public partial class TeamRenameWindow : Window
    {
        public TeamRenameWindow(string currentName)
        {
            InitializeComponent();
            TeamNameTextBox.Text = currentName ?? string.Empty;
            TeamNameTextBox.SelectAll();
            Loaded += TeamRenameWindow_Loaded;
        }

        public string TeamName { get; private set; }

        private void TeamRenameWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= TeamRenameWindow_Loaded;
            TeamNameTextBox.Focus();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            string value = TeamNameTextBox.Text == null ? string.Empty : TeamNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                MessageBox.Show("Заполните поле \"Название команды\".", "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            TeamName = value;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
