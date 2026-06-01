using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tournaments.WPF.Services;

namespace Tournaments.WPF.Views
{
    public partial class TournamentPreviewPickerWindow : Window
    {
        private readonly TournamentPreviewStore _previewStore;
        private readonly List<PreviewChoice> _choices = new List<PreviewChoice>();
        private string _selectedPreviewPath;

        public TournamentPreviewPickerWindow(TournamentPreviewStore previewStore, string initialPreviewPath)
        {
            InitializeComponent();
            _previewStore = previewStore ?? throw new ArgumentNullException(nameof(previewStore));
            Title = "Превью турнира";
            BuildChoices();
            SelectInitialPreview(initialPreviewPath);
        }

        public string SelectedPreviewPath { get; private set; }

        private void BuildChoices()
        {
            foreach (EmbeddedTournamentPreview preview in _previewStore.GetEmbeddedPreviews())
            {
                ImageSource image = LoadPreviewImage(preview.StorageKey);
                if (image == null)
                {
                    continue;
                }

                _choices.Add(new PreviewChoice(preview.Title, preview.StorageKey, image));
            }

            PreviewsListBox.ItemsSource = _choices;
        }

        private void SelectInitialPreview(string initialPreviewPath)
        {
            string previewPath = string.IsNullOrWhiteSpace(initialPreviewPath)
                ? _previewStore.GetDefaultPreviewPath()
                : initialPreviewPath;

            PreviewChoice choice = FindChoice(previewPath) ?? FindChoice(_previewStore.GetDefaultPreviewPath());
            if (choice != null)
            {
                PreviewsListBox.SelectedItem = choice;
                ApplySelection(choice.StorageKey, choice.Title, choice.Image);
                return;
            }

            ApplySelection(_previewStore.GetDefaultPreviewPath(), "Нет фото", LoadPreviewImage(_previewStore.GetDefaultPreviewPath()));
        }

        private void PreviewsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PreviewChoice choice = PreviewsListBox.SelectedItem as PreviewChoice;
            if (choice == null)
            {
                return;
            }

            ApplySelection(choice.StorageKey, choice.Title, choice.Image);
        }

        private void ChooseFile_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*",
                Title = "Выберите изображение турнира"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            ImageSource image = LoadPreviewImage(dialog.FileName);
            if (image == null)
            {
                MessageBox.Show("Не удалось открыть выбранное изображение.", "Tournaments WPF", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            PreviewsListBox.SelectedItem = null;
            string title = Path.GetFileName(dialog.FileName);
            ApplySelection(dialog.FileName, string.IsNullOrWhiteSpace(title) ? "Свое изображение" : title, image);
        }

        private void DefaultPreview_Click(object sender, RoutedEventArgs e)
        {
            PreviewChoice choice = FindChoice(_previewStore.GetDefaultPreviewPath());
            if (choice != null)
            {
                PreviewsListBox.SelectedItem = choice;
                ApplySelection(choice.StorageKey, choice.Title, choice.Image);
                return;
            }

            ApplySelection(_previewStore.GetDefaultPreviewPath(), "Нет фото", LoadPreviewImage(_previewStore.GetDefaultPreviewPath()));
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            SelectedPreviewPath = string.IsNullOrWhiteSpace(_selectedPreviewPath)
                ? _previewStore.GetDefaultPreviewPath()
                : _selectedPreviewPath;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void ApplySelection(string previewPath, string title, ImageSource image)
        {
            _selectedPreviewPath = previewPath;
            CurrentPreviewImage.Source = image;
            CurrentPreviewText.Text = title;
        }

        private PreviewChoice FindChoice(string previewPath)
        {
            return _choices.FirstOrDefault(choice => PreviewPathsEqual(choice.StorageKey, previewPath));
        }

        private static bool PreviewPathsEqual(string firstPath, string secondPath)
        {
            return string.Equals(
                NormalizePreviewPath(firstPath),
                NormalizePreviewPath(secondPath),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizePreviewPath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Replace('\\', '/');
        }

        private static ImageSource LoadPreviewImage(string previewPath)
        {
            Uri previewUri = TournamentPreviewStore.CreatePreviewUri(previewPath);
            if (previewUri == null)
            {
                return null;
            }

            try
            {
                BitmapImage image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                image.UriSource = previewUri;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
        }

        private sealed class PreviewChoice
        {
            public PreviewChoice(string title, string storageKey, ImageSource image)
            {
                Title = title;
                StorageKey = storageKey;
                Image = image;
            }

            public string Title { get; }

            public string StorageKey { get; }

            public ImageSource Image { get; }
        }
    }
}
