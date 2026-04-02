using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Tournaments.WPF.Models;
using Tournaments.WPF.Services;

namespace Tournaments.WPF.Views
{
    public partial class LoginWindow : Window
    {
        private readonly DatabaseService _database;
        private readonly SqlServerConnectionService _sqlConnectionService;
        private bool _isSqlMode;
        private bool _isConnecting;

        public LoginWindow()
        {
            InitializeComponent();
            Loaded += LoginWindow_Loaded;
            _sqlConnectionService = SqlServerConnectionService.Instance;

            try
            {
                _database = new DatabaseService();
                _database.EnsureOrganizerUser("admin", "password");
                SetLoginMessage(null);
            }
            catch (Exception ex)
            {
                SetLoginMessage("Не удалось инициализировать внутреннее хранилище: " + ex.Message);
            }

            SwitchMode(false);
            UpdateConnectionIndicator(false);
        }

        private void LoginWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoginTextBox.Focus();
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            SetLoginMessage(null);

            if (_database == null)
            {
                SetLoginMessage("Хранилище приложения не инициализировано.");
                return;
            }

            string login = LoginTextBox.Text.Trim();
            string password = PasswordTextBox.Password;

            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            {
                SetLoginMessage("Введите логин и пароль.");
                return;
            }

            if (!_database.ValidateLogin(login, password))
            {
                SetLoginMessage("Неверный логин или пароль.");
                return;
            }

            MainWindow window = new MainWindow(_database, login);
            Application.Current.MainWindow = window;
            window.Show();
            Close();
        }

        private void SqlModeButton_Click(object sender, RoutedEventArgs e)
        {
            PopulateSqlSettings();
            SetSqlMessage(null);
            SwitchMode(true);
        }

