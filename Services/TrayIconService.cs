using System;

namespace OverWatchELD.Services
{
    public sealed class TrayIconService : IDisposable
    {
        private readonly System.Windows.Window _owner;
        private readonly System.Windows.Forms.NotifyIcon _notifyIcon;

        public bool MinimizeToTrayEnabled { get; set; } = true;

        public TrayIconService(System.Windows.Window owner)
        {
            _owner = owner;

            _notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Visible = true,
                Text = "OverWatch ELD",
                Icon = LoadIcon()
            };

            var menu = new System.Windows.Forms.ContextMenuStrip();

            var showItem = new System.Windows.Forms.ToolStripMenuItem("Show OverWatch ELD");
            showItem.Click += (_, __) => ShowFromTray();
            menu.Items.Add(showItem);

            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit");
            exitItem.Click += (_, __) =>
            {
                try
                {
                    _notifyIcon.Visible = false;
                    _owner.Dispatcher.Invoke(() => _owner.Close());
                }
                catch { }
            };
            menu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.DoubleClick += (_, __) => ShowFromTray();
        }

        public void HideToTray()
        {
            try
            {
                _owner.Hide();
                _notifyIcon.Visible = true;
                _notifyIcon.BalloonTipTitle = "OverWatch ELD";
                _notifyIcon.BalloonTipText = "Running in tray.";
                _notifyIcon.ShowBalloonTip(800);
            }
            catch { }
        }

        public void ShowFromTray()
        {
            try
            {
                _owner.Dispatcher.Invoke(() =>
                {
                    _owner.Show();
                    _owner.WindowState = System.Windows.WindowState.Normal;
                    _owner.Activate();

                    // Force focus (Windows being Windows)
                    _owner.Topmost = true;
                    _owner.Topmost = false;
                    _owner.Focus();
                });
            }
            catch { }
        }

        private static global::System.Drawing.Icon LoadIcon()
        {
            try
            {
                var path = System.IO.Path.Combine(
                    AppContext.BaseDirectory,
                    "Assets",
                    "OverWatchELD.ico"
                );

                if (System.IO.File.Exists(path))
                    return new global::System.Drawing.Icon(path);
            }
            catch { }

            return global::System.Drawing.SystemIcons.Application;
        }

        public void Dispose()
        {
            try
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            catch { }
        }
    }
}
