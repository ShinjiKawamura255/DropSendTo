using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;

namespace DropSendTo.Services;

internal sealed class ShortcutTriggeredEventArgs : EventArgs
{
    public ShortcutTriggeredEventArgs(ushort mainKey, IReadOnlyList<ushort> modifierKeys, KeyChord registeredChord)
    {
        MainKey = mainKey;
        ModifierKeys = modifierKeys;
        RegisteredChord = registeredChord;
    }

    public ushort MainKey { get; }
    public IReadOnlyList<ushort> ModifierKeys { get; }
    public KeyChord RegisteredChord { get; }
    public string RegisteredText => RegisteredChord.NormalizedString;
}

internal sealed class PrefixPassthroughEventArgs : EventArgs
{
    public PrefixPassthroughEventArgs(string shortcutText)
    {
        ShortcutText = shortcutText;
    }

    public string ShortcutText { get; }
}

internal sealed class PrefixStateChangedEventArgs : EventArgs
{
    public PrefixStateChangedEventArgs(bool isArmed)
    {
        IsArmed = isArmed;
    }

    public bool IsArmed { get; }
}

internal sealed class ShortcutService : IDisposable
{
    private const int PrefixTimeoutMilliseconds = 4_000;
    private readonly object _stateLock = new();
    private readonly LoggerService _logger = LoggerService.Instance;
    private readonly Dispatcher _dispatcher;
    private LowLevelKeyboardProc? _hookCallback;
    private IntPtr _hookHandle = IntPtr.Zero;
    private LowLevelMouseProc? _mouseHookCallback;
    private IntPtr _mouseHookHandle = IntPtr.Zero;
    private bool _disposed;

    private KeyChord? _prefixChord;
    private string _prefixText = string.Empty;
    private bool _prefixArmed;
    private DateTime _prefixArmedAtUtc;
    private IReadOnlyList<ModifierKind> _prefixModifiers = Array.Empty<ModifierKind>();
    private readonly Dictionary<ushort, int> _suppressedKeyUps = new();
    private readonly List<KeyChord> _availableShortcuts = new();
    private readonly HashSet<ushort> _activeModifiers = new();
    private readonly Timer _prefixTimeoutTimer;
    private bool _usingFallbackPrefix;

    public ShortcutService()
    {
        _dispatcher = ApplicationDispatcherProvider.GetDispatcher();
        _prefixTimeoutTimer = new Timer(OnPrefixTimeout, null, Timeout.Infinite, Timeout.Infinite);
    }

    public event EventHandler<ShortcutTriggeredEventArgs>? ShortcutTriggered;
    public event EventHandler<PrefixPassthroughEventArgs>? PrefixPassthroughRequested;
    public event EventHandler<PrefixStateChangedEventArgs>? PrefixStateChanged;

    public string CurrentPrefixText => _prefixText;
    public bool IsUsingFallbackPrefix => _usingFallbackPrefix;

