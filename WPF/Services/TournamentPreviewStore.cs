using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Tournaments.WPF.Services
{
    public sealed class TournamentPreviewStore
    {
        private const string StorageFileName = "tournament-previews.tsv";
        private const string PreviewDirectoryName = "tournament-previews";

        private static readonly string ProjectDirectory = ResolveProjectDirectory();
        private static readonly string StoragePath = Path.Combine(ProjectDirectory, StorageFileName);
        private static readonly string PreviewDirectory = Path.Combine(ProjectDirectory, PreviewDirectoryName);
        private static readonly string LegacyStoragePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Tournaments.WPF",
            StorageFileName);

        private readonly Dictionary<int, string> _previewPaths = new Dictionary<int, string>();

        public TournamentPreviewStore()
        {
            Load();
            EnsureStorageFile();
        }

        public string GetPreviewPath(int tournamentId)
        {
            return _previewPaths.TryGetValue(tournamentId, out string path) ? path : null;
        }

        public void SetPreviewPath(int tournamentId, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                RemovePreviewPath(tournamentId);
                return;
            }

            string previousPath = GetPreviewPath(tournamentId);
            string importedPath = ImportPreviewFile(tournamentId, path);
            _previewPaths[tournamentId] = importedPath;
            DeleteLocalPreviewFile(previousPath, importedPath);
            Save();
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
                if (!loaded && !PathsEqual(StoragePath, LegacyStoragePath))
                {
                    loaded = LoadFromFile(LegacyStoragePath, ref changed);
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
