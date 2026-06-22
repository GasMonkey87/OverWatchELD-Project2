using OverWatchELD.Models.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OverWatchELD.Services.Media
{
    public static class LocalMusicLibraryService
    {
        private static readonly string[] SupportedExtensions =
        {
            ".mp3", ".wav", ".wma", ".aac", ".m4a"
        };

        public static string AtsMusicFolder =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "American Truck Simulator",
                "music");

        public static List<MediaTrack> LoadAtsMusicFolder()
        {
            return LoadFolder(AtsMusicFolder);
        }

        public static List<MediaTrack> LoadFolder(string? folder)
        {
            var result = new List<MediaTrack>();

            try
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                    return result;

                foreach (var file in Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(file);
                    if (!SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                        continue;

                    result.Add(new MediaTrack
                    {
                        Title = Path.GetFileNameWithoutExtension(file),
                        FullPath = file,
                        Source = folder.Equals(AtsMusicFolder, StringComparison.OrdinalIgnoreCase)
                            ? "ATS Music Folder"
                            : "Local Music"
                    });
                }
            }
            catch
            {
            }

            return result
                .OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
