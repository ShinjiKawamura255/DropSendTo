using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DropSendTo.Services;

internal sealed class MacroRecordingService : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly List<MacroRecordingEvent> _recordedEvents = new();
    private readonly HashSet<string> _activeRecordedKeys = new(StringComparer.OrdinalIgnoreCase);
    private (IntPtr Window, int X, int Y)? _lastRelativePosition;
    private readonly object _mouseSuppressLock = new();
    private HookProc? _keyboardHookProc;
    private HookProc? _mouseHookProc;
    private IntPtr _keyboardHookHandle;
    private IntPtr _mouseHookHandle;
    private IntPtr _ownerWindowHandle;
    private bool _disposed;
    private volatile bool _isRecording;
    private bool _suppressNextLeftButtonDown;
    private bool _suppressNextLeftButtonUp;

    public event EventHandler<string>? LineRecorded;

    public bool IsRecording => _isRecording;

    public bool StartRecording(IntPtr ownerWindowHandle, out string? error)
    {
        lock (_syncRoot)
        {
            if (_disposed)
            {
                error = "録音サービスは破棄されています。";
                return false;
            }

            if (_isRecording)
            {
                error = "録音は既に開始されています。";
                return false;
            }

            _ownerWindowHandle = ownerWindowHandle;
            _recordedEvents.Clear();
            _activeRecordedKeys.Clear();
            _lastRelativePosition = null;
            _suppressNextLeftButtonDown = false;
            _suppressNextLeftButtonUp = false;

            _keyboardHookProc ??= KeyboardHookCallback;
            _mouseHookProc ??= MouseHookCallback;

            _keyboardHookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardHookProc, GetModuleHandle(null), 0);
            if (_keyboardHookHandle == IntPtr.Zero)
            {
                error = $"キーボードフックの設定に失敗しました (Error={Marshal.GetLastWin32Error()}).";
                CleanupHooks();
                return false;
            }

            _mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookProc, GetModuleHandle(null), 0);
            if (_mouseHookHandle == IntPtr.Zero)
            {
                error = $"マウスフックの設定に失敗しました (Error={Marshal.GetLastWin32Error()}).";
                CleanupHooks();
                return false;
            }

            _isRecording = true;
            SuppressNextLeftButtonUp();
            error = null;
            return true;
        }
    }

    public IReadOnlyList<string> StopRecording()
    {
        lock (_syncRoot)
        {
            if (!_isRecording)
            {
                return Array.Empty<string>();
            }

            CleanupHooks();
            _lastRelativePosition = null;
            _ownerWindowHandle = IntPtr.Zero;
            _isRecording = false;
            _suppressNextLeftButtonDown = false;
            _suppressNextLeftButtonUp = false;
            _activeRecordedKeys.Clear();
            var optimized = MacroRecordingOptimizer.Optimize(_recordedEvents);
            _recordedEvents.Clear();
            return optimized;
        }
    }

    public void SuppressNextLeftButtonDown()
    {
        lock (_mouseSuppressLock)
        {
            _suppressNextLeftButtonDown = true;
        }
    }

    public void SuppressNextLeftButtonUp()
    {
        lock (_mouseSuppressLock)
        {
            _suppressNextLeftButtonUp = true;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        StopRecording();
        _disposed = true;
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isRecording)
        {
            var message = unchecked((int)wParam);
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            ProcessKeyboardMessage(message, data);
        }

        return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isRecording)
        {
            var message = unchecked((int)wParam);
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            ProcessMouseMessage(message, data);
        }

        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    private void ProcessKeyboardMessage(int message, KBDLLHOOKSTRUCT data)
    {
        if ((data.flags & LLKHF_INJECTED) != 0) return;

        bool isKeyDown = message is WM_KEYDOWN or WM_SYSKEYDOWN;
        bool isKeyUp = message is WM_KEYUP or WM_SYSKEYUP;
        if (!isKeyDown && !isKeyUp) return;

        ushort vk = (ushort)data.vkCode;
        string token;
        ModifierKind? modifierKind;
        if (TryGetModifierKind(vk, out var modifier))
        {
            modifierKind = modifier;
            token = GetModifierToken(modifier);
        }
        else
        {
            modifierKind = null;
            if (!KeyChordParser.TryGetCanonicalToken(vk, out token))
            {
                return;
            }
        }

        var foreground = GetForegroundWindow();
        bool shouldRecord = foreground != IntPtr.Zero && foreground != _ownerWindowHandle && IsWindow(foreground);
        bool recorded = false;

        lock (_syncRoot)
        {
            if (isKeyDown)
            {
                if (!shouldRecord || _activeRecordedKeys.Contains(token))
                {
                    return;
                }

                _activeRecordedKeys.Add(token);
                _recordedEvents.Add(MacroRecordingEvent.KeyDown(token, modifierKind));
                recorded = true;
            }
            else if (isKeyUp)
            {
                if (!shouldRecord && !_activeRecordedKeys.Contains(token))
                {
                    return;
                }

                _activeRecordedKeys.Remove(token);
                _recordedEvents.Add(MacroRecordingEvent.KeyUp(token, modifierKind));
                recorded = true;
            }
        }

        if (!recorded) return;

        var line = isKeyDown ? $"KEYDOWN {token}" : $"KEYUP {token}";
        LineRecorded?.Invoke(this, line);
    }

    private void ProcessMouseMessage(int message, MSLLHOOKSTRUCT data)
    {
        if ((data.flags & (LLMHF_INJECTED | LLMHF_LOWER_IL_INJECTED)) != 0) return;

        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == _ownerWindowHandle || !IsWindow(foreground))
        {
            return;
        }

        var windowAtPoint = WindowFromPoint(data.pt);
        if (windowAtPoint != IntPtr.Zero)
        {
            if (windowAtPoint == _ownerWindowHandle || IsChild(_ownerWindowHandle, windowAtPoint))
            {
                return;
            }
        }

        if (message == WM_LBUTTONDOWN && ConsumeSuppressFlag(ref _suppressNextLeftButtonDown))
        {
            return;
        }
        if (message == WM_LBUTTONUP && ConsumeSuppressFlag(ref _suppressNextLeftButtonUp))
        {
            return;
        }

        switch (message)
        {
            case WM_LBUTTONDOWN:
                RecordMouseMove(foreground, data.pt);
                AddLine("MOUSELEFTDOWN");
                break;
            case WM_LBUTTONUP:
                AddLine("MOUSELEFTUP");
                break;
            case WM_RBUTTONDOWN:
                RecordMouseMove(foreground, data.pt);
                AddLine("MOUSERIGHTDOWN");
                break;
            case WM_RBUTTONUP:
                AddLine("MOUSERIGHTUP");
                break;
            case WM_MBUTTONDOWN:
                RecordMouseMove(foreground, data.pt);
                AddLine("MOUSEMIDDLEDOWN");
                break;
            case WM_MBUTTONUP:
                AddLine("MOUSEMIDDLEUP");
                break;
            case WM_MOUSEWHEEL:
                RecordMouseWheel(data.mouseData, vertical: true);
                break;
            case WM_MOUSEHWHEEL:
                RecordMouseWheel(data.mouseData, vertical: false);
                break;
        }
    }

    private void RecordMouseMove(IntPtr windowHandle, POINT point)
    {
        if (!TryGetWindowBounds(windowHandle, out var rect))
        {
            return;
        }

        int relativeX = point.x - rect.Left;
        int relativeY = point.y - rect.Top;
        var current = (Window: windowHandle, X: relativeX, Y: relativeY);

        lock (_syncRoot)
        {
            if (_lastRelativePosition.HasValue && _lastRelativePosition.Value == current)
            {
                return;
            }
            _lastRelativePosition = current;
        }

        AddLine($"MOUSEMOVEWIN {relativeX} {relativeY}");
    }

    private void RecordMouseWheel(uint mouseData, bool vertical)
    {
        short delta = unchecked((short)((mouseData >> 16) & 0xFFFF));
        if (delta == 0) return;

        int steps = Math.Max(1, Math.Abs(delta) / WHEEL_DELTA);
        string command = vertical
            ? (delta > 0 ? "MOUSESCROLLUP" : "MOUSESCROLLDOWN")
            : (delta > 0 ? "MOUSESCROLLRIGHT" : "MOUSESCROLLLEFT");

        if (steps > 1)
        {
            command += $" {steps}";
        }

        AddLine(command);
    }

    private void AddLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        lock (_syncRoot)
        {
            _recordedEvents.Add(MacroRecordingEvent.Raw(line));
        }
        LineRecorded?.Invoke(this, line);
    }

    private void CleanupHooks()
    {
        if (_keyboardHookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHookHandle);
            _keyboardHookHandle = IntPtr.Zero;
        }

        if (_mouseHookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero;
        }
    }

    private static bool TryGetWindowBounds(IntPtr hwnd, out RECT rect)
    {
        rect = default;
        if (!IsWindow(hwnd))
        {
            return false;
        }

        if (Environment.OSVersion.Version.Major >= 6)
        {
            try
            {
                if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>()) == 0)
                {
                    return true;
                }
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        return GetWindowRect(hwnd, out rect);
    }

    private static bool TryGetModifierKind(ushort vk, out ModifierKind kind)
    {
        kind = vk switch
        {
            VK_CONTROL or VK_LCONTROL or VK_RCONTROL => ModifierKind.Control,
            VK_SHIFT or VK_LSHIFT or VK_RSHIFT => ModifierKind.Shift,
            VK_MENU or VK_LMENU or VK_RMENU => ModifierKind.Alt,
            VK_LWIN or VK_RWIN => ModifierKind.Win,
            _ => (ModifierKind)(-1)
        };
        return Enum.IsDefined(typeof(ModifierKind), kind) && kind != (ModifierKind)(-1);
    }

    private static string GetModifierToken(ModifierKind kind) =>
        kind switch
        {
            ModifierKind.Control => "Ctrl",
            ModifierKind.Shift => "Shift",
            ModifierKind.Alt => "Alt",
            ModifierKind.Win => "Win",
            ModifierKind.LeftWin => "LWin",
            ModifierKind.RightWin => "RWin",
            _ => kind.ToString()
        };

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hHook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hHook, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_MOUSEHWHEEL = 0x020E;

    private const uint LLKHF_INJECTED = 0x00000010;
    private const uint LLMHF_INJECTED = 0x00000001;
    private const uint LLMHF_LOWER_IL_INJECTED = 0x00000002;

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int WHEEL_DELTA = 120;

    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_LCONTROL = 0xA2;
    private const ushort VK_RCONTROL = 0xA3;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_LSHIFT = 0xA0;
    private const ushort VK_RSHIFT = 0xA1;
    private const ushort VK_MENU = 0x12;
    private const ushort VK_LMENU = 0xA4;
    private const ushort VK_RMENU = 0xA5;
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_RWIN = 0x5C;

    private bool ConsumeSuppressFlag(ref bool flag)
    {
        lock (_mouseSuppressLock)
        {
            if (!flag) return false;
            flag = false;
            return true;
        }
    }
}

internal enum MacroRecordingEventKind
{
    RawCommand,
    KeyDown,
    KeyUp
}

internal readonly struct MacroRecordingEvent
{
    public MacroRecordingEvent(MacroRecordingEventKind kind, string value, ModifierKind? modifierKind)
    {
        Kind = kind;
        Value = value;
        ModifierKind = modifierKind;
    }

    public MacroRecordingEventKind Kind { get; }
    public string Value { get; }
    public ModifierKind? ModifierKind { get; }

    public static MacroRecordingEvent Raw(string command) =>
        new(MacroRecordingEventKind.RawCommand, command, null);

    public static MacroRecordingEvent KeyDown(string token, ModifierKind? modifierKind) =>
        new(MacroRecordingEventKind.KeyDown, token, modifierKind);

    public static MacroRecordingEvent KeyUp(string token, ModifierKind? modifierKind) =>
        new(MacroRecordingEventKind.KeyUp, token, modifierKind);
}
