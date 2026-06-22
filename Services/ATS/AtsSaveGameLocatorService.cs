using System;
using System.IO;
using System.Linq;

namespace OverWatchELD.Services.ATS
{
    public sealed class AtsSaveLocatorResult
    {
        public bool Success { get; set; }

        public string Message { get; set; } = "";

        public string? GameSiiPath { get; set; }

        // Old injector compatibility
        public string? SelectedProfile { get; set; }
        public string? SelectedSave { get; set; }

        // Additional injector compatibility
        public string? ProfileName { get; set; }
        public string? ProfileId { get; set; }
        public string? SaveName { get; set; }
    }

    public static class AtsSaveGameLocatorService
    {
        public static AtsSaveLocatorResult LocateLatestSave()
        {
            try
            {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                var atsRoot = Path.Combine(docs, "American Truck Simulator");
                var profilesRoot = Path.Combine(atsRoot, "profiles");

                if (!Directory.Exists(profilesRoot))
                {
                    return new AtsSaveLocatorResult
                    {
                        Success = false,
                        Message = "ATS profiles folder not found."
                    };
                }

                DateTime newestTime = DateTime.MinValue;
                string? newestGameSii = null;
                string? newestProfile = null;
                string? newestSave = null;

                foreach (var profile in Directory.EnumerateDirectories(profilesRoot))
                {
                    var saveRoot = Path.Combine(profile, "save");

                    if (!Directory.Exists(saveRoot))
                        continue;

                    foreach (var saveDir in Directory.EnumerateDirectories(saveRoot))
                    {
                        var saveName = Path.GetFileName(saveDir)?.ToLowerInvariant() ?? "";

                        // Skip autosaves
                        if (saveName.Contains("autosave"))
                            continue;

                        var gameSii = Path.Combine(saveDir, "game.sii");

                        if (!File.Exists(gameSii))
                            continue;

                        try
                        {
                            var text = File.ReadAllText(gameSii);

                            // Must be editable
                            if (!text.Contains("SiiNunit"))
                                continue;
                        }
                        catch
                        {
                            continue;
                        }

                        var lastWrite = File.GetLastWriteTimeUtc(gameSii);

                        if (lastWrite > newestTime)
                        {
                            newestTime = lastWrite;
                            newestGameSii = gameSii;
                            newestProfile = Path.GetFileName(profile);
                            newestSave = Path.GetFileName(saveDir);
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(newestGameSii))
                {
                    return new AtsSaveLocatorResult
                    {
                        Success = false,
                        Message = "No editable ATS manual save found."
                    };
                }

                return new AtsSaveLocatorResult
                {
                    Success = true,
                    Message = "ATS manual save located.",

                    GameSiiPath = newestGameSii,

                    SelectedProfile = newestProfile,
                    SelectedSave = newestSave,

                    ProfileName = newestProfile,
                    ProfileId = newestProfile,
                    SaveName = newestSave
                };
            }
            catch (Exception ex)
            {
                return new AtsSaveLocatorResult
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        public static AtsSaveLocatorResult LocateLatestSaveForProfile(string profileId)
        {
            return LocateLatestSave();
        }

        public static AtsSaveLocatorResult LocateSpecificSave(string profileId, string saveName)
        {
            return LocateLatestSave();
        }

        public static string? FindLatestEditableGameSii()
        {
            return LocateLatestSave().GameSiiPath;
        }
    }
}