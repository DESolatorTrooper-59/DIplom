using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using Tournaments.WPF.Models;
using Tournaments.WPF.Services;

namespace Tournaments.WPF.Views
{
    public partial class LoginWindow : Window
    {
        private readonly DatabaseService _testDatabase;
        private DatabaseService _database;
        private DatabaseService _sqlDatabase;
        private readonly SqlServerConnectionService _sqlConnectionService;
        private bool _isConnecting;
        private bool _isPasswordVisible;
        private bool _isUpdatingPasswordFields;

        public LoginWindow()
        {
            InitializeComponent();
            Loaded += LoginWindow_Loaded;
            _sqlConnectionService = SqlServerConnectionService.Instance;
            SwitchMode(false);

            try
            {
                _testDatabase = DatabaseService.CreateInMemory();
                _testDatabase.EnsureOrganizerUser("admin", "password");
                ActivateTestMode(true);
                if (_sqlConnectionService.HasSuccessfulConnection)
                {
                    TryRestoreSqlMode();
                }
                else
                {
                    UpdateConnectionIndicator(false);
                }
            }
            catch (Exception ex)
            {
                SetLoginMessage("Не удалось инициализировать внутреннее хранилище: " + ex.Message);
                UpdateConnectionIndicator(false);
                UpdateTestModeIndicator(false);
            }
        }

        private void LoginWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdatePasswordVisibility(false);
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
            string password = GetLoginPassword();
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

        private void TestModeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnecting)
            {
                return;
            }

            SetSqlMessage(null);
            SwitchMode(false);
            ActivateTestMode(true);
            LoginTextBox.Focus();
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
            SetLoginMessage(null);
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
                DatabaseService sqlDatabase = null;
                SetConnectingState(true);
                await Task.Run(() =>
                {
                    _sqlConnectionService.Connect(settings);
                    sqlDatabase = DatabaseService.CreateSqlServer(_sqlConnectionService.ActiveConnectionString, _sqlConnectionService.ActiveConnectionLabel);
                    sqlDatabase.ValidateCompatibility();
                });

