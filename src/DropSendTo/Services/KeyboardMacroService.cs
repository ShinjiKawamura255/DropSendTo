using System;
using System.Buffers;
using WpfClipboard = System.Windows.Clipboard;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Linq;
using DropSendTo.Models;

namespace DropSendTo.Services;

public sealed class KeyboardMacroService : IDisposable
{
    private const int MaxRepeatCount = 1000;
    private const int TextSendInterCharacterDelayMilliseconds = 18;
    private const int TextSendWhitespaceDelayMilliseconds = 28;
    private const int ClipTextAutoWaitMilliseconds = 30;
    private const string MacroPopupTitle = "DropSendTo Macro";

    private readonly SemaphoreSlim _macroLock = new(1, 1);
    private readonly object _stateLock = new();
    private readonly LoggerService _logger = LoggerService.Instance;
    private readonly Stack<MacroExecutionEntry> _macroStack = new();
    private readonly Stack<(MacroSuspensionHandle Handle, MacroExecutionSession Session)> _suspensionStack = new();
    private TaskCompletionSource<object?> _macroIdleTcs = CreateIdleTask(completed: true);
    private int _macroRunningCount;
    private IntPtr _windowHandle;
    private IntPtr _lastExternalWindow;
    private static readonly AsyncLocal<MacroCursorContext?> CurrentMacroCursor = new();
    private static readonly AsyncLocal<MouseHoldTracker?> _currentMouseTracker = new();
    private static int _useTestActiveWindowBounds;
    private static RECT _testActiveWindowRect;
    private IntPtr _winEventHook;
    private WinEventDelegate? _winEventCallback;
    private bool _disposed;
    private uint _ownerThreadId;
    private Func<KeyChord?>? _prefixChordAccessor;
    private Func<bool>? _prefixIsArmedAccessor;
    private Action? _prefixResetAction;
    private static Func<uint, INPUT[], int, uint>? _sendInputOverride;
    private const int DefaultReadFileLimitBytes = 4096;
    private const int MaxReadFileLimitBytes = 1024 * 1024;

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

    internal void SetPrefixStateAccessors(Func<KeyChord?>? chordAccessor, Func<bool>? isArmedAccessor, Action? resetAction)
    {
        _prefixChordAccessor = chordAccessor;
        _prefixIsArmedAccessor = isArmedAccessor;
        _prefixResetAction = resetAction;
    }

    public bool IsMacroRunning => Volatile.Read(ref _macroRunningCount) > 0;

    public bool CancelCurrentMacro()
    {
        MacroExecutionEntry? entry = null;
        lock (_stateLock)
        {
            if (_macroStack.Count > 0)
            {
                entry = _macroStack.Peek();
            }
        }

        if (entry is null) return false;
        var cts = entry.Value.Cancellation;
        if (cts.IsCancellationRequested) return false;

        try
        {
            cts.Cancel();
            if (entry.Value.Session.IsPaused)
            {
                entry.Value.Session.ResumeAfterCancel();
            }
            _logger.Info("Macro execution cancel requested.");
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public Task WaitForIdleAsync(CancellationToken cancellationToken)
    {
        Task waitTask;
        lock (_stateLock)
        {
            if (_macroStack.Count == 0)
            {
                return Task.CompletedTask;
            }
            waitTask = _macroIdleTcs.Task;
        }

        return waitTask.WaitAsync(cancellationToken);
    }

    public static bool TryValidateScript(string? script, SlotExecutionMode mode, out string? error)
    {
        using var service = new KeyboardMacroService();
        return service.TryValidateScriptInternal(script, mode, out error);
    }

    private bool TryValidateScriptInternal(string? script, SlotExecutionMode mode, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(script))
        {
            return true;
        }

        bool lockTaken = false;
        var session = new MacroExecutionSession(_macroLock);
        try
        {
            _macroLock.Wait();
            lockTaken = true;
            session.MarkLockHeld();
            MacroExecutionContext? context = mode == SlotExecutionMode.MacroScriptExtended
                ? new MacroExecutionContext(mode, _ => LaunchResult.Ok(), "(Validation)", string.Empty)
                : null;

            var result = RunMacroInternal(script, context, CancellationToken.None, session, validateOnly: true);
            if (!result.Success)
            {
                error = string.IsNullOrWhiteSpace(result.Message)
                    ? "マクロの構文に誤りがあります。"
                    : result.Message;
            }
            if (session.LockHeld)
            {
                _macroLock.Release();
                lockTaken = false;
            }
            return result.Success;
        }
        finally
        {
            if (lockTaken)
            {
                _macroLock.Release();
            }
        }
    }

    public async Task<bool> CancelAllRunningMacrosAsync(CancellationToken cancellationToken)
    {
        bool issued = false;
        while (true)
        {
            bool hasMacro;
            lock (_stateLock)
            {
                hasMacro = _macroStack.Count > 0;
            }
            if (!hasMacro)
            {
                break;
            }

            if (CancelCurrentMacro())
            {
                issued = true;
            }
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        await WaitForIdleAsync(cancellationToken).ConfigureAwait(false);
        return issued;
    }

    public async Task<IAsyncDisposable?> SuspendCurrentMacroAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        MacroExecutionEntry? entry = null;
        lock (_stateLock)
        {
            if (_macroStack.Count > 0)
            {
                entry = _macroStack.Peek();
            }
        }

        if (entry is null) return null;

        var session = entry.Value.Session;
        if (session.IsPaused)
        {
            var existingHandle = new MacroSuspensionHandle(this);
            lock (_stateLock)
            {
                _suspensionStack.Push((existingHandle, session));
            }
            return existingHandle;
        }

        var paused = await session.PauseAsync(timeout, cancellationToken).ConfigureAwait(false);
        if (!paused)
        {
            return null;
        }

        var handle = new MacroSuspensionHandle(this);
        lock (_stateLock)
        {
            _suspensionStack.Push((handle, session));
        }
        _logger.Info("Macro execution suspended to allow nested macro execution.");
        return handle;
    }

    private async Task ResumeSuspendedMacroAsync(MacroSuspensionHandle handle, CancellationToken cancellationToken)
    {
        bool match;
        MacroExecutionSession? session = null;
        lock (_stateLock)
        {
            if (_suspensionStack.Count > 0 && ReferenceEquals(_suspensionStack.Peek().Handle, handle))
            {
                session = _suspensionStack.Pop().Session;
                match = true;
            }
            else
            {
                match = false;
            }
        }

        if (!match)
        {
            throw new InvalidOperationException("Suspended macro resume order mismatch.");
        }

        if (session != null)
        {
            await session.ResumeAsync(cancellationToken).ConfigureAwait(false);
        }
        _logger.Info("Suspended macro resumed.");
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

    private void SetMacroRunning(CancellationTokenSource cts, MacroExecutionSession session)
    {
        lock (_stateLock)
        {
            _macroStack.Push(new MacroExecutionEntry(cts, session));
            var count = _macroStack.Count;
            Volatile.Write(ref _macroRunningCount, count);
            if (count == 1 || _macroIdleTcs.Task.IsCompleted)
            {
                _macroIdleTcs = CreateIdleTask(completed: false);
            }
        }
    }

    private void ClearMacroRunning(MacroExecutionSession session)
    {
        TaskCompletionSource<object?>? idleSource = null;
        lock (_stateLock)
        {
            if (_macroStack.Count > 0 && ReferenceEquals(_macroStack.Peek().Session, session))
            {
                _macroStack.Pop();
            }
            var count = _macroStack.Count;
            Volatile.Write(ref _macroRunningCount, count);
            if (count == 0)
            {
                idleSource = _macroIdleTcs;
                _macroIdleTcs = CreateIdleTask(completed: true);
            }
        }
        idleSource?.TrySetResult(null);
    }

    private static TaskCompletionSource<object?> CreateIdleTask(bool completed)
    {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (completed)
        {
            tcs.SetResult(null);
        }
        return tcs;
    }

    private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd == IntPtr.Zero) return;
        if (_windowHandle != IntPtr.Zero && hwnd == _windowHandle) return;
        if (!IsWindow(hwnd)) return;
        Interlocked.Exchange(ref _lastExternalWindow, hwnd);
    }

    public async Task<MacroExecutionResult> RunMacroAsync(string? script, MacroExecutionContext? context = null, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(KeyboardMacroService));
        if (string.IsNullOrWhiteSpace(script))
            return MacroExecutionResult.Skip("No macro script configured.");
        var scriptToRun = script!;
        var trimmedScript = scriptToRun.Trim();

        if (trimmedScript.Equals("PREFIX PASSTHROUGH", StringComparison.OrdinalIgnoreCase) && IsMacroRunning)
        {
            if (TrySendPrefixPassthroughDirect(out var passthroughError))
            {
                _logger.Info("Prefix passthrough executed while macro is running.");
                return MacroExecutionResult.Ok();
            }

            var failMessage = passthroughError ?? "PREFIX PASSTHROUGH の実行に失敗しました。";
            _logger.Warn($"Failed to execute prefix passthrough while macro is running: {failMessage}");
            return MacroExecutionResult.Fail(failMessage);
        }

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

        MacroExecutionSession? session = null;
        CancellationTokenSource? linkedCts = null;
        try
        {
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            session = new MacroExecutionSession(_macroLock);
            session.MarkLockHeld();
            SetMacroRunning(linkedCts, session);
            MacroExecutionResult result;
            try
            {
                _logger.Info($"Macro execution started (length={scriptToRun.Length} chars).");
                result = await Task.Run(() => RunMacroInternal(scriptToRun, context, linkedCts.Token, session, validateOnly: false), linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                result = MacroExecutionResult.Canceled("マクロ実行がキャンセルされました。");
            }

            if (result.Success)
            {
                if (result.Executed)
                {
                    _logger.Info("Macro execution completed successfully.");
                }
                else if (!string.IsNullOrEmpty(result.Message))
                {
                    _logger.Info($"Macro execution skipped: {result.Message}");
                }
                else
                {
                    _logger.Info("Macro execution skipped.");
                }
            }
            else if (result.IsCanceled)
            {
                _logger.Info("Macro execution canceled.");
            }
            else
            {
                _logger.Warn(string.IsNullOrEmpty(result.Message)
                    ? "Macro execution failed."
                    : $"Macro execution failed: {result.Message}");
            }

            return result;
        }
        finally
        {
            if (session != null)
            {
                ClearMacroRunning(session);
            }
            linkedCts?.Dispose();
            if (session?.LockHeld ?? false)
            {
                _macroLock.Release();
            }
            else if (lockTaken)
            {
                _macroLock.Release();
            }
        }
    }

    private MacroExecutionResult RunMacroInternal(string script, MacroExecutionContext? context, CancellationToken cancellationToken, MacroExecutionSession session, bool validateOnly)
    {
        ThrowIfPausedOrCanceled(session, cancellationToken);
        IntPtr target = ResolveTargetWindow();
        bool targetAvailable = target != IntPtr.Zero;
        if (!targetAvailable)
        {
            _logger.Warn("No previous window captured; continuing macro execution without focusing a target.");
        }

        var buffer = new List<INPUT>(16);
        var keyTracker = new KeyHoldTracker();
        var mouseTracker = new MouseHoldTracker();
        _currentMouseTracker.Value = mouseTracker;
        var cursorScope = default(MacroCursorScope);
        bool prefixArmRequested = false;
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inactiveVariableScopes = validateOnly ? new Stack<Dictionary<string, string>>() : null;

        if (TryGetCursorPosition(out var cursorX, out var cursorY))
        {
            cursorScope = new MacroCursorScope(cursorX, cursorY);
        }
        else
        {
        _logger.Warn("Failed to capture initial cursor position for macro execution.");
    }

        static string FormatLineError(int number, string message) =>
            $"行 {number}: {message}";

        MacroExecutionResult CompleteResult(MacroExecutionResult result)
        {
            if (!result.Success)
            {
                buffer.Clear();
            }
            else if (validateOnly)
            {
                buffer.Clear();
            }

            if (mouseTracker.HasHeldButtons)
            {
                mouseTracker.ReleaseAll(buffer);
            }

            if (keyTracker.HasHeldKeys)
            {
                keyTracker.ReleaseAll(buffer);
            }

            if (!TryFlushInputsSafe(buffer, validateOnly, out var finalError))
            {
                if (result.Success)
                {
                    result = MacroExecutionResult.Fail(finalError ?? "SendInput の実行に失敗しました。");
                }
            }

            if (!validateOnly && prefixArmRequested && _prefixResetAction != null)
            {
                bool wasArmed = false;
                try
                {
                    wasArmed = _prefixIsArmedAccessor?.Invoke() ?? false;
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Prefix armed state check failed after macro execution: {ex}");
                }

                try
                {
                    _logger.Info(wasArmed
                        ? "Prefix disarmed after macro execution."
                        : "Prefix state cleared after macro execution.");
                    _prefixResetAction();
                }
                catch (Exception ex)
                {
                    _logger.Warn($"Failed to reset prefix state after macro execution: {ex}");
                }
            }

            return result;
        }

        void SyncInactiveVariableScopes(int previousDepth, int currentDepth)
        {
            if (!validateOnly || inactiveVariableScopes == null || previousDepth == currentDepth)
            {
                return;
            }

            if (previousDepth < currentDepth)
            {
                for (int depth = previousDepth; depth < currentDepth; depth++)
                {
                    inactiveVariableScopes.Push(variables);
                    variables = new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase);
                }
                return;
            }

            for (int depth = previousDepth; depth > currentDepth; depth--)
            {
                if (inactiveVariableScopes.Count == 0)
                {
                    break;
                }
                variables = inactiveVariableScopes.Pop();
            }
        }

        try
        {
            Func<ClipboardSnapshot> clipboardSnapshotProvider = validateOnly
                ? CreateValidationClipboardSnapshotProvider()
                : () => ClipboardHistoryService.Instance.GetSnapshot(null);
            IReadOnlyList<string>? dropPaths = validateOnly
                ? ValidationDropEntries
                : context?.DroppedPaths;
            var dropEntries = dropPaths ?? Array.Empty<string>();
            var specialResolver = CreateSpecialVariableResolver(
                clipboardSnapshotProvider,
                dropPaths,
                validationMode: validateOnly);
            var ifStack = new Stack<IfBlockState>();
            int inactiveIfDepth = 0;

            if (targetAvailable && !validateOnly)
            {
                if (!TryFocusTarget(target, out string? focusError))
                {
                    var message = focusError ?? "ターゲットのフォーカス取得に失敗しました。";
                    _logger.Warn($"Failed to focus previous window; attempting fallback to current foreground: {message}");

                    var foreground = GetForegroundWindow();
                    if (foreground != IntPtr.Zero && foreground != _windowHandle && IsWindow(foreground))
                    {
                        target = foreground;
                        if (TryFocusTarget(target, out var fallbackError))
                        {
                            _logger.Info("Fallback focus succeeded on current foreground window.");
                        }
                        else
                        {
                            var fallbackMessage = fallbackError ?? "SetForegroundWindow に失敗しました。";
                            _logger.Warn($"Fallback focus to current foreground window failed; continuing without focus: {fallbackMessage}");
                        }
                    }
                    else
                    {
                        _logger.Warn("No valid foreground window found for fallback focus; continuing without focus.");
                    }
                }
            }

            var lines = script.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            if (!TryExpandRepeatBlocks(lines, out var expandedLines, out var expandError))
            {
                return CompleteResult(MacroExecutionResult.Fail(expandError ?? "REPEAT ブロックの解釈に失敗しました。"));
            }
            if (!TryExpandForeachDropBlocks(expandedLines, dropEntries, out var foreachExpandedLines, out var foreachError))
            {
                return CompleteResult(MacroExecutionResult.Fail(foreachError ?? "FOREACH_DROP ブロックの解釈に失敗しました。"));
            }
            expandedLines = foreachExpandedLines;
            for (int lineIndex = 0; lineIndex < expandedLines.Count; lineIndex++)
            {
                var rawLine = expandedLines[lineIndex];
                int lineNumber = lineIndex + 1;
                ThrowIfPausedOrCanceled(session, cancellationToken);
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;

                if (StartsWithCommand(line, "IF"))
                {
                    var conditionText = line.Length > 2 ? line[2..].Trim() : string.Empty;
                    conditionText = TrimInlineComment(conditionText);
                    var previousDepth = inactiveIfDepth;
                    if (!TryHandleIfDirective(conditionText, variables, specialResolver, ifStack, ref inactiveIfDepth, out var ifError))
                    {
                        var message = ifError ?? $"IF 条件の解釈に失敗しました: \"{line}\"";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    SyncInactiveVariableScopes(previousDepth, inactiveIfDepth);
                    continue;
                }

                if (TryParseElseIfCondition(line, out var elseIfCondition))
                {
                    var previousDepth = inactiveIfDepth;
                    if (!TryHandleElseIfDirective(elseIfCondition, variables, specialResolver, ifStack, ref inactiveIfDepth, out var elseIfError))
                    {
                        var message = elseIfError ?? $"ELSEIF 条件の解釈に失敗しました: \"{line}\"";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    SyncInactiveVariableScopes(previousDepth, inactiveIfDepth);
                    continue;
                }

                if (StartsWithCommand(line, "ELSE"))
                {
                    var trailing = line.Length > 4 ? line[4..].Trim() : string.Empty;
                    trailing = TrimInlineComment(trailing);
                    if (!string.IsNullOrWhiteSpace(trailing))
                    {
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, "ELSE の後ろに余分な記述があります。")));
                    }
                    var previousDepth = inactiveIfDepth;
                    if (!TryHandleElseDirective(ifStack, ref inactiveIfDepth, out var elseError))
                    {
                        var message = elseError ?? "ELSE の解釈に失敗しました。";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    SyncInactiveVariableScopes(previousDepth, inactiveIfDepth);
                    continue;
                }

                if (StartsWithCommand(line, "ENDIF"))
                {
                    var trailing = line.Length > 5 ? line[5..].Trim() : string.Empty;
                    trailing = TrimInlineComment(trailing);
                    if (!string.IsNullOrWhiteSpace(trailing))
                    {
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, "ENDIF の後ろに余分な記述があります。")));
                    }
                    var previousDepth = inactiveIfDepth;
                    if (!TryHandleEndIfDirective(ifStack, ref inactiveIfDepth, out var endifError))
                    {
                        var message = endifError ?? "ENDIF の解釈に失敗しました。";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    SyncInactiveVariableScopes(previousDepth, inactiveIfDepth);
                    continue;
                }

