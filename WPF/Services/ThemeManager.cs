using System;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace Tournaments.WPF.Services
{
    public static class ThemeManager
    {
        private static readonly Uri LightThemeUri = new Uri("Themes/LightTheme.xaml", UriKind.Relative);
        private static readonly Uri DarkThemeUri = new Uri("Themes/DarkTheme.xaml", UriKind.Relative);
        private static readonly string ThemeSettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Tournaments.WPF",
            "theme.txt");

        public static AppTheme CurrentTheme { get; private set; } = AppTheme.Light;

        public static void Initialize()
        {
            ApplyTheme(LoadSavedTheme(), false);
        }

        public static void ApplyTheme(AppTheme theme, bool persist = true)
        {
            Application app = Application.Current;
            if (app == null)
            {
                CurrentTheme = theme;
                return;
            }

            ResourceDictionary resources = app.Resources;
            ResourceDictionary dictionary = new ResourceDictionary
            {
                Source = theme == AppTheme.Dark ? DarkThemeUri : LightThemeUri
            };

            int themeDictionaryIndex = FindThemeDictionaryIndex(resources);
            if (themeDictionaryIndex >= 0)
            {
                resources.MergedDictionaries[themeDictionaryIndex] = dictionary;
            }
            else
            {
                resources.MergedDictionaries.Insert(0, dictionary);
            }

            CurrentTheme = theme;
            if (persist)
            {
                SaveTheme(theme);
            }
        }

        public static Brush GetBrush(string key, Brush fallback = null)
        {
            return Application.Current?.TryFindResource(key) as Brush ?? fallback ?? Brushes.Transparent;
        }

        public static Color GetColor(string key, Color fallback)
        {
            SolidColorBrush brush = Application.Current?.TryFindResource(key) as SolidColorBrush;
            return brush == null ? fallback : brush.Color;
        }

        public static LinearGradientBrush CreateVerticalGradientBrush(string startBrushKey, string endBrushKey, Color fallbackStart, Color fallbackEnd)
        {
            return new LinearGradientBrush(
                GetColor(startBrushKey, fallbackStart),
                GetColor(endBrushKey, fallbackEnd),
                new Point(0.5, 0),
                new Point(0.5, 1));
        }

        private static int FindThemeDictionaryIndex(ResourceDictionary resources)
        {
            for (int index = 0; index < resources.MergedDictionaries.Count; index++)
            {
                Uri source = resources.MergedDictionaries[index].Source;
                if (source == null)
                {
                    continue;
                }

                string path = source.OriginalString;
                if (path.EndsWith("LightTheme.xaml", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return -1;
        }

        private static AppTheme LoadSavedTheme()
        {
            try
            {
                if (File.Exists(ThemeSettingsPath))
                {
                    string value = File.ReadAllText(ThemeSettingsPath).Trim();
                    if (Enum.TryParse(value, true, out AppTheme theme))
                    {
                        return theme;
                    }
                }
            }
            catch
            {
            }

            return AppTheme.Light;
        }

        private static void SaveTheme(AppTheme theme)
        {
            try
            {
                string directory = Path.GetDirectoryName(ThemeSettingsPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(ThemeSettingsPath, theme.ToString());
            }
            catch
            {
            }
        }
    }
}
