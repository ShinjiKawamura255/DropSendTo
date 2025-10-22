using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows;
using System.Text;
using System.Globalization;

namespace DropSendTo.Services;

public sealed class KeyboardMacroService : IDisposable
{
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
            _logger.Info("Macro execution cancel requested.");
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
        var scriptToRun = script!;

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
            MacroExecutionResult result;
            try
            {
                _logger.Info($"Macro execution started (length={scriptToRun.Length} chars).");
                result = await Task.Run(() => RunMacroInternal(scriptToRun, linkedCts.Token), linkedCts.Token).ConfigureAwait(false);
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
        {
            _logger.Warn("Macro execution aborted: no target window available.");
            return MacroExecutionResult.Fail("ターゲットとなる直前のウィンドウが見つかりません。");
        }

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
            var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawLine in expandedLines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;

                if (StartsWithCommand(line, "SET"))
                {
                    if (!TryApplySetDirective(line, variables, out var setName, out var setValue, out var setError))
                    {
                        return MacroExecutionResult.Fail(setError ?? $"SET コマンドの解釈に失敗しました: \"{line}\"");
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
                        return MacroExecutionResult.Fail(unsetError ?? $"UNSET コマンドの解釈に失敗しました: \"{line}\"");
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
                    if (!TryApplyMathDirective(line, variables, out var mathName, out var beforeValue, out var operandValue, out var resultValue, out var mathError))
                    {
                        return MacroExecutionResult.Fail(mathError ?? $"数値演算の解釈に失敗しました: \"{line}\"");
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
                    if (!TryApplyConcatDirective(line, variables, prepend, out var concatName, out var newValue, out var concatError))
                    {
                        return MacroExecutionResult.Fail(concatError ?? $"文字列結合の解釈に失敗しました: \"{line}\"");
                    }
                    if (!string.IsNullOrEmpty(concatName))
                    {
                        _logger.Info($"Macro variable {(prepend ? "prepend" : "append")}: {concatName} -> \"{TruncateForLog(newValue)}\"");
                    }
                    continue;
                }

                if (StartsWithCommand(line, "WAIT"))
                {
                    var waitToken = line.Length > 4 ? line[4..].Trim() : string.Empty;
                    if (!TryExpandVariables(waitToken, variables, out var expandedWait, out var waitExpandError))
                    {
                        return MacroExecutionResult.Fail(waitExpandError ?? $"変数の解決に失敗しました: \"{line}\"");
                    }
                    if (!int.TryParse(expandedWait, out var waitMs) || waitMs < 0 || waitMs > 60000)
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
                    if (!TryExpandVariables(text, variables, out var expandedText, out var textExpandError))
                    {
                        return MacroExecutionResult.Fail(textExpandError ?? $"変数の解決に失敗しました: \"{line}\"");
                    }
                    if (!TryFlushInputs(buffer, out var flushBeforeTextError))
                    {
                        return MacroExecutionResult.Fail(flushBeforeTextError ?? "SendInput の実行に失敗しました。");
                    }
                    if (!TrySendUnicodeText(expandedText, cancellationToken, out var textError))
                    {
                        return MacroExecutionResult.Fail(textError ?? "TEXT コマンドの送信に失敗しました。");
                    }
                    continue;
                }

                if (StartsWithCommand(line, "CLIPTEXT"))
                {
                    var text = line.Length > 8 ? line[8..].TrimStart() : string.Empty;
                    if (!TryExpandVariables(text, variables, out var clipText, out var clipExpandError))
                    {
                        return MacroExecutionResult.Fail(clipExpandError ?? $"変数の解決に失敗しました: \"{line}\"");
                    }
                    if (!TryFlushInputs(buffer, out var flushBeforeClipError))
                    {
                        return MacroExecutionResult.Fail(flushBeforeClipError ?? "SendInput の実行に失敗しました。");
                    }
                    if (!TrySetClipboardText(clipText, out var clipboardError))
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
                    if (!TryExpandVariables(token, variables, out var expandedToken, out var tokenExpandError))
                    {
                        return MacroExecutionResult.Fail(tokenExpandError ?? $"変数の解決に失敗しました: \"{line}\"");
                    }
                    var resolvedToken = expandedToken.Trim();
                    if (!KeyChordParser.TryResolveKeyToken(resolvedToken, out var key))
                        return MacroExecutionResult.Fail($"KEYDOWN のキー名が不正です: \"{resolvedToken}\"");
                    AppendKey(buffer, key, false);
                    continue;
                }

                if (StartsWithCommand(line, "KEYUP"))
                {
                    var token = line.Length > 5 ? line[5..].Trim() : string.Empty;
                    if (!TryExpandVariables(token, variables, out var expandedToken, out var tokenExpandError))
                    {
                        return MacroExecutionResult.Fail(tokenExpandError ?? $"変数の解決に失敗しました: \"{line}\"");
                    }
                    var resolvedToken = expandedToken.Trim();
                    if (!KeyChordParser.TryResolveKeyToken(resolvedToken, out var key))
                        return MacroExecutionResult.Fail($"KEYUP のキー名が不正です: \"{resolvedToken}\"");
                    AppendKey(buffer, key, true);
                    continue;
                }

                if (StartsWithCommand(line, "KEY"))
                {
                    var combo = line.Length > 3 ? line[3..].Trim() : string.Empty;
                    if (!TryExpandVariables(combo, variables, out var expandedCombo, out var comboExpandError))
                    {
                        return MacroExecutionResult.Fail(comboExpandError ?? $"変数の解決に失敗しました: \"{line}\"");
                    }
                    if (!TryAppendCombination(expandedCombo.Trim(), buffer, out var error))
                    {
                        return MacroExecutionResult.Fail(error ?? $"KEY の書式が不正です: \"{expandedCombo}\"");
                    }
                    continue;
                }

                if (StartsWithCommand(line, "MOUSE"))
                {
                    if (!TryExpandVariables(line, variables, out var expandedMouse, out var mouseExpandError))
                    {
                        return MacroExecutionResult.Fail(mouseExpandError ?? $"変数の解決に失敗しました: \"{line}\"");
                    }
                    if (!TryHandleMouseCommand(expandedMouse, buffer, out var mouseError))
                    {
                        return MacroExecutionResult.Fail(mouseError ?? $"MOUSE コマンドの書式が不正です: \"{expandedMouse}\"");
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

    internal static bool TryExpandVariables(string input, IReadOnlyDictionary<string, string> variables, out string result, out string? error)
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
                if (!IsValidVariableName(name))
                {
                    error = $"変数名が不正です: \"{token}\"";
                    return false;
                }

                if (!variables.TryGetValue(name, out var value))
                {
                    error = $"変数 \"{name}\" は定義されていません。";
                    return false;
                }

                sb.Append(value);
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

    internal static bool TryApplySetDirective(string line, Dictionary<string, string> variables, out string? name, out string? value, out string? error)
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
        if (!TryExpandVariables(trimmedValue, variables, out var expandedValue, out var expandError))
        {
            error = expandError;
            return false;
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

    internal static bool TryApplyMathDirective(string line, Dictionary<string, string> variables, out string? name, out long beforeValue, out long operandValue, out long resultValue, out string? error)
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
        if (!TryExpandVariables(operandRaw, variables, out var expandedOperand, out var expandError))
        {
            error = expandError;
            return false;
        }
        if (!long.TryParse(expandedOperand.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out operandValue))
        {
            error = $"{command} の値は整数で指定してください: \"{expandedOperand}\"";
            return false;
        }

        if (!long.TryParse(currentRaw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out beforeValue))
        {
            error = $"変数 \"{nameToken}\" の値を整数として解釈できません: \"{currentRaw}\"";
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

    internal static bool TryApplyConcatDirective(string line, Dictionary<string, string> variables, bool prepend, out string? name, out string? newValue, out string? error)
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
        if (!TryExpandVariables(operandRaw, variables, out var expandedOperand, out var expandError))
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
                    dwExtraInfo = InputExtraInfo.MacroInjectionPointer
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
                    dwExtraInfo = InputExtraInfo.MacroInjectionPointer
                }
            }
        };

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
                    dwExtraInfo = InputExtraInfo.MacroInjectionPointer
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
            AppendKey(buffer, mod, keyUp: false);
        }
        AppendKey(buffer, chord.MainKey, keyUp: false);
        AppendKey(buffer, chord.MainKey, keyUp: true);
        for (int i = modifierKeys.Count - 1; i >= 0; i--)
        {
            AppendKey(buffer, modifierKeys[i], keyUp: true);
        }

        return true;
    }

    private static bool TryHandleMouseCommand(string line, List<INPUT> buffer, out string? error)
    {
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
            if (!TryParseIntArguments(args, 2, "MOUSEMOVEABS", out var values, out error))
            {
                return false;
            }
            return TryAppendMouseMoveAbsolute(values[0], values[1], buffer, out error);
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
            AppendMouseButton(buffer, MOUSEEVENTF_LEFTDOWN);
            return true;
        }

        if (command.Equals("MOUSELEFTUP", StringComparison.OrdinalIgnoreCase))
        {
            if (!ValidateNoArguments(args, "MOUSELEFTUP", out error)) return false;
            AppendMouseButton(buffer, MOUSEEVENTF_LEFTUP);
            return true;
        }

        if (command.Equals("MOUSERIGHTDOWN", StringComparison.OrdinalIgnoreCase))
        {
            if (!ValidateNoArguments(args, "MOUSERIGHTDOWN", out error)) return false;
            AppendMouseButton(buffer, MOUSEEVENTF_RIGHTDOWN);
            return true;
        }

        if (command.Equals("MOUSERIGHTUP", StringComparison.OrdinalIgnoreCase))
        {
            if (!ValidateNoArguments(args, "MOUSERIGHTUP", out error)) return false;
            AppendMouseButton(buffer, MOUSEEVENTF_RIGHTUP);
            return true;
        }

        if (command.Equals("MOUSEMIDDLEDOWN", StringComparison.OrdinalIgnoreCase))
        {
            if (!ValidateNoArguments(args, "MOUSEMIDDLEDOWN", out error)) return false;
            AppendMouseButton(buffer, MOUSEEVENTF_MIDDLEDOWN);
            return true;
        }

        if (command.Equals("MOUSEMIDDLEUP", StringComparison.OrdinalIgnoreCase))
        {
            if (!ValidateNoArguments(args, "MOUSEMIDDLEUP", out error)) return false;
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
    private static extern int GetSystemMetrics(int nIndex);

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
