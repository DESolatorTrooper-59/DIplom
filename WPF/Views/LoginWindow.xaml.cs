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
        private enum LoginWindowMode
        {
            Login,
            Sql,
            Register
        }

        private DatabaseService _database;
        private DatabaseService _sqlDatabase;
        private readonly SqlServerConnectionService _sqlConnectionService;
        private bool _isConnecting;
        private bool _isPasswordVisible;
        private bool _isUpdatingPasswordFields;
        private bool _isRegisterPasswordVisible;
        private bool _isUpdatingRegisterPasswordFields;
        private string _loginMessage;
        private bool _isLoginMessageError = true;
        private string _sqlMessage;
        private bool _isSqlMessageError = true;
        private string _registerMessage;
        private bool _isRegisterMessageError = true;

        public LoginWindow()
        {
            InitializeComponent();
            Loaded += LoginWindow_Loaded;
            _sqlConnectionService = SqlServerConnectionService.Instance;
            SwitchMode(LoginWindowMode.Login);
            UpdateThemeToggleState();
            UpdateConnectionIndicator(false);
            PopulateSqlSettings();
            UpdateDatabaseAvailabilityState();
            TryActivateConfiguredSqlDatabase();
        }

        private void LoginWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdatePasswordVisibility(false);
            RegisterBirthDatePicker.DisplayDateEnd = DateTime.Today;
            UpdateRegisterPasswordVisibility(false);
            if (_database == null && SqlModeGrid.Visibility == Visibility.Visible)
            {
                SqlServerTextBox.Focus();
                return;
            }

            LoginTextBox.Focus();
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            SetLoginMessage(null);

            if (_database == null)
            {
                SetLoginMessage(BuildDatabaseRequiredMessage());
                return;
            }

            string login = LoginTextBox.Text.Trim();
            string password = GetLoginPassword();
            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            {
                SetLoginMessage("Введите логин или никнейм и пароль.");
                return;
            }

            UserRole? role = _database.AuthenticateUser(login, password);
            if (!role.HasValue)
            {
                SetLoginMessage("Неверный логин или пароль.");
                return;
            }

            OpenMainWindow(login, role.Value);
        }

        private void Register_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnecting)
            {
                return;
            }

            if (_database == null)
            {
                SetLoginMessage(BuildDatabaseRequiredMessage());
                return;
            }

            ResetRegistrationForm();
            SwitchMode(LoginWindowMode.Register);
            RegisterNicknameTextBox.Focus();
        }

        private void GuestLogin_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnecting)
            {
                return;
            }

            SetLoginMessage(null);
            if (_database == null)
            {
                SetLoginMessage(BuildDatabaseRequiredMessage());
                return;
            }

            OpenMainWindow("Гость", UserRole.Guest);
        }

        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ApplyTheme(ThemeManager.CurrentTheme == AppTheme.Light ? AppTheme.Dark : AppTheme.Light);
        }

        private void SqlModeButton_Click(object sender, RoutedEventArgs e)
        {
            PopulateSqlSettings();
            SetSqlMessage(null);
            SwitchMode(LoginWindowMode.Sql);
        }

        private void BackToLogin_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnecting)
            {
                return;
            }

            SwitchMode(LoginWindowMode.Login);
            if (_database == null)
            {
                SetLoginMessage(BuildDatabaseRequiredMessage(), false);
            }

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
                SwitchMode(LoginWindowMode.Login);
                LoginTextBox.Focus();
            }
            catch (Exception ex)
            {
                DeactivateSqlDatabase();
                _sqlConnectionService.ClearSuccessfulConnection();
                SetSqlMessage("Не удалось подключиться к MS SQL Server: " + ex.Message, true);
                SetLoginMessage(BuildDatabaseRequiredMessage(), false);
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

        private void RegisterInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            CompleteRegistration_Click(sender, new RoutedEventArgs());
        }

        private void ApplyTheme(AppTheme theme)
        {
            ThemeManager.ApplyTheme(theme);
            UpdateThemeToggleState();
            RefreshThemeSensitiveVisuals();
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

        private void OpenMainWindow(string login, UserRole role)
        {
            MainWindow window = new MainWindow(_database, login, role);
            Application.Current.MainWindow = window;
            window.Show();
            Close();
        }

        private string GetLoginPassword()
        {
            return _isPasswordVisible ? PasswordVisibleTextBox.Text : PasswordTextBox.Password;
        }

        private void UpdateThemeToggleState()
        {
            bool isLightTheme = ThemeManager.CurrentTheme == AppTheme.Light;
            ApplyThemeBadgeState(ThemeToggleBadge, ThemeToggleIcon);
            ThemeToggleIcon.Text = isLightTheme ? "☀" : "☾";
            ThemeToggleButton.ToolTip = isLightTheme
                ? "Светлая тема активна. Нажмите, чтобы включить тёмную."
                : "Тёмная тема активна. Нажмите, чтобы включить светлую.";
        }

        private void RefreshThemeSensitiveVisuals()
        {
            ApplyMessageState(LoginMessageContainer, LoginMessageText, _loginMessage, _isLoginMessageError);
            ApplyMessageState(SqlMessageContainer, SqlMessageText, _sqlMessage, _isSqlMessageError);
            ApplyMessageState(RegisterMessageContainer, RegisterMessageText, _registerMessage, _isRegisterMessageError);
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

        private void CompleteRegistration_Click(object sender, RoutedEventArgs e)
        {
            SetRegisterMessage(null);

            if (_database == null)
            {
                SetRegisterMessage(BuildDatabaseRequiredMessage());
                return;
            }

            string nickname = RegisterNicknameTextBox.Text.Trim();
            DateTime? birthDate = RegisterBirthDatePicker.SelectedDate;
            string realName = RegisterRealNameTextBox.Text.Trim();
            string password = GetRegisterPassword();
            string confirmPassword = GetRegisterConfirmPassword();

            if (string.IsNullOrWhiteSpace(nickname))
            {
                SetRegisterMessage("Введите никнейм.");
                return;
            }

            if (!birthDate.HasValue)
            {
                SetRegisterMessage("Укажите дату рождения.");
                return;
            }

            if (birthDate.Value.Date > DateTime.Today)
            {
                SetRegisterMessage("Дата рождения не может быть больше текущей даты.");
                return;
            }

            if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(confirmPassword))
            {
                SetRegisterMessage("Введите пароль и подтверждение пароля.");
                return;
            }

            if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
            {
                SetRegisterMessage("Пароли не совпадают.");
                return;
            }

            try
            {
                _database.RegisterPlayer(nickname, birthDate.Value.Date, realName, password);
                LoginTextBox.Text = nickname;
                PasswordTextBox.Password = string.Empty;
                PasswordVisibleTextBox.Text = string.Empty;
                ResetRegistrationForm();
                SwitchMode(LoginWindowMode.Login);
                SetLoginMessage("Регистрация завершена. Теперь войдите под своим никнеймом.", false);
                LoginTextBox.Focus();
                LoginTextBox.SelectAll();
            }
            catch (Exception ex)
            {
                SetRegisterMessage(ex.Message);
            }
        }

        private void RegisterTogglePasswordVisibility_Click(object sender, RoutedEventArgs e)
        {
            UpdateRegisterPasswordVisibility(!_isRegisterPasswordVisible);
        }

        private void RegisterPasswordTextBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingRegisterPasswordFields)
            {
                return;
            }

            _isUpdatingRegisterPasswordFields = true;
            try
            {
                if (RegisterPasswordVisibleTextBox.Text != RegisterPasswordTextBox.Password)
                {
                    RegisterPasswordVisibleTextBox.Text = RegisterPasswordTextBox.Password;
                }
            }
            finally
            {
                _isUpdatingRegisterPasswordFields = false;
            }
        }

        private void RegisterPasswordVisibleTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingRegisterPasswordFields)
            {
                return;
            }

            _isUpdatingRegisterPasswordFields = true;
            try
            {
                if (RegisterPasswordTextBox.Password != RegisterPasswordVisibleTextBox.Text)
                {
                    RegisterPasswordTextBox.Password = RegisterPasswordVisibleTextBox.Text;
                }
            }
            finally
            {
                _isUpdatingRegisterPasswordFields = false;
            }
        }

        private void RegisterConfirmPasswordTextBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingRegisterPasswordFields)
            {
                return;
            }

            _isUpdatingRegisterPasswordFields = true;
            try
            {
                if (RegisterConfirmPasswordVisibleTextBox.Text != RegisterConfirmPasswordTextBox.Password)
                {
                    RegisterConfirmPasswordVisibleTextBox.Text = RegisterConfirmPasswordTextBox.Password;
                }
            }
            finally
            {
                _isUpdatingRegisterPasswordFields = false;
            }
        }

        private void RegisterConfirmPasswordVisibleTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingRegisterPasswordFields)
            {
                return;
            }

            _isUpdatingRegisterPasswordFields = true;
            try
            {
                if (RegisterConfirmPasswordTextBox.Password != RegisterConfirmPasswordVisibleTextBox.Text)
                {
                    RegisterConfirmPasswordTextBox.Password = RegisterConfirmPasswordVisibleTextBox.Text;
                }
            }
            finally
            {
                _isUpdatingRegisterPasswordFields = false;
            }
        }

        private string GetRegisterPassword()
        {
            return _isRegisterPasswordVisible ? RegisterPasswordVisibleTextBox.Text : RegisterPasswordTextBox.Password;
        }

        private string GetRegisterConfirmPassword()
        {
            return _isRegisterPasswordVisible ? RegisterConfirmPasswordVisibleTextBox.Text : RegisterConfirmPasswordTextBox.Password;
        }

        private void UpdateRegisterPasswordVisibility(bool isVisible)
        {
            _isRegisterPasswordVisible = isVisible;
            if (RegisterPasswordTextBox == null ||
                RegisterPasswordVisibleTextBox == null ||
                RegisterConfirmPasswordTextBox == null ||
                RegisterConfirmPasswordVisibleTextBox == null ||
                RegisterTogglePasswordVisibilityButton == null)
            {
                return;
            }

            _isUpdatingRegisterPasswordFields = true;
            try
            {
                if (isVisible)
                {
                    RegisterPasswordVisibleTextBox.Text = RegisterPasswordTextBox.Password;
                    RegisterConfirmPasswordVisibleTextBox.Text = RegisterConfirmPasswordTextBox.Password;
                }
                else
                {
                    RegisterPasswordTextBox.Password = RegisterPasswordVisibleTextBox.Text;
                    RegisterConfirmPasswordTextBox.Password = RegisterConfirmPasswordVisibleTextBox.Text;
                }
            }
            finally
            {
                _isUpdatingRegisterPasswordFields = false;
            }

            RegisterPasswordTextBox.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
            RegisterPasswordVisibleTextBox.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            RegisterConfirmPasswordTextBox.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
            RegisterConfirmPasswordVisibleTextBox.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            RegisterTogglePasswordVisibilityButton.Content = isVisible ? "Скрыть" : "Показать";

            if (!IsLoaded || RegistrationModeGrid.Visibility != Visibility.Visible)
            {
                return;
            }

            if (isVisible)
            {
                RegisterConfirmPasswordVisibleTextBox.Focus();
                RegisterConfirmPasswordVisibleTextBox.CaretIndex = RegisterConfirmPasswordVisibleTextBox.Text.Length;
            }
            else
            {
                RegisterConfirmPasswordTextBox.Focus();
            }
        }

        private void ResetRegistrationForm()
        {
            RegisterNicknameTextBox.Text = string.Empty;
            RegisterBirthDatePicker.SelectedDate = null;
            RegisterRealNameTextBox.Text = string.Empty;
            RegisterPasswordTextBox.Password = string.Empty;
            RegisterPasswordVisibleTextBox.Text = string.Empty;
            RegisterConfirmPasswordTextBox.Password = string.Empty;
            RegisterConfirmPasswordVisibleTextBox.Text = string.Empty;
            UpdateRegisterPasswordVisibility(false);
            SetRegisterMessage(null);
        }

        private void SwitchMode(LoginWindowMode mode)
        {
            LoginModeGrid.Visibility = mode == LoginWindowMode.Login ? Visibility.Visible : Visibility.Collapsed;
            SqlModeGrid.Visibility = mode == LoginWindowMode.Sql ? Visibility.Visible : Visibility.Collapsed;
            RegistrationModeGrid.Visibility = mode == LoginWindowMode.Register ? Visibility.Visible : Visibility.Collapsed;

            switch (mode)
            {
                case LoginWindowMode.Sql:
                    TitleText.Text = "MS SQL Server";
                    SubtitleText.Text = "Укажите параметры сервера и проверьте соединение с базой данных прямо в этом окне.";
                    break;
                case LoginWindowMode.Register:
                    TitleText.Text = "Регистрация";
                    SubtitleText.Text = "Зарегистрируйтесь в подключенной базе данных для участия в турнирах и просмотра данных.";
                    break;
                default:
                    TitleText.Text = "Tournaments WPF";
                    SubtitleText.Text = "Подключитесь к MS SQL Server и войдите как администратор, игрок или гость.";
                    break;
            }
        }

        private void SetConnectingState(bool isConnecting)
        {
            _isConnecting = isConnecting;
            SqlBusyOverlay.Visibility = isConnecting ? Visibility.Visible : Visibility.Collapsed;
            ConnectSqlButton.IsEnabled = !isConnecting;
            SqlModeButton.IsEnabled = !isConnecting;
            WindowsAuthCheckBox.IsEnabled = !isConnecting;
            SqlServerTextBox.IsEnabled = !isConnecting;
            SqlDatabaseTextBox.IsEnabled = !isConnecting;
            ApplyAuthenticationMode();
            UpdateDatabaseAvailabilityState();
            if (isConnecting)
            {
                SqlUserTextBox.IsEnabled = false;
                SqlPasswordTextBox.IsEnabled = false;
            }
        }

        private void TryActivateConfiguredSqlDatabase()
        {
            SqlServerConnectionSettings settings = _sqlConnectionService.GetSettings();
            bool hasConfiguredConnection =
                !string.IsNullOrWhiteSpace(settings.Server) &&
                !string.IsNullOrWhiteSpace(settings.Database);

            if (!hasConfiguredConnection)
            {
                DeactivateSqlDatabase();
                SwitchMode(LoginWindowMode.Sql);
                SetSqlMessage(BuildDatabaseRequiredMessage(), false);
                SetLoginMessage(BuildDatabaseRequiredMessage(), false);
                return;
            }

            try
            {
                _sqlConnectionService.Connect(settings);
                DatabaseService sqlDatabase = DatabaseService.CreateSqlServer(_sqlConnectionService.ActiveConnectionString, _sqlConnectionService.ActiveConnectionLabel);
                sqlDatabase.ValidateCompatibility();
                ActivateSqlDatabase(sqlDatabase, false);
                SetSqlMessage(null);
                SetLoginMessage(BuildConnectedMessage(), false);
            }
            catch (Exception ex)
            {
                DeactivateSqlDatabase();
                _sqlConnectionService.ClearSuccessfulConnection();
                SwitchMode(LoginWindowMode.Sql);
                SetSqlMessage("Не удалось подключиться к настроенной базе MS SQL Server: " + ex.Message, true);
                SetLoginMessage(BuildDatabaseRequiredMessage(), false);
            }
        }

        private void ActivateSqlDatabase(DatabaseService sqlDatabase, bool showMessage)
        {
            _sqlDatabase = sqlDatabase ?? throw new ArgumentNullException(nameof(sqlDatabase));
            _database = _sqlDatabase;
            UpdateConnectionIndicator(true);
            UpdateDatabaseAvailabilityState();
            if (showMessage)
            {
                SetLoginMessage(BuildConnectedMessage(), false);
            }
        }

        private void DeactivateSqlDatabase()
        {
            _sqlDatabase = null;
            _database = null;
            UpdateConnectionIndicator(false);
            UpdateDatabaseAvailabilityState();
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

        private void UpdateDatabaseAvailabilityState()
        {
            bool hasDatabase = _database != null;
            bool canUseDatabaseActions = hasDatabase && !_isConnecting;

            LoginButton.IsEnabled = canUseDatabaseActions;
            GuestLoginButton.IsEnabled = canUseDatabaseActions;
            RegisterModeButton.IsEnabled = canUseDatabaseActions;
        }

        private string BuildConnectedMessage()
        {
            return string.IsNullOrWhiteSpace(_sqlConnectionService.ActiveConnectionLabel)
                ? "Активен режим MS SQL Server. Вход и данные приложения используют подключённую базу данных."
                : "Активен режим MS SQL Server: " + _sqlConnectionService.ActiveConnectionLabel + ".";
        }

        private static string BuildDatabaseRequiredMessage()
        {
            return "Подключите MS SQL Server, чтобы войти, зарегистрироваться и открыть приложение.";
        }

        private void SetLoginMessage(string message, bool isError = true)
        {
            _loginMessage = message;
            _isLoginMessageError = isError;
            ApplyMessageState(LoginMessageContainer, LoginMessageText, message, isError);
        }

        private void SetSqlMessage(string message, bool isError = true)
        {
            _sqlMessage = message;
            _isSqlMessageError = isError;
            ApplyMessageState(SqlMessageContainer, SqlMessageText, message, isError);
        }

        private void SetRegisterMessage(string message, bool isError = true)
        {
            _registerMessage = message;
            _isRegisterMessageError = isError;
            ApplyMessageState(RegisterMessageContainer, RegisterMessageText, message, isError);
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
                container.Background = ThemeManager.GetBrush("MessageErrorBackgroundBrush", new SolidColorBrush(Color.FromRgb(254, 228, 226)));
                container.BorderBrush = ThemeManager.GetBrush("MessageErrorBorderBrush", new SolidColorBrush(Color.FromRgb(254, 202, 202)));
                textBlock.Foreground = ThemeManager.GetBrush("MessageErrorForegroundBrush", new SolidColorBrush(Color.FromRgb(180, 35, 24)));
                return;
            }

            container.Background = ThemeManager.GetBrush("MessageSuccessBackgroundBrush", new SolidColorBrush(Color.FromRgb(220, 252, 231)));
            container.BorderBrush = ThemeManager.GetBrush("MessageSuccessBorderBrush", new SolidColorBrush(Color.FromRgb(187, 247, 208)));
            textBlock.Foreground = ThemeManager.GetBrush("MessageSuccessForegroundBrush", new SolidColorBrush(Color.FromRgb(22, 101, 52)));
        }

        private static void ApplyThemeBadgeState(Border badge, TextBlock icon)
        {
            badge.Background = ThemeManager.GetBrush(
                "ThemeToggleBackgroundBrush",
                Brushes.Transparent);
            badge.BorderBrush = ThemeManager.GetBrush(
                "ThemeToggleBorderBrush",
                Brushes.Transparent);
            icon.Foreground = ThemeManager.GetBrush(
                "ThemeToggleIconBrush",
                ThemeManager.GetBrush("TextPrimaryBrush", Brushes.White));
        }
    }
}
