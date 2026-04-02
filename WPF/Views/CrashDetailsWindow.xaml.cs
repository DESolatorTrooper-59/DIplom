using System;
using System.ComponentModel;
using System.Windows;

namespace Tournaments.WPF.Views
{
    public partial class CrashDetailsWindow : Window
    {
        private bool _isConfirmed;

        public CrashDetailsWindow(string summary, string details)
        {
            InitializeComponent();
            SummaryText.Text = string.IsNullOrWhiteSpace(summary)
                ? "Приложение столкнулось с необработанной ошибкой."
                : summary;
            DetailsTextBox.Text = details ?? string.Empty;
            Loaded += CrashDetailsWindow_Loaded;
        }

        private void CrashDetailsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= CrashDetailsWindow_Loaded;
            DetailsTextBox.Focus();
            DetailsTextBox.CaretIndex = 0;
            DetailsTextBox.ScrollToHome();
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(DetailsTextBox.Text ?? string.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось скопировать текст ошибки: " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            _isConfirmed = true;
            DialogResult = true;
            Close();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_isConfirmed)
            {
                return;
            }

            e.Cancel = true;
        }
    }
}
