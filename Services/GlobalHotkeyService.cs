using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace OverWatchELD.Services
{
    public sealed class GlobalHotkeyService : IDisposable
    {
        private readonly Window _window;
        private HwndSource? _source;
        private int _id;
        private Action? _onToggle;

        public GlobalHotkeyService(Window window)
        {
            _window = window;
        }

        public bool RegisterToggle(HotkeyBinding binding, Action onToggle)
        {
            _onToggle = onToggle;

            if (!binding.IsValid)
                return false;

            // Must have HWND
            var hwnd = new WindowInteropHelper(_window).Handle;
            if (hwnd == IntPtr.Zero)
                return false;

            _source = HwndSource.FromHwnd(hwnd);
            _source?.AddHook(WndProc);

            // Unique ID per window instance
            _id = unchecked((int)DateTime.UtcNow.Ticks);

            uint modsNative = ToNativeModifiers(binding.Modifiers);
            int vk = KeyInterop.VirtualKeyFromKey(binding.Key);

            return RegisterHotKey(hwnd, _id, modsNative, (uint)vk);
        }

        public void Unregister()
        {
            try
            {
                var hwnd = new WindowInteropHelper(_window).Handle;
                if (hwnd != IntPtr.Zero && _id != 0)
                    UnregisterHotKey(hwnd, _id);
            }
            catch { }

            try
            {
                if (_source != null)
                    _source.RemoveHook(WndProc);
            }
            catch { }

            _id = 0;
            _source = null;
        }

        public void Dispose()
        {
            Unregister();
            GC.SuppressFinalize(this);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;

            if (msg == WM_HOTKEY && wParam.ToInt32() == _id)
            {
                handled = true;
                try { _onToggle?.Invoke(); } catch { }
            }

            return IntPtr.Zero;
        }

        private static uint ToNativeModifiers(ModifierKeys mods)
        {
            uint m = 0;
            if (mods.HasFlag(ModifierKeys.Alt)) m |= 0x0001;     // MOD_ALT
            if (mods.HasFlag(ModifierKeys.Control)) m |= 0x0002; // MOD_CONTROL
            if (mods.HasFlag(ModifierKeys.Shift)) m |= 0x0004;   // MOD_SHIFT
            if (mods.HasFlag(ModifierKeys.Windows)) m |= 0x0008; // MOD_WIN
            return m;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
