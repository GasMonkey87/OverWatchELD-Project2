using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace OverWatchELD.Overlay
{
    public static class OverlayStarter
    {
        private static OverlayWindow? _overlay;
        private static bool _started;

        public static void Start()
        {
            if (_started)
                return;

            _started = true;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_overlay != null)
                    return;

                _overlay = new OverlayWindow();
                _overlay.Show();

                RegisterHotkeys(_overlay);
            }), DispatcherPriority.ApplicationIdle);
        }

        private static void RegisterHotkeys(OverlayWindow overlay)
        {
            var showHide = new RoutedCommand();
            showHide.InputGestures.Add(new KeyGesture(Key.F9));
            overlay.CommandBindings.Add(new CommandBinding(showHide, (_, _) => overlay.ToggleVisible()));

            var lockUnlock = new RoutedCommand();
            lockUnlock.InputGestures.Add(new KeyGesture(Key.F10));
            overlay.CommandBindings.Add(new CommandBinding(lockUnlock, (_, _) => overlay.ToggleClickThrough()));
        }
    }
}
