using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Tournaments.WPF.Models;
using Tournaments.WPF.Services;

namespace Tournaments.WPF.Views
{
    public partial class MainWindow : Window
    {
        private readonly DatabaseService _database;
        private readonly EntityCrudService _crud;
        private string _currentLogin;
        private readonly UserRole _role;

        public MainWindow(DatabaseService database, string login, UserRole role)
        {
            InitializeComponent();
            _database = database;
            _crud = new EntityCrudService(database);
            _currentLogin = login;
            _role = role;
            UpdateThemeToggleState();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshAccountPanel();
            EntitiesList.ItemsSource = BuildNavigationItems(_role);
            EntitiesList.DisplayMemberPath = "Title";
            if (EntitiesList.Items.Count > 0)
            {
                EntitiesList.SelectedIndex = 0;
            }
        }

        private void EntitiesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            NavigationItem item = EntitiesList.SelectedItem as NavigationItem;
            NavigateTo(item);
        }

        public bool OpenEntityPage(string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
            {
                return false;
            }

            NavigationItem target = EntitiesList.Items
                .OfType<NavigationItem>()
                .FirstOrDefault(item => item.EntityDefinition != null &&
                    string.Equals(item.EntityDefinition.TableName, tableName, StringComparison.OrdinalIgnoreCase));

            if (target == null)
            {
                return false;
            }

            if (!ReferenceEquals(EntitiesList.SelectedItem, target))
            {
                EntitiesList.SelectedItem = target;
            }
            else
            {
                NavigateTo(target);
            }

            return true;
        }

        private void NavigateTo(NavigationItem item)
        {
            if (item == null)
            {
                return;
            }

            if (item.PageKey == "Bracket")
            {
                PageHost.Content = new TournamentBracketPage(_database, _role, _currentLogin);
                return;
            }

            if (item.EntityDefinition != null)
            {
                if (string.Equals(item.EntityDefinition.TableName, "Tournaments", StringComparison.OrdinalIgnoreCase))
                {
                    PageHost.Content = new TournamentCatalogPage(_database, _crud, _currentLogin, _role);
                    return;
                }

                if (string.Equals(item.EntityDefinition.TableName, "Teams", StringComparison.OrdinalIgnoreCase))
                {
                    PageHost.Content = new TeamCatalogPage(_database, _crud, _role);
                    return;
                }

                PageHost.Content = new CrudPage(_database, _crud, _database.GetEffectiveDefinition(item.EntityDefinition), _currentLogin, _role);
            }
        }

        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            LoginWindow loginWindow = new LoginWindow();
            Application.Current.MainWindow = loginWindow;
            loginWindow.Show();
            Close();
        }

        private void ThemeToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ThemeManager.ApplyTheme(ThemeManager.CurrentTheme == AppTheme.Light ? AppTheme.Dark : AppTheme.Light);
            UpdateThemeToggleState();
        }

        private void ProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (_role == UserRole.Guest)
            {
                return;
            }

            try
            {
                UserProfileWindow profileWindow = new UserProfileWindow(_database, _currentLogin, _role)
                {
                    Owner = this
                };

                if (profileWindow.ShowDialog() == true)
                {
                    _currentLogin = profileWindow.UpdatedLogin;
                    RefreshAccountPanel();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось открыть профиль: " + ex.Message, "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void UpdateThemeToggleState()
        {
            bool isLightTheme = ThemeManager.CurrentTheme == AppTheme.Light;
            ThemeToggleIcon.Text = isLightTheme ? "☀" : "☾";
            ThemeToggleButton.ToolTip = isLightTheme
                ? "Светлая тема активна. Нажмите, чтобы включить тёмную."
                : "Тёмная тема активна. Нажмите, чтобы включить светлую.";
        }

        private void RefreshAccountPanel()
        {
            string displayName = AccessPolicy.GetAccountTitle(_currentLogin, _role);
            bool canOpenProfile = _role != UserRole.Guest;

            if (canOpenProfile)
            {
                try
                {
                    UserProfileData profile = _database.GetUserProfile(_currentLogin, _role);
                    if (!string.IsNullOrWhiteSpace(profile.Nickname))
                    {
                        displayName = profile.Nickname;
                    }

                    ProfileButton.IsEnabled = profile.CanEditExtendedProfile || profile.CanChangePassword;
                }
                catch
                {
                    ProfileButton.IsEnabled = false;
                }
            }

            ProfileButton.Visibility = canOpenProfile ? Visibility.Visible : Visibility.Collapsed;
            ProfileNameText.Text = displayName;
            UserText.Text = "Учетная запись: " + AccessPolicy.GetAccountTitle(_currentLogin, _role) +
                "\nРоль: " + AccessPolicy.GetRoleTitle(_role) +
                "\nРежим: " + _database.ModeTitle +
                "\nХранилище: " + _database.StorageLabel;
        }

        private static List<NavigationItem> BuildNavigationItems(UserRole role)
        {
            List<NavigationItem> items = new List<NavigationItem>();
            List<NavigationItem> entityItems = EntityRegistry.All
                .Where(definition => AccessPolicy.CanAccessEntity(role, definition.TableName))
                .Select(NavigationItem.ForEntity)
                .ToList();

            NavigationItem tournamentsItem = entityItems.FirstOrDefault(item =>
                item.EntityDefinition != null &&
                string.Equals(item.EntityDefinition.TableName, "Tournaments", StringComparison.OrdinalIgnoreCase));
            if (tournamentsItem != null)
            {
                items.Add(tournamentsItem);
                entityItems.Remove(tournamentsItem);
            }

            if (AccessPolicy.CanAccessBracket(role))
            {
                items.Add(NavigationItem.ForBracket());
            }

            items.AddRange(entityItems);
            return items;
        }
    }
}
