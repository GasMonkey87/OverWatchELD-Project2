using System;
using System.IO;

namespace OverWatchELD.Services
{
    public static class AppPaths
    {
        private static string? _rootOverride;

        public static void SetRootOverride(string? path)
        {
            _rootOverride = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
        }

        public static string Root
        {
            get
            {
                var root = !string.IsNullOrWhiteSpace(_rootOverride)
                    ? _rootOverride!
                    : Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "OverWatchELD");

                Directory.CreateDirectory(root);
                return root;
            }
        }

        public static string ConfigFolder(string? subfolder = null)
        {
            var path = string.IsNullOrWhiteSpace(subfolder)
                ? Path.Combine(Root, "Config")
                : Path.Combine(Root, "Config", subfolder);

            Directory.CreateDirectory(path);
            return path;
        }

        public static string DataFolder(string? subfolder = null)
        {
            var path = string.IsNullOrWhiteSpace(subfolder)
                ? Path.Combine(Root, "Data")
                : Path.Combine(Root, "Data", subfolder);

            Directory.CreateDirectory(path);
            return path;
        }

        public static string LogsFolder(string? subfolder = null)
        {
            var path = string.IsNullOrWhiteSpace(subfolder)
                ? Path.Combine(Root, "Logs")
                : Path.Combine(Root, "Logs", subfolder);

            Directory.CreateDirectory(path);
            return path;
        }

        public static string ExportsFolder(string? subfolder = null)
        {
            var path = string.IsNullOrWhiteSpace(subfolder)
                ? Path.Combine(Root, "Exports")
                : Path.Combine(Root, "Exports", subfolder);

            Directory.CreateDirectory(path);
            return path;
        }

        public static string FileInConfig(string fileName)
            => Path.Combine(ConfigFolder(), fileName);

        public static string FileInData(string fileName)
            => Path.Combine(DataFolder(), fileName);

        public static string FileInLogs(string fileName)
            => Path.Combine(LogsFolder(), fileName);

        public static string FileInExports(string fileName)
            => Path.Combine(ExportsFolder(), fileName);
    }
}