                if (inactiveIfDepth > 0 && !validateOnly)
                {
                    continue;
                }

                if (StartsWithCommand(line, "TESTPATH"))
                {
                    var payload = line.Length > 8 ? line[8..].Trim() : string.Empty;
                    payload = TrimInlineComment(payload);
                    if (string.IsNullOrWhiteSpace(payload))
                    {
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, "TESTPATH には変数名とパスを指定してください。")));
                    }

                    var firstSpace = FindFirstWhitespace(payload);
                    if (firstSpace < 0)
                    {
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, "TESTPATH には変数名とパスを指定してください。")));
                    }

                    var variableName = payload[..firstSpace].Trim();
                    if (!IsValidVariableName(variableName))
                    {
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, $"変数名が不正です: \"{variableName}\"")));
                    }

                    var operandText = payload[(firstSpace + 1)..].Trim();
                    if (operandText.Length == 0)
                    {
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, "TESTPATH にはパスを指定してください。")));
                    }

                    string rawPathLiteral;
                    if (operandText.Length > 0 && operandText[0] == '"')
                    {
                        int argIndex = 0;
                        if (!TryParseQuotedArgument(operandText, ref argIndex, "TESTPATH", "パス", out rawPathLiteral, out var quotedError))
                        {
                            var message = quotedError ?? "TESTPATH のパス指定が不正です。";
                            return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                        }
                        if (argIndex < operandText.Length && !string.IsNullOrWhiteSpace(operandText[argIndex..]))
                        {
                            return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, "TESTPATH のパス指定の後ろに余分な記述があります。")));
                        }
                    }
                    else
                    {
                        rawPathLiteral = operandText;
                    }

                    if (!TryExpandVariables(rawPathLiteral, variables, out var expandedPath, out var pathExpandError, specialResolver))
                    {
                        var message = pathExpandError ?? "TESTPATH のパス展開に失敗しました。";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }

                    var normalizedPath = expandedPath.Trim();
                    if (normalizedPath.Length >= 2 &&
                        normalizedPath.StartsWith("\"", StringComparison.Ordinal) &&
                        normalizedPath.EndsWith("\"", StringComparison.Ordinal))
                    {
                        normalizedPath = normalizedPath.Substring(1, normalizedPath.Length - 2);
                    }
                    variables[variableName] = PathExists(normalizedPath) ? "1" : "0";
                    continue;
                }

                if (StartsWithCommand(line, "RENAME"))
                {
                    var payload = line.Length > 6 ? line[6..].Trim() : string.Empty;
                    payload = TrimInlineComment(payload);
                    if (!TryApplyRenameDirective(payload, variables, specialResolver, validateOnly, out var renameError))
                    {
                        var message = renameError ?? "RENAME の解釈に失敗しました。";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    continue;
                }

                if (StartsWithCommand(line, "RESOLVE_LINK"))
                {
                    var payload = line.Length > 12 ? line[12..].Trim() : string.Empty;
                    payload = TrimInlineComment(payload);
                    if (!TryApplyResolveLinkDirective(payload, variables, specialResolver, validateOnly, out var resolveError))
                    {
                        var message = resolveError ?? "RESOLVE_LINK の解釈に失敗しました。";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    continue;
                }

                if (StartsWithCommand(line, "READFILE"))
                {
                    var payload = line.Length > 8 ? line[8..].Trim() : string.Empty;
                    payload = TrimInlineComment(payload);
                    if (!TryApplyReadFileDirective(payload, variables, specialResolver, validateOnly, out var readError))
                    {
                        var message = readError ?? "READFILE の解釈に失敗しました。";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    continue;
                }

                if (StartsWithCommand(line, "POPUP"))
                {
                    var payload = line.Length > 5 ? line[5..].Trim() : string.Empty;
                    payload = TrimInlineComment(payload);
                    if (string.IsNullOrWhiteSpace(payload))
                    {
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, "POPUP にはメッセージを指定してください。")));
                    }

                    int argIndex = 0;
                    if (!TryParseQuotedArgument(payload, ref argIndex, "POPUP", "メッセージ", out var messageLiteral, out var popupParseError))
                    {
                        var message = popupParseError ?? "POPUP のメッセージ指定が不正です。";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }

                    if (argIndex < payload.Length && !string.IsNullOrWhiteSpace(payload[argIndex..]))
                    {
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, "POPUP のメッセージの後ろに余分な記述があります。")));
                    }

                    if (!TryExpandVariables(messageLiteral, variables, out var popupMessage, out var popupExpandError, specialResolver))
                    {
                        var message = popupExpandError ?? "POPUP のメッセージ展開に失敗しました。";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }

                    if (!validateOnly)
                    {
                        if (!TryShowPopup(popupMessage, MacroPopupTitle, MessageBoxImage.Information, out var popupError))
                        {
                            var message = popupError ?? "POPUP の表示に失敗しました。";
                            return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                        }
                    }

                    continue;
                }

                if (StartsWithCommand(line, "SET"))
                {
                    if (!TryApplySetDirective(line, variables, out var setName, out var setValue, out var setError, specialResolver))
                    {
                        var message = setError ?? $"SET コマンドの解釈に失敗しました: \"{line}\"";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    if (!string.IsNullOrEmpty(setName))
                    {
                        _logger.Info($"Macro variable set: {setName} (length={setValue?.Length ?? 0})");
                    }
                    continue;
                }

                if (StartsWithCommand(line, "UNSET"))
                {
                    if (!TryApplyUnsetDirective(line, variables, out var unsetName, out var removed, out var unsetError))
                    {
                        var message = unsetError ?? $"UNSET コマンドの解釈に失敗しました: \"{line}\"";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    if (!string.IsNullOrEmpty(unsetName))
                    {
                        if (removed)
                        {
                            _logger.Info($"Macro variable removed: {unsetName}");
                        }
                        else
                        {
                            _logger.Info($"Macro variable unset requested but not defined: {unsetName}");
                        }
                    }
                    continue;
                }

                if (StartsWithCommand(line, "ADD") || StartsWithCommand(line, "SUB") ||
                    StartsWithCommand(line, "MUL") || StartsWithCommand(line, "DIV"))
                {
                    if (!TryApplyMathDirective(line, variables, out var mathName, out var beforeValue, out var operandValue, out var resultValue, out var mathError, specialResolver))
                    {
                        var message = mathError ?? $"数値演算の解釈に失敗しました: \"{line}\"";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    if (!string.IsNullOrEmpty(mathName))
                    {
                        _logger.Info($"Macro variable math: {mathName} ({beforeValue}) -> {resultValue} (operand={operandValue}, op={ExtractCommandName(line)})");
                    }
                    continue;
                }

                if (StartsWithCommand(line, "APPEND") || StartsWithCommand(line, "PREPEND"))
                {
                    bool prepend = StartsWithCommand(line, "PREPEND");
                    if (!TryApplyConcatDirective(line, variables, prepend, out var concatName, out var newValue, out var concatError, specialResolver))
                    {
                        var message = concatError ?? $"文字列結合の解釈に失敗しました: \"{line}\"";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    if (!string.IsNullOrEmpty(concatName))
                    {
                        _logger.Info($"Macro variable {(prepend ? "prepend" : "append")}: {concatName} -> \"{TruncateForLog(newValue)}\"");
                    }
                    continue;
                }

                if (StartsWithCommand(line, "REPLACE_REGEX") || StartsWithCommand(line, "REPLACE-REGEX"))
                {
                    if (!TryApplyRegexReplaceDirective(line, variables, out var regexName, out var regexValue, out var regexError, specialResolver, out var regexReplacements))
                    {
                        var message = regexError ?? $"REPLACE_REGEX コマンドの解釈に失敗しました: \"{line}\"";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    if (!string.IsNullOrEmpty(regexName))
                    {
                        _logger.Info($"Macro variable regex replace: {regexName} (replaced {regexReplacements} match(es)) -> \"{TruncateForLog(regexValue)}\"");
                    }
                    continue;
                }

                if (StartsWithCommand(line, "REPLACE"))
                {
                    if (!TryApplyReplaceDirective(line, variables, out var replaceName, out var replaceValue, out var replaceError, specialResolver, out var replacements))
                    {
                        var message = replaceError ?? $"REPLACE コマンドの解釈に失敗しました: \"{line}\"";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    if (!string.IsNullOrEmpty(replaceName))
                    {
                        _logger.Info($"Macro variable replace: {replaceName} (replaced {replacements} occurrence(s)) -> \"{TruncateForLog(replaceValue)}\"");
                    }
                    continue;
                }

                if (StartsWithCommand(line, "WAIT"))
                {
                    var waitToken = line.Length > 4 ? line[4..].Trim() : string.Empty;
                    if (!TryExpandVariables(waitToken, variables, out var expandedWait, out var waitExpandError, specialResolver))
                    {
                        var message = waitExpandError ?? $"変数の解決に失敗しました: \"{line}\"";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    if (!int.TryParse(expandedWait, out var waitMs) || waitMs < 0 || waitMs > 60000)
                    {
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, $"WAIT に指定できる時間は 0〜60000 ミリ秒です: \"{line}\"")));
                    }
                    if (!TryFlushInputsSafe(buffer, validateOnly, out var flushError))
                    {
                        var message = flushError ?? "SendInput の実行に失敗しました。";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    if (!validateOnly)
                    {
                        DelayFor(waitMs, session, cancellationToken);
                    }
                    continue;
                }

                if (StartsWithCommand(line, "RETURN"))
                {
                    var payload = line.Length > 6 ? line[6..].Trim() : string.Empty;
                    payload = TrimInlineComment(payload);
                    string returnMessage = string.Empty;
                    if (!string.IsNullOrWhiteSpace(payload))
                    {
                        int argIndex = 0;
                        if (payload[0] == '"')
                    {
                        if (!TryParseQuotedArgument(payload, ref argIndex, "RETURN", "メッセージ", out var messageLiteral, out var returnParseError))
                        {
                            var message = returnParseError ?? "RETURN のメッセージ指定が不正です。";
                            return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                            }
                            if (argIndex < payload.Length && !string.IsNullOrWhiteSpace(payload[argIndex..]))
                            {
                                return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, "RETURN のメッセージの後ろに余分な記述があります。")));
                            }
                            if (!TryExpandVariables(messageLiteral, variables, out var expanded, out var returnExpandError, specialResolver))
                            {
                                var message = returnExpandError ?? "RETURN のメッセージ展開に失敗しました。";
                                return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                            }
                            returnMessage = expanded;
                        }
                        else
                        {
                            if (!TryExpandVariables(payload, variables, out var expanded, out var returnExpandError, specialResolver))
                            {
                                var message = returnExpandError ?? "RETURN のメッセージ展開に失敗しました。";
                                return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                            }
                            returnMessage = expanded;
                        }
                    }

                    if (!validateOnly && !string.IsNullOrWhiteSpace(returnMessage))
                    {
                        _logger.Info($"Macro RETURN: {returnMessage}");
                    }
                    return CompleteResult(MacroExecutionResult.Ok(returnMessage));
                }

                if (StartsWithCommand(line, "PREFIX"))
                {
                    var token = line.Length > 6 ? line[6..].Trim() : string.Empty;
                    bool passthrough = false;
                    if (!string.IsNullOrEmpty(token))
                    {
                        if (token.Equals("PASSTHROUGH", StringComparison.OrdinalIgnoreCase))
                        {
                            passthrough = true;
                        }
                        else if (token.Equals("SEND", StringComparison.OrdinalIgnoreCase) || token.Equals("ARM", StringComparison.OrdinalIgnoreCase))
                        {
                            passthrough = false;
                        }
                        else
                        {
                            var message = $"PREFIX コマンドの書式が不正です: \"{line}\"";
                            return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                        }
                    }
                    if (!TryAppendPrefixSequence(buffer, passthrough, validateOnly, out var prefixError))
                    {
                        var message = prefixError ?? "PREFIX コマンドの実行に失敗しました。";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    if (!passthrough)
                    {
                        prefixArmRequested = true;
                    }
                    continue;
                }

                if (StartsWithCommand(line, "COMMAND"))
                {
                    if (context?.SlotMode != SlotExecutionMode.MacroScriptExtended)
                    {
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, "COMMAND コマンドは Macro Script 拡張モードでのみ使用できます。")));
                    }

                    var payload = line.Length > 7 ? line[7..].TrimStart() : string.Empty;
                    string? overrideArguments = null;
                    if (!string.IsNullOrEmpty(payload))
                    {
                        if (!TryExpandVariables(payload, variables, out var expandedPayload, out var payloadError, specialResolver))
                        {
                            var expansionMessage = payloadError ?? $"変数の解決に失敗しました: \"{line}\"";
                            return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, expansionMessage)));
                        }
                        overrideArguments = expandedPayload;
                    }

                    if (validateOnly)
                    {
                        continue;
                    }

                    if (!TryFlushInputsSafe(buffer, validateOnly, out var flushBeforeCommandError))
                    {
                        var message = flushBeforeCommandError ?? "SendInput の実行に失敗しました。";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }

                    LaunchResult launchResult;
                    try
                    {
                        launchResult = context.CommandInvoker?.Invoke(overrideArguments) ?? LaunchResult.Fail("COMMAND invoker is not available。");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Macro command invocation failed: {ex}");
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, "COMMAND コマンドの実行に失敗しました。")));
                    }

                    if (!launchResult.Success)
                    {
                        var message = string.IsNullOrWhiteSpace(launchResult.Message)
                            ? "COMMAND コマンドによる呼び出しに失敗しました。"
                            : $"COMMAND コマンドによる呼び出しに失敗しました: {launchResult.Message}";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }

                    var slotLabel = (context.SlotTitle ?? string.Empty).ReplaceLineEndings(" ").Trim();
                    if (slotLabel.Length == 0)
                    {
                        slotLabel = "(untitled)";
                    }
                    var overrideInfo = overrideArguments == null
                        ? "template arguments used"
                        : $"override length={overrideArguments.Length}";
                    var commandPath = string.IsNullOrWhiteSpace(context.CommandPath)
                        ? "(unspecified)"
                        : context.CommandPath;
                    _logger.Info($"Slot command invoked via macro (slot=\"{slotLabel}\", command=\"{commandPath}\", {overrideInfo}).");
                    continue;
                }

                if (StartsWithCommand(line, "TEXT"))
                {
                    var text = line.Length > 4 ? line[4..].TrimStart() : string.Empty;
                    if (!TryExpandVariables(text, variables, out var expandedText, out var textExpandError, specialResolver))
                    {
                        var message = textExpandError ?? $"変数の解決に失敗しました: \"{line}\"";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    if (!TryFlushInputsSafe(buffer, validateOnly, out var flushBeforeTextError))
                    {
                        var message = flushBeforeTextError ?? "SendInput の実行に失敗しました。";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    if (!validateOnly && !TrySendUnicodeText(expandedText, session, cancellationToken, out var textError))
                    {
                        var message = textError ?? "TEXT コマンドの送信に失敗しました。";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    continue;
                }

                if (StartsWithCommand(line, "SETCLIP"))
                {
                    var text = line.Length > 7 ? line[7..].TrimStart() : string.Empty;
                    if (!TryExpandVariables(text, variables, out var clipText, out var clipExpandError, specialResolver))
                    {
                        var message = clipExpandError ?? $"変数の解決に失敗しました: \"{line}\"";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    if (!TryFlushInputsSafe(buffer, validateOnly, out var flushBeforeClipError))
                    {
                        var message = flushBeforeClipError ?? "SendInput の実行に失敗しました。";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    if (!validateOnly && !TrySetClipboardText(clipText, out var clipboardError))
                    {
                        var message = clipboardError ?? "クリップボード操作に失敗しました。";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    continue;
                }

                if (StartsWithCommand(line, "CLIPTEXT"))
                {
                    var text = line.Length > 8 ? line[8..].TrimStart() : string.Empty;
                    if (!TryExpandVariables(text, variables, out var clipText, out var clipExpandError, specialResolver))
                    {
                        var message = clipExpandError ?? $"変数の解決に失敗しました: \"{line}\"";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    if (!TryFlushInputsSafe(buffer, validateOnly, out var flushBeforeClipError))
                    {
                        var message = flushBeforeClipError ?? "SendInput の実行に失敗しました。";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    if (!validateOnly && !TrySetClipboardText(clipText, out var clipboardError))
                    {
                        var message = clipboardError ?? "クリップボード操作に失敗しました。";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    if (!validateOnly && !TryAppendCombination("CTRL+V", buffer, InputExtraInfo.MacroPassthroughPointer, out var pasteError))
                    {
                        var message = pasteError ?? "Ctrl+V の送信に失敗しました。";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    if (!TryFlushInputsSafe(buffer, validateOnly, out var flushAfterClipError))
                    {
                        var message = flushAfterClipError ?? "SendInput の実行に失敗しました。";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    if (!validateOnly)
                    {
                        DelayFor(ClipTextAutoWaitMilliseconds, session, cancellationToken);
                    }
                    continue;
                }

                if (StartsWithCommand(line, "KEYDOWN"))
                {
                    var token = line.Length > 7 ? line[7..].Trim() : string.Empty;
                    if (!TryExpandVariables(token, variables, out var expandedToken, out var tokenExpandError, specialResolver))
                    {
                        var message = tokenExpandError ?? $"変数の解決に失敗しました: \"{line}\"";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    var resolvedToken = expandedToken.Trim();
                    if (!KeyChordParser.TryResolveKeyToken(resolvedToken, out var key))
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, $"KEYDOWN のキー名が不正です: \"{resolvedToken}\"")));
                    AppendKey(buffer, key, false);
                    keyTracker.TrackKeyDown(key);
                    continue;
                }

                if (StartsWithCommand(line, "KEYUP"))
                {
                    var token = line.Length > 5 ? line[5..].Trim() : string.Empty;
                    if (!TryExpandVariables(token, variables, out var expandedToken, out var tokenExpandError, specialResolver))
                    {
                        var message = tokenExpandError ?? $"変数の解決に失敗しました: \"{line}\"";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    var resolvedToken = expandedToken.Trim();
                    if (!KeyChordParser.TryResolveKeyToken(resolvedToken, out var key))
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, $"KEYUP のキー名が不正です: \"{resolvedToken}\"")));
                    AppendKey(buffer, key, true);
                    keyTracker.TrackKeyUp(key);
                    continue;
                }

                if (StartsWithCommand(line, "KEY"))
                {
                    var combo = line.Length > 3 ? line[3..].Trim() : string.Empty;
                    if (!TryExpandVariables(combo, variables, out var expandedCombo, out var comboExpandError, specialResolver))
                    {
                        var message = comboExpandError ?? $"変数の解決に失敗しました: \"{line}\"";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    if (!TryAppendCombination(expandedCombo.Trim(), buffer, InputExtraInfo.MacroPassthroughPointer, out var error))
                    {
                        var message = error ?? $"KEY の書式が不正です: \"{expandedCombo}\"";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    continue;
                }

                if (StartsWithCommand(line, "MOUSE", requireDelimiter: false))
                {
                    if (!TryExpandVariables(line, variables, out var expandedMouse, out var mouseExpandError, specialResolver))
                    {
                        var message = mouseExpandError ?? $"変数の解決に失敗しました: \"{line}\"";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    if (!TryHandleMouseCommand(expandedMouse, buffer, out var mouseError))
                    {
                        var message = mouseError ?? $"MOUSE コマンドの書式が不正です: \"{expandedMouse}\"";
                        return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, message)));
                    }
                    continue;
                }

                return CompleteResult(MacroExecutionResult.Fail(FormatLineError(lineNumber, $"未知のマクロ命令です: \"{line}\"")));
            }

            if (ifStack.Count > 0)
            {
                return CompleteResult(MacroExecutionResult.Fail(FormatLineError(expandedLines.Count, "IF ブロックが ENDIF で閉じられていません。")));
            }

            return CompleteResult(MacroExecutionResult.Ok());
        }
        catch (OperationCanceledException)
        {
            return CompleteResult(MacroExecutionResult.Canceled("マクロ実行がキャンセルされました。"));
        }
        finally
        {
            cursorScope.Dispose();
            _currentMouseTracker.Value = null;
        }
    }

    private static bool StartsWithCommand(string line, string command, bool requireDelimiter = true)
    {
        if (line.Length < command.Length) return false;
        if (!line.StartsWith(command, StringComparison.OrdinalIgnoreCase)) return false;
        if (!requireDelimiter) return true;
        if (line.Length == command.Length) return true;
        var next = line[command.Length];
        return char.IsWhiteSpace(next) || !IsAsciiLetter(next);
    }

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
                countToken = TrimInlineComment(countToken);
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
                remainder = TrimInlineComment(remainder);
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

    private static bool TryExpandForeachDropBlocks(IReadOnlyList<string> lines, IReadOnlyList<string> dropEntries, out List<string> expanded, out string? error)
    {
        expanded = new List<string>(lines.Count);
        error = null;
        var stack = new Stack<ForeachDropFrame>();
        for (int i = 0; i < lines.Count; i++)
        {
            var rawLine = lines[i];
            var trimmed = rawLine.Trim();

            if (StartsWithCommand(trimmed, "FOREACH_DROP"))
            {
                var args = trimmed.Length > 12 ? trimmed[12..].Trim() : string.Empty;
                args = TrimInlineComment(args);
                if (!TryParseForeachDropHeader(args, out var variableName, out var indexVariable, out var headerError))
                {
                    error = headerError ?? $"FOREACH_DROP の書式が不正です: \"{trimmed}\"";
                    return false;
                }

                stack.Push(new ForeachDropFrame(variableName, indexVariable));
                continue;
            }

            if (StartsWithCommand(trimmed, "ENDFOREACH"))
            {
                var remainder = trimmed.Length > 10 ? trimmed[10..].Trim() : string.Empty;
                remainder = TrimInlineComment(remainder);
                if (!string.IsNullOrWhiteSpace(remainder))
                {
                    error = $"ENDFOREACH 行に余分な記述があります: \"{trimmed}\"";
                    return false;
                }
                if (stack.Count == 0)
                {
                    error = "ENDFOREACH に対応する FOREACH_DROP が見つかりません。";
                    return false;
                }

                var frame = stack.Pop();
                var target = stack.Count > 0 ? stack.Peek().Lines : expanded;
                if (dropEntries.Count == 0)
                {
                    continue;
                }

                for (int dropIndex = 0; dropIndex < dropEntries.Count; dropIndex++)
                {
                    var oneBasedIndex = dropIndex + 1;
                    target.Add(BuildDropPathAssignment(frame.VariableName, oneBasedIndex));
                    if (!string.IsNullOrEmpty(frame.IndexVariableName))
                    {
                        target.Add($"SET {frame.IndexVariableName} {oneBasedIndex}");
                    }
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
            error = "FOREACH_DROP ブロックが ENDFOREACH で閉じられていません。";
            return false;
        }

        return true;
    }

    private static bool TryParseForeachDropHeader(string args, out string variableName, out string? indexVariableName, out string? error)
    {
        variableName = string.Empty;
        indexVariableName = null;
        error = null;
        if (string.IsNullOrWhiteSpace(args))
        {
            error = "FOREACH_DROP に変数名を指定してください。";
            return false;
        }

        var tokens = args.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            error = "FOREACH_DROP に変数名を指定してください。";
            return false;
        }

        variableName = tokens[0];
        if (!IsValidVariableName(variableName))
        {
            error = $"FOREACH_DROP の変数名が不正です: \"{variableName}\"";
            return false;
        }

        if (tokens.Length == 1)
        {
            return true;
        }

        if (tokens.Length != 3 || !tokens[1].Equals("INDEX", StringComparison.OrdinalIgnoreCase))
        {
            error = "FOREACH_DROP の書式は \"FOREACH_DROP <変数> [INDEX <カウンター変数>]\" です。";
            return false;
        }

        indexVariableName = tokens[2];
        if (!IsValidVariableName(indexVariableName))
        {
            error = $"FOREACH_DROP の INDEX 変数名が不正です: \"{indexVariableName}\"";
            return false;
        }

        return true;
    }

    private static string BuildDropPathAssignment(string variableName, int oneBasedIndex) =>
        $"SET {variableName} {{{{drop_path:{oneBasedIndex}}}}}";

    private static bool IsAsciiLetter(char ch) =>
        (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z');

    private static string TrimInlineComment(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        bool inQuotes = false;
        bool escape = false;
        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (!escape && ch == '"')
            {
                inQuotes = !inQuotes;
            }

            if (!inQuotes && ch == '#' && (i == 0 || char.IsWhiteSpace(text[i - 1])))
            {
                return text[..i].TrimEnd();
            }

            escape = !escape && ch == '\\';
        }

        return text;
    }

    private static bool TryHandleIfDirective(
        string args,
        Dictionary<string, string> variables,
        SpecialVariableResolver? specialResolver,
        Stack<IfBlockState> stack,
        ref int inactiveDepth,
        out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(args))
        {
            error = "IF 条件を指定してください。";
            return false;
        }

        bool parentActive = inactiveDepth == 0;
        bool conditionResult = false;
        if (parentActive)
        {
            if (!TryEvaluateCondition(args, variables, specialResolver, out conditionResult, out error))
            {
                return false;
            }
        }

        var executing = parentActive && conditionResult;
        var frame = new IfBlockState
        {
            ParentActive = parentActive,
            Executing = executing,
            ElseEncountered = false,
            HasMatchedBranch = executing
        };
        if (!executing)
        {
            inactiveDepth++;
        }
        stack.Push(frame);
        return true;
    }

    private static bool TryParseElseIfCondition(string line, out string condition)
    {
        if (StartsWithCommand(line, "ELSEIF"))
        {
            condition = line.Length > 6 ? line[6..].Trim() : string.Empty;
            condition = TrimInlineComment(condition);
            return true;
        }

        if (StartsWithCommand(line, "ELSE"))
        {
            var remainder = line.Length > 4 ? line[4..] : string.Empty;
            var trimmed = remainder.TrimStart();
            if (StartsWithCommand(trimmed, "IF"))
            {
                condition = trimmed.Length > 2 ? trimmed[2..].Trim() : string.Empty;
                condition = TrimInlineComment(condition);
                return true;
            }
        }

        condition = string.Empty;
        return false;
    }

    private static bool TryHandleElseIfDirective(
        string args,
        Dictionary<string, string> variables,
        SpecialVariableResolver? specialResolver,
        Stack<IfBlockState> stack,
        ref int inactiveDepth,
        out string? error)
    {
        error = null;
        if (stack.Count == 0)
        {
            error = "ELSEIF に対応する IF が見つかりません。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(args))
        {
            error = "ELSEIF 条件を指定してください。";
            return false;
        }

        var frame = stack.Pop();
        if (frame.ElseEncountered)
        {
            error = "ELSE の後に ELSEIF は使用できません。";
            return false;
        }

        bool evaluateCondition = frame.ParentActive && !frame.HasMatchedBranch;
        bool conditionResult = false;
        if (evaluateCondition)
        {
            if (!TryEvaluateCondition(args, variables, specialResolver, out conditionResult, out error))
            {
                return false;
            }
        }

        bool newExecuting = evaluateCondition && conditionResult;
        UpdateIfExecutionState(ref frame, newExecuting, ref inactiveDepth);
        if (newExecuting)
        {
            frame.HasMatchedBranch = true;
        }
        stack.Push(frame);
        return true;
    }

    private static bool TryHandleElseDirective(Stack<IfBlockState> stack, ref int inactiveDepth, out string? error)
    {
        error = null;
        if (stack.Count == 0)
        {
            error = "ELSE に対応する IF が見つかりません。";
            return false;
        }

        var frame = stack.Pop();
        if (frame.ElseEncountered)
        {
            error = "1 つの IF に複数の ELSE は使用できません。";
            return false;
        }

        bool newExecuting = frame.ParentActive && !frame.HasMatchedBranch;
        UpdateIfExecutionState(ref frame, newExecuting, ref inactiveDepth);
        if (newExecuting)
        {
            frame.HasMatchedBranch = true;
        }
        frame.ElseEncountered = true;
        stack.Push(frame);
        return true;
    }

    private static bool TryHandleEndIfDirective(Stack<IfBlockState> stack, ref int inactiveDepth, out string? error)
    {
        error = null;
        if (stack.Count == 0)
        {
            error = "ENDIF に対応する IF が見つかりません。";
            return false;
        }

        var frame = stack.Pop();
        if (!frame.Executing && inactiveDepth > 0)
        {
            inactiveDepth--;
        }
        return true;
    }

    private static void UpdateIfExecutionState(ref IfBlockState frame, bool newExecuting, ref int inactiveDepth)
    {
        if (!frame.Executing && inactiveDepth > 0)
        {
            inactiveDepth--;
        }
        frame.Executing = newExecuting;
        if (!frame.Executing)
        {
            inactiveDepth++;
        }
    }

    internal static bool TryEvaluateCondition(string args, Dictionary<string, string> variables, SpecialVariableResolver? specialResolver, out bool result, out string? error)
    {
        result = false;
        error = null;
        if (string.IsNullOrWhiteSpace(args))
        {
            error = "IF 条件を指定してください。";
            return false;
        }

        if (!TryExpandVariables(args, variables, out var expanded, out var expandError, specialResolver))
        {
            error = expandError;
            return false;
        }

        if (!TrySplitConditionTokens(expanded, out var tokens, out var splitError))
        {
            error = splitError;
            return false;
        }

        if (tokens.Count != 3)
        {
            error = "IF 条件は「左辺 演算子 右辺」の形式で指定してください（空白を含む値は \"\" で囲んでください）。";
            return false;
        }

        var left = tokens[0];
        var op = tokens[1].Trim();
        var right = tokens[2];
        var opNormalized = op.ToUpperInvariant();

        bool IsNumeric(string value, out long number) =>
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);

        static bool CompareStrings(string l, string r) => string.Equals(l, r, StringComparison.Ordinal);

        switch (opNormalized)
        {
            case "==":
            case "=":
                if (IsNumeric(left, out var eqLeft) && IsNumeric(right, out var eqRight))
                {
                    result = eqLeft == eqRight;
                    return true;
                }
                result = CompareStrings(left, right);
                return true;
            case "!=":
                if (IsNumeric(left, out var neLeft) && IsNumeric(right, out var neRight))
                {
                    result = neLeft != neRight;
                    return true;
                }
                result = !CompareStrings(left, right);
                return true;
            case ">":
            case "<":
            case ">=":
            case "<=":
                if (!IsNumeric(left, out var numLeft) || !IsNumeric(right, out var numRight))
                {
                    error = $"IF の演算子 \"{op}\" には整数を指定してください。";
                    return false;
                }
                result = opNormalized switch
                {
                    ">" => numLeft > numRight,
                    "<" => numLeft < numRight,
                    ">=" => numLeft >= numRight,
                    "<=" => numLeft <= numRight,
                    _ => false
                };
                return true;
            case "CONTAINS":
            case "CONTAIN":
                result = left.Contains(right, StringComparison.Ordinal);
                return true;
            case "NOTCONTAINS":
                result = !left.Contains(right, StringComparison.Ordinal);
                return true;
            case "STARTSWITH":
            case "SW":
                result = left.StartsWith(right, StringComparison.Ordinal);
                return true;
            case "ENDSWITH":
            case "EW":
                result = left.EndsWith(right, StringComparison.Ordinal);
                return true;
            default:
                error = $"IF でサポートされていない演算子です: \"{op}\"";
                return false;
        }
    }

    private static bool TrySplitConditionTokens(string input, out List<string> tokens, out string? error)
    {
        tokens = new List<string>(capacity: 3);
        error = null;
        int index = 0;

        static void SkipWhitespace(string text, ref int idx)
        {
            while (idx < text.Length && char.IsWhiteSpace(text[idx]))
            {
                idx++;
            }
        }

        SkipWhitespace(input, ref index);
        if (!TryParseConditionToken(input, ref index, out var left, out error))
        {
            return false;
        }
        tokens.Add(left);

        SkipWhitespace(input, ref index);
        int opStart = index;
        while (index < input.Length && !char.IsWhiteSpace(input[index]))
        {
            index++;
        }
        if (index == opStart)
        {
            error = "IF 条件が不完全です。演算子を指定してください。";
            return false;
        }
        tokens.Add(input[opStart..index]);

        SkipWhitespace(input, ref index);
        if (!TryParseConditionToken(input, ref index, out var right, out error))
        {
            return false;
        }
        tokens.Add(right);

        SkipWhitespace(input, ref index);
        if (index < input.Length)
        {
            error = "IF 条件の末尾に余分な文字があります。空白を含む値は \"\" で囲んでください。";
            return false;
        }

        return true;
    }

    private static bool TryParseConditionToken(string input, ref int index, out string token, out string? error)
    {
        token = string.Empty;
        error = null;
        while (index < input.Length && char.IsWhiteSpace(input[index]))
        {
            index++;
        }

        if (index >= input.Length)
        {
            error = "IF 条件が不完全です。";
            return false;
        }

        if (input[index] != '"')
        {
            int start = index;
            while (index < input.Length && !char.IsWhiteSpace(input[index]))
            {
                index++;
            }
            token = input[start..index];
            return true;
        }

        if (!TryReadQuotedContent(input, ref index, "IF", "条件", out var literal, out error))
        {
            return false;
        }

        token = literal;
        return true;
    }

    private const int ClipboardVariableLimit = 20;

    private static readonly string[] ValidationClipboardEntries =
    {
        "__CLIPBOARD_LINE1__",
        "__CLIPBOARD_LINE2__"
    };

    private static readonly string[] ValidationDropEntries =
    {
        @"C:\Drop\Validation1.txt",
        @"C:\Drop\Validation2.txt",
        @"C:\Drop\Validation3.txt",
        @"C:\Drop\Validation4.txt",
        @"C:\Drop\Validation5.txt"
    };

    private const string ValidationClipboardValue = "__CLIPBOARD__";

    private static Func<ClipboardSnapshot> CreateValidationClipboardSnapshotProvider() =>
        () => new ClipboardSnapshot(ValidationClipboardValue, ValidationClipboardEntries, ValidationClipboardEntries);

    private static SpecialVariableResolver CreateSpecialVariableResolver(Func<ClipboardSnapshot> snapshotProvider, IReadOnlyList<string>? droppedPaths = null, bool validationMode = false)
    {
        var dropEntries = validationMode
            ? ValidationDropEntries
            : droppedPaths ?? Array.Empty<string>();

        return (string token, out string value, out string? error) =>
        {
            var snapshot = snapshotProvider();
            var rawText = validationMode ? ValidationClipboardValue : snapshot.RawText?.Trim() ?? string.Empty;
            var latestEntries = validationMode
                ? ValidationClipboardEntries
                : snapshot.LatestEntries ?? Array.Empty<string>();
            var historyEntries = validationMode
                ? ValidationClipboardEntries
                : snapshot.Entries ?? Array.Empty<string>();
            value = string.Empty;
            error = null;

            if (string.Equals(token, "clipboard", StringComparison.OrdinalIgnoreCase))
            {
                value = rawText;
                return true;
            }

            if (string.Equals(token, "clipboard_args", StringComparison.OrdinalIgnoreCase))
            {
                value = JoinClipboardEntries(latestEntries);
                return true;
            }

            if (token.StartsWith("clipboard:", StringComparison.OrdinalIgnoreCase))
            {
                var suffix = token["clipboard:".Length..];
                return TryResolveClipboardHistory(suffix, historyEntries, out value, out error);
            }

            if (token.StartsWith("clipboard_args:", StringComparison.OrdinalIgnoreCase))
            {
                var suffix = token["clipboard_args:".Length..];
                return TryResolveClipboardHistory(suffix, historyEntries, out value, out error);
            }

            if (string.Equals(token, "drop_args", StringComparison.OrdinalIgnoreCase))
            {
                value = BuildDropArgs(dropEntries);
                return true;
            }

            if (string.Equals(token, "drop_count", StringComparison.OrdinalIgnoreCase))
            {
                value = dropEntries.Count.ToString(CultureInfo.InvariantCulture);
                return true;
            }

            if (token.StartsWith("drop_path", StringComparison.OrdinalIgnoreCase))
            {
                if (dropEntries.Count == 0)
                {
                    value = string.Empty;
                    return true;
                }

                if (token.Length == "drop_path".Length)
                {
                    value = dropEntries[0];
                    return true;
                }

                if (!token.StartsWith("drop_path:", StringComparison.OrdinalIgnoreCase))
                {
                    error = $"drop_path の指定が不正です: \"{token}\"";
                    value = string.Empty;
                    return true;
                }

                var indexToken = token["drop_path:".Length..];
                if (!int.TryParse(indexToken, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                {
                    error = $"drop_path のインデックスが不正です: \"{indexToken}\"";
                    value = string.Empty;
                    return true;
                }

                if (index <= 0)
                {
                    error = "drop_path のインデックスは 1 以上を指定してください。";
                    value = string.Empty;
                    return true;
                }

                var zeroBased = index - 1;
                if (zeroBased < 0 || zeroBased >= dropEntries.Count)
                {
                    value = string.Empty;
                    return true;
                }

                value = dropEntries[zeroBased];
                return true;
            }

            return false;
        };
    }

    internal static SpecialVariableResolver CreateSpecialVariableResolverForTesting(
        ClipboardSnapshot snapshot,
        IReadOnlyList<string>? droppedPaths = null,
        bool validationMode = false) =>
        CreateSpecialVariableResolver(() => snapshot, droppedPaths, validationMode);

    private static bool TryResolveClipboardHistory(string suffix, IReadOnlyList<string> entries, out string value, out string? error)
    {
        value = string.Empty;
        error = null;

        if (!int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var limit))
        {
            error = $"clipboard の個数指定が不正です: \"{suffix}\"";
            return true;
        }

        if (limit <= 0 || entries.Count == 0)
        {
            return true;
        }

        limit = Math.Min(limit, Math.Min(entries.Count, ClipboardVariableLimit));
        if (limit <= 0)
        {
            return true;
        }

        var start = entries.Count - limit;
        var selection = entries.Skip(start).Take(limit);
        value = string.Join(Environment.NewLine, selection);
        return true;
    }

    private static string JoinClipboardEntries(IReadOnlyList<string> entries)
    {
        if (entries.Count == 0)
        {
            return string.Empty;
        }
        return string.Join(Environment.NewLine, entries);
    }

    private static string BuildDropArgs(IReadOnlyList<string> entries)
    {
        if (entries.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(" ", entries.Select(QuoteArgumentPath));
    }

    private static string QuoteArgumentPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "\"\"";
        }

        bool alreadyQuoted = path.Length >= 2 &&
                             path.StartsWith("\"", StringComparison.Ordinal) &&
                             path.EndsWith("\"", StringComparison.Ordinal);
        bool needsQuoting = path.Any(char.IsWhiteSpace);
        if (needsQuoting && !alreadyQuoted)
        {
            return $"\"{path}\"";
        }

        return path;
    }

    private static bool PathExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return File.Exists(path) || Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    internal delegate bool SpecialVariableResolver(string token, out string value, out string? error);

    private struct IfBlockState
    {
        public bool ParentActive;
        public bool Executing;
        public bool ElseEncountered;
        public bool HasMatchedBranch;
    }

    internal static bool TryExpandVariables(string input, IReadOnlyDictionary<string, string> variables, out string result, out string? error, SpecialVariableResolver? specialResolver = null)
    {
        result = input;
        error = null;
        if (string.IsNullOrEmpty(input))
        {
            return true;
        }

        if (input.IndexOf("{{", StringComparison.Ordinal) < 0)
        {
            return true;
        }

        var sb = new StringBuilder(input.Length);
        for (int i = 0; i < input.Length;)
        {
            if (input[i] == '{' && i + 1 < input.Length && input[i + 1] == '{')
            {
                int end = input.IndexOf("}}", i + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    error = $"変数プレースホルダーの閉じ括弧が見つかりません: \"{input}\"";
                    return false;
                }

                var token = input.Substring(i + 2, end - i - 2);
                var name = token.Trim();
                if (IsValidVariableName(name))
                {
                    if (variables.TryGetValue(name, out var value))
                    {
                        sb.Append(value);
                    }
                    else if (specialResolver != null && specialResolver(name, out var specialValue, out var specialError))
                    {
                        if (specialError != null)
                        {
                            error = specialError;
                            return false;
                        }
                        sb.Append(specialValue);
                    }
                    else
                    {
                        error = $"変数 \"{name}\" は定義されていません。";
                        return false;
                    }
                }
                else
                {
                    if (specialResolver == null || !specialResolver(name, out var specialValue, out var specialError))
                    {
                        error = $"変数名が不正です: \"{token}\"";
                        return false;
                    }
                    if (specialError != null)
                    {
                        error = specialError;
                        return false;
                    }
                    sb.Append(specialValue);
                }
                i = end + 2;
                continue;
            }

            if (input[i] == '}' && i + 1 < input.Length && input[i + 1] == '}')
            {
                error = $"閉じ括弧が余分です: \"{input}\"";
                return false;
            }

            sb.Append(input[i]);
            i++;
        }

        result = sb.ToString();
        return true;
    }

    private static bool TryApplyRenameDirective(string payload, Dictionary<string, string> variables, SpecialVariableResolver? specialResolver, bool validateOnly, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            error = "RENAME には元パスと新しいパスを指定してください。";
            return false;
        }

        int index = 0;
        if (!TryParsePathOperand(payload, ref index, "RENAME", "元パス", out var sourceRaw, out error))
        {
            return false;
        }
        if (!TryParsePathOperand(payload, ref index, "RENAME", "新しいパス", out var targetRaw, out error))
        {
            return false;
        }

        if (index < payload.Length && !string.IsNullOrWhiteSpace(payload[index..]))
        {
            error = "RENAME の引数の後ろに余分な記述があります。";
            return false;
        }

        if (!TryExpandVariables(sourceRaw, variables, out var expandedSource, out var sourceExpandError, specialResolver))
        {
            error = sourceExpandError ?? "RENAME の元パス展開に失敗しました。";
            return false;
        }
        if (!TryExpandVariables(targetRaw, variables, out var expandedTarget, out var targetExpandError, specialResolver))
        {
            error = targetExpandError ?? "RENAME の新しいパス展開に失敗しました。";
            return false;
        }

        var sourcePath = TrimPathQuotes(expandedSource);
        var targetPath = TrimPathQuotes(expandedTarget);
        if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(targetPath))
        {
            error = "RENAME のパスは空にできません。";
            return false;
        }

        if (validateOnly)
        {
            return true;
        }

        try
        {
            if (File.Exists(sourcePath))
            {
                File.Move(sourcePath, targetPath);
                return true;
            }

            if (Directory.Exists(sourcePath))
            {
                Directory.Move(sourcePath, targetPath);
                return true;
            }

            error = $"RENAME 元パスが存在しません: \"{sourcePath}\"";
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"RENAME の実行に失敗しました: {ex.Message}";
            return false;
        }
    }

    private static bool TryApplyResolveLinkDirective(string payload, Dictionary<string, string> variables, SpecialVariableResolver? specialResolver, bool validateOnly, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            error = "RESOLVE_LINK には変数名とパスを指定してください。";
            return false;
        }

        int index = 0;
        while (index < payload.Length && char.IsWhiteSpace(payload[index]))
        {
            index++;
        }
        var firstSpace = FindFirstWhitespace(payload[index..]);
        if (firstSpace < 0)
        {
            error = "RESOLVE_LINK には変数名とパスを指定してください。";
            return false;
        }

        var nameToken = payload.Substring(index, firstSpace).Trim();
        if (!IsValidVariableName(nameToken))
        {
            error = $"RESOLVE_LINK の変数名が不正です: \"{nameToken}\"";
            return false;
        }

        index += firstSpace;
        while (index < payload.Length && char.IsWhiteSpace(payload[index]))
        {
            index++;
        }
        if (index >= payload.Length)
        {
            error = "RESOLVE_LINK にパスを指定してください。";
            return false;
        }

        if (!TryParsePathOperand(payload, ref index, "RESOLVE_LINK", "パス", out var pathRaw, out error))
        {
            return false;
        }

        if (index < payload.Length && !string.IsNullOrWhiteSpace(payload[index..]))
        {
            error = "RESOLVE_LINK の引数の後ろに余分な記述があります。";
            return false;
        }

        if (!TryExpandVariables(pathRaw, variables, out var expandedPath, out var pathExpandError, specialResolver))
        {
            error = pathExpandError ?? "RESOLVE_LINK のパス展開に失敗しました。";
            return false;
        }

        var normalizedPath = TrimPathQuotes(expandedPath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            error = "RESOLVE_LINK のパスは空にできません。";
            return false;
        }

        if (validateOnly)
        {
            variables[nameToken] = normalizedPath;
            return true;
        }

        if (!TryResolveLinkTarget(normalizedPath, out var resolved, out var resolveError))
        {
            error = resolveError ?? $"RESOLVE_LINK の解決に失敗しました: \"{normalizedPath}\"";
            return false;
        }

        variables[nameToken] = resolved;
        return true;
    }

    private static bool TryApplyReadFileDirective(string payload, Dictionary<string, string> variables, SpecialVariableResolver? specialResolver, bool validateOnly, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            error = "READFILE には変数名とパスを指定してください。";
            return false;
        }

        int index = 0;
        while (index < payload.Length && char.IsWhiteSpace(payload[index]))
        {
            index++;
        }

        var firstSpace = FindFirstWhitespace(payload[index..]);
        if (firstSpace < 0)
        {
            error = "READFILE には変数名とパスを指定してください。";
            return false;
        }

        var nameToken = payload.Substring(index, firstSpace).Trim();
        if (!IsValidVariableName(nameToken))
        {
            error = $"READFILE の変数名が不正です: \"{nameToken}\"";
            return false;
        }

        index += firstSpace;
        while (index < payload.Length && char.IsWhiteSpace(payload[index]))
        {
            index++;
        }
        if (index >= payload.Length)
        {
            error = "READFILE にパスを指定してください。";
            return false;
        }

        if (!TryParsePathOperand(payload, ref index, "READFILE", "パス", out var pathRaw, out error))
        {
            return false;
        }

        int maxBytes = DefaultReadFileLimitBytes;
        bool maxExplicit = false;

        while (index < payload.Length && char.IsWhiteSpace(payload[index]))
        {
            index++;
        }

        if (index < payload.Length)
        {
            var remaining = TrimInlineComment(payload[index..]).Trim();
            if (remaining.Length > 0)
            {
                if (!TryParseReadFileMax(remaining, out maxBytes, out error))
                {
                    return false;
                }
                maxExplicit = true;
            }
        }

        if (!TryExpandVariables(pathRaw, variables, out var expandedPath, out var pathExpandError, specialResolver))
        {
            error = pathExpandError ?? "READFILE のパス展開に失敗しました。";
            return false;
        }

        var normalizedPath = TrimPathQuotes(expandedPath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            error = "READFILE のパスは空にできません。";
            return false;
        }

        if (validateOnly)
        {
            variables[nameToken] = string.Empty;
            return true;
        }

        if (!File.Exists(normalizedPath))
        {
            error = $"READFILE のパスが存在しません: \"{normalizedPath}\"";
            return false;
        }

        var info = new FileInfo(normalizedPath);
        if (!maxExplicit && info.Length > DefaultReadFileLimitBytes)
        {
            error = $"READFILE は既定で {DefaultReadFileLimitBytes} バイトまでです。MAX <bytes> を指定してください（最大 {MaxReadFileLimitBytes} バイト）。";
            return false;
        }

        maxBytes = Math.Clamp(maxBytes, 1, MaxReadFileLimitBytes);
        int bytesToRead = (int)Math.Min(info.Length, maxBytes);
        try
        {
            using var stream = new FileStream(normalizedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length < bytesToRead)
            {
                bytesToRead = (int)stream.Length;
            }

            var buffer = ArrayPool<byte>.Shared.Rent(bytesToRead);
            try
            {
                int read = stream.Read(buffer, 0, bytesToRead);
                var text = Encoding.UTF8.GetString(buffer, 0, read);
                variables[nameToken] = text;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException or DecoderFallbackException)
        {
            error = $"READFILE の実行に失敗しました: {ex.Message}";
            return false;
        }

        return true;
    }

    private static bool TryParseReadFileMax(string remaining, out int maxBytes, out string? error)
    {
        maxBytes = DefaultReadFileLimitBytes;
        error = null;
        if (!remaining.StartsWith("MAX", StringComparison.OrdinalIgnoreCase))
        {
            error = "READFILE の引数の後ろに余分な記述があります。";
            return false;
        }

        var rest = remaining[3..].TrimStart();
        if (rest.Length == 0)
        {
            error = "READFILE の MAX にはバイト数を指定してください。";
            return false;
        }

        if (rest.Length > 0 && (rest[0] == '=' || rest[0] == ':'))
        {
            rest = rest[1..].TrimStart();
        }

        int firstSpace = FindFirstWhitespace(rest);
        string numberToken = firstSpace >= 0 ? rest[..firstSpace] : rest;
        if (!int.TryParse(numberToken, NumberStyles.None, CultureInfo.InvariantCulture, out maxBytes) || maxBytes <= 0)
        {
            error = "READFILE の MAX は正の整数で指定してください。";
            return false;
        }
        if (maxBytes > MaxReadFileLimitBytes)
        {
            error = $"READFILE の MAX は {MaxReadFileLimitBytes} バイト以下で指定してください。";
            return false;
        }

        if (firstSpace >= 0 && !string.IsNullOrWhiteSpace(rest[firstSpace..]))
        {
            error = "READFILE の引数の後ろに余分な記述があります。";
            return false;
        }

        return true;
    }

    private static bool TryParsePathOperand(string input, ref int index, string command, string operandName, out string operand, out string? error)
    {
        operand = string.Empty;
        error = null;
        while (index < input.Length && char.IsWhiteSpace(input[index]))
        {
            index++;
        }

        if (index >= input.Length)
        {
            error = $"{command} には {operandName} を指定してください。";
            return false;
        }

        if (input[index] == '"')
        {
            if (!TryParseQuotedArgument(input, ref index, command, operandName, out operand, out error))
            {
                return false;
            }
            return true;
        }

        int start = index;
        while (index < input.Length && !char.IsWhiteSpace(input[index]))
        {
            index++;
        }
        operand = input[start..index];
        return true;
    }

    private static string TrimPathQuotes(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            trimmed.StartsWith("\"", StringComparison.Ordinal) &&
            trimmed.EndsWith("\"", StringComparison.Ordinal))
        {
            trimmed = trimmed.Substring(1, trimmed.Length - 2);
        }
        return trimmed;
    }

    private static bool TryResolveLinkTarget(string path, out string resolvedPath, out string? error)
    {
        resolvedPath = path;
        error = null;

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            error = $"RESOLVE_LINK のパスが存在しません: \"{path}\"";
            return false;
        }

        try
        {
            if (string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryResolveShellShortcut(path, out var shortcutTarget, out var shortcutError))
                {
                    error = shortcutError ?? "ショートカットの解決に失敗しました。";
                    return false;
                }

                resolvedPath = shortcutTarget;
                return true;
            }

            var info = File.Exists(path)
                ? (FileSystemInfo)new FileInfo(path)
                : new DirectoryInfo(path);
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            if (target != null)
            {
                resolvedPath = target.FullName;
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = $"RESOLVE_LINK の解決に失敗しました: {ex.Message}";
            return false;
        }
    }

    private static bool TryResolveShellShortcut(string path, out string targetPath, out string? error)
    {
        targetPath = path;
        error = null;

        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                error = "WScript.Shell の取得に失敗しました。";
                return false;
            }

            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell == null)
            {
                error = "ショートカットの解決に必要なオブジェクトの作成に失敗しました。";
                return false;
            }

            dynamic? shortcut = shell.CreateShortcut(path);
            string? resolved = shortcut?.TargetPath as string;
            if (string.IsNullOrWhiteSpace(resolved))
            {
                error = "ショートカットのリンク先が空です。";
                return false;
            }

            targetPath = Path.GetFullPath(resolved);
            return true;
        }
        catch (Exception ex)
        {
            error = $"ショートカットの解決に失敗しました: {ex.Message}";
            return false;
        }
    }

    internal static bool TryApplySetDirective(string line, Dictionary<string, string> variables, out string? name, out string? value, out string? error, SpecialVariableResolver? specialResolver = null)
    {
        name = null;
        value = null;
        error = null;
        var content = line.Length > 3 ? line[3..].Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            error = "SET に変数名を指定してください。";
            return false;
        }

        int separatorIndex = -1;
        for (int i = 0; i < content.Length; i++)
        {
            if (char.IsWhiteSpace(content[i]))
            {
                separatorIndex = i;
                break;
            }
        }

        var nameCandidate = separatorIndex < 0 ? content : content[..separatorIndex];
        if (!IsValidVariableName(nameCandidate))
        {
            error = $"変数名が不正です: \"{nameCandidate}\"";
            return false;
        }

        var rawValue = separatorIndex < 0 ? string.Empty : content[(separatorIndex + 1)..];
        var trimmedValue = rawValue.Length == 0 ? string.Empty : rawValue.TrimStart();
        if (!TryExpandVariables(trimmedValue, variables, out var expandedValue, out var expandError, specialResolver))
        {
            error = expandError;
            return false;
        }

        if (!string.IsNullOrWhiteSpace(expandedValue))
        {
            if (TryParseInt64OrWindowToken(expandedValue, out var numericValue, out var numericError))
            {
                expandedValue = numericValue.ToString(CultureInfo.InvariantCulture);
            }
            else if (numericError != null)
            {
                error = numericError;
                return false;
            }
        }

        variables[nameCandidate] = expandedValue;
        name = nameCandidate;
        value = expandedValue;
        return true;
    }

    internal static bool TryApplyUnsetDirective(string line, Dictionary<string, string> variables, out string? name, out bool removed, out string? error)
    {
        name = null;
        error = null;
        removed = false;
        var content = line.Length > 5 ? line[5..].Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            error = "UNSET に変数名を指定してください。";
            return false;
        }

        if (!IsValidVariableName(content))
        {
            error = $"変数名が不正です: \"{content}\"";
            return false;
        }

        removed = variables.Remove(content);
        name = content;
        return true;
    }

    internal static bool IsValidVariableName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        name = name.Trim();
        if (!char.IsLetter(name[0]) && name[0] != '_')
        {
            return false;
        }

        for (int i = 1; i < name.Length; i++)
        {
            var ch = name[i];
            if (!char.IsLetterOrDigit(ch) && ch != '_')
            {
                return false;
            }
        }

        return true;
    }

    internal static bool TryApplyMathDirective(string line, Dictionary<string, string> variables, out string? name, out long beforeValue, out long operandValue, out long resultValue, out string? error, SpecialVariableResolver? specialResolver = null)
    {
        name = null;
        beforeValue = 0;
        operandValue = 0;
        resultValue = 0;
        error = null;

        var command = ExtractCommandName(line);
        if (command is not ("ADD" or "SUB" or "MUL" or "DIV"))
        {
            error = "未知の演算コマンドです。";
            return false;
        }

        var content = line.Length > command.Length ? line[command.Length..].Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            error = $"{command} には変数名と値を指定してください。";
            return false;
        }

        var firstSpace = FindFirstWhitespace(content);
        var nameToken = firstSpace < 0 ? content : content[..firstSpace];
        if (!IsValidVariableName(nameToken))
        {
            error = $"変数名が不正です: \"{nameToken}\"";
            return false;
        }

        if (!variables.TryGetValue(nameToken, out var currentRaw))
        {
            error = $"変数 \"{nameToken}\" は定義されていません。";
            return false;
        }

        var operandRaw = firstSpace < 0 ? string.Empty : content[(firstSpace + 1)..].Trim();
        if (!TryExpandVariables(operandRaw, variables, out var expandedOperand, out var expandError, specialResolver))
        {
            error = expandError;
            return false;
        }

        if (!TryParseInt64OrWindowToken(expandedOperand, out operandValue, out var operandError))
        {
            error = operandError ?? $"{command} の値は整数で指定してください: \"{expandedOperand}\"";
            return false;
        }

        if (!TryParseInt64OrWindowToken(currentRaw, out beforeValue, out var currentValueError))
        {
            error = currentValueError ?? $"変数 \"{nameToken}\" の値を整数として解釈できません: \"{currentRaw}\"";
            return false;
        }

        try
        {
            checked
            {
                resultValue = command switch
                {
                    "ADD" => beforeValue + operandValue,
                    "SUB" => beforeValue - operandValue,
                    "MUL" => beforeValue * operandValue,
                    "DIV" => operandValue switch
                    {
                        0 => throw new DivideByZeroException(),
                        _ => beforeValue / operandValue
                    },
                    _ => beforeValue
                };
            }
        }
        catch (DivideByZeroException)
        {
            error = "DIV の値に 0 は指定できません。";
            return false;
        }
        catch (OverflowException)
        {
            error = "演算結果が整数の範囲を超えました。";
            return false;
        }

        variables[nameToken] = resultValue.ToString(CultureInfo.InvariantCulture);
        name = nameToken;
        return true;
    }

    internal static bool TryApplyConcatDirective(string line, Dictionary<string, string> variables, bool prepend, out string? name, out string? newValue, out string? error, SpecialVariableResolver? specialResolver = null)
    {
        name = null;
        newValue = null;
        error = null;

        var command = ExtractCommandName(line);
        if (command is not ("APPEND" or "PREPEND"))
        {
            error = "未知の結合コマンドです。";
            return false;
        }

        var content = line.Length > command.Length ? line[command.Length..].Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            error = $"{command} には変数名と値を指定してください。";
            return false;
        }

        var firstSpace = FindFirstWhitespace(content);
        var nameToken = firstSpace < 0 ? content : content[..firstSpace];
        if (!IsValidVariableName(nameToken))
        {
            error = $"変数名が不正です: \"{nameToken}\"";
            return false;
        }

        var operandRaw = firstSpace < 0 ? string.Empty : content[(firstSpace + 1)..].TrimStart();
        if (!TryExpandVariables(operandRaw, variables, out var expandedOperand, out var expandError, specialResolver))
        {
            error = expandError;
            return false;
        }

        var current = variables.TryGetValue(nameToken, out var currentValue) ? currentValue ?? string.Empty : string.Empty;
        newValue = prepend ? (expandedOperand + current) : (current + expandedOperand);
        variables[nameToken] = newValue;
        name = nameToken;
        return true;
    }

    internal static bool TryApplyReplaceDirective(
        string line,
        Dictionary<string, string> variables,
        out string? name,
        out string? newValue,
        out string? error,
        SpecialVariableResolver? specialResolver,
        out int replacements)
    {
        name = null;
        newValue = null;
        error = null;
        replacements = 0;

        var command = ExtractCommandName(line);
        if (!string.Equals(command, "REPLACE", StringComparison.OrdinalIgnoreCase))
        {
            error = "未知の REPLACE コマンドです。";
            return false;
        }

        var content = line.Length > command.Length ? line[command.Length..].Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            error = "REPLACE には変数名と検索/置換文字列を指定してください。";
            return false;
        }

        var firstSpace = FindFirstWhitespace(content);
        if (firstSpace < 0)
        {
            error = "REPLACE には変数名に続いて検索文字列と置換文字列を指定してください。";
            return false;
        }

        var nameToken = content[..firstSpace];
        if (!IsValidVariableName(nameToken))
        {
            error = $"変数名が不正です: \"{nameToken}\"";
            return false;
        }

        if (!variables.TryGetValue(nameToken, out var currentValue))
        {
            error = $"変数 \"{nameToken}\" は定義されていません。";
            return false;
        }
        currentValue ??= string.Empty;

        var operandText = content[(firstSpace + 1)..];
        int argIndex = 0;
        if (!TryParseQuotedArgument(operandText, ref argIndex, out var searchLiteral, out var quotedError))
        {
            error = quotedError ?? "REPLACE の検索文字列を \"\" で囲んでください。";
            return false;
        }

        if (!TryParseQuotedArgument(operandText, ref argIndex, out var replaceLiteral, out quotedError))
        {
            error = quotedError ?? "REPLACE の置換文字列を \"\" で囲んでください。";
            return false;
        }

        // Ensure残余 token only whitespace.
        while (argIndex < operandText.Length)
        {
            if (!char.IsWhiteSpace(operandText[argIndex]))
            {
                error = "REPLACE の引数の後ろに余分な文字があります。";
                return false;
            }
            argIndex++;
        }

        if (!TryExpandVariables(searchLiteral, variables, out var expandedSearch, out var expandError, specialResolver))
        {
            error = expandError;
            return false;
        }
        if (string.IsNullOrEmpty(expandedSearch))
        {
            error = "REPLACE の検索文字列を空にすることはできません。";
            return false;
        }

        if (!TryExpandVariables(replaceLiteral, variables, out var expandedReplace, out expandError, specialResolver))
        {
            error = expandError;
            return false;
        }
        expandedReplace ??= string.Empty;

        var builder = new StringBuilder();
        int cursor = 0;
        while (true)
        {
            int hit = currentValue.IndexOf(expandedSearch, cursor, StringComparison.Ordinal);
            if (hit < 0)
            {
                break;
            }
            replacements++;
            builder.Append(currentValue, cursor, hit - cursor);
            builder.Append(expandedReplace);
            cursor = hit + expandedSearch.Length;
        }

        if (replacements == 0)
        {
            newValue = currentValue;
        }
        else
        {
            builder.Append(currentValue, cursor, currentValue.Length - cursor);
            newValue = builder.ToString();
            variables[nameToken] = newValue;
        }

        name = nameToken;
        return true;
    }

    internal static bool TryApplyRegexReplaceDirective(
        string line,
        Dictionary<string, string> variables,
        out string? name,
        out string? newValue,
        out string? error,
        SpecialVariableResolver? specialResolver,
        out int replacements)
    {
        name = null;
        newValue = null;
        error = null;
        replacements = 0;

        var extractedCommand = ExtractCommandName(line);
        var command = extractedCommand.Replace('-', '_');
        if (!string.Equals(command, "REPLACE_REGEX", StringComparison.OrdinalIgnoreCase))
        {
            error = "未知の REPLACE_REGEX コマンドです。";
            return false;
        }

        var content = line.Length > extractedCommand.Length ? line[extractedCommand.Length..].Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(content))
        {
            error = "REPLACE_REGEX には変数名と検索/置換文字列を指定してください。";
            return false;
        }

        var firstSpace = FindFirstWhitespace(content);
        if (firstSpace < 0)
        {
            error = "REPLACE_REGEX には変数名に続いて検索文字列と置換文字列を指定してください。";
            return false;
        }

        var nameToken = content[..firstSpace];
        if (!IsValidVariableName(nameToken))
        {
            error = $"変数名が不正です: \"{nameToken}\"";
            return false;
        }

        if (!variables.TryGetValue(nameToken, out var currentValue))
        {
            error = $"変数 \"{nameToken}\" は定義されていません。";
            return false;
        }
        currentValue ??= string.Empty;

        var operandText = content[(firstSpace + 1)..];
        int argIndex = 0;
        if (!TryParseQuotedArgument(operandText, ref argIndex, out var patternLiteral, out var quotedError))
        {
            error = quotedError ?? "REPLACE_REGEX の検索パターンを \"\" で囲んでください。";
            return false;
        }

        if (!TryParseQuotedArgument(operandText, ref argIndex, out var replaceLiteral, out quotedError))
        {
            error = quotedError ?? "REPLACE_REGEX の置換文字列を \"\" で囲んでください。";
            return false;
        }

        if (!TryExpandVariables(patternLiteral, variables, out var expandedPattern, out var expandError, specialResolver))
        {
            error = expandError;
            return false;
        }
        if (string.IsNullOrEmpty(expandedPattern))
        {
            error = "REPLACE_REGEX の検索パターンを空にすることはできません。";
            return false;
        }

        if (!TryExpandVariables(replaceLiteral, variables, out var expandedReplace, out expandError, specialResolver))
        {
            error = expandError;
            return false;
        }
        expandedReplace ??= string.Empty;

        var remainder = argIndex < operandText.Length ? operandText[argIndex..] : string.Empty;
        if (!TryParseRegexOptions(remainder, out var regexOptions, out var optionError))
        {
            error = optionError;
            return false;
        }

        Regex regex;
        try
        {
            regex = new Regex(expandedPattern, RegexOptions.CultureInvariant | regexOptions);
        }
        catch (ArgumentException ex)
        {
            error = $"REPLACE_REGEX の検索パターンが不正です: {ex.Message}";
            return false;
        }

        string result;
        int replacedCount = 0;
        try
        {
            result = regex.Replace(currentValue, match =>
            {
                replacedCount++;
                return match.Result(expandedReplace);
            });
        }
        catch (ArgumentException ex)
        {
            error = $"REPLACE_REGEX の置換文字列が不正です: {ex.Message}";
            return false;
        }

        replacements = replacedCount;

        if (replacements == 0)
        {
            newValue = currentValue;
        }
        else
        {
            newValue = result;
            variables[nameToken] = newValue;
        }

        name = nameToken;
        return true;
    }

    private static bool TryParseRegexOptions(string input, out RegexOptions options, out string? error)
    {
        options = RegexOptions.None;
        error = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return true;
        }

        var tokens = input.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var token in tokens)
        {
            switch (token.ToUpperInvariant())
            {
                case "IGNORECASE":
                case "ICASE":
                case "I":
                    options |= RegexOptions.IgnoreCase;
                    break;
                case "MULTILINE":
                case "M":
                    options |= RegexOptions.Multiline;
                    break;
                case "SINGLELINE":
                case "DOTALL":
                case "S":
                    options |= RegexOptions.Singleline;
                    break;
                case "IGNOREWHITESPACE":
                case "X":
                    options |= RegexOptions.IgnorePatternWhitespace;
                    break;
                default:
                    error = $"REPLACE_REGEX でサポートされていないオプションです: \"{token}\"";
                    return false;
            }
        }

        return true;
    }

    private static int FindFirstWhitespace(string input)
    {
        for (int i = 0; i < input.Length; i++)
        {
            if (char.IsWhiteSpace(input[i]))
            {
                return i;
            }
        }
        return -1;
    }

    private static bool TryParseQuotedArgument(string input, ref int index, out string value, out string? error) =>
        TryParseQuotedArgument(input, ref index, "REPLACE", "引数", out value, out error);

    private static bool TryParseQuotedArgument(string input, ref int index, string commandName, string argumentName, out string value, out string? error)
    {
        value = string.Empty;
        error = null;
        if (input == null)
        {
            error = "引数が不足しています。";
            return false;
        }

        while (index < input.Length && char.IsWhiteSpace(input[index]))
        {
            index++;
        }

        if (index >= input.Length || input[index] != '"')
        {
            error = $"{commandName} の {argumentName} は \"\" で囲んでください。";
            return false;
        }

        if (!TryReadQuotedContent(input, ref index, commandName, argumentName, out var literal, out error))
        {
            value = string.Empty;
            return false;
        }

        value = literal;
        return true;
    }

    private static string ExtractCommandName(string line)
    {
        int idx = FindFirstWhitespace(line);
        return (idx < 0 ? line : line[..idx]).Trim().ToUpperInvariant();
    }

    private static string TruncateForLog(string? value)
    {
        if (value == null) return string.Empty;
        const int MaxLength = 48;
        if (value.Length <= MaxLength) return value;
        return value[..MaxLength] + "...";
    }

    private static bool TryReadQuotedContent(string input, ref int index, string commandName, string argumentName, out string value, out string? error)
    {
        index++; // skip opening quote
        var sb = new StringBuilder();
        error = null;
        bool closed = false;

        while (index < input.Length)
        {
            char ch = input[index++];
            if (ch == '\\')
            {
                int slashCount = 1;
                while (index < input.Length && input[index] == '\\')
                {
                    slashCount++;
                    index++;
                }

                if (index >= input.Length)
                {
                    sb.Append('\\', slashCount);
                    break;
                }

                var nextChar = input[index];
                if (nextChar == '"')
                {
                    sb.Append('\\', slashCount / 2);
                    if (slashCount % 2 == 0)
                    {
                        index++;
                        closed = true;
                        break;
                    }

                    if (IsQuoteTerminator(input, index + 1))
                    {
                        sb.Append('\\');
                        index++;
                        closed = true;
                        break;
                    }

                    index++;
                    sb.Append('"');
                    continue;
                }

                var literalPairs = slashCount / 2;
                if (literalPairs > 0)
                {
                    sb.Append('\\', literalPairs);
                }
                if (slashCount % 2 == 1)
                {
                    if (nextChar == 'n' || nextChar == 'r' || nextChar == 't' || nextChar == '"' || nextChar == '\\')
                    {
                        index++;
                        sb.Append(nextChar switch
                        {
                            'n' => '\n',
                            'r' => '\r',
                            't' => '\t',
                            '"' => '"',
                            '\\' => '\\',
                            _ => nextChar
                        });
                        continue;
                    }

                    sb.Append('\\');
                    continue;
                }

                continue;
            }

            if (ch == '"')
            {
                closed = true;
                break;
            }

            sb.Append(ch);
        }

        if (!closed)
        {
            error = $"{commandName} の {argumentName} が閉じられていません。";
            value = string.Empty;
            return false;
        }

        value = sb.ToString();
        return true;
    }

    private static bool IsQuoteTerminator(string input, int startIndex)
    {
        for (int i = startIndex; i < input.Length; i++)
        {
            char c = input[i];
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            return c == '#';
        }

        return true;
    }

    private bool TrySetClipboardText(string text, out string? error)
    {
        error = null;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
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
                    WpfClipboard.SetText(clipboardText);
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
                _logger.Error($"WpfClipboard.SetText failed: {operationError}");
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

    private bool TrySendPrefixPassthroughDirect(out string? error)
    {
        error = null;
        var buffer = new List<INPUT>(8);
        if (!TryAppendPrefixSequence(buffer, passthrough: true, allowMissingResolver: false, out error))
        {
            return false;
        }
        return TryFlushInputs(buffer, out error);
    }

    private bool TryFlushInputsSafe(List<INPUT> buffer, bool validateOnly, out string? error)
    {
        if (validateOnly)
        {
            buffer.Clear();
            error = null;
            return true;
        }

        return TryFlushInputs(buffer, out error);
    }

    private bool TryFlushInputs(List<INPUT> buffer, out string? error)
    {
        error = null;
        if (buffer.Count == 0) return true;
        var arr = buffer.ToArray();
        buffer.Clear();
        return TrySendInputArray(arr, arr.Length, out error);
    }

    private static INPUT CreateVirtualKeyInput(ushort vk, bool keyUp, IntPtr extraInfo)
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
                    dwExtraInfo = extraInfo
                }
            }
        };
    }

    private static INPUT CreateMouseInput(int dx, int dy, uint mouseData, uint flags) =>
        new()
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    mouseData = mouseData,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = InputExtraInfo.MacroPassthroughPointer
                }
            }
        };

    private static void AppendKey(List<INPUT> buffer, ushort vk, bool keyUp) =>
        AppendKey(buffer, vk, keyUp, InputExtraInfo.MacroPassthroughPointer);

    private static void AppendKey(List<INPUT> buffer, ushort vk, bool keyUp, IntPtr extraInfo) =>
        buffer.Add(CreateVirtualKeyInput(vk, keyUp, extraInfo));

    private bool TrySendUnicodeText(string text, MacroExecutionSession session, CancellationToken cancellationToken, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(text)) return true;

        var targetWindow = GetForegroundWindow();
        uint targetThread = targetWindow != IntPtr.Zero
            ? GetWindowThreadProcessId(targetWindow, out _)
            : GetCurrentThreadId();
        var keyboardLayout = GetKeyboardLayout(targetThread);
        if (keyboardLayout == IntPtr.Zero)
        {
            keyboardLayout = GetKeyboardLayout(0);
        }

        Span<char> runeBuffer = stackalloc char[2];
        var unicodeInputs = new INPUT[2];

        foreach (var rune in text.EnumerateRunes())
        {
            ThrowIfPausedOrCanceled(session, cancellationToken);

            if (!TrySendRuneUsingKeyboardLayout(rune, keyboardLayout, session, cancellationToken, out var layoutError))
            {
                if (layoutError != null)
                {
                    error = layoutError;
                    return false;
                }

                if (!TrySendRuneUsingUnicode(rune, runeBuffer, unicodeInputs, session, cancellationToken, out var unicodeError))
                {
                    error = unicodeError;
                    return false;
                }
            }

            bool isWhitespace = Rune.IsWhiteSpace(rune);
            DelayFor(isWhitespace ? TextSendWhitespaceDelayMilliseconds : TextSendInterCharacterDelayMilliseconds, session, cancellationToken);
        }

        error = null;
        return true;
    }

    private bool TryShowPopup(string message, string caption, MessageBoxImage image, out string? error)
    {
        error = null;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            error = "アプリケーションのディスパッチャが利用できません。";
            return false;
        }

        string popupMessage = message ?? string.Empty;
        string popupCaption = string.IsNullOrWhiteSpace(caption) ? "DropSendTo" : caption.Trim();
        Exception? operationError = null;

        void ShowPopup()
        {
            try
            {
                IntPtr ownerHandle = IntPtr.Zero;
                Window? ownerWindow = null;
                var owner = GetPopupOwnerWindow();
                if (owner != null)
                {
                    try
                    {
                        ownerHandle = new WindowInteropHelper(owner).EnsureHandle();
                        ownerWindow = owner;
                    }
                    catch
                    {
                        ownerHandle = IntPtr.Zero;
                        ownerWindow = null;
                    }
                }

                if (ownerHandle == IntPtr.Zero && _windowHandle != IntPtr.Zero)
                {
                    ownerHandle = _windowHandle;
                }

                bool taskbarToggled = false;
                if (ownerWindow != null && !ownerWindow.ShowInTaskbar)
                {
                    ownerWindow.ShowInTaskbar = true;
                    taskbarToggled = true;
                }

                try
                {
                    var flags = MessageBoxConstants.MB_OK |
                                MessageBoxConstants.MB_SETFOREGROUND |
                                MessageBoxConstants.MB_TOPMOST |
                                GetMessageBoxIconFlag(image);
                    MessageBoxW(ownerHandle, popupMessage, popupCaption, flags);
                }
                finally
                {
                    if (taskbarToggled && ownerWindow != null)
                    {
                        ownerWindow.ShowInTaskbar = false;
                    }
                }
            }
            catch (Exception ex)
            {
                operationError = ex;
            }
        }

        if (dispatcher.CheckAccess())
        {
            ShowPopup();
        }
        else
        {
            dispatcher.Invoke(ShowPopup);
        }

        if (operationError != null)
        {
            error = operationError.Message;
            return false;
        }

        return true;
    }

    private static Window? GetPopupOwnerWindow()
    {
        var app = System.Windows.Application.Current;
        if (app == null)
        {
            return null;
        }

        try
        {
            var active = app.Windows.OfType<Window>()
                .FirstOrDefault(w => w.IsVisible && w.IsActive);
            if (active != null)
            {
                return active;
            }

            var visible = app.Windows.OfType<Window>()
                .FirstOrDefault(w => w.IsVisible);
            if (visible != null)
            {
                return visible;
            }
        }
        catch
        {
            // ignore window enumeration errors
        }

        return app.MainWindow?.IsVisible == true ? app.MainWindow : null;
    }

    private static uint GetMessageBoxIconFlag(MessageBoxImage image) =>
        image switch
        {
            MessageBoxImage.Error => MessageBoxConstants.MB_ICONHAND,
            MessageBoxImage.Question => MessageBoxConstants.MB_ICONQUESTION,
            MessageBoxImage.Warning => MessageBoxConstants.MB_ICONEXCLAMATION,
            MessageBoxImage.Information => MessageBoxConstants.MB_ICONINFORMATION,
            _ => 0
        };

    private bool TrySendRuneUsingUnicode(Rune rune, Span<char> buffer, INPUT[] inputs, MacroExecutionSession session, CancellationToken cancellationToken, out string? error)
    {
        error = null;
        int length = rune.EncodeToUtf16(buffer);
        for (int i = 0; i < length; i++)
        {
            ThrowIfPausedOrCanceled(session, cancellationToken);
            var unit = buffer[i];
            inputs[0] = CreateUnicodeInput(unit, keyUp: false);
            inputs[1] = CreateUnicodeInput(unit, keyUp: true);
            if (!TrySendInputArray(inputs, inputs.Length, out var charError))
            {
                error = charError;
                return false;
            }
        }

        return true;
    }

    private bool TrySendRuneUsingKeyboardLayout(Rune rune, IntPtr keyboardLayout, MacroExecutionSession session, CancellationToken cancellationToken, out string? error)
    {
        error = null;
        ThrowIfPausedOrCanceled(session, cancellationToken);
        if (!rune.IsBmp)
        {
            return false;
        }

        char ch = (char)rune.Value;
        short result = VkKeyScanEx(ch, keyboardLayout);
        if (result == -1)
        {
            return false;
        }

        ushort vk = (ushort)(result & 0xFF);
        if (vk == 0xFFFF)
        {
            return false;
        }

        ushort modifiers = (ushort)((result >> 8) & 0xFF);
        if ((modifiers & ~0x7) != 0)
        {
            return false;
        }

        Span<INPUT> span = stackalloc INPUT[8];
        int count = 0;

        if ((modifiers & 0x1) != 0)
        {
            span[count++] = CreateVirtualKeyInput(VK_SHIFT, keyUp: false, InputExtraInfo.MacroPassthroughPointer);
        }
        if ((modifiers & 0x2) != 0)
        {
            span[count++] = CreateVirtualKeyInput(VK_CONTROL, keyUp: false, InputExtraInfo.MacroPassthroughPointer);
        }
        if ((modifiers & 0x4) != 0)
        {
            span[count++] = CreateVirtualKeyInput(VK_MENU, keyUp: false, InputExtraInfo.MacroPassthroughPointer);
        }

        span[count++] = CreateVirtualKeyInput(vk, keyUp: false, InputExtraInfo.MacroPassthroughPointer);
        span[count++] = CreateVirtualKeyInput(vk, keyUp: true, InputExtraInfo.MacroPassthroughPointer);

        if ((modifiers & 0x4) != 0)
        {
            span[count++] = CreateVirtualKeyInput(VK_MENU, keyUp: true, InputExtraInfo.MacroPassthroughPointer);
        }
        if ((modifiers & 0x2) != 0)
        {
            span[count++] = CreateVirtualKeyInput(VK_CONTROL, keyUp: true, InputExtraInfo.MacroPassthroughPointer);
        }
        if ((modifiers & 0x1) != 0)
        {
            span[count++] = CreateVirtualKeyInput(VK_SHIFT, keyUp: true, InputExtraInfo.MacroPassthroughPointer);
        }

        if (count == 0)
        {
            return true;
        }

        var rented = ArrayPool<INPUT>.Shared.Rent(count);
        try
        {
            for (int i = 0; i < count; i++)
            {
                rented[i] = span[i];
            }

            if (!TrySendInputArray(rented, count, out var sendError))
            {
                error = sendError;
                return false;
            }
        }
        finally
        {
            ArrayPool<INPUT>.Shared.Return(rented);
        }

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
                    dwExtraInfo = InputExtraInfo.MacroPassthroughPointer
                }
            }
        };

    private bool TrySendInputArray(INPUT[] inputs, int length, out string? error)
    {
        error = null;
        if (length == 0) return true;
        int cbSize = Marshal.SizeOf<INPUT>();
        var sender = _sendInputOverride;
        var sent = sender != null
            ? sender((uint)length, inputs, cbSize)
            : SendInput((uint)length, inputs, cbSize);
        if (sent == length) return true;
        int err = Marshal.GetLastWin32Error();
        error = $"SendInput の呼び出しに失敗しました (Error={err}).";
        _logger.Error($"SendInput failure: requested={length}, sent={sent}, cbSize={cbSize}, error={err}, firstType={(length > 0 ? inputs[0].type : 0)}, firstVk={(length > 0 ? inputs[0].u.ki.wVk : 0)}, firstFlags={(length > 0 ? inputs[0].u.ki.dwFlags : 0)}");
        return false;
    }

    private static void ThrowIfPausedOrCanceled(MacroExecutionSession session, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        session.WaitIfPaused(cancellationToken);
    }

    private static void DelayFor(int milliseconds, MacroExecutionSession session, CancellationToken cancellationToken)
    {
        if (milliseconds <= 0) return;
        int remaining = milliseconds;
        const int Slice = 25;
        var handles = new[] { cancellationToken.WaitHandle, session.PauseWaitHandle };

        while (remaining > 0)
        {
            int wait = Math.Min(Slice, remaining);
            int signaled = WaitHandle.WaitAny(handles, wait);
            if (signaled == WaitHandle.WaitTimeout)
            {
                remaining -= wait;
                continue;
            }

            if (signaled == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
            else if (signaled == 1)
            {
                session.WaitIfPaused(cancellationToken);
            }
        }
    }

    private static bool TryAppendCombination(string combo, List<INPUT> buffer, IntPtr extraInfo, out string? error)
    {
        error = null;
        if (!KeyChordParser.TryParse(combo, out var chord, out error))
        {
            return false;
        }

        var modifierKeys = new List<ushort>(chord.Modifiers.Count);
        foreach (var modifier in chord.Modifiers)
        {
            if (!KeyChordParser.TryGetModifierVirtualKey(modifier, out var vk))
            {
                error = $"修飾キーに対応する仮想キーを取得できません: \"{modifier}\"";
                return false;
            }
            modifierKeys.Add(vk);
        }

        foreach (var mod in modifierKeys)
        {
            AppendKey(buffer, mod, keyUp: false, extraInfo);
        }
        AppendKey(buffer, chord.MainKey, keyUp: false, extraInfo);
        AppendKey(buffer, chord.MainKey, keyUp: true, extraInfo);
        for (int i = modifierKeys.Count - 1; i >= 0; i--)
        {
            AppendKey(buffer, modifierKeys[i], keyUp: true, extraInfo);
        }

        return true;
    }

    private bool TryAppendPrefixSequence(List<INPUT> buffer, bool passthrough, bool allowMissingResolver, out string? error)
    {
        error = null;
        var resolver = _prefixChordAccessor;
        if (resolver == null)
        {
            if (allowMissingResolver)
            {
                return true;
            }
            error = "PREFIX コマンドは現在利用できません。";
            return false;
        }

        var chord = resolver();
        if (chord == null)
        {
            if (allowMissingResolver)
            {
                return true;
            }
            error = "Prefix が無効化されているため PREFIX コマンドを使用できません。";
            return false;
        }

        var extraInfo = passthrough ? InputExtraInfo.MacroPassthroughPointer : InputExtraInfo.MacroInjectionPointer;
        var modifierKeys = new List<ushort>(chord.Modifiers.Count);
        foreach (var modifier in chord.Modifiers)
        {
            if (!KeyChordParser.TryGetModifierVirtualKey(modifier, out var vk))
            {
                error = $"PREFIX コマンドで修飾キーを解決できません: \"{modifier}\"";
                return false;
            }
            modifierKeys.Add(vk);
        }

        foreach (var mod in modifierKeys)
        {
            AppendKey(buffer, mod, keyUp: false, extraInfo);
        }
        AppendKey(buffer, chord.MainKey, keyUp: false, extraInfo);
        AppendKey(buffer, chord.MainKey, keyUp: true, extraInfo);
        for (int i = modifierKeys.Count - 1; i >= 0; i--)
        {
            AppendKey(buffer, modifierKeys[i], keyUp: true, extraInfo);
        }

        _logger.Info($"Macro PREFIX {(passthrough ? "passthrough" : "arm")} sequence enqueued.");
        return true;
    }

    private static bool TryHandleMouseCommand(string line, List<INPUT> buffer, out string? error)
    {
        var mouseTracker = _currentMouseTracker.Value ?? new MouseHoldTracker();
        error = null;
        string command = line;
        string args = string.Empty;

        int spaceIndex = line.IndexOf(' ', StringComparison.Ordinal);
        if (spaceIndex >= 0)
        {
            command = line[..spaceIndex];
            args = line[(spaceIndex + 1)..].Trim();
        }

        if (command.Equals("MOUSEMOVEABS", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseAbsoluteMouseArguments(args, out var x, out var y, out error))
            {
                return false;
            }
            return TryAppendMouseMoveAbsolute(x, y, buffer, out error);
        }

        if (command.Equals("MOUSEMOVEWIN", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseIntArguments(args, 2, "MOUSEMOVEWIN", out var values, out error))
            {
                return false;
            }
            return TryAppendMouseMoveRelativeToWindow(values[0], values[1], buffer, out error);
        }

        if (command.Equals("MOUSEMOVEREL", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseIntArguments(args, 2, "MOUSEMOVEREL", out var values, out error))
            {
                return false;
            }
            AppendMouseMoveRelative(values[0], values[1], buffer);
            return true;
        }

        if (command.Equals("MOUSELEFTDOWN", StringComparison.OrdinalIgnoreCase))
        {
            if (!ValidateNoArguments(args, "MOUSELEFTDOWN", out error)) return false;
            mouseTracker.TrackDown(MouseButton.Left);
            AppendMouseButton(buffer, MOUSEEVENTF_LEFTDOWN);
            return true;
        }

        if (command.Equals("MOUSELEFTUP", StringComparison.OrdinalIgnoreCase))
        {
            if (!ValidateNoArguments(args, "MOUSELEFTUP", out error)) return false;
            mouseTracker.TrackUp(MouseButton.Left);
            AppendMouseButton(buffer, MOUSEEVENTF_LEFTUP);
            return true;
        }

        if (command.Equals("MOUSERIGHTDOWN", StringComparison.OrdinalIgnoreCase))
        {
            if (!ValidateNoArguments(args, "MOUSERIGHTDOWN", out error)) return false;
            mouseTracker.TrackDown(MouseButton.Right);
            AppendMouseButton(buffer, MOUSEEVENTF_RIGHTDOWN);
            return true;
        }

        if (command.Equals("MOUSERIGHTUP", StringComparison.OrdinalIgnoreCase))
        {
            if (!ValidateNoArguments(args, "MOUSERIGHTUP", out error)) return false;
            mouseTracker.TrackUp(MouseButton.Right);
            AppendMouseButton(buffer, MOUSEEVENTF_RIGHTUP);
            return true;
        }

        if (command.Equals("MOUSEMIDDLEDOWN", StringComparison.OrdinalIgnoreCase))
        {
            if (!ValidateNoArguments(args, "MOUSEMIDDLEDOWN", out error)) return false;
            mouseTracker.TrackDown(MouseButton.Middle);
            AppendMouseButton(buffer, MOUSEEVENTF_MIDDLEDOWN);
            return true;
        }

        if (command.Equals("MOUSEMIDDLEUP", StringComparison.OrdinalIgnoreCase))
        {
            if (!ValidateNoArguments(args, "MOUSEMIDDLEUP", out error)) return false;
            mouseTracker.TrackUp(MouseButton.Middle);
            AppendMouseButton(buffer, MOUSEEVENTF_MIDDLEUP);
            return true;
        }

        if (command.Equals("MOUSELEFTCLICK", StringComparison.OrdinalIgnoreCase))
        {
            if (!ValidateNoArguments(args, "MOUSELEFTCLICK", out error)) return false;
            AppendMouseClick(buffer, MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
            return true;
        }

        if (command.Equals("MOUSERIGHTCLICK", StringComparison.OrdinalIgnoreCase))
        {
            if (!ValidateNoArguments(args, "MOUSERIGHTCLICK", out error)) return false;
            AppendMouseClick(buffer, MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP);
            return true;
        }

        if (command.Equals("MOUSEMIDDLECLICK", StringComparison.OrdinalIgnoreCase))
        {
            if (!ValidateNoArguments(args, "MOUSEMIDDLECLICK", out error)) return false;
            AppendMouseClick(buffer, MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP);
            return true;
        }

        if (command.Equals("MOUSELEFTDOUBLECLICK", StringComparison.OrdinalIgnoreCase))
        {
            if (!ValidateNoArguments(args, "MOUSELEFTDOUBLECLICK", out error)) return false;
            AppendMouseClick(buffer, MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
            AppendMouseClick(buffer, MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
            return true;
        }

        if (command.Equals("MOUSESCROLLUP", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseOptionalIntArgument(args, "MOUSESCROLLUP", out var steps, 1, out error))
            {
                return false;
            }
            if (steps <= 0)
            {
                error = "MOUSESCROLLUP のスクロール量には 1 以上の整数を指定してください。";
                return false;
            }
            AppendMouseWheel(buffer, Math.Clamp(steps, 1, 100) * WHEEL_DELTA);
            return true;
        }

        if (command.Equals("MOUSESCROLLDOWN", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseOptionalIntArgument(args, "MOUSESCROLLDOWN", out var steps, 1, out error))
            {
                return false;
            }
            if (steps <= 0)
            {
                error = "MOUSESCROLLDOWN のスクロール量には 1 以上の整数を指定してください。";
                return false;
            }
            AppendMouseWheel(buffer, -Math.Clamp(steps, 1, 100) * WHEEL_DELTA);
            return true;
        }

        if (command.Equals("MOUSESCROLLLEFT", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseOptionalIntArgument(args, "MOUSESCROLLLEFT", out var steps, 1, out error))
            {
                return false;
            }
            if (steps <= 0)
            {
                error = "MOUSESCROLLLEFT のスクロール量には 1 以上の整数を指定してください。";
                return false;
            }
            AppendMouseHWheel(buffer, -Math.Clamp(steps, 1, 100) * WHEEL_DELTA);
            return true;
        }

        if (command.Equals("MOUSESCROLLRIGHT", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseOptionalIntArgument(args, "MOUSESCROLLRIGHT", out var steps, 1, out error))
            {
                return false;
            }
            if (steps <= 0)
            {
                error = "MOUSESCROLLRIGHT のスクロール量には 1 以上の整数を指定してください。";
                return false;
            }
            AppendMouseHWheel(buffer, Math.Clamp(steps, 1, 100) * WHEEL_DELTA);
            return true;
        }

        error = $"未知のマウスコマンドです: \"{command}\"";
        return false;
    }

    private static bool TryAppendMouseMoveAbsolute(int x, int y, List<INPUT> buffer, out string? error)
    {
        error = null;
        if (!TryNormalizeAbsoluteCoordinates(x, y, out var normalizedX, out var normalizedY, out error))
        {
            return false;
        }
        buffer.Add(CreateMouseInput(normalizedX, normalizedY, 0, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK));
        return true;
    }

    private static bool TryAppendMouseMoveRelativeToWindow(int offsetX, int offsetY, List<INPUT> buffer, out string? error)
    {
        error = null;
        if (!TryGetActiveWindowBounds(out var rect, out var boundsError))
        {
            error = boundsError ?? "アクティブウィンドウの取得に失敗しました。";
            return false;
        }

        long targetX = (long)rect.Left + offsetX;
        long targetY = (long)rect.Top + offsetY;
        targetX = Math.Clamp(targetX, int.MinValue, int.MaxValue);
        targetY = Math.Clamp(targetY, int.MinValue, int.MaxValue);
        return TryAppendMouseMoveAbsolute((int)targetX, (int)targetY, buffer, out error);
    }

    private static void AppendMouseMoveRelative(int dx, int dy, List<INPUT> buffer)
    {
        buffer.Add(CreateMouseInput(dx, dy, 0, MOUSEEVENTF_MOVE));
    }

    private static void AppendMouseButton(List<INPUT> buffer, uint flag)
    {
        buffer.Add(CreateMouseInput(0, 0, 0, flag));
    }

    private static void AppendMouseClick(List<INPUT> buffer, uint downFlag, uint upFlag)
    {
        AppendMouseButton(buffer, downFlag);
        AppendMouseButton(buffer, upFlag);
    }

    private static void AppendMouseWheel(List<INPUT> buffer, int amount)
    {
        buffer.Add(CreateMouseInput(0, 0, unchecked((uint)amount), MOUSEEVENTF_WHEEL));
    }

    private static void AppendMouseHWheel(List<INPUT> buffer, int amount)
    {
        buffer.Add(CreateMouseInput(0, 0, unchecked((uint)amount), MOUSEEVENTF_HWHEEL));
    }

    private static bool TryGetCursorPosition(out int x, out int y)
    {
        if (GetCursorPos(out var point))
        {
            x = point.X;
            y = point.Y;
            return true;
        }

        x = 0;
        y = 0;
        return false;
    }

    private static bool TryNormalizeAbsoluteCoordinates(int x, int y, out int normalizedX, out int normalizedY, out string? error)
    {
        error = null;
        int virtualWidth = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int virtualHeight = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        int virtualLeft = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int virtualTop = GetSystemMetrics(SM_YVIRTUALSCREEN);

        if (virtualWidth <= 0 || virtualHeight <= 0)
        {
            normalizedX = 0;
            normalizedY = 0;
            error = "画面サイズの取得に失敗しました。";
            return false;
        }

        double width = Math.Max(virtualWidth - 1, 1);
        double height = Math.Max(virtualHeight - 1, 1);
        double nx = ((double)(x - virtualLeft) * NormalizedCoordinateMax) / width;
        double ny = ((double)(y - virtualTop) * NormalizedCoordinateMax) / height;
        normalizedX = Math.Clamp((int)Math.Round(nx), 0, 65535);
        normalizedY = Math.Clamp((int)Math.Round(ny), 0, 65535);
        return true;
    }

    private static bool TryParseIntArguments(string args, int expectedCount, string commandName, out int[] values, out string? error)
    {
        error = null;
        var tokens = SplitArguments(args);
        values = Array.Empty<int>();
        if (tokens.Length != expectedCount)
        {
            error = $"{commandName} には {expectedCount} 個の整数引数を指定してください。";
            return false;
        }

        values = new int[expectedCount];
        for (int i = 0; i < expectedCount; i++)
        {
            if (!int.TryParse(tokens[i], out values[i]))
            {
                error = $"{commandName} の引数は整数で指定してください。";
                return false;
            }
        }

        return true;
    }

    private static bool TryParseOptionalIntArgument(string args, string commandName, out int value, int defaultValue, out string? error)
    {
        error = null;
        value = defaultValue;
        if (string.IsNullOrWhiteSpace(args))
        {
            return true;
        }

        var tokens = SplitArguments(args);
        if (tokens.Length != 1)
        {
            error = $"{commandName} には 1 個の整数引数のみ指定できます。";
            return false;
        }

        if (!int.TryParse(tokens[0], out value))
        {
            error = $"{commandName} の引数は整数で指定してください。";
            return false;
        }

        return true;
    }

    private static bool ValidateNoArguments(string args, string commandName, out string? error)
    {
        error = null;
        if (!string.IsNullOrWhiteSpace(args))
        {
            error = $"{commandName} に追加の引数は指定できません。";
            return false;
        }

        return true;
    }

    private static string[] SplitArguments(string args) =>
        string.IsNullOrWhiteSpace(args)
            ? Array.Empty<string>()
            : args.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

private static bool TryParseAbsoluteMouseArguments(string args, out int x, out int y, out string? error)
{
    error = null;
    x = 0;
    y = 0;

    var tokens = SplitArguments(args);
    if (tokens.Length == 1)
    {
        return TryResolveWindowCoordinateToken(tokens[0], out x, out y, out error);
    }

    if (tokens.Length == 2)
    {
        if (!int.TryParse(tokens[0], out x) || !int.TryParse(tokens[1], out y))
        {
            error = "MOUSEMOVEABS の引数は整数で指定するか、座標予約語を使用してください。";
            return false;
        }
        return true;
    }

    error = "MOUSEMOVEABS には座標予約語 1 個、または 2 個の整数引数を指定してください。";
    return false;
}

private static bool TryResolveWindowCoordinateToken(string token, out int x, out int y, out string? error)
{
    x = 0;
    y = 0;
    error = null;

    if (token.EndsWith("_X", StringComparison.OrdinalIgnoreCase) ||
        token.EndsWith("_Y", StringComparison.OrdinalIgnoreCase))
    {
        error = $"MOUSEMOVEABS の座標予約語が不正です: \"{token}\"";
        return false;
    }

    if (!TryResolveWindowCoordinatePoint(token, out var px, out var py, out error))
    {
        return false;
    }

    x = (int)px;
    y = (int)py;
    return true;
}

private static bool TryResolveWindowCoordinatePoint(string token, out long x, out long y, out string? error)
{
    x = 0;
    y = 0;
    error = null;

    if (string.IsNullOrWhiteSpace(token))
    {
        error = "座標予約語が空です。";
        return false;
    }

    if (TryResolveCursorCoordinate(token, out var cursorX, out var cursorY, out error))
    {
        if (error != null)
        {
            return false;
        }

        x = cursorX;
        y = cursorY;
        return true;
    }

    if (!TryGetActiveWindowBounds(out var rect, out error))
    {
        error ??= "アクティブウィンドウの座標を取得できませんでした。";
        return false;
    }

    long left = rect.Left;
    long top = rect.Top;
    long right = rect.Right - 1L;
    long bottom = rect.Bottom - 1L;

    if (right < left)
    {
        right = left;
    }
    if (bottom < top)
    {
        bottom = top;
    }

    long middleX = left + ((right - left) / 2L);
    long middleY = top + ((bottom - top) / 2L);

    var normalized = token.Replace("_", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
    switch (normalized)
    {
        case "WINTOPLEFT":
            x = left;
            y = top;
            return true;
        case "WINTOPCENTER":
        case "WINTOPMIDDLE":
            x = middleX;
            y = top;
            return true;
        case "WINTOPRIGHT":
            x = right;
            y = top;
            return true;
        case "WINLEFTCENTER":
        case "WINLEFTMIDDLE":
            x = left;
            y = middleY;
            return true;
        case "WINRIGHTCENTER":
        case "WINRIGHTMIDDLE":
            x = right;
            y = middleY;
            return true;
        case "WINBOTTOMLEFT":
            x = left;
            y = bottom;
            return true;
        case "WINBOTTOMCENTER":
        case "WINBOTTOMMIDDLE":
            x = middleX;
            y = bottom;
            return true;
        case "WINBOTTOMRIGHT":
            x = right;
            y = bottom;
            return true;
        case "WINCENTER":
        case "WINMIDDLE":
        case "WINMID":
            x = middleX;
            y = middleY;
            return true;
        default:
            error = $"座標予約語が不正です: \"{token}\"";
            return false;
    }
}

    private static bool TryGetActiveWindowBounds(out RECT rect, out string? error)
    {
        if (Volatile.Read(ref _useTestActiveWindowBounds) == 1)
        {
            rect = _testActiveWindowRect;
            error = null;
            return true;
        }

        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd))
        {
            rect = default;
            error = "アクティブウィンドウが見つかりません。";
            return false;
        }

        if (TryGetWindowBounds(hwnd, out rect))
        {
            error = null;
            return true;
        }

        rect = default;
        error = "アクティブウィンドウの領域取得に失敗しました。";
        return false;
    }

    private static bool TryGetWindowBounds(IntPtr hwnd, out RECT rect)
    {
        rect = default;
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

    private static bool TryResolveWindowCoordinateComponentToken(string token, out int value, out string? error)
    {
        value = 0;
        error = null;

        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var trimmed = token.Trim();
        var looksLikeWindowToken = trimmed.StartsWith("WIN", StringComparison.OrdinalIgnoreCase);

        bool isX = false;
        bool isY = false;
        if (trimmed.EndsWith("_X", StringComparison.OrdinalIgnoreCase))
        {
            isX = true;
            trimmed = trimmed[..^2];
        }
        else if (trimmed.EndsWith("_Y", StringComparison.OrdinalIgnoreCase))
        {
            isY = true;
            trimmed = trimmed[..^2];
        }

        trimmed = trimmed.TrimEnd('_');

        if (!isX && !isY)
        {
            if (looksLikeWindowToken)
            {
                error = $"座標予約語には \"_X\" または \"_Y\" を付けてください: \"{token}\"";
            }
            return false;
        }

        if (!TryResolveWindowCoordinatePoint(trimmed, out var pointX, out var pointY, out var pointError))
        {
            if (pointError != null)
            {
                error = pointError;
            }
            else if (looksLikeWindowToken)
            {
                error = $"座標予約語の解決に失敗しました: \"{token}\"";
            }
            return false;
        }

        value = isX ? (int)pointX : (int)pointY;
        return true;
    }

    private static bool TryParseInt64OrWindowToken(string input, out long value, out string? error)
    {
        value = 0;
        error = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var trimmed = input.Trim();
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        if (TryResolveWindowCoordinateComponentToken(trimmed, out var component, out var coordError))
        {
            value = component;
            return true;
        }

        if (coordError != null)
        {
            error = coordError;
        }
        return false;
    }

    internal static void SetMacroCursorStartForTesting(int x, int y)
    {
        CurrentMacroCursor.Value = new MacroCursorContext(x, y);
    }

    internal static void ClearMacroCursorForTesting()
    {
        CurrentMacroCursor.Value = null;
    }

    internal static void SetActiveWindowBoundsForTesting(int left, int top, int right, int bottom)
    {
        _testActiveWindowRect = new RECT
        {
            Left = left,
            Top = top,
            Right = right,
            Bottom = bottom
        };
        Volatile.Write(ref _useTestActiveWindowBounds, 1);
    }

    internal static void ClearActiveWindowBoundsForTesting()
    {
        Volatile.Write(ref _useTestActiveWindowBounds, 0);
    }

    private readonly struct MacroCursorScope : IDisposable
    {
        private readonly MacroCursorContext? _previous;
        private readonly bool _active;

        public MacroCursorScope(int x, int y)
        {
            _previous = CurrentMacroCursor.Value;
            CurrentMacroCursor.Value = new MacroCursorContext(x, y);
            _active = true;
        }

        public void Dispose()
        {
            if (_active)
            {
                CurrentMacroCursor.Value = _previous;
            }
        }
    }

    private readonly struct MacroCursorContext
    {
        public MacroCursorContext(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }
    }

    private sealed class KeyHoldTracker
    {
        private readonly List<ushort> _order = new();
        private readonly HashSet<ushort> _set = new();

        public bool HasHeldKeys => _order.Count > 0;

        public void TrackKeyDown(ushort key)
        {
            if (_set.Add(key))
            {
                _order.Add(key);
            }
        }

        public void TrackKeyUp(ushort key)
        {
            if (!_set.Remove(key)) return;
            for (int i = _order.Count - 1; i >= 0; i--)
            {
                if (_order[i] == key)
                {
                    _order.RemoveAt(i);
                    break;
                }
            }
        }

        public void ReleaseAll(List<INPUT> buffer)
        {
            for (int i = _order.Count - 1; i >= 0; i--)
            {
                AppendKey(buffer, _order[i], true);
            }
            _order.Clear();
            _set.Clear();
        }
    }

    private sealed class MouseHoldTracker
    {
        private bool _left;
        private bool _right;
        private bool _middle;

        public bool HasHeldButtons => _left || _right || _middle;

        public void TrackDown(MouseButton button)
        {
            switch (button)
            {
                case MouseButton.Left:
                    _left = true;
                    break;
                case MouseButton.Right:
                    _right = true;
                    break;
                case MouseButton.Middle:
                    _middle = true;
                    break;
            }
        }

        public void TrackUp(MouseButton button)
        {
            switch (button)
            {
                case MouseButton.Left:
                    _left = false;
                    break;
                case MouseButton.Right:
                    _right = false;
                    break;
                case MouseButton.Middle:
                    _middle = false;
                    break;
            }
        }

        public void ReleaseAll(List<INPUT> buffer)
        {
            if (_left)
            {
                AppendMouseButton(buffer, MOUSEEVENTF_LEFTUP);
                _left = false;
            }
            if (_right)
            {
                AppendMouseButton(buffer, MOUSEEVENTF_RIGHTUP);
                _right = false;
            }
            if (_middle)
            {
                AppendMouseButton(buffer, MOUSEEVENTF_MIDDLEUP);
                _middle = false;
            }
        }
    }

    private enum MouseButton
    {
        Left,
        Right,
        Middle
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

    private sealed class ForeachDropFrame
    {
        public ForeachDropFrame(string variableName, string? indexVariableName)
        {
            VariableName = variableName;
            IndexVariableName = indexVariableName;
        }

        public string VariableName { get; }
        public string? IndexVariableName { get; }
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

    private static bool TryGetMacroCursor(out MacroCursorContext context)
    {
        var current = CurrentMacroCursor.Value;
        if (current.HasValue)
        {
            context = current.Value;
            return true;
        }

        context = default;
        return false;
    }

    private static bool TryResolveCursorCoordinate(string token, out long x, out long y, out string? error)
    {
        x = 0;
        y = 0;
        error = null;

        var normalized = token.Replace("_", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        switch (normalized)
        {
            case "CURSORSTART":
            case "CURSORORIGIN":
            case "CURSORHOME":
            case "CURSORSTARTPOSITION":
                if (TryGetMacroCursor(out var context))
                {
                    x = context.X;
                    y = context.Y;
                    return true;
                }

                error = "マクロ開始時のマウス座標が利用できません。";
                return true;
            default:
                return false;
        }
    }

    private static bool IsExtendedKey(ushort vk) =>
        vk == VK_RIGHT || vk == VK_LEFT || vk == VK_UP || vk == VK_DOWN ||
        vk == VK_HOME || vk == VK_END || vk == VK_PRIOR || vk == VK_NEXT ||
        vk == VK_INSERT || vk == VK_DELETE || vk == VK_APPS ||
        vk == VK_RWIN || vk == VK_LWIN || vk == VK_MENU;

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_HWHEEL = 0x01000;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    private const int WHEEL_DELTA = 120;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const double NormalizedCoordinateMax = 65535.0;
    private const int SW_RESTORE = 9;

    private const uint MAPVK_VK_TO_VSC = 0x0;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;

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
    private static class MessageBoxConstants
    {
        public const uint MB_OK = 0x00000000;
        public const uint MB_ICONHAND = 0x00000010;
        public const uint MB_ICONQUESTION = 0x00000020;
        public const uint MB_ICONEXCLAMATION = 0x00000030;
        public const uint MB_ICONINFORMATION = 0x00000040;
        public const uint MB_SETFOREGROUND = 0x00010000;
        public const uint MB_TOPMOST = 0x00040000;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

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

    internal static void SetSendInputOverrideForTesting(Func<uint, int, uint>? overrideFunc)
    {
        if (overrideFunc is null)
        {
            _sendInputOverride = null;
            return;
        }

        _sendInputOverride = (count, _, size) => overrideFunc(count, size);
    }

    private sealed class MacroSuspensionHandle : IAsyncDisposable
    {
        private readonly KeyboardMacroService _owner;
        private bool _resumed;

        internal MacroSuspensionHandle(KeyboardMacroService owner)
        {
            _owner = owner;
        }

        public Task ResumeAsync(CancellationToken cancellationToken = default)
        {
            if (_resumed)
            {
                return Task.CompletedTask;
            }
            _resumed = true;
            return _owner.ResumeSuspendedMacroAsync(this, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (_resumed) return;
            await ResumeAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private sealed class MacroExecutionSession
    {
        private readonly SemaphoreSlim _lock;
        private readonly MacroPauseCoordinator _pauseCoordinator = new();
        private bool _lockHeld;

        public MacroExecutionSession(SemaphoreSlim macroLock)
        {
            _lock = macroLock;
        }

        public bool LockHeld => _lockHeld;
        public bool IsPaused => _pauseCoordinator.IsPaused;
        public WaitHandle PauseWaitHandle => _pauseCoordinator.PauseRequestHandle;

        public void MarkLockHeld() => _lockHeld = true;

        public void WaitIfPaused(CancellationToken cancellationToken) =>
            _pauseCoordinator.WaitIfPaused(cancellationToken);

        public async Task<bool> PauseAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            var paused = await _pauseCoordinator.RequestPauseAsync(timeout, cancellationToken).ConfigureAwait(false);
            if (!paused)
            {
                return false;
            }

            if (_lockHeld)
            {
                _lock.Release();
                _lockHeld = false;
            }

            return true;
        }

        public async Task ResumeAsync(CancellationToken cancellationToken)
        {
            if (!_lockHeld)
            {
                await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
                _lockHeld = true;
            }
            _pauseCoordinator.Resume();
        }

        public void ResumeAfterCancel()
        {
            if (!_lockHeld)
            {
                try
                {
                    _lock.Wait();
                    _lockHeld = true;
                }
                catch (ObjectDisposedException)
                {
                    // disposing; ignore
                }
            }
            _pauseCoordinator.Resume();
        }
    }

    private sealed class MacroPauseCoordinator
    {
        private readonly ManualResetEventSlim _resumeEvent = new(true);
        private readonly ManualResetEventSlim _pauseRequestEvent = new(false);
        private readonly object _syncRoot = new();
        private TaskCompletionSource<bool>? _pauseSignal;
        private bool _pauseRequested;
        private bool _isPaused;

        public WaitHandle PauseRequestHandle => _pauseRequestEvent.WaitHandle;

        public bool IsPaused
        {
            get
            {
                lock (_syncRoot)
                {
                    return _isPaused;
                }
            }
        }

        public void WaitIfPaused(CancellationToken cancellationToken)
        {
            TaskCompletionSource<bool>? signal = null;
            lock (_syncRoot)
            {
                if (!_pauseRequested)
                {
                    return;
                }

                if (!_isPaused)
                {
                    _isPaused = true;
                    signal = _pauseSignal;
                }
            }

            signal?.TrySetResult(true);
            _resumeEvent.Wait(cancellationToken);

            lock (_syncRoot)
            {
                if (!_pauseRequested)
                {
                    _isPaused = false;
                }
            }
        }

        public async Task<bool> RequestPauseAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            TaskCompletionSource<bool> signal;
            lock (_syncRoot)
            {
                if (_pauseRequested)
                {
                    signal = _pauseSignal ?? new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _pauseSignal = signal;
                }
                else
                {
                    _pauseRequested = true;
                    _resumeEvent.Reset();
                    _pauseRequestEvent.Set();
                    signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    _pauseSignal = signal;
                    if (_isPaused)
                    {
                        signal.TrySetResult(true);
                    }
                }
            }

            if (timeout <= TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            {
                timeout = TimeSpan.FromMilliseconds(1);
            }

            try
            {
                if (timeout == Timeout.InfiniteTimeSpan)
                {
                    await signal.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    return true;
                }

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeout);
                await signal.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }

        public void Resume()
        {
            lock (_syncRoot)
            {
                _pauseRequested = false;
                _isPaused = false;
                _pauseSignal = null;
                _pauseRequestEvent.Reset();
                _resumeEvent.Set();
            }
        }
    }

    private readonly record struct MacroExecutionEntry(CancellationTokenSource Cancellation, MacroExecutionSession Session);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

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

[DllImport("user32.dll")]
private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    private static extern short VkKeyScanEx(char ch, IntPtr dwhkl);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);


    public record MacroExecutionResult(bool Success, string Message, bool Executed, bool IsCanceled)
    {
        public static MacroExecutionResult Ok() => new(true, string.Empty, true, false);
        public static MacroExecutionResult Ok(string message) => new(true, message ?? string.Empty, true, false);
        public static MacroExecutionResult Skip(string message) => new(true, message, false, false);
        public static MacroExecutionResult Fail(string message) => new(false, message, false, false);
        public static MacroExecutionResult Canceled(string message) => new(false, message, false, true);
    }
}
