using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Tournaments.WPF.Services
{
    public sealed class TournamentPreviewStore
    {
        private static readonly string StoragePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Tournaments.WPF",
            "tournament-previews.tsv");

        private readonly Dictionary<int, string> _previewPaths = new Dictionary<int, string>();

        public TournamentPreviewStore()
        {
            Load();
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

            _previewPaths[tournamentId] = path.Trim();
            Save();
        }

        public void RemovePreviewPath(int tournamentId)
        {
            if (_previewPaths.Remove(tournamentId))
            {
                Save();
            }
        }

        private void Load()
        {
            _previewPaths.Clear();

            try
            {
                if (!File.Exists(StoragePath))
                {
                    return;
                }

                foreach (string line in File.ReadAllLines(StoragePath))
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

                    string path = parts[1].Trim();
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        _previewPaths[tournamentId] = path;
                    }
                }
            }
            catch
            {
            }
        }

        private void Save()
        {
            try
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
                        .Select(entry => entry.Key.ToString(CultureInfo.InvariantCulture) + "\t" + entry.Value));
            }
            catch
            {
            }
        }
    }
}
