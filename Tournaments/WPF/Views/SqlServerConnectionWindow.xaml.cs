using System;
using System.Windows;
using System.Windows.Media;
using Tournaments.WPF.Models;
using Tournaments.WPF.Services;

namespace Tournaments.WPF.Views
{
    public partial class SqlServerConnectionWindow : Window
    {
        private readonly SqlServerConnectionService _connectionService;

        public SqlServerConnectionWindow()
        {
            InitializeComponent();
            _connectionService = SqlServerConnectionService.Instance;
            LoadDefaults();
        }

        private void LoadDefaults()
        {
            SqlServerConnectionSettings settings = _connectionService.GetSettings();
            ServerTextBox.Text = settings.Server ?? string.Empty;
            DatabaseTextBox.Text = settings.Database ?? string.Empty;
            UserNameTextBox.Text = settings.UserName ?? string.Empty;
            PasswordTextBox.Password = settings.Password ?? string.Empty;
            WindowsAuthCheckBox.IsChecked = settings.UseWindowsAuthentication;
            ApplyAuthenticationMode();
        }

        private void WindowsAuthCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            ApplyAuthenticationMode();
        }

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SqlServerConnectionSettings settings = new SqlServerConnectionSettings
                {
                    Server = ServerTextBox.Text.Trim(),
                    Database = DatabaseTextBox.Text.Trim(),
                    UserName = UserNameTextBox.Text.Trim(),
                    Password = PasswordTextBox.Password,
                    UseWindowsAuthentication = WindowsAuthCheckBox.IsChecked == true
                };

                _connectionService.Connect(settings);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                SetMessage(ex.Message, true);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void ApplyAuthenticationMode()
        {
            bool useWindowsAuthentication = WindowsAuthCheckBox.IsChecked == true;
            UserNameTextBox.IsEnabled = !useWindowsAuthentication;
            PasswordTextBox.IsEnabled = !useWindowsAuthentication;

            if (useWindowsAuthentication)
            {
                SetMessage("Будет использована Windows Authentication для подключения к SQL Server.", false);
                return;
            }

            SetMessage(null, true);
        }

        private void SetMessage(string message, bool isError)
        {
            bool hasMessage = !string.IsNullOrWhiteSpace(message);
            MessageContainer.Visibility = hasMessage ? Visibility.Visible : Visibility.Collapsed;
            MessageText.Text = hasMessage ? message : string.Empty;

            if (!hasMessage)
            {
                return;
            }

            if (isError)
            {
                MessageContainer.Background = new SolidColorBrush(Color.FromRgb(254, 228, 226));
                MessageContainer.BorderBrush = new SolidColorBrush(Color.FromRgb(254, 202, 202));
                MessageText.Foreground = new SolidColorBrush(Color.FromRgb(180, 35, 24));
                return;
            }

            MessageContainer.Background = new SolidColorBrush(Color.FromRgb(220, 252, 231));
            MessageContainer.BorderBrush = new SolidColorBrush(Color.FromRgb(187, 247, 208));
            MessageText.Foreground = new SolidColorBrush(Color.FromRgb(22, 101, 52));
        }
    }
}