    public void Initialize(string? prefixExpression)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ShortcutService));
        UpdatePrefix(prefixExpression);
        if (_hookHandle != IntPtr.Zero) return;

        _hookCallback = KeyboardHookProc;
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookCallback, IntPtr.Zero, 0);
        if (_hookHandle == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"Failed to install keyboard hook. Error={err}");
        }

        _mouseHookCallback = MouseHookProc;
        _mouseHookHandle = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookCallback, IntPtr.Zero, 0);
        if (_mouseHookHandle == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
            throw new InvalidOperationException($"Failed to install mouse hook. Error={err}");
        }
    }

    public void UpdatePrefix(string? prefixExpression)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ShortcutService));
        KeyChord? chord = null;
        string target = prefixExpression ?? string.Empty;
        if (!KeyChordParser.TryParse(target, out chord!, out var error))
        {
            _logger.Warn($"Prefix parse failed ({error ?? "unknown error"}). Fallback to default Ctrl+Q.");
            KeyChordParser.TryParse("CTRL+Q", out chord!, out _);
            _usingFallbackPrefix = true;
        }
        else
        {
            _usingFallbackPrefix = false;
        }

        lock (_stateLock)
        {
            _prefixChord = chord;
            _prefixText = chord.NormalizedString;
            _prefixModifiers = chord.Modifiers;
            ResetPrefixStateLocked();
        }
    }

    public void UpdateAvailableShortcuts(IEnumerable<string> shortcuts)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ShortcutService));
        lock (_stateLock)
        {
            _availableShortcuts.Clear();
            foreach (var entry in shortcuts ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                if (KeyChordParser.TryParse(entry, out var chord, out var error))
                {
                    _availableShortcuts.Add(chord);
                }
                else
                {
                    _logger.Warn($"Failed to parse shortcut registration \"{entry}\": {error}");
                }
            }
        }
    }

    private void ResetPrefixStateLocked()
    {
        SetPrefixArmedLocked(false, DateTime.UtcNow);
    }

    private void SetPrefixArmedLocked(bool armed, DateTime timestampUtc)
    {
        if (_prefixArmed == armed)
        {
            if (armed)
            {
                _prefixArmedAtUtc = timestampUtc;
                SchedulePrefixTimeoutLocked();
            }
            return;
        }

        _prefixArmed = armed;
        _prefixArmedAtUtc = armed ? timestampUtc : DateTime.MinValue;
        SchedulePrefixTimeoutLocked();
        NotifyPrefixState(armed);
    }

    private void NotifyPrefixState(bool armed)
    {
        _dispatcher.BeginInvoke(() => PrefixStateChanged?.Invoke(this, new PrefixStateChangedEventArgs(armed)));
    }

    private IntPtr KeyboardHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        var msg = (int)wParam;
        var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        if ((info.flags & (LLKHF_INJECTED | LLKHF_LOWER_IL_INJECTED)) != 0)
        {
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        bool suppress = false;
        ShortcutAction action = ShortcutAction.None;
        lock (_stateLock)
        {
            if (_prefixChord == null)
            {
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
            }

            if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
            {
                action = ProcessKeyDown(info.vkCode, out suppress);
            }
            else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
            {
                suppress = ProcessKeyUp(info.vkCode);
            }
        }

        DispatchAction(action);

        if (suppress)
        {
            return new IntPtr(1);
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private ShortcutAction ProcessKeyDown(uint vkCode, out bool suppress)
    {
        suppress = false;
        var chord = _prefixChord!;
        var now = DateTime.UtcNow;
        var vk = (ushort)vkCode;
        TrackModifierKeyDownLocked(vk);
        if (_prefixArmed && (now - _prefixArmedAtUtc).TotalMilliseconds > PrefixTimeoutMilliseconds)
        {
            ResetPrefixStateLocked();
        }

        if (!_prefixArmed)
        {
            if (vk != chord.MainKey)
            {
                return ShortcutAction.None;
            }

            if (!AreModifiersSatisfied(chord.Modifiers))
            {
                return ShortcutAction.None;
            }

            SetPrefixArmedLocked(true, now);
            MarkKeyForSuppression(vk);
            suppress = true;
            return ShortcutAction.None;
        }

        if (vk == chord.MainKey && AreModifiersSatisfied(chord.Modifiers))
        {
            SetPrefixArmedLocked(false, now);
            MarkKeyForSuppression(vk);
            suppress = true;
            return ShortcutAction.CreatePrefixPassthrough(_prefixText);
        }

        if (IsModifierVirtualKey(vk))
        {
            // Modifiers should not remain latched system-wide, so we only block the key down event.
            suppress = true;
            return ShortcutAction.None;
        }

        var modifiers = CollectActiveModifierKeysLocked();
        RemovePrefixModifiers(modifiers, _prefixModifiers);
        SetPrefixArmedLocked(false, now);

        if (!TryMatchAvailableShortcut(vk, modifiers, out var matchedChord))
        {
            suppress = false;
            return ShortcutAction.None;
        }

        MarkKeyForSuppression(vk);
        suppress = true;
        return ShortcutAction.CreateShortcut(vk, modifiers, matchedChord);
    }

    private bool ProcessKeyUp(uint vkCode)
    {
        var vk = (ushort)vkCode;
        TrackModifierKeyUpLocked(vk);
        if (ConsumeSuppressedKey(vk))
        {
            return true;
        }

        if (_prefixArmed && _prefixChord != null)
        {
            var now = DateTime.UtcNow;
            if ((now - _prefixArmedAtUtc).TotalMilliseconds > PrefixTimeoutMilliseconds)
            {
                ResetPrefixStateLocked();
            }
        }

        return false;
    }

    private bool TryMatchAvailableShortcut(ushort mainKey, HashSet<ushort> modifiers, out KeyChord matchedChord)
    {
        matchedChord = null!;
        if (_availableShortcuts.Count == 0) return false;
        foreach (var chord in _availableShortcuts)
        {
            if (ShortcutMatches(chord, mainKey, modifiers))
            {
                matchedChord = chord;
                return true;
            }
        }
        return false;
    }

    private static bool ShortcutMatches(KeyChord chord, ushort mainKey, HashSet<ushort> modifiers)
    {
        if (chord.MainKey != mainKey) return false;
        var working = new HashSet<ushort>(modifiers);
        foreach (var modifier in chord.Modifiers)
        {
            if (!TryConsumeModifier(working, modifier))
            {
                return false;
            }
        }
        return working.Count == 0;
    }

    private static bool TryConsumeModifier(HashSet<ushort> actual, ModifierKind modifier)
    {
        foreach (var candidate in KeyChordParser.GetCandidateModifierVirtualKeys(modifier))
        {
            if (actual.Remove(candidate))
            {
                return true;
            }
        }
        return false;
    }

    private void DispatchAction(ShortcutAction action)
    {
        if (action.Type == ShortcutActionType.None)
        {
            return;
        }

        switch (action.Type)
        {
            case ShortcutActionType.PrefixPassthrough:
                var prefixArgs = new PrefixPassthroughEventArgs(action.PrefixText ?? _prefixText);
                _dispatcher.BeginInvoke(() => PrefixPassthroughRequested?.Invoke(this, prefixArgs));
                break;
            case ShortcutActionType.TriggerShortcut:
                if (action.RegisteredChord is null)
                {
                    return;
                }
                var modifiers = action.ModifierKeys ?? Array.Empty<ushort>();
                var args = new ShortcutTriggeredEventArgs(action.MainKey, modifiers, action.RegisteredChord);
                _dispatcher.BeginInvoke(() => ShortcutTriggered?.Invoke(this, args));
                break;
        }
    }

    private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
        }

        var msg = (int)wParam;
        if (msg is WM_MOUSEMOVE or WM_NCMOUSEMOVE)
        {
            return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
        }

        bool shouldReset = msg is WM_LBUTTONDOWN or WM_LBUTTONUP
            or WM_RBUTTONDOWN or WM_RBUTTONUP
            or WM_MBUTTONDOWN or WM_MBUTTONUP
            or WM_XBUTTONDOWN or WM_XBUTTONUP
            or WM_MOUSEWHEEL or WM_MOUSEHWHEEL
            or WM_NCLBUTTONDOWN or WM_NCRBUTTONDOWN;

        if (shouldReset)
        {
            lock (_stateLock)
            {
                if (_prefixArmed)
                {
                    ResetPrefixStateLocked();
                }
            }
        }

        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _prefixTimeoutTimer.Change(Timeout.Infinite, Timeout.Infinite);
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
        if (_mouseHookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHookHandle);
            _mouseHookHandle = IntPtr.Zero;
        }
        _prefixTimeoutTimer.Dispose();
    }

    private void TrackModifierKeyDownLocked(ushort vk)
    {
        if (IsModifierVirtualKey(vk))
        {
            _activeModifiers.Add(vk);
        }
    }

    private void TrackModifierKeyUpLocked(ushort vk)
    {
        if (IsModifierVirtualKey(vk))
        {
            _activeModifiers.Remove(vk);
        }
    }

    private void SchedulePrefixTimeoutLocked()
    {
        if (_disposed)
        {
            return;
        }

        if (_prefixArmed)
        {
            var elapsed = (int)(DateTime.UtcNow - _prefixArmedAtUtc).TotalMilliseconds;
            var remaining = PrefixTimeoutMilliseconds - elapsed;
            if (remaining < 1) remaining = 1;
            _prefixTimeoutTimer.Change(remaining, Timeout.Infinite);
        }
        else
        {
            _prefixTimeoutTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private static void RemovePrefixModifiers(HashSet<ushort> modifiers, IReadOnlyList<ModifierKind> prefixModifiers)
    {
        foreach (var kind in prefixModifiers)
        {
            foreach (var candidate in KeyChordParser.GetCandidateModifierVirtualKeys(kind))
            {
                modifiers.Remove(candidate);
            }
        }
    }

    private HashSet<ushort> CollectActiveModifierKeysLocked()
    {
        return new HashSet<ushort>(_activeModifiers);
    }

    private bool AreModifiersSatisfied(IReadOnlyList<ModifierKind> modifiers)
    {
        if (modifiers.Count == 0) return true;
        foreach (var modifier in modifiers)
        {
            if (!IsModifierGroupDown(modifier))
            {
                return false;
            }
        }
        return true;
    }

    private bool IsModifierGroupDown(ModifierKind modifier) =>
        modifier switch
        {
            ModifierKind.Control => IsAnyModifierDown(VK_LCONTROL, VK_RCONTROL),
            ModifierKind.Shift => IsAnyModifierDown(VK_LSHIFT, VK_RSHIFT),
            ModifierKind.Alt => IsAnyModifierDown(VK_LMENU, VK_RMENU),
            ModifierKind.Win => IsAnyModifierDown(VK_LWIN, VK_RWIN),
            ModifierKind.LeftWin => _activeModifiers.Contains(VK_LWIN),
            ModifierKind.RightWin => _activeModifiers.Contains(VK_RWIN),
            _ => false
        };

    private bool IsAnyModifierDown(params ushort[] keys)
    {
        foreach (var key in keys)
        {
            if (_activeModifiers.Contains(key))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsModifierVirtualKey(ushort vk) =>
        vk is VK_LCONTROL or VK_RCONTROL or VK_LSHIFT or VK_RSHIFT or VK_LMENU or VK_RMENU or VK_LWIN or VK_RWIN;

    private void OnPrefixTimeout(object? state)
    {
        lock (_stateLock)
        {
            if (_disposed || !_prefixArmed)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if ((now - _prefixArmedAtUtc).TotalMilliseconds >= PrefixTimeoutMilliseconds)
            {
                SetPrefixArmedLocked(false, now);
            }
            else
            {
                SchedulePrefixTimeoutLocked();
            }
        }
    }

    private void MarkKeyForSuppression(ushort vk)
    {
        if (_suppressedKeyUps.TryGetValue(vk, out var count))
        {
            _suppressedKeyUps[vk] = count + 1;
        }
        else
        {
            _suppressedKeyUps[vk] = 1;
        }
    }

    private bool ConsumeSuppressedKey(ushort vk)
    {
        if (!_suppressedKeyUps.TryGetValue(vk, out var count) || count <= 0)
        {
            return false;
        }
        if (count == 1)
        {
            _suppressedKeyUps.Remove(vk);
        }
        else
        {
            _suppressedKeyUps[vk] = count - 1;
        }
        return true;
    }

    private struct ShortcutAction
    {
        public static readonly ShortcutAction None = new(ShortcutActionType.None, 0, null, null, null);

        private ShortcutAction(ShortcutActionType type, ushort mainKey, IReadOnlyList<ushort>? modifiers, string? text, KeyChord? chord)
        {
            Type = type;
            MainKey = mainKey;
            ModifierKeys = modifiers;
            PrefixText = text;
            RegisteredChord = chord;
        }

        public ShortcutActionType Type { get; }
        public ushort MainKey { get; }
        public IReadOnlyList<ushort>? ModifierKeys { get; }
        public string? PrefixText { get; }
        public KeyChord? RegisteredChord { get; }

        public static ShortcutAction CreateShortcut(ushort mainKey, HashSet<ushort> modifiers, KeyChord chord)
        {
            var buffer = new ushort[modifiers.Count];
            modifiers.CopyTo(buffer);
            return new ShortcutAction(ShortcutActionType.TriggerShortcut, mainKey, buffer, null, chord);
        }

        public static ShortcutAction CreatePrefixPassthrough(string text) =>
            new(ShortcutActionType.PrefixPassthrough, 0, null, text, null);
    }

    private enum ShortcutActionType
    {
        None,
        TriggerShortcut,
        PrefixPassthrough
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WH_KEYBOARD_LL = 13;
    private const uint LLKHF_INJECTED = 0x00000010;
    private const uint LLKHF_LOWER_IL_INJECTED = 0x00000002;
    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_MOUSEHWHEEL = 0x020E;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_XBUTTONUP = 0x020C;
    private const int WM_NCMOUSEMOVE = 0x00A0;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int WM_NCRBUTTONDOWN = 0x00A4;
    private const int WH_MOUSE_LL = 14;

    private const ushort VK_LCONTROL = 0xA2;
    private const ushort VK_RCONTROL = 0xA3;
    private const ushort VK_LSHIFT = 0xA0;
    private const ushort VK_RSHIFT = 0xA1;
    private const ushort VK_LMENU = 0xA4;
    private const ushort VK_RMENU = 0xA5;
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_RWIN = 0x5C;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

}

internal static class ApplicationDispatcherProvider
{
    public static Dispatcher GetDispatcher()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            throw new InvalidOperationException("Dispatcher is not available.");
        }
        return dispatcher;
    }
}