        private void BackToLogin_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnecting)
            {
                return;
            }

            SwitchMode(false);
            LoginTextBox.Focus();
        }

        private async void ConnectSql_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnecting)
            {
                return;
            }

            SetSqlMessage(null);
            SqlServerConnectionSettings settings = new SqlServerConnectionSettings
            {
                Server = SqlServerTextBox.Text.Trim(),
                Database = SqlDatabaseTextBox.Text.Trim(),
                UserName = SqlUserTextBox.Text.Trim(),
                Password = SqlPasswordTextBox.Password,
                UseWindowsAuthentication = WindowsAuthCheckBox.IsChecked == true
            };

            try
            {
                SetConnectingState(true);
                await Task.Run(() => _sqlConnectionService.Connect(settings));
                UpdateConnectionIndicator(true);
                SetLoginMessage(BuildConnectedMessage(), false);
                SetSqlMessage(null);
                SwitchMode(false);
                LoginTextBox.Focus();
            }
            catch (Exception ex)
            {
                _sqlConnectionService.ClearSuccessfulConnection();
                UpdateConnectionIndicator(false);
                SetSqlMessage("Не удалось подключиться к MS SQL Server: " + ex.Message, true);
            }
            finally
            {
                SetConnectingState(false);
            }
        }

        private void WindowsAuthCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            ApplyAuthenticationMode();
        }

        private void LoginInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            Login_Click(sender, new RoutedEventArgs());
        }

        private void SqlInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || _isConnecting)
            {
                return;
            }

            e.Handled = true;
            ConnectSql_Click(sender, new RoutedEventArgs());
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void PopulateSqlSettings()
        {
            SqlServerConnectionSettings settings = _sqlConnectionService.GetSettings();
            SqlServerTextBox.Text = settings.Server ?? string.Empty;
            SqlDatabaseTextBox.Text = settings.Database ?? string.Empty;
            SqlUserTextBox.Text = settings.UserName ?? string.Empty;
            SqlPasswordTextBox.Password = settings.Password ?? string.Empty;
            WindowsAuthCheckBox.IsChecked = settings.UseWindowsAuthentication;
            ApplyAuthenticationMode();
        }

        private void ApplyAuthenticationMode()
        {
            bool useWindowsAuthentication = WindowsAuthCheckBox.IsChecked == true;
            SqlUserTextBox.IsEnabled = !useWindowsAuthentication;
            SqlPasswordTextBox.IsEnabled = !useWindowsAuthentication;
        }

        private void SwitchMode(bool isSqlMode)
        {
            _isSqlMode = isSqlMode;
            LoginModeGrid.Visibility = isSqlMode ? Visibility.Collapsed : Visibility.Visible;
            SqlModeGrid.Visibility = isSqlMode ? Visibility.Visible : Visibility.Collapsed;
            TitleText.Text = isSqlMode ? "MS SQL Server" : "Tournaments WPF";
            SubtitleText.Text = isSqlMode
                ? "Укажите параметры сервера и проверьте соединение с базой данных прямо в этом окне."
                : "Войдите под учётной записью организатора, чтобы открыть управление турнирами.";
        }

        private void SetConnectingState(bool isConnecting)
        {
            _isConnecting = isConnecting;
            SqlBusyOverlay.Visibility = isConnecting ? Visibility.Visible : Visibility.Collapsed;
            ConnectSqlButton.IsEnabled = !isConnecting;
            SqlModeButton.IsEnabled = !isConnecting;
            LoginButton.IsEnabled = !isConnecting;
            WindowsAuthCheckBox.IsEnabled = !isConnecting;
            SqlServerTextBox.IsEnabled = !isConnecting;
            SqlDatabaseTextBox.IsEnabled = !isConnecting;
            ApplyAuthenticationMode();
            if (isConnecting)
            {
                SqlUserTextBox.IsEnabled = false;
                SqlPasswordTextBox.IsEnabled = false;
            }
        }

        private void UpdateConnectionIndicator(bool isConnected)
        {
            Color accent = isConnected ? Color.FromRgb(129, 181, 143) : Color.FromRgb(208, 127, 127);
            SolidColorBrush strokeBrush = new SolidColorBrush(accent);
            SolidColorBrush fillBrush = new SolidColorBrush(Color.FromArgb(56, accent.R, accent.G, accent.B));
            SolidColorBrush solidBrush = new SolidColorBrush(accent);

            SocketBody.Stroke = strokeBrush;
            SocketBody.Fill = fillBrush;
            SocketHoleTop.Fill = solidBrush;
            SocketHoleBottom.Fill = solidBrush;
            PlugBody.Stroke = strokeBrush;
            PlugBody.Fill = fillBrush;
            PlugPinTop.Fill = solidBrush;
            PlugPinBottom.Fill = solidBrush;
            PlugCable.Stroke = solidBrush;
            PlugTransform.X = isConnected ? 0 : 8;
            DbStatusBadge.ToolTip = isConnected
                ? "Соединение с MS SQL Server установлено."
                : "Подключение к MS SQL Server не установлено.";
        }

        private string BuildConnectedMessage()
        {
            return string.IsNullOrWhiteSpace(_sqlConnectionService.ActiveConnectionLabel)
                ? "Соединение с MS SQL Server успешно установлено."
                : "Соединение с MS SQL Server успешно установлено: " + _sqlConnectionService.ActiveConnectionLabel + ".";
        }

        private void SetLoginMessage(string message, bool isError = true)
        {
            ApplyMessageState(LoginMessageContainer, LoginMessageText, message, isError);
        }

        private void SetSqlMessage(string message, bool isError = true)
        {
            ApplyMessageState(SqlMessageContainer, SqlMessageText, message, isError);
        }

        private static void ApplyMessageState(System.Windows.Controls.Border container, System.Windows.Controls.TextBlock textBlock, string message, bool isError)
        {
            bool hasMessage = !string.IsNullOrWhiteSpace(message);
            container.Visibility = hasMessage ? Visibility.Visible : Visibility.Collapsed;
            textBlock.Text = hasMessage ? message : string.Empty;

            if (!hasMessage)
            {
                return;
            }

            if (isError)
            {
                container.Background = new SolidColorBrush(Color.FromRgb(254, 228, 226));
                container.BorderBrush = new SolidColorBrush(Color.FromRgb(254, 202, 202));
                textBlock.Foreground = new SolidColorBrush(Color.FromRgb(180, 35, 24));
                return;
            }

            container.Background = new SolidColorBrush(Color.FromRgb(220, 252, 231));
            container.BorderBrush = new SolidColorBrush(Color.FromRgb(187, 247, 208));
            textBlock.Foreground = new SolidColorBrush(Color.FromRgb(22, 101, 52));
        }
    }
}
