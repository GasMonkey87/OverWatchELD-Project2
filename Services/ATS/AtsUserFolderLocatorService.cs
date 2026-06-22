using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OverWatchELD.Services.ATS
{
    /// <summary>
    /// Locates the current user's American Truck Simulator folders without hardcoded personal paths.
    /// Public-release safe: no personal defaults.
    /// </summary>
    public sealed class AtsUserFolderLocatorService
    {
        public string DocumentsRoot { get; }

        public AtsUserFolderLocatorService()
        {
            DocumentsRoot = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        public string GetAtsRoot()
        {
            var path = Path.Combine(DocumentsRoot, "American Truck Simulator");
            return Directory.Exists(path) ? path : string.Empty;
        }

        public string GetModFolder()
        {
            var root = GetAtsRoot();
            if (string.IsNullOrWhiteSpace(root)) return string.Empty;

            var path = Path.Combine(root, "mod");
            return Directory.Exists(path) ? path : string.Empty;
        }

        public string GetProfilesFolder()
        {
            var root = GetAtsRoot();
            if (string.IsNullOrWhiteSpace(root)) return string.Empty;

            var path = Path.Combine(root, "profiles");
            return Directory.Exists(path) ? path : string.Empty;
        }

        public IReadOnlyList<string> GetProfileFolders()
        {
            var profiles = GetProfilesFolder();
            if (string.IsNullOrWhiteSpace(profiles)) return Array.Empty<string>();

            return Directory.GetDirectories(profiles)
                .OrderByDescending(d => Directory.GetLastWriteTimeUtc(d))
                .ToArray();
        }

        public string GetMostRecentProfileFolder()
        {
            return GetProfileFolders().FirstOrDefault() ?? string.Empty;
        }

        public IReadOnlyList<string> GetSaveFolders(string? profileFolder = null)
        {
            var profile = string.IsNullOrWhiteSpace(profileFolder) ? GetMostRecentProfileFolder() : profileFolder;
            if (string.IsNullOrWhiteSpace(profile)) return Array.Empty<string>();

            var saveRoot = Path.Combine(profile, "save");
            if (!Directory.Exists(saveRoot)) return Array.Empty<string>();

            return Directory.GetDirectories(saveRoot)
                .OrderByDescending(d => Directory.GetLastWriteTimeUtc(d))
                .ToArray();
        }

        public string GetMostRecentSaveFolder(string? profileFolder = null)
        {
            return GetSaveFolders(profileFolder).FirstOrDefault() ?? string.Empty;
        }
    }
}
