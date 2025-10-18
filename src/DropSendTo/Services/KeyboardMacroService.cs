using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows;

namespace DropSendTo.Services;

public sealed class KeyboardMacroService : IDisposable
{
    private static readonly Dictionary<string, ushort> KeyMap = CreateKeyMap();
    private static readonly HashSet<string> ModifierTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "CTRL", "CONTROL", "SHIFT", "ALT", "MENU", "WIN", "LWIN", "RWIN"
    };
    private const int MaxRepeatCount = 1000;
    private const int TextSendInterCharacterDelayMilliseconds = 18;
    private const int TextSendWhitespaceDelayMilliseconds = 28;
    private const int ClipTextAutoWaitMilliseconds = 30;

    private readonly SemaphoreSlim _macroLock = new(1, 1);
    private readonly object _stateLock = new();
    private readonly LoggerService _logger = LoggerService.Instance;
    private CancellationTokenSource? _currentMacroCts;
    private int _macroRunningFlag;
    private IntPtr _windowHandle;
    private IntPtr _lastExternalWindow;
    private IntPtr _winEventHook;
    private WinEventDelegate? _winEventCallback;
    private bool _disposed;
    private uint _ownerThreadId;

    public void Initialize(WindowInteropHelper helper)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(KeyboardMacroService));
        _windowHandle = helper.Handle;
        if (_windowHandle == IntPtr.Zero) throw new InvalidOperationException("Window handle is not ready.");
        if (_winEventHook != IntPtr.Zero) return;
        _winEventCallback = OnWinEvent;
        _winEventHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
            _winEventCallback, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (_winEventHook == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"Failed to set win event hook. Error={err}");
        }
        _ownerThreadId = GetCurrentThreadId();
        var fg = GetForegroundWindow();
        if (fg != IntPtr.Zero && fg != _windowHandle)
        {
            Volatile.Write(ref _lastExternalWindow, fg);
        }
    }

    public bool IsMacroRunning => Volatile.Read(ref _macroRunningFlag) == 1;

    public bool CancelCurrentMacro()
    {
        CancellationTokenSource? cts;
        lock (_stateLock)
        {
            cts = _currentMacroCts;
        }

        if (cts == null || cts.IsCancellationRequested) return false;

        try
        {
            cts.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            CancelCurrentMacro();
        }
        catch
        {
            // Dispose 中はキャンセル要求の成否に依存しない
        }
        if (_winEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }
        _winEventCallback = null;
        _macroLock.Dispose();
    }

    private void SetMacroRunning(CancellationTokenSource cts)
    {
        lock (_stateLock)
        {
            _currentMacroCts = cts;
            Volatile.Write(ref _macroRunningFlag, 1);
        }
    }

    private void ClearMacroRunning()
    {
        lock (_stateLock)
        {
            _currentMacroCts = null;
            Volatile.Write(ref _macroRunningFlag, 0);
        }
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd == IntPtr.Zero) return;
        if (_windowHandle != IntPtr.Zero && hwnd == _windowHandle) return;
        if (!IsWindow(hwnd)) return;
        Interlocked.Exchange(ref _lastExternalWindow, hwnd);
    }

    public async Task<MacroExecutionResult> RunMacroAsync(string? script, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(KeyboardMacroService));
        if (string.IsNullOrWhiteSpace(script))
            return MacroExecutionResult.Skip("No macro script configured.");

        bool lockTaken = false;
        try
        {
            await _macroLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            lockTaken = true;
        }
        catch (OperationCanceledException)
        {
            return MacroExecutionResult.Canceled("マクロ実行がキャンセルされました。");
        }

        CancellationTokenSource? linkedCts = null;
        try
        {
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            SetMacroRunning(linkedCts);
            try
            {
                return await Task.Run(() => RunMacroInternal(script!, linkedCts.Token), linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return MacroExecutionResult.Canceled("マクロ実行がキャンセルされました。");
            }
        }
        finally
        {
            ClearMacroRunning();
            linkedCts?.Dispose();
            if (lockTaken)
            {
                _macroLock.Release();
            }
        }
    }

    private MacroExecutionResult RunMacroInternal(string script, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IntPtr target = ResolveTargetWindow();
        if (target == IntPtr.Zero)
            return MacroExecutionResult.Fail("ターゲットとなる直前のウィンドウが見つかりません。");

        try
        {
            if (!TryFocusTarget(target, out string? focusError))
            {
                return MacroExecutionResult.Fail(focusError ?? "ターゲットのフォーカス取得に失敗しました。");
            }

            var lines = script.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            if (!TryExpandRepeatBlocks(lines, out var expandedLines, out var expandError))
            {
                return MacroExecutionResult.Fail(expandError ?? "REPEAT ブロックの解釈に失敗しました。");
            }
            var buffer = new List<INPUT>(16);
            foreach (var rawLine in expandedLines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;

                if (StartsWithCommand(line, "WAIT"))
                {
                    if (!int.TryParse(line.AsSpan(4).Trim(), out var waitMs) || waitMs < 0 || waitMs > 60000)
                    {
                        return MacroExecutionResult.Fail($"WAIT に指定できる時間は 0〜60000 ミリ秒です: \"{line}\"");
                    }
                    if (!TryFlushInputs(buffer, out var flushError))
                    {
                        return MacroExecutionResult.Fail(flushError ?? "SendInput の実行に失敗しました。");
                    }
                    if (waitMs > 0 && cancellationToken.WaitHandle.WaitOne(waitMs))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    continue;
                }

                if (StartsWithCommand(line, "TEXT"))
                {
                    var text = line.Length > 4 ? line[4..].TrimStart() : string.Empty;
                    if (!TryFlushInputs(buffer, out var flushBeforeTextError))
                    {
                        return MacroExecutionResult.Fail(flushBeforeTextError ?? "SendInput の実行に失敗しました。");
                    }
                    if (!TrySendUnicodeText(text, cancellationToken, out var textError))
                    {
                        return MacroExecutionResult.Fail(textError ?? "TEXT コマンドの送信に失敗しました。");
                    }
                    continue;
                }

                if (StartsWithCommand(line, "CLIPTEXT"))
                {
                    var text = line.Length > 8 ? line[8..].TrimStart() : string.Empty;
                    if (!TryFlushInputs(buffer, out var flushBeforeClipError))
                    {
                        return MacroExecutionResult.Fail(flushBeforeClipError ?? "SendInput の実行に失敗しました。");
                    }
                    if (!TrySetClipboardText(text, out var clipboardError))
                    {
                        return MacroExecutionResult.Fail(clipboardError ?? "クリップボード操作に失敗しました。");
                    }
                    if (!TryAppendCombination("CTRL+V", buffer, out var pasteError))
                    {
                        return MacroExecutionResult.Fail(pasteError ?? "Ctrl+V の送信に失敗しました。");
                    }
                    if (!TryFlushInputs(buffer, out var flushAfterClipError))
                    {
                        return MacroExecutionResult.Fail(flushAfterClipError ?? "SendInput の実行に失敗しました。");
                    }
                    if (ClipTextAutoWaitMilliseconds > 0 && cancellationToken.WaitHandle.WaitOne(ClipTextAutoWaitMilliseconds))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    continue;
                }

                if (StartsWithCommand(line, "KEYDOWN"))
                {
                    var token = line.Length > 7 ? line[7..].Trim() : string.Empty;
                    if (!TryResolveKey(token, out var key))
                        return MacroExecutionResult.Fail($"KEYDOWN のキー名が不正です: \"{token}\"");
                    AppendKey(buffer, key, false);
                    continue;
                }

                if (StartsWithCommand(line, "KEYUP"))
                {
                    var token = line.Length > 5 ? line[5..].Trim() : string.Empty;
                    if (!TryResolveKey(token, out var key))
                        return MacroExecutionResult.Fail($"KEYUP のキー名が不正です: \"{token}\"");
                    AppendKey(buffer, key, true);
                    continue;
                }

                if (StartsWithCommand(line, "KEY"))
                {
                    var combo = line.Length > 3 ? line[3..].Trim() : string.Empty;
                    if (!TryAppendCombination(combo, buffer, out var error))
                    {
                        return MacroExecutionResult.Fail(error ?? $"KEY の書式が不正です: \"{combo}\"");
                    }
                    continue;
                }

                return MacroExecutionResult.Fail($"未知のマクロ命令です: \"{line}\"");
            }

            if (!TryFlushInputs(buffer, out var finalFlushError))
            {
                return MacroExecutionResult.Fail(finalFlushError ?? "SendInput の実行に失敗しました。");
            }

            return MacroExecutionResult.Ok();
        }
        catch (OperationCanceledException)
        {
            return MacroExecutionResult.Canceled("マクロ実行がキャンセルされました。");
        }
    }

    private static bool StartsWithCommand(string line, string command) =>
        line.Length >= command.Length && line.StartsWith(command, StringComparison.OrdinalIgnoreCase);

    private static bool TryExpandRepeatBlocks(string[] lines, out List<string> expanded, out string? error)
    {
        expanded = new List<string>(lines.Length);
        error = null;
        var stack = new Stack<RepeatFrame>();
        for (int i = 0; i < lines.Length; i++)
        {
            var rawLine = lines[i];
            var trimmed = rawLine.Trim();

            if (StartsWithCommand(trimmed, "REPEAT"))
            {
                var countToken = trimmed.Length > 6 ? trimmed[6..].Trim() : string.Empty;
                if (!int.TryParse(countToken, out var repeat))
                {
                    error = $"REPEAT の回数指定が不正です: \"{trimmed}\"";
                    return false;
                }
                if (repeat < 0 || repeat > MaxRepeatCount)
                {
                    error = $"REPEAT に指定できる回数は 0〜{MaxRepeatCount} です: \"{trimmed}\"";
                    return false;
                }

                stack.Push(new RepeatFrame(repeat));
                continue;
            }

            if (StartsWithCommand(trimmed, "ENDREPEAT"))
            {
                var remainder = trimmed.Length > 9 ? trimmed[9..].Trim() : string.Empty;
                if (remainder.Length > 0)
                {
                    error = $"ENDREPEAT 行に余分な記述があります: \"{trimmed}\"";
                    return false;
                }
                if (stack.Count == 0)
                {
                    error = "ENDREPEAT に対応する REPEAT が見つかりません。";
                    return false;
                }

                var frame = stack.Pop();
                if (frame.Count == 0)
                {
                    continue;
                }

                var target = stack.Count > 0 ? stack.Peek().Lines : expanded;
                for (int r = 0; r < frame.Count; r++)
                {
                    target.AddRange(frame.Lines);
                }
                continue;
            }

            if (stack.Count > 0)
            {
                stack.Peek().Lines.Add(rawLine);
            }
            else
            {
                expanded.Add(rawLine);
            }
        }

        if (stack.Count > 0)
        {
            error = "REPEAT ブロックが ENDREPEAT で閉じられていません。";
            return false;
        }

        return true;
    }

    private bool TrySetClipboardText(string text, out string? error)
    {
        error = null;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            error = "アプリケーションのディスパッチャが利用できません。";
            return false;
        }

        string clipboardText = text ?? string.Empty;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            Exception? operationError = null;

            void SetClipboard()
            {
                try
                {
                    Clipboard.SetText(clipboardText);
                }
                catch (Exception ex)
                {
                    operationError = ex;
                }
            }

            if (dispatcher.CheckAccess())
            {
                SetClipboard();
            }
            else
            {
                dispatcher.Invoke(SetClipboard);
            }

            if (operationError == null)
            {
                return true;
            }

            if (attempt == 2)
            {
                error = $"クリップボードへの書き込みに失敗しました: {operationError.Message}";
                _logger.Error($"Clipboard.SetText failed: {operationError}");
                return false;
            }

            Thread.Sleep(20);
        }

        return false;
    }

    private IntPtr ResolveTargetWindow()
    {
        var target = Volatile.Read(ref _lastExternalWindow);
        if (target != IntPtr.Zero && IsWindow(target))
        {
            return target;
        }

        var fg = GetForegroundWindow();
        if (fg != IntPtr.Zero && fg != _windowHandle && IsWindow(fg))
        {
            return fg;
        }

        return IntPtr.Zero;
    }

    private bool TryFlushInputs(List<INPUT> buffer, out string? error)
    {
        error = null;
        if (buffer.Count == 0) return true;
        var arr = buffer.ToArray();
        buffer.Clear();
        return TrySendInputArray(arr, arr.Length, out error);
    }

    private static INPUT CreateVirtualKeyInput(ushort vk, bool keyUp)
    {
        ushort scanCode = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
        uint flags = KEYEVENTF_SCANCODE;
        if (IsExtendedKey(vk))
        {
            flags |= KEYEVENTF_EXTENDEDKEY;
        }
        if (keyUp) flags |= KEYEVENTF_KEYUP;

        // Fallback to virtual-key mode when scan code is missing
        bool useVirtualKey = scanCode == 0;

        return new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = useVirtualKey ? vk : (ushort)0,
                    wScan = useVirtualKey ? (ushort)0 : scanCode,
                    dwFlags = useVirtualKey ? (keyUp ? KEYEVENTF_KEYUP : 0) : flags,
                    dwExtraInfo = GetMessageExtraInfo()
                }
            }
        };
    }

    private static void AppendKey(List<INPUT> buffer, ushort vk, bool keyUp)
    {
        buffer.Add(CreateVirtualKeyInput(vk, keyUp));
    }

    private bool TrySendUnicodeText(string text, CancellationToken cancellationToken, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(text)) return true;

        var inputs = new INPUT[2];
        foreach (var ch in text)
        {
            cancellationToken.ThrowIfCancellationRequested();

            inputs[0] = CreateUnicodeInput(ch, keyUp: false);
            inputs[1] = CreateUnicodeInput(ch, keyUp: true);
            cancellationToken.ThrowIfCancellationRequested();
            if (!TrySendInputArray(inputs, inputs.Length, out var charError))
            {
                error = charError;
                return false;
            }
            bool isWhitespace = char.IsWhiteSpace(ch);
            DelayFor(isWhitespace ? TextSendWhitespaceDelayMilliseconds : TextSendInterCharacterDelayMilliseconds, cancellationToken);
        }

        error = null;
        return true;
    }

    private static INPUT CreateUnicodeInput(char ch, bool keyUp) =>
        new()
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = ch,
                    dwFlags = keyUp ? (KEYEVENTF_UNICODE | KEYEVENTF_KEYUP) : KEYEVENTF_UNICODE,
                    dwExtraInfo = GetMessageExtraInfo()
                }
            }
        };

    private bool TrySendInputArray(INPUT[] inputs, int length, out string? error)
    {
        error = null;
        if (length == 0) return true;
        var sent = SendInput((uint)length, inputs, Marshal.SizeOf<INPUT>());
        if (sent == length) return true;
        int err = Marshal.GetLastWin32Error();
        error = $"SendInput の呼び出しに失敗しました (Error={err}).";
        _logger.Error($"SendInput failure: requested={length}, sent={sent}, cbSize={Marshal.SizeOf<INPUT>()}, error={err}, firstType={(length > 0 ? inputs[0].type : 0)}, firstVk={(length > 0 ? inputs[0].u.ki.wVk : 0)}, firstFlags={(length > 0 ? inputs[0].u.ki.dwFlags : 0)}");
        return false;
    }

    private static void DelayFor(int milliseconds, CancellationToken cancellationToken)
    {
        if (milliseconds <= 0) return;
        if (cancellationToken.WaitHandle.WaitOne(milliseconds))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static bool TryAppendCombination(string combo, List<INPUT> buffer, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(combo))
        {
            error = "KEY の後にキー指定がありません。";
            return false;
        }

        var parts = combo.Split('+', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            error = "KEY の書式が不正です。";
            return false;
        }

        var modifiers = new List<ushort>();
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var token = parts[i].Trim();
            if (!ModifierTokens.Contains(token))
            {
                error = $"修飾キーの指定が不正です: \"{token}\"";
                return false;
            }
            if (!TryResolveModifier(token, out var vk))
            {
                error = $"修飾キーに対応する仮想キーを取得できません: \"{token}\"";
                return false;
            }
            modifiers.Add(vk);
        }

        if (!TryResolveKey(parts[^1].Trim(), out var mainKey))
        {
            error = $"キーの指定が不正です: \"{parts[^1].Trim()}\"";
            return false;
        }

        foreach (var mod in modifiers)
        {
            AppendKey(buffer, mod, keyUp: false);
        }
        AppendKey(buffer, mainKey, keyUp: false);
        AppendKey(buffer, mainKey, keyUp: true);
        for (int i = modifiers.Count - 1; i >= 0; i--)
        {
            AppendKey(buffer, modifiers[i], keyUp: true);
        }

        return true;
    }

    private sealed class RepeatFrame
    {
        public RepeatFrame(int count)
        {
            Count = count;
        }

        public int Count { get; }
        public List<string> Lines { get; } = new();
    }

    private bool TryFocusTarget(IntPtr hwnd, out string? error)
    {
        error = null;
        if (!IsWindow(hwnd))
        {
            error = "ターゲットウィンドウが存在しません。";
            return false;
        }

        uint ownerThread = _ownerThreadId;
        if (ownerThread == 0 && _windowHandle != IntPtr.Zero)
        {
            ownerThread = GetWindowThreadProcessId(_windowHandle, out _);
        }
        var currentThread = GetCurrentThreadId();
        var targetThread = GetWindowThreadProcessId(hwnd, out _);
        bool attachedOwner = false;
        bool attachedCurrent = false;
        if (ownerThread != 0 && ownerThread != targetThread)
        {
            attachedOwner = AttachThreadInput(ownerThread, targetThread, true);
            if (!attachedOwner)
            {
                error = "入力スレッドの一時接続に失敗しました。";
                return false;
            }
        }
        if (currentThread != targetThread && currentThread != ownerThread)
        {
            if (!AttachThreadInput(currentThread, targetThread, true))
            {
                error = "入力スレッドの一時接続に失敗しました。";
                return false;
            }
            attachedCurrent = true;
        }

        try
        {
            if (IsIconic(hwnd))
            {
                ShowWindow(hwnd, SW_RESTORE);
            }

            if (!SetForegroundWindow(hwnd))
            {
                error = "SetForegroundWindow に失敗しました。";
                return false;
            }
        }
        finally
        {
            if (attachedCurrent)
            {
                AttachThreadInput(currentThread, targetThread, false);
            }
            if (attachedOwner)
            {
                AttachThreadInput(ownerThread, targetThread, false);
            }
        }

        return true;
    }

    private static bool TryResolveKey(string token, out ushort key)
    {
        key = 0;
        if (string.IsNullOrWhiteSpace(token)) return false;
        token = token.Trim();
        if (KeyMap.TryGetValue(token, out key)) return true;
        if (token.Length == 1)
        {
            key = (ushort)char.ToUpperInvariant(token[0]);
            return true;
        }
        return false;
    }

    private static bool TryResolveModifier(string token, out ushort key)
    {
        key = 0;
        return token.ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => (key = VK_CONTROL) != 0,
            "SHIFT" => (key = VK_SHIFT) != 0,
            "ALT" or "MENU" => (key = VK_MENU) != 0,
            "WIN" or "LWIN" => (key = VK_LWIN) != 0,
            "RWIN" => (key = VK_RWIN) != 0,
            _ => false
        };
    }

    private static Dictionary<string, ushort> CreateKeyMap()
    {
        var map = new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["ENTER"] = VK_RETURN,
            ["RETURN"] = VK_RETURN,
            ["ESC"] = VK_ESCAPE,
            ["ESCAPE"] = VK_ESCAPE,
            ["TAB"] = VK_TAB,
            ["SPACE"] = VK_SPACE,
            ["ALT"] = VK_MENU,
            ["BACK"] = VK_BACK,
            ["BACKSPACE"] = VK_BACK,
            ["UP"] = VK_UP,
            ["DOWN"] = VK_DOWN,
            ["LEFT"] = VK_LEFT,
            ["RIGHT"] = VK_RIGHT,
            ["HOME"] = VK_HOME,
            ["END"] = VK_END,
            ["PGUP"] = VK_PRIOR,
            ["PAGEUP"] = VK_PRIOR,
            ["PGDN"] = VK_NEXT,
            ["PAGEDOWN"] = VK_NEXT,
            ["DELETE"] = VK_DELETE,
            ["DEL"] = VK_DELETE,
            ["INSERT"] = VK_INSERT,
            ["INS"] = VK_INSERT,
            ["F1"] = VK_F1,
            ["F2"] = VK_F2,
            ["F3"] = VK_F3,
            ["F4"] = VK_F4,
            ["F5"] = VK_F5,
            ["F6"] = VK_F6,
            ["F7"] = VK_F7,
            ["F8"] = VK_F8,
            ["F9"] = VK_F9,
            ["F10"] = VK_F10,
            ["F11"] = VK_F11,
            ["F12"] = VK_F12,
            ["F13"] = VK_F13,
            ["F14"] = VK_F14,
            ["F15"] = VK_F15,
            ["F16"] = VK_F16,
            ["F17"] = VK_F17,
            ["F18"] = VK_F18,
            ["F19"] = VK_F19,
            ["F20"] = VK_F20,
            ["F21"] = VK_F21,
            ["F22"] = VK_F22,
            ["F23"] = VK_F23,
            ["F24"] = VK_F24,
            ["CAPSLOCK"] = VK_CAPITAL,
            ["SCROLLLOCK"] = VK_SCROLL,
            ["PAUSE"] = VK_PAUSE,
            ["BREAK"] = VK_PAUSE,
            ["PRINTSCREEN"] = VK_SNAPSHOT,
            ["PRTSC"] = VK_SNAPSHOT,
            ["APPS"] = VK_APPS,
            ["MENU"] = VK_APPS,
            ["NUMLOCK"] = VK_NUMLOCK,
            ["NUM0"] = VK_NUMPAD0,
            ["NUM1"] = VK_NUMPAD1,
            ["NUM2"] = VK_NUMPAD2,
            ["NUM3"] = VK_NUMPAD3,
            ["NUM4"] = VK_NUMPAD4,
            ["NUM5"] = VK_NUMPAD5,
            ["NUM6"] = VK_NUMPAD6,
            ["NUM7"] = VK_NUMPAD7,
            ["NUM8"] = VK_NUMPAD8,
            ["NUM9"] = VK_NUMPAD9,
            ["MULTIPLY"] = VK_MULTIPLY,
            ["ADD"] = VK_ADD,
            ["SUBTRACT"] = VK_SUBTRACT,
            ["MINUS"] = VK_OEM_MINUS,
            ["DIVIDE"] = VK_DIVIDE,
            ["SEPARATOR"] = VK_SEPARATOR,
            ["DECIMAL"] = VK_DECIMAL,
            ["OEM1"] = VK_OEM_1,
            ["OEMPLUS"] = VK_OEM_PLUS,
            ["OEMCOMMA"] = VK_OEM_COMMA,
            ["OEMMINUS"] = VK_OEM_MINUS,
            ["OEMPERIOD"] = VK_OEM_PERIOD,
            ["OEM2"] = VK_OEM_2,
            ["OEM3"] = VK_OEM_3,
            ["OEM4"] = VK_OEM_4,
            ["OEM5"] = VK_OEM_5,
            ["OEM6"] = VK_OEM_6,
            ["OEM7"] = VK_OEM_7
        };

        for (char c = 'A'; c <= 'Z'; c++)
        {
            map[c.ToString()] = (ushort)c;
        }

        for (char c = '0'; c <= '9'; c++)
        {
            map[c.ToString()] = (ushort)c;
        }

        return map;
    }

    private static bool IsExtendedKey(ushort vk) =>
        vk == VK_RIGHT || vk == VK_LEFT || vk == VK_UP || vk == VK_DOWN ||
        vk == VK_HOME || vk == VK_END || vk == VK_PRIOR || vk == VK_NEXT ||
        vk == VK_INSERT || vk == VK_DELETE || vk == VK_APPS ||
        vk == VK_RWIN || vk == VK_LWIN || vk == VK_MENU;

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const int SW_RESTORE = 9;

    private const uint MAPVK_VK_TO_VSC = 0x0;

    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_MENU = 0x12;
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_RWIN = 0x5C;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_ESCAPE = 0x1B;
    private const ushort VK_TAB = 0x09;
    private const ushort VK_SPACE = 0x20;
    private const ushort VK_BACK = 0x08;
    private const ushort VK_UP = 0x26;
    private const ushort VK_DOWN = 0x28;
    private const ushort VK_LEFT = 0x25;
    private const ushort VK_RIGHT = 0x27;
    private const ushort VK_HOME = 0x24;
    private const ushort VK_END = 0x23;
    private const ushort VK_PRIOR = 0x21;
    private const ushort VK_NEXT = 0x22;
    private const ushort VK_DELETE = 0x2E;
    private const ushort VK_INSERT = 0x2D;
    private const ushort VK_F1 = 0x70;
    private const ushort VK_F2 = 0x71;
    private const ushort VK_F3 = 0x72;
    private const ushort VK_F4 = 0x73;
    private const ushort VK_F5 = 0x74;
    private const ushort VK_F6 = 0x75;
    private const ushort VK_F7 = 0x76;
    private const ushort VK_F8 = 0x77;
    private const ushort VK_F9 = 0x78;
    private const ushort VK_F10 = 0x79;
    private const ushort VK_F11 = 0x7A;
    private const ushort VK_F12 = 0x7B;
    private const ushort VK_F13 = 0x7C;
    private const ushort VK_F14 = 0x7D;
    private const ushort VK_F15 = 0x7E;
    private const ushort VK_F16 = 0x7F;
    private const ushort VK_F17 = 0x80;
    private const ushort VK_F18 = 0x81;
    private const ushort VK_F19 = 0x82;
    private const ushort VK_F20 = 0x83;
    private const ushort VK_F21 = 0x84;
    private const ushort VK_F22 = 0x85;
    private const ushort VK_F23 = 0x86;
    private const ushort VK_F24 = 0x87;
    private const ushort VK_CAPITAL = 0x14;
    private const ushort VK_SCROLL = 0x91;
    private const ushort VK_PAUSE = 0x13;
    private const ushort VK_SNAPSHOT = 0x2C;
    private const ushort VK_APPS = 0x5D;
    private const ushort VK_NUMLOCK = 0x90;
    private const ushort VK_NUMPAD0 = 0x60;
    private const ushort VK_NUMPAD1 = 0x61;
    private const ushort VK_NUMPAD2 = 0x62;
    private const ushort VK_NUMPAD3 = 0x63;
    private const ushort VK_NUMPAD4 = 0x64;
    private const ushort VK_NUMPAD5 = 0x65;
    private const ushort VK_NUMPAD6 = 0x66;
    private const ushort VK_NUMPAD7 = 0x67;
    private const ushort VK_NUMPAD8 = 0x68;
    private const ushort VK_NUMPAD9 = 0x69;
    private const ushort VK_MULTIPLY = 0x6A;
    private const ushort VK_ADD = 0x6B;
    private const ushort VK_SEPARATOR = 0x6C;
    private const ushort VK_SUBTRACT = 0x6D;
    private const ushort VK_DECIMAL = 0x6E;
    private const ushort VK_DIVIDE = 0x6F;
    private const ushort VK_OEM_1 = 0xBA;
    private const ushort VK_OEM_PLUS = 0xBB;
    private const ushort VK_OEM_COMMA = 0xBC;
    private const ushort VK_OEM_MINUS = 0xBD;
    private const ushort VK_OEM_PERIOD = 0xBE;
    private const ushort VK_OEM_2 = 0xBF;
    private const ushort VK_OEM_3 = 0xC0;
    private const ushort VK_OEM_4 = 0xDB;
    private const ushort VK_OEM_5 = 0xDC;
    private const ushort VK_OEM_6 = 0xDD;
    private const ushort VK_OEM_7 = 0xDE;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetMessageExtraInfo();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    public record MacroExecutionResult(bool Success, string Message, bool Executed, bool IsCanceled)
    {
        public static MacroExecutionResult Ok() => new(true, string.Empty, true, false);
        public static MacroExecutionResult Skip(string message) => new(true, message, false, false);
        public static MacroExecutionResult Fail(string message) => new(false, message, false, false);
        public static MacroExecutionResult Canceled(string message) => new(false, message, false, true);
    }
}
