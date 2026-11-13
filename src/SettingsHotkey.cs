using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Input;

namespace ClearDesk
{
    internal sealed class SettingsHotkey : IDisposable
    {
        internal const int Alt = 0x0001, Control = 0x0002, Shift = 0x0004, Windows = 0x0008;
        const int HotkeyId = 0x4344, WmHotkey = 0x0312, NoRepeat = 0x4000;
        const int AllModifiers = Alt | Control | Shift | Windows;

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnregisterHotKey(IntPtr window, int id);

        readonly HwndSource source;
        readonly Action activated;
        bool registered, disposed;
        int modifiers, key;

        internal SettingsHotkey(Action activated)
        {
            this.activated = activated;
            source = new HwndSource(new HwndSourceParameters("ClearDesk Settings Hotkey")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0,
                ExtendedWindowStyle = 0x80
            });
            source.AddHook(Hook);
        }

        internal static string Format(int modifiers, int key)
        {
            var parts = new List<string>();
            if ((modifiers & Control) != 0) parts.Add("Ctrl");
            if ((modifiers & Alt) != 0) parts.Add("Alt");
            if ((modifiers & Shift) != 0) parts.Add("Shift");
            if ((modifiers & Windows) != 0) parts.Add("Win");
            Key code = KeyInterop.KeyFromVirtualKey(key);
            parts.Add(code == Key.None ? "VK" + key.ToString("X2") : code.ToString());
            return string.Join("+", parts);
        }

        internal bool TrySet(int nextModifiers, int nextKey, out string error)
        {
            error = null;
            if (disposed || nextModifiers == 0 || (nextModifiers & ~AllModifiers) != 0 || nextKey <= 0 || nextKey > 0xff)
            {
                error = "快捷键无效，请至少组合一个 Ctrl、Alt、Shift 或 Win 键。";
                return false;
            }
            if (registered && modifiers == nextModifiers && key == nextKey) return true;

            bool restore = registered;
            int oldModifiers = modifiers, oldKey = key;
            if (restore && !UnregisterHotKey(source.Handle, HotkeyId))
            {
                error = "无法替换当前快捷键。";
                return false;
            }
            registered = false;
            if (!RegisterHotKey(source.Handle, HotkeyId, (uint)(nextModifiers | NoRepeat), (uint)nextKey))
            {
                int code = Marshal.GetLastWin32Error();
                if (restore && RegisterHotKey(source.Handle, HotkeyId, (uint)(oldModifiers | NoRepeat), (uint)oldKey))
                {
                    registered = true;
                    modifiers = oldModifiers;
                    key = oldKey;
                }
                error = code == 1409 ? "该快捷键已被系统或其他程序占用。" : "系统无法注册该快捷键（错误 " + code + "）。";
                return false;
            }

            registered = true;
            modifiers = nextModifiers;
            key = nextKey;
            return true;
        }

        IntPtr Hook(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (!disposed && message == WmHotkey && wParam.ToInt32() == HotkeyId)
            {
                handled = true;
                if (activated != null) activated();
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (registered) UnregisterHotKey(source.Handle, HotkeyId);
            registered = false;
            source.RemoveHook(Hook);
            source.Dispose();
        }
    }
}
