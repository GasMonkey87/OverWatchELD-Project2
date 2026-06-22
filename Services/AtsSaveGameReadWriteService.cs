using System;
using System.IO;
using System.Text;

namespace OverWatchELD.Services
{
    public sealed class AtsSaveReadWriteResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string OriginalPath { get; set; } = "";
        public string BackupPath { get; set; } = "";
    }

    public static class AtsSaveGameReadWriteService
    {
        public static AtsSaveReadWriteResult BackupSave(string gameSiiPath)
        {
            var result = new AtsSaveReadWriteResult
            {
                OriginalPath = gameSiiPath
            };

            try
            {
                if (string.IsNullOrWhiteSpace(gameSiiPath) || !File.Exists(gameSiiPath))
                {
                    result.Success = false;
                    result.Message = "game.sii file not found.";
                    return result;
                }

                var dir = Path.GetDirectoryName(gameSiiPath) ?? "";
                var fileName = Path.GetFileNameWithoutExtension(gameSiiPath);

                var backupPath = Path.Combine(
                    dir,
                    $"{fileName}.bak_{DateTime.Now:yyyyMMdd_HHmmss}.sii");

                File.Copy(gameSiiPath, backupPath, overwrite: false);

                result.Success = true;
                result.BackupPath = backupPath;
                result.Message = "Backup created.";

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Backup failed: {ex.Message}";
                return result;
            }
        }

        public static string ReadSave(string gameSiiPath)
        {
            try
            {
                if (!File.Exists(gameSiiPath))
                    return "";

                return File.ReadAllText(gameSiiPath, Encoding.UTF8);
            }
            catch
            {
                return "";
            }
        }

        public static AtsSaveReadWriteResult WriteSave(string gameSiiPath, string content)
        {
            var result = new AtsSaveReadWriteResult
            {
                OriginalPath = gameSiiPath
            };

            try
            {
                if (string.IsNullOrWhiteSpace(gameSiiPath))
                {
                    result.Success = false;
                    result.Message = "Invalid save path.";
                    return result;
                }

                if (string.IsNullOrWhiteSpace(content))
                {
                    result.Success = false;
                    result.Message = "Save content is empty.";
                    return result;
                }

                // Always backup before writing
                var backup = BackupSave(gameSiiPath);
                if (!backup.Success)
                {
                    result.Success = false;
                    result.Message = "Write aborted: backup failed.";
                    return result;
                }

                result.BackupPath = backup.BackupPath;

                // Write temp file first
                var tempPath = gameSiiPath + ".tmp";

                File.WriteAllText(tempPath, content, Encoding.UTF8);

                // Replace original safely
                File.Delete(gameSiiPath);
                File.Move(tempPath, gameSiiPath);

                result.Success = true;
                result.Message = "Save written successfully.";

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Write failed: {ex.Message}";
                return result;
            }
        }

        public static bool IsSaveReadable(string gameSiiPath)
        {
            try
            {
                if (!File.Exists(gameSiiPath))
                    return false;

                var text = File.ReadAllText(gameSiiPath, Encoding.UTF8);

                // Basic sanity check
                return text.Contains("SiiNunit");
            }
            catch
            {
                return false;
            }
        }
    }
}