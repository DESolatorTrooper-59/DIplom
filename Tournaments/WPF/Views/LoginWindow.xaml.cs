using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Tournaments.WPF.Services;

namespace Tournaments.WPF.Views
{
    public partial class LoginWindow : Window
    {
        private readonly DatabaseService _database;
        private readonly SqlServerConnectionService _sqlConnectionService;

        public LoginWindow()
        {
            InitializeComponent();
            Loaded += LoginWindow_Loaded;
            _sqlConnectionService = SqlServerConnectionService.Instance;

            try
            {
                _database = new DatabaseService();
                _database.EnsureOrganizerUser("admin", "password");
                SetMessage(null);
            }
            catch (Exception ex)
            {
                SetMessage("Не удалось инициализировать внутреннее хранилище: " + ex.Message);
            }
        }

        private void LoginWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoginTextBox.Focus();
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            SetMessage(null);

            if (_database == null)
            {
                SetMessage("Хранилище приложения не инициализировано.");
                return;
            }

            string login = LoginTextBox.Text.Trim();
            string password = PasswordTextBox.Password;

            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            {
                SetMessage("Введите логин и пароль.");
                return;
            }

            if (!_database.ValidateLogin(login, password))
            {
                SetMessage("Неверный логин или пароль.");
                return;
            }

            MainWindow window = new MainWindow(_database, login);
            Application.Current.MainWindow = window;
            window.Show();
            Close();
        }

        private void SqlConnect_Click(object sender, RoutedEventArgs e)
        {
            SqlServerConnectionWindow window = new SqlServerConnectionWindow
            {
                Owner = this
            };

            bool? dialogResult = window.ShowDialog();
            if (dialogResult == true)
            {
                string label = string.IsNullOrWhiteSpace(_sqlConnectionService.ActiveConnectionLabel)
                    ? "Соединение с MS SQL Server успешно установлено."
                    : "Соединение с MS SQL Server успешно установлено: " + _sqlConnectionService.ActiveConnectionLabel + ".";
                SetMessage(label, false);
            }
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            Login_Click(sender, new RoutedEventArgs());
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void SetMessage(string message, bool isError = true)
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
