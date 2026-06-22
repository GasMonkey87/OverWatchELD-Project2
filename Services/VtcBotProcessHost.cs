using System;
using System.Diagnostics;
using System.IO;

namespace OverWatchELD.Services
{
    public sealed class VtcBotProcessHost : IDisposable
    {
        private Process? _proc;

        public bool IsRunning => _proc != null && !_proc.HasExited;

        public void Start(string exePath, string? workingDir = null, string? args = null)
        {
            if (IsRunning) return;

            if (!File.Exists(exePath))
                throw new FileNotFoundException("VTC bot exe not found", exePath);

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args ?? "",
                WorkingDirectory = workingDir ?? Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,          // run hidden
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,  // optional (nice for logs)
                RedirectStandardError = true
            };

            _proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _proc.Start();

            // Optional: you can read output for diagnostics
            _proc.BeginOutputReadLine();
            _proc.BeginErrorReadLine();
        }

        public void Stop()
        {
            try
            {
                if (_proc == null) return;
                if (_proc.HasExited) return;

                // Graceful first
                _proc.CloseMainWindow();
                if (!_proc.WaitForExit(1500))
                {
                    // Then force
                    _proc.Kill(entireProcessTree: true);
                    _proc.WaitForExit(1500);
                }
            }
            catch { /* swallow */ }
            finally
            {
                _proc?.Dispose();
                _proc = null;
            }
        }

        public void Dispose() => Stop();
    }
}