                ActivateSqlDatabase(sqlDatabase, true);
                SetSqlMessage(null);
                SwitchMode(false);
                LoginTextBox.Focus();
            }
            catch (Exception ex)
            {
                _sqlDatabase = null;
                _sqlConnectionService.ClearSuccessfulConnection();
                UpdateConnectionIndicator(false);
                ActivateTestMode(false);
                SetSqlMessage("Не удалось подключиться к MS SQL Server: " + ex.Message, true);
                SetLoginMessage(BuildTestModeMessage(), false);
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

        private void TogglePasswordVisibility_Click(object sender, RoutedEventArgs e)
        {
            UpdatePasswordVisibility(!_isPasswordVisible);
        }

        private void PasswordTextBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingPasswordFields)
            {
                return;
            }

            _isUpdatingPasswordFields = true;
            try
            {
                if (PasswordVisibleTextBox.Text != PasswordTextBox.Password)
                {
                    PasswordVisibleTextBox.Text = PasswordTextBox.Password;
                }
            }
            finally
            {
                _isUpdatingPasswordFields = false;
            }
        }

        private void PasswordVisibleTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingPasswordFields)
            {
                return;
            }

            _isUpdatingPasswordFields = true;
            try
            {
                if (PasswordTextBox.Password != PasswordVisibleTextBox.Text)
                {
                    PasswordTextBox.Password = PasswordVisibleTextBox.Text;
                }
            }
            finally
            {
                _isUpdatingPasswordFields = false;
            }
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

        private string GetLoginPassword()
        {
            return _isPasswordVisible ? PasswordVisibleTextBox.Text : PasswordTextBox.Password;
        }

        private void UpdatePasswordVisibility(bool isVisible)
        {
            _isPasswordVisible = isVisible;
            if (PasswordTextBox == null || PasswordVisibleTextBox == null || TogglePasswordVisibilityButton == null)
            {
                return;
            }

            _isUpdatingPasswordFields = true;
            try
            {
                if (isVisible)
                {
                    PasswordVisibleTextBox.Text = PasswordTextBox.Password;
                }
                else
                {
                    PasswordTextBox.Password = PasswordVisibleTextBox.Text;
                }
            }
            finally
            {
                _isUpdatingPasswordFields = false;
            }

            PasswordTextBox.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
            PasswordVisibleTextBox.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            TogglePasswordVisibilityButton.Content = isVisible ? "Скрыть" : "Показать";

            if (!IsLoaded)
            {
                return;
            }

            if (isVisible)
            {
                PasswordVisibleTextBox.Focus();
                PasswordVisibleTextBox.CaretIndex = PasswordVisibleTextBox.Text.Length;
            }
            else
            {
                PasswordTextBox.Focus();
            }
        }

        private void SwitchMode(bool isSqlMode)
        {
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
            TestModeButton.IsEnabled = !isConnecting;
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

        private void TryRestoreSqlMode()
        {
            try
            {
                DatabaseService sqlDatabase = DatabaseService.CreateSqlServer(_sqlConnectionService.ActiveConnectionString, _sqlConnectionService.ActiveConnectionLabel);
                sqlDatabase.ValidateCompatibility();
                ActivateSqlDatabase(sqlDatabase, false);
                SetLoginMessage(BuildConnectedMessage(), false);
            }
            catch
            {
                _sqlDatabase = null;
                _sqlConnectionService.ClearSuccessfulConnection();
                UpdateConnectionIndicator(false);
                ActivateTestMode(false);
            }
        }

        private void ActivateTestMode(bool showMessage)
        {
            if (_testDatabase == null)
            {
                UpdateTestModeIndicator(false);
                return;
            }

            _database = _testDatabase;
            UpdateTestModeIndicator(true);
            if (!_sqlConnectionService.HasSuccessfulConnection)
            {
                UpdateConnectionIndicator(false);
            }

            if (showMessage)
            {
                SetLoginMessage(BuildTestModeMessage(), false);
            }
        }

        private void ActivateSqlDatabase(DatabaseService sqlDatabase, bool showMessage)
        {
            _sqlDatabase = sqlDatabase ?? throw new ArgumentNullException(nameof(sqlDatabase));
            _database = _sqlDatabase;
            UpdateConnectionIndicator(true);
            UpdateTestModeIndicator(false);
            if (showMessage)
            {
                SetLoginMessage(BuildConnectedMessage(), false);
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
            DbStatusBadge.ToolTip = isConnected ? "Соединение с MS SQL Server установлено." : "Подключение к MS SQL Server не установлено.";
        }

        private void UpdateTestModeIndicator(bool isActive)
        {
            Color accent = isActive ? Color.FromRgb(99, 163, 171) : Color.FromRgb(148, 163, 184);
            TestModeBadge.BorderBrush = new SolidColorBrush(accent);
            TestModeBadge.Background = new SolidColorBrush(Color.FromArgb(isActive ? (byte)56 : (byte)24, accent.R, accent.G, accent.B));
            SolidColorBrush iconBrush = new SolidColorBrush(accent);

            RobotHead.Stroke = iconBrush;
            RobotAntenna.Stroke = iconBrush;
            RobotArmLeft.Stroke = iconBrush;
            RobotArmRight.Stroke = iconBrush;
            RobotHead.Fill = new SolidColorBrush(Color.FromArgb(36, accent.R, accent.G, accent.B));
            RobotAntennaTip.Fill = iconBrush;
            RobotEyeLeft.Fill = iconBrush;
            RobotEyeRight.Fill = iconBrush;
            RobotMouth.Fill = iconBrush;
            TestModeButton.ToolTip = isActive ? "Активен тестовый режим." : "Переключиться на тестовый режим.";
        }

        private string BuildConnectedMessage()
        {
            return string.IsNullOrWhiteSpace(_sqlConnectionService.ActiveConnectionLabel)
                ? "Активен режим MS SQL Server. Вход и данные приложения используют подключённую базу данных."
                : "Активен режим MS SQL Server: " + _sqlConnectionService.ActiveConnectionLabel + ".";
        }

        private static string BuildTestModeMessage()
        {
            return "Активен тестовый режим. Для демонстрационной версии можно войти как admin / password.";
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
