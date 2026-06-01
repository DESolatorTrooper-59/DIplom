using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Tournaments.WPF.Services
{
    public sealed class EmbeddedTournamentPreview
    {
        public EmbeddedTournamentPreview(int tournamentId, string title, string resourcePath)
        {
            TournamentId = tournamentId;
            Title = title;
            ResourcePath = resourcePath;
            StorageKey = EmbeddedPreviewPrefix + resourcePath.Replace('\\', '/');
        }

        internal const string EmbeddedPreviewPrefix = "embedded:";

        public int TournamentId { get; }

        public string Title { get; }

        public string ResourcePath { get; }

        public string StorageKey { get; }
    }

    public sealed class TournamentPreviewStore
    {
        private const string StorageFileName = "tournament-previews.tsv";
        private const string PreviewDirectoryName = "tournament-previews";
        private const string EmbeddedPreviewPrefix = EmbeddedTournamentPreview.EmbeddedPreviewPrefix;

        private static readonly string ProjectDirectory = ResolveProjectDirectory();
        private static readonly string StoragePath = Path.Combine(ProjectDirectory, StorageFileName);
        private static readonly string PreviewDirectory = Path.Combine(ProjectDirectory, PreviewDirectoryName);
        private static readonly string PreviousAppDataStoragePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Tournaments.WPF",
            StorageFileName);
        private static readonly EmbeddedTournamentPreview DefaultEmbeddedPreview =
            new EmbeddedTournamentPreview(0, "Нет фото", "Assets/TournamentPreviews/no-photo.png");
        private static readonly IReadOnlyList<EmbeddedTournamentPreview> EmbeddedPreviews = new[]
        {
            DefaultEmbeddedPreview,
            new EmbeddedTournamentPreview(1, "Spring Brawl", "Assets/TournamentPreviews/tournament-1-KWTournament-TW-1.png"),
            new EmbeddedTournamentPreview(2, "World Champion Series 2v2", "Assets/TournamentPreviews/tournament-2-2v2.png"),
            new EmbeddedTournamentPreview(4, "World Champion Series 1v1", "Assets/TournamentPreviews/tournament-4-1v1.png"),
            new EmbeddedTournamentPreview(5, "13 KW Preview", "Assets/TournamentPreviews/tournament-5-13-KW-Preview.webp"),
            new EmbeddedTournamentPreview(0, "Buggy", "Assets/TournamentPreviews/Buggy.png"),
            new EmbeddedTournamentPreview(0, "Cyborg Green", "Assets/TournamentPreviews/CyborgGreen.png"),
            new EmbeddedTournamentPreview(0, "Cyborg Purple", "Assets/TournamentPreviews/CyborgPurple.png"),
            new EmbeddedTournamentPreview(0, "Desert MCV", "Assets/TournamentPreviews/DesertMCV.png"),
            new EmbeddedTournamentPreview(0, "Helicopters In Ocean", "Assets/TournamentPreviews/HelicoptersInOcean.png"),
            new EmbeddedTournamentPreview(0, "Mantis Tank", "Assets/TournamentPreviews/MantisTank.png"),
            new EmbeddedTournamentPreview(0, "Orca Art", "Assets/TournamentPreviews/Orca-Art.png"),
            new EmbeddedTournamentPreview(0, "Predator Tank", "Assets/TournamentPreviews/PredatorTank.png"),
            new EmbeddedTournamentPreview(0, "Red Alert 3", "Assets/TournamentPreviews/RedAlert-3.jpg"),
            new EmbeddedTournamentPreview(0, "ZOCOM SonicAir", "Assets/TournamentPreviews/ZOCOM-SonicAir.jpg"),
            new EmbeddedTournamentPreview(0, "Zone Raider", "Assets/TournamentPreviews/ZoneRaider.png")
        };

        private readonly Dictionary<int, string> _previewPaths = new Dictionary<int, string>();

        public TournamentPreviewStore()
        {
            Load();
            EnsureStorageFile();
        }

        public string GetPreviewPath(int tournamentId)
        {
            if (_previewPaths.TryGetValue(tournamentId, out string path))
            {
                return path;
            }

            return DefaultEmbeddedPreview.StorageKey;
        }

        public void SetPreviewPath(int tournamentId, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                RemovePreviewPath(tournamentId);
                return;
            }

            if (IsDefaultPreviewPath(path))
            {
                RemovePreviewPath(tournamentId);
                return;
            }

            string previousPath = GetPreviewPath(tournamentId);
            if (IsEmbeddedPreviewPath(path))
            {
                _previewPaths[tournamentId] = NormalizeEmbeddedPreviewPath(path);
                DeleteLocalPreviewFile(previousPath, path);
                Save();
                return;
            }

            string importedPath = ImportPreviewFile(tournamentId, path);
            _previewPaths[tournamentId] = importedPath;
            DeleteLocalPreviewFile(previousPath, importedPath);
            Save();
        }

        public IReadOnlyList<EmbeddedTournamentPreview> GetEmbeddedPreviews()
        {
            return EmbeddedPreviews;
        }

        public bool HasStoredPreviewPath(int tournamentId)
        {
            return _previewPaths.ContainsKey(tournamentId);
        }

        public string GetDefaultPreviewPath()
        {
            return DefaultEmbeddedPreview.StorageKey;
        }

        public static Uri CreatePreviewUri(string previewPath)
        {
            if (string.IsNullOrWhiteSpace(previewPath))
            {
                return null;
            }

            if (IsEmbeddedPreviewPath(previewPath))
            {
                string resourcePath = NormalizeEmbeddedPreviewPath(previewPath).Substring(EmbeddedPreviewPrefix.Length);
                return new Uri("pack://application:,,,/" + resourcePath, UriKind.Absolute);
            }

            return File.Exists(previewPath) ? new Uri(previewPath, UriKind.Absolute) : null;
        }

        public static bool IsEmbeddedPreviewPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                   path.Trim().StartsWith(EmbeddedPreviewPrefix, StringComparison.OrdinalIgnoreCase);
        }

        public static string GetPreviewFileName(string previewPath)
        {
            if (string.IsNullOrWhiteSpace(previewPath))
            {
                return null;
            }

            if (IsEmbeddedPreviewPath(previewPath))
            {
                string resourcePath = NormalizeEmbeddedPreviewPath(previewPath).Substring(EmbeddedPreviewPrefix.Length);
                return resourcePath.Split('/').LastOrDefault();
            }

            return Path.GetFileName(previewPath);
        }

        public static bool IsDefaultPreviewPath(string path)
        {
            return string.Equals(
                NormalizeEmbeddedPreviewPath(path),
                DefaultEmbeddedPreview.StorageKey,
                StringComparison.OrdinalIgnoreCase);
        }

        public void RemovePreviewPath(int tournamentId)
        {
            string previousPath = GetPreviewPath(tournamentId);
            if (_previewPaths.Remove(tournamentId))
            {
                DeleteLocalPreviewFile(previousPath, null);
                Save();
            }
        }

        private void Load()
        {
            _previewPaths.Clear();

            try
            {
                bool changed = false;
                bool loaded = LoadFromFile(StoragePath, ref changed);
                if (!loaded && !PathsEqual(StoragePath, PreviousAppDataStoragePath))
                {
                    loaded = LoadFromFile(PreviousAppDataStoragePath, ref changed);
                    changed = loaded;
                }

                if (changed)
                {
                    SaveSilently();
                }
            }
            catch
            {
            }
        }

        private bool LoadFromFile(string storagePath, ref bool changed)
        {
            if (string.IsNullOrWhiteSpace(storagePath) || !File.Exists(storagePath))
            {
                return false;
            }

            foreach (string line in File.ReadAllLines(storagePath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] parts = line.Split(new[] { '\t' }, 2);
                if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int tournamentId))
                {
                    continue;
                }

                string path = ResolveStoredPath(parts[1].Trim());
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                string localPath = TryImportExistingPreviewFile(tournamentId, path);
                if (!PathsEqual(path, localPath))
                {
                    changed = true;
                }

                _previewPaths[tournamentId] = localPath;
            }

            return true;
        }

        private void Save()
        {
            string directory = Path.GetDirectoryName(StoragePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllLines(
                StoragePath,
                _previewPaths
                    .OrderBy(entry => entry.Key)
                    .Select(entry => entry.Key.ToString(CultureInfo.InvariantCulture) + "\t" + ToStoredPath(entry.Value)));
        }

        private static string ResolveProjectDirectory()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string projectFallback = null;

            DirectoryInfo current = new DirectoryInfo(baseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "Tournaments.sln")))
                {
                    return current.FullName;
                }

                if (projectFallback == null && File.Exists(Path.Combine(current.FullName, "Tournaments.WPF.csproj")))
                {
                    projectFallback = current.FullName;
                }

                current = current.Parent;
            }

            return projectFallback ?? TrimDirectoryEnd(baseDirectory);
        }

        private static string ImportPreviewFile(int tournamentId, string sourcePath)
        {
            if (IsEmbeddedPreviewPath(sourcePath))
            {
                return NormalizeEmbeddedPreviewPath(sourcePath);
            }

            string fullSourcePath = Path.GetFullPath(sourcePath.Trim());
            if (!File.Exists(fullSourcePath))
            {
                throw new FileNotFoundException("Файл превью не найден.", fullSourcePath);
            }

            Directory.CreateDirectory(PreviewDirectory);
            if (IsInsideDirectory(fullSourcePath, PreviewDirectory))
            {
                return fullSourcePath;
            }

            string extension = Path.GetExtension(fullSourcePath);
            string sourceName = Path.GetFileNameWithoutExtension(fullSourcePath);
            string safeName = MakeSafeFileName(sourceName);
            string fileName = "tournament-" + tournamentId.ToString(CultureInfo.InvariantCulture) + "-" + safeName + extension;
            string destinationPath = Path.Combine(PreviewDirectory, fileName);

            if (!PathsEqual(fullSourcePath, destinationPath))
            {
                File.Copy(fullSourcePath, destinationPath, true);
            }

            return destinationPath;
        }

        private static string TryImportExistingPreviewFile(int tournamentId, string sourcePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourcePath) ||
                    IsEmbeddedPreviewPath(sourcePath) ||
                    !File.Exists(sourcePath) ||
                    IsInsideDirectory(sourcePath, PreviewDirectory))
                {
                    return sourcePath;
                }

                return ImportPreviewFile(tournamentId, sourcePath);
            }
            catch
            {
                return sourcePath;
            }
        }

        private static void DeleteLocalPreviewFile(string path, string pathToKeep)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) ||
                    IsEmbeddedPreviewPath(path) ||
                    PathsEqual(path, pathToKeep) ||
                    !IsInsideDirectory(path, PreviewDirectory) ||
                    !File.Exists(path))
                {
                    return;
                }

                File.Delete(path);
            }
            catch
            {
            }
        }

        private static string ResolveStoredPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (IsEmbeddedPreviewPath(path))
            {
                return NormalizeEmbeddedPreviewPath(path);
            }

            return Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(ProjectDirectory, path));
        }

        private static string ToStoredPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            if (IsEmbeddedPreviewPath(path))
            {
                return NormalizeEmbeddedPreviewPath(path);
            }

            string fullPath = Path.GetFullPath(path);
            return IsInsideDirectory(fullPath, ProjectDirectory)
                ? MakeRelativePath(ProjectDirectory, fullPath)
                : fullPath;
        }

        private static string MakeRelativePath(string fromDirectory, string toPath)
        {
            Uri fromUri = new Uri(AppendDirectorySeparator(Path.GetFullPath(fromDirectory)));
            Uri toUri = new Uri(Path.GetFullPath(toPath));

            if (!string.Equals(fromUri.Scheme, toUri.Scheme, StringComparison.OrdinalIgnoreCase))
            {
                return toPath;
            }

            return Uri.UnescapeDataString(fromUri.MakeRelativeUri(toUri).ToString())
                .Replace('/', Path.DirectorySeparatorChar);
        }

        private static bool IsInsideDirectory(string path, string directory)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            string fullPath = Path.GetFullPath(path);
            string fullDirectory = AppendDirectorySeparator(Path.GetFullPath(directory));
            return fullPath.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase);
        }

        private static string AppendDirectorySeparator(string path)
        {
            if (string.IsNullOrEmpty(path) ||
                path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }

        private static string TrimDirectoryEnd(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? path
                : path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool PathsEqual(string firstPath, string secondPath)
        {
            if (string.IsNullOrWhiteSpace(firstPath) || string.IsNullOrWhiteSpace(secondPath))
            {
                return false;
            }

            if (IsEmbeddedPreviewPath(firstPath) || IsEmbeddedPreviewPath(secondPath))
            {
                return string.Equals(
                    NormalizeEmbeddedPreviewPath(firstPath),
                    NormalizeEmbeddedPreviewPath(secondPath),
                    StringComparison.OrdinalIgnoreCase);
            }

            try
            {
                return string.Equals(
                    Path.GetFullPath(firstPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(secondPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(firstPath, secondPath, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string NormalizeEmbeddedPreviewPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            string trimmed = path.Trim().Replace('\\', '/');
            return trimmed.StartsWith(EmbeddedPreviewPrefix, StringComparison.OrdinalIgnoreCase)
                ? EmbeddedPreviewPrefix + trimmed.Substring(EmbeddedPreviewPrefix.Length)
                : trimmed;
        }

        private static string MakeSafeFileName(string value)
        {
            string source = string.IsNullOrWhiteSpace(value) ? "preview" : value.Trim();
            char[] invalidChars = Path.GetInvalidFileNameChars();
            string safeName = new string(source.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray());

            return string.IsNullOrWhiteSpace(safeName) ? "preview" : safeName;
        }

        private static void EnsureStorageFile()
        {
            try
            {
                string directory = Path.GetDirectoryName(StoragePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (!File.Exists(StoragePath))
                {
                    File.WriteAllText(StoragePath, string.Empty);
                }
            }
            catch
            {
            }
        }

        private void SaveSilently()
        {
            try
            {
                Save();
            }
            catch
            {
            }
        }
    }
}
