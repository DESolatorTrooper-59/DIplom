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
        private readonly string _login;

        public MainWindow(DatabaseService database, string login)
        {
            InitializeComponent();
            _database = database;
            _crud = new EntityCrudService(database);
            _login = login;
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UserText.Text = "Организатор: " + _login + "\nРежим: " + _database.ModeTitle + "\nХранилище: " + _database.StorageLabel;
            EntitiesList.ItemsSource = BuildNavigationItems();
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
                PageHost.Content = new TournamentBracketPage(_database);
                return;
            }

            if (item.EntityDefinition != null)
            {
                PageHost.Content = new CrudPage(_database, _crud, _database.GetEffectiveDefinition(item.EntityDefinition), _login);
            }
        }

        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            LoginWindow loginWindow = new LoginWindow();
            Application.Current.MainWindow = loginWindow;
            loginWindow.Show();
            Close();
        }

        private static List<NavigationItem> BuildNavigationItems()
        {
            List<NavigationItem> items = new List<NavigationItem>
            {
                NavigationItem.ForBracket()
            };

            items.AddRange(EntityRegistry.All.Select(NavigationItem.ForEntity));
            return items;
        }
    }
}
