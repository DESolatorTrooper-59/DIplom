using System;
using System.Windows;
using Tournaments.WPF.Models;
using Tournaments.WPF.Services;

namespace Tournaments.WPF.Views
{
    public partial class UserProfileWindow : Window
    {
        private readonly DatabaseService _database;
        private readonly UserRole _role;
        private readonly string _currentLogin;
        private readonly UserProfileData _profile;

        public UserProfileWindow(DatabaseService database, string currentLogin, UserRole role)
        {
            InitializeComponent();
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _currentLogin = currentLogin ?? string.Empty;
            _role = role;
            _profile = _database.GetUserProfile(_currentLogin, _role);
            UpdatedLogin = _currentLogin;
            ApplyProfile();
        }

        public string UpdatedLogin { get; private set; }

        private void ApplyProfile()
        {
            LoginTextBox.Text = _profile.Login ?? string.Empty;
            NicknameTextBox.Text = _profile.Nickname ?? string.Empty;
            RealNameTextBox.Text = _profile.RealName ?? string.Empty;
            CountryTextBox.Text = _profile.Country ?? string.Empty;
            BirthDatePicker.SelectedDate = _profile.BirthDate;
            BirthDatePicker.DisplayDateEnd = DateTime.Today;

            bool canEditExtendedProfile = _profile.CanEditExtendedProfile;
            NicknameTextBox.IsEnabled = canEditExtendedProfile;
            RealNameTextBox.IsEnabled = canEditExtendedProfile;
            CountryTextBox.IsEnabled = canEditExtendedProfile;
            BirthDatePicker.IsEnabled = canEditExtendedProfile;

            bool canChangePassword = _profile.CanChangePassword;
            NewPasswordBox.IsEnabled = canChangePassword;
            ConfirmPasswordBox.IsEnabled = canChangePassword;

            if (!canEditExtendedProfile)
            {
                InfoBanner.Visibility = Visibility.Visible;
                InfoBannerText.Text = _role == UserRole.Administrator
                    ? "Для администратора в этом окне доступна смена пароля. Игровые поля редактируются только для учетной записи игрока."
                    : "Профиль доступен только для авторизованного пользователя.";
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string newPassword = ReadPasswordChange();
                string updatedLogin = _database.UpdateUserProfile(
                    _role,
                    _currentLogin,
                    NicknameTextBox.Text,
                    RealNameTextBox.Text,
                    CountryTextBox.Text,
                    BirthDatePicker.SelectedDate,
                    newPassword);

                UpdatedLogin = string.IsNullOrWhiteSpace(updatedLogin) ? _currentLogin : updatedLogin;
                DialogResult = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private string ReadPasswordChange()
        {
            string newPassword = NewPasswordBox.Password ?? string.Empty;
            string confirmPassword = ConfirmPasswordBox.Password ?? string.Empty;
            bool hasPasswordInput = !string.IsNullOrWhiteSpace(newPassword) || !string.IsNullOrWhiteSpace(confirmPassword);

            if (!hasPasswordInput)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(newPassword) || string.IsNullOrWhiteSpace(confirmPassword))
            {
                throw new InvalidOperationException("Введите новый пароль в оба поля.");
            }

            if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Подтверждение пароля не совпадает.");
            }

            return newPassword;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
