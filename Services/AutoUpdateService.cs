using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using OverWatchELD.Views;

namespace OverWatchELD.Services
{
    public static class AutoUpdateService
    {
        private static readonly HttpClient Http;
        private static bool _started;
        private static bool _promptShown;
        private static readonly SemaphoreSlim CheckLock = new SemaphoreSlim(1, 1);

        private const string UpdateManifestUrl =
            "https://raw.githubusercontent.com/GasMonkey87/OverWatchELD-Bot/main/wwwroot/updates/update.json";

        static AutoUpdateService()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true
            };

            Http = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
        }

        private static string LogPath
        {
            get
            {
                try
                {
                    var dir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "OverWatchELD");
                    Directory.CreateDirectory(dir);
                    return Path.Combine(dir, "updater.log");
                }
                catch
                {
                    return Path.Combine(Path.GetTempPath(), "OverWatchELD_updater.log");
                }
            }
        }

        public static void StartBackground()
        {
            if (_started) return;
            _started = true;

            try
            {
                var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
                dispatcher.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        await Task.Delay(6000);
                        await CheckInternalAsync(showUpToDateMessage: false, forcePrompt: false);
                    }
                    catch (Exception ex)
                    {
                        Log("StartBackground failed", ex);
                    }
                }));
            }
            catch (Exception ex)
            {
                Log("StartBackground outer failure", ex);
            }
        }

        public static Task<AppUpdateResult> CheckNowAsync()
        {
            return CheckInternalAsync(showUpToDateMessage: true, forcePrompt: true);
        }

        private static async Task<AppUpdateResult> CheckInternalAsync(bool showUpToDateMessage, bool forcePrompt)
        {
            await CheckLock.WaitAsync();
            try
            {
                Log($"CheckInternalAsync start | showUpToDateMessage={showUpToDateMessage} forcePrompt={forcePrompt}");

                var manifest = await GetManifestAsync();
                if (manifest == null)
                {
                    Log("Manifest was null.");

                    if (showUpToDateMessage)
                    {
                        await ShowMessageAsync(
                            "Unable to contact the update server.",
                            "OverWatch ELD Update",
                            MessageBoxImage.Warning);
                    }

                    return new AppUpdateResult
                    {
                        Success = false,
                        UpdateAvailable = false,
                        Message = "Unable to contact the update server."
                    };
                }

                var currentVersion = GetCurrentVersionString();
                var latestVersion = Safe(manifest.Version);
                var downloadUrl = Safe(manifest.DownloadUrl);
                var notes = Safe(manifest.Notes);

                Log($"CurrentVersion={currentVersion} | LatestVersion={latestVersion} | DownloadUrl={downloadUrl}");

                var current = ParseVersion(currentVersion);
                var latest = ParseVersion(latestVersion);

                if (current == null || latest == null)
                {
                    Log("Version parse failed.");

                    if (showUpToDateMessage)
                    {
                        await ShowMessageAsync(
                            $"Could not compare versions.\n\nCurrent: {currentVersion}\nLatest: {latestVersion}",
                            "OverWatch ELD Update",
                            MessageBoxImage.Warning);
                    }

                    return new AppUpdateResult
                    {
                        Success = false,
                        CurrentVersion = currentVersion,
                        LatestVersion = latestVersion,
                        Message = "Could not compare versions."
                    };
                }

                Log($"Parsed current={current} latest={latest}");

                if (latest <= current)
                {
                    Log("No update available.");

                    if (showUpToDateMessage)
                    {
                        await ShowMessageAsync(
                            $"You already have the newest edition installed.\n\nCurrent Version: {currentVersion}",
                            "OverWatch ELD Update",
                            MessageBoxImage.Information);
                    }

                    return new AppUpdateResult
                    {
                        Success = true,
                        UpdateAvailable = false,
                        CurrentVersion = currentVersion,
                        LatestVersion = latestVersion,
                        Message = "Already up to date."
                    };
                }

                if (!_promptShown || forcePrompt)
                {
                    _promptShown = true;

                    MessageBoxResult result = MessageBoxResult.No;

                    await (Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher).InvokeAsync(() =>
                    {
                        var text =
                            $"A new OverWatch ELD release is available.\n\n" +
                            $"Current Version: {currentVersion}\n" +
                            $"Newest Version: {latestVersion}\n\n" +
                            (string.IsNullOrWhiteSpace(notes) ? "" : $"What's New:\n{notes}\n\n") +
                            "Would you like to download and install the newest edition now?";

                        result = MessageBox.Show(
                            Application.Current?.MainWindow,
                            text,
                            "Update Available",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Information);
                    });

                    Log($"Prompt result={result}");

                    if (result != MessageBoxResult.Yes)
                    {
                        return new AppUpdateResult
                        {
                            Success = true,
                            UpdateAvailable = true,
                            CurrentVersion = currentVersion,
                            LatestVersion = latestVersion,
                            Message = "User chose not to update now."
                        };
                    }
                }
                else
                {
                    Log("Prompt already shown.");
                    return new AppUpdateResult
                    {
                        Success = true,
                        UpdateAvailable = true,
                        CurrentVersion = currentVersion,
                        LatestVersion = latestVersion,
                        Message = "Update prompt already shown."
                    };
                }

                if (string.IsNullOrWhiteSpace(downloadUrl))
                {
                    Log("Download URL missing.");

                    await ShowMessageAsync(
                        "The newest release was found, but no download link was provided.",
                        "OverWatch ELD Update",
                        MessageBoxImage.Warning);

                    return new AppUpdateResult
                    {
                        Success = false,
                        UpdateAvailable = true,
                        CurrentVersion = currentVersion,
                        LatestVersion = latestVersion,
                        Message = "Missing download URL."
                    };
                }

                var downloadResult = await DownloadInstallerWithUiAsync(downloadUrl, latestVersion);

                if (!downloadResult.Success)
                {
                    Log($"Download failed: {downloadResult.ErrorMessage}");

                    await ShowMessageAsync(
                        $"Failed to download update.\n\n{downloadResult.ErrorMessage}",
                        "OverWatch ELD Update",
                        MessageBoxImage.Error);

                    return new AppUpdateResult
                    {
                        Success = false,
                        UpdateAvailable = true,
                        CurrentVersion = currentVersion,
                        LatestVersion = latestVersion,
                        Message = downloadResult.ErrorMessage ?? "Download failed."
                    };
                }

                try
                {
                    Log($"Launching installer quietly: {downloadResult.LocalPath}");

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = downloadResult.LocalPath!,
                        Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-",
                        UseShellExecute = true,
                        Verb = "runas"
                    });

                    await ShowMessageAsync(
                        "The newest edition has been downloaded. OverWatch ELD will now close so the update can install quietly.",
                        "OverWatch ELD Update",
                        MessageBoxImage.Information);

                    try
                    {
                        Application.Current?.Shutdown();
                    }
                    catch (Exception ex)
                    {
                        Log("Shutdown failed after installer launch", ex);
                    }

                    return new AppUpdateResult
                    {
                        Success = true,
                        UpdateAvailable = true,
                        UpdateApplied = true,
                        CurrentVersion = currentVersion,
                        LatestVersion = latestVersion,
                        Message = "Installer downloaded and launched quietly."
                    };
                }
                catch (Exception ex)
                {
                    Log("Installer launch failed", ex);

                    await ShowMessageAsync(
                        $"The update downloaded, but the installer could not be launched.\n\n{ex.Message}",
                        "OverWatch ELD Update",
                        MessageBoxImage.Error);

                    return new AppUpdateResult
                    {
                        Success = false,
                        UpdateAvailable = true,
                        CurrentVersion = currentVersion,
                        LatestVersion = latestVersion,
                        Message = ex.Message
                    };
                }
            }
            catch (Exception ex)
            {
                Log("CheckInternalAsync failed", ex);

                if (showUpToDateMessage)
                {
                    await ShowMessageAsync(
                        $"Update check failed.\n\n{ex.Message}",
                        "OverWatch ELD Update",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }

                return new AppUpdateResult
                {
                    Success = false,
                    UpdateAvailable = false,
                    Message = ex.Message
                };
            }
            finally
            {
                CheckLock.Release();
            }
        }

        private static async Task<DownloadInstallerResult> DownloadInstallerWithUiAsync(string downloadUrl, string latestVersion)
        {
            UpdateDownloadWindow? win = null;
            var owner = Application.Current?.MainWindow;

            try
            {
                await (Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher).InvokeAsync(() =>
                {
                    win = new UpdateDownloadWindow
                    {
                        Owner = owner
                    };
                    win.SetStatus($"Downloading OverWatch ELD {latestVersion}...");
                    win.SetProgress(0);
                    win.Show();
                });

                var tempRoot = Path.Combine(Path.GetTempPath(), "OverWatchELD", "Updates");
                Directory.CreateDirectory(tempRoot);

                var fileName = GetSafeFileNameFromUrl(downloadUrl);
                if (string.IsNullOrWhiteSpace(fileName))
                    fileName = "OverWatchELD_Setup.exe";

                var localPath = Path.Combine(tempRoot, fileName);

                using var response = await Http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength;
                await using var input = await response.Content.ReadAsStreamAsync();
                await using var output = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[81920];
                long totalRead = 0;
                int read;

                while ((read = await input.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await output.WriteAsync(buffer, 0, read);
                    totalRead += read;

                    if (totalBytes.GetValueOrDefault() > 0)
                    {
                        double pct = (double)totalRead / totalBytes.Value * 100d;

                        if (win != null)
                        {
                            var progressPct = pct;
                            var currentMb = totalRead / 1024d / 1024d;
                            var totalMb = totalBytes.Value / 1024d / 1024d;

                            await (Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher).InvokeAsync(() =>
                            {
                                win.SetProgress(progressPct);
                                win.SetStatus($"Downloading... {currentMb:0.0} MB / {totalMb:0.0} MB");
                            });
                        }
                    }
                    else if (win != null)
                    {
                        var currentMb = totalRead / 1024d / 1024d;
                        await (Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher).InvokeAsync(() =>
                        {
                            win.SetProgress(null);
                            win.SetStatus($"Downloading... {currentMb:0.0} MB");
                        });
                    }
                }

                if (win != null)
                {
                    await (Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher).InvokeAsync(() =>
                    {
                        win.SetProgress(100);
                        win.SetStatus("Download complete.");
                        win.Close();
                    });
                }

                return new DownloadInstallerResult
                {
                    Success = true,
                    LocalPath = localPath
                };
            }
            catch (Exception ex)
            {
                Log("DownloadInstallerWithUiAsync failed", ex);

                try
                {
                    if (win != null)
                    {
                        await (Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher).InvokeAsync(() =>
                        {
                            try { win.Close(); } catch { }
                        });
                    }
                }
                catch (Exception closeEx)
                {
                    Log("Download window close failed", closeEx);
                }

                return new DownloadInstallerResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private static async Task<UpdateManifest?> GetManifestAsync()
        {
            try
            {
                var json = await Http.GetStringAsync(UpdateManifestUrl);
                Log($"Manifest raw: {json}");

                var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (manifest != null)
                {
                    Log($"Manifest parsed | Version={manifest.Version} | DownloadUrl={manifest.DownloadUrl} | Notes={manifest.Notes}");
                }

                return manifest;
            }
            catch (Exception ex)
            {
                Log("GetManifestAsync failed", ex);
                return null;
            }
        }

        private static string GetCurrentVersionString()
        {
            try
            {
                var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();

                var fileVersion = FileVersionInfo.GetVersionInfo(asm.Location)?.FileVersion;
                if (!string.IsNullOrWhiteSpace(fileVersion))
                    return fileVersion;

                var productVersion = FileVersionInfo.GetVersionInfo(asm.Location)?.ProductVersion;
                if (!string.IsNullOrWhiteSpace(productVersion))
                    return productVersion;

                var v = asm.GetName().Version;
                if (v != null)
                    return $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
            }
            catch (Exception ex)
            {
                Log("GetCurrentVersionString failed", ex);
            }

            return "1.0.0";
        }

        private static string? TryExtractVersion(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var m = Regex.Match(value, @"\d+\.\d+\.\d+(\.\d+)?");
            return m.Success ? m.Value : null;
        }

        private static Version? ParseVersion(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var normalized = TryExtractVersion(value.Trim()) ?? value.Trim();
            return Version.TryParse(normalized, out var v) ? v : null;
        }

        private static string Safe(string? value) => value?.Trim() ?? "";

        private static string GetSafeFileNameFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                var name = Path.GetFileName(uri.LocalPath);
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
            catch (Exception ex)
            {
                Log("GetSafeFileNameFromUrl failed", ex);
            }

            return "OverWatchELD_Setup.exe";
        }

        private static Task ShowMessageAsync(string text, string title, MessageBoxImage icon)
        {
            return (Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher).InvokeAsync(() =>
            {
                MessageBox.Show(
                    Application.Current?.MainWindow,
                    text,
                    title,
                    MessageBoxButton.OK,
                    icon);
            }).Task;
        }

        private static Task ShowMessageAsync(string text, string title, MessageBoxButton buttons, MessageBoxImage icon)
        {
            return (Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher).InvokeAsync(() =>
            {
                MessageBox.Show(
                    Application.Current?.MainWindow,
                    text,
                    title,
                    buttons,
                    icon);
            }).Task;
        }

        private static void Log(string message, Exception? ex = null)
        {
            try
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                if (ex != null)
                    line += $" | {ex}";
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch
            {
            }
        }

        private sealed class UpdateManifest
        {
            public string? Version { get; set; }

            [JsonPropertyName("url")]
            public string? DownloadUrl { get; set; }

            public string? Notes { get; set; }
        }

        private sealed class DownloadInstallerResult
        {
            public bool Success { get; set; }
            public string? LocalPath { get; set; }
            public string? ErrorMessage { get; set; }
        }
    }
}