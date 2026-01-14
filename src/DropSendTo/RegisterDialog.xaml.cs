using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMessageBox = System.Windows.MessageBox;
using Win32OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using DropSendTo.Models;
using DropSendTo.Services;
using System.Windows.Interop;
using System.Text;
using System.Windows.Media;

namespace DropSendTo;

public partial class RegisterDialog : Window
{
    private static readonly (SlotAccentColor Color, string Name)[] AccentColorNames =
    {
        (SlotAccentColor.Default, "Default"),
        (SlotAccentColor.Teal, "Teal"),
        (SlotAccentColor.Indigo, "Indigo"),
        (SlotAccentColor.Azure, "Azure"),
        (SlotAccentColor.Amber, "Amber"),
        (SlotAccentColor.Olive, "Olive"),
        (SlotAccentColor.Emerald, "Emerald"),
        (SlotAccentColor.Crimson, "Crimson"),
        (SlotAccentColor.Magenta, "Magenta")
    };

    private readonly IReadOnlyList<SlotColorOption> _accentColorOptions;

    public string AppTitle => TitleBox.Text.Trim();
    public string CommandPath => ExecutionMode == SlotExecutionMode.MacroScript
        ? string.Empty
        : CommandBox.Text.Trim();
    public string ArgumentsTemplate => ExecutionMode == SlotExecutionMode.MacroScript
        ? "{args}"
        : ArgsBox.Text;
    public string MacroScript => ExecutionMode == SlotExecutionMode.Command
        ? string.Empty
        : MacroBox.Text;
    public string SearchKeywords => SearchKeywordBox.Text.Trim();
    public string ShortcutChord { get; private set; } = string.Empty;
    public SlotExecutionMode ExecutionMode => CurrentMode;
    public event EventHandler<SlotSavedEventArgs>? SlotSaved;

    private SlotExecutionMode CurrentMode
    {
        get
        {
            if (ModeComboBox?.SelectedValue is SlotExecutionMode mode)
            {
                return mode;
            }
            return SlotExecutionMode.Command;
        }
    }
    private MacroTipsWindow? _tipsWindow;
    private MacroSnippetSearchWindow? _snippetSearchWindow;
    private readonly MacroRecordingService _recordingService = new();
    private int _recordedLineCount;
    private int _recordingStartTextLength;
    private const int MacroIndentSize = 4;
    private static readonly string[] WinAnchorTokens =
    {
        "WIN_TOPLEFT",
        "WIN_TOPCENTER",
        "WIN_TOPRIGHT",
        "WIN_LEFTCENTER",
        "WIN_CENTER",
        "WIN_RIGHTCENTER",
        "WIN_BOTTOMLEFT",
        "WIN_BOTTOMCENTER",
        "WIN_BOTTOMRIGHT"
    };

    private static readonly string[] WinAliasTokens =
    {
        "WIN_TOPMIDDLE",
        "WIN_LEFTMIDDLE",
        "WIN_RIGHTMIDDLE",
        "WIN_BOTTOMMIDDLE",
        "WIN_MIDDLE",
        "WIN_MID"
    };

    private static readonly IReadOnlyList<MacroSnippetGroup> MacroSnippetGroups = new[]
    {
        new MacroSnippetGroup("変数", new[]
        {
            new MacroSnippet("SET <名前> <値>", "SET Name Value"),
            new MacroSnippet("UNSET <名前>", "UNSET Name"),
            new MacroSnippet("ADD <名前> <整数>", "ADD Counter 1"),
            new MacroSnippet("SUB <名前> <整数>", "SUB Counter 1"),
            new MacroSnippet("MUL <名前> <整数>", "MUL Counter 2"),
            new MacroSnippet("DIV <名前> <整数>", "DIV Counter 2"),
            new MacroSnippet("APPEND <名前> <文字列>", "APPEND Message !"),
            new MacroSnippet("PREPEND <名前> <文字列>", "PREPEND Message ["),
            new MacroSnippet("REPLACE <名前> \"検索\" \"置換\"", "REPLACE Body \" \" \"\""),
            new MacroSnippet("REPLACE_REGEX <名前> \"正規表現\" \"置換\"", "REPLACE_REGEX Body \"[0-9]+\" \"\""),
            new MacroSnippet("TESTPATH <名前> <パス>", "TESTPATH PathOk {{drop_path}}"),
            new MacroSnippet("RESOLVE_LINK <変数> <パス>", "RESOLVE_LINK TargetPath {{drop_path}}"),
            new MacroSnippet("RENAME <元パス> <新しいパス>", "RENAME {{drop_path}} {{drop_path}}.bak"),
            new MacroSnippet("READFILE <変数> <パス> [MAX n]", "READFILE Body {{drop_path}}"),
            new MacroSnippet("PROMPT <名前> \"メッセージ\"", "PROMPT InputValue \"値を入力してください\""),
            new MacroSnippet("PROMPT ... TIMEOUT <ms> \"値\"", "PROMPT InputValue \"値を入力してください\" TIMEOUT 5000 \"(timeout)\""),
            new MacroSnippet("POPUP \"メッセージ\"", "POPUP \"{{drop_path}} が見つかりません\"")
        }),
        new MacroSnippetGroup("フロー制御 / Prefix", new[]
        {
            new MacroSnippet("WAIT <ミリ秒>", "WAIT 250"),
            new MacroSnippet("REPEAT ... ENDREPEAT", "REPEAT 3\n    \nENDREPEAT"),
            new MacroSnippet("FOREACH_DROP ... ENDFOREACH", "FOREACH_DROP Item INDEX i\n    TEXT [{{i}}] {{Item}}\nENDFOREACH"),
            new MacroSnippet("IF / ELSE / ENDIF", "IF \"{{clipboard}}\" CONTAINS \"keyword\"\n    TEXT matched\nELSE\n    TEXT missed\nENDIF"),
            new MacroSnippet("COMMAND (テンプレ展開)", "COMMAND"),
            new MacroSnippet("COMMAND [引数指定]", "COMMAND {{clipboard}}"),
            new MacroSnippet("RETURN [\"メッセージ\"]", "RETURN \"finished\""),
            new MacroSnippet("PREFIX SEND", "PREFIX SEND"),
            new MacroSnippet("PREFIX ARM", "PREFIX ARM"),
            new MacroSnippet("PREFIX PASSTHROUGH", "PREFIX PASSTHROUGH")
        }),
        new MacroSnippetGroup("キーボード操作", new[]
        {
            new MacroSnippet("KEY <修飾+キー>", "KEY Ctrl+Shift+S"),
            new MacroSnippet("KEYDOWN <キー>", "KEYDOWN Ctrl"),
            new MacroSnippet("KEYUP <キー>", "KEYUP Ctrl"),
            new MacroSnippet("TEXT <文字列>", "TEXT Hello"),
            new MacroSnippet("SETCLIP <文字列>", "SETCLIP {{drop_path}}"),
            new MacroSnippet("CLIPTEXT <文字列>", "CLIPTEXT {{clipboard}}")
        }),
        new MacroSnippetGroup("COMMAND 命令", new[]
        {
            new MacroSnippet("COMMAND (テンプレ展開)", "COMMAND"),
            new MacroSnippet("COMMAND [引数指定]", "COMMAND {{drop_args}}"),
            new MacroSnippet("COMMAND_APP <パス>", "COMMAND_APP \"C:\\\\apps\\\\custom.exe\"")
        }),
        new MacroSnippetGroup("マウス操作", new[]
        {
            new MacroSnippet("MOUSEMOVEABS <X> <Y>", "MOUSEMOVEABS 640 360"),
            new MacroSnippet("MOUSEMOVEABS WIN_* 予約語", "MOUSEMOVEABS WIN_CENTER"),
            new MacroSnippet("MOUSEMOVEWIN <dX> <dY>", "MOUSEMOVEWIN 100 60"),
            new MacroSnippet("MOUSEMOVEREL <dX> <dY>", "MOUSEMOVEREL 30 -20"),
            new MacroSnippet("MOUSELEFTCLICK", "MOUSELEFTCLICK"),
            new MacroSnippet("MOUSELEFTDOUBLECLICK", "MOUSELEFTDOUBLECLICK"),
            new MacroSnippet("MOUSELEFTDOWN", "MOUSELEFTDOWN"),
            new MacroSnippet("MOUSELEFTUP", "MOUSELEFTUP"),
            new MacroSnippet("MOUSERIGHTCLICK", "MOUSERIGHTCLICK"),
            new MacroSnippet("MOUSEMIDDLEDOWN / MOUSEMIDDLEUP", "MOUSEMIDDLEDOWN\nMOUSEMIDDLEUP"),
            new MacroSnippet("MOUSESCROLLDOWN [回数]", "MOUSESCROLLDOWN 3"),
            new MacroSnippet("MOUSESCROLLLEFT/RIGHT [回数]", "MOUSESCROLLLEFT 1")
        }),
        new MacroSnippetGroup("プレースホルダー / 予約語", new[]
        {
            new MacroSnippet("{{clipboard}}", "{{clipboard}}"),
            new MacroSnippet("{{clipboard_args}}", "{{clipboard_args}}"),
            new MacroSnippet("{{clipboard_args:n}}", "{{clipboard_args:3}}"),
            new MacroSnippet("{{drop_args}}", "{{drop_args}}"),
            new MacroSnippet("{{drop_path}}", "{{drop_path}}"),
            new MacroSnippet("{{drop_path:n}}", "{{drop_path:1}}"),
            new MacroSnippet("{{drop_count}}", "{{drop_count}}"),
            new MacroSnippet("CURSOR_START (_X/_Y)", "CURSOR_START"),
            new MacroSnippet("{{args}} (Args Template 展開)", "{{args}}")
        }),
        new MacroSnippetGroup("よくある例", new[]
        {
            new MacroSnippet("コピー→貼り付け後に追記", "KEY Ctrl+C\nWAIT 200\nTEXT processed"),
            new MacroSnippet("REPEAT ブロック例", "REPEAT 3\n    KEYDOWN Shift\n    KEY A\n    KEYUP Shift\nENDREPEAT"),
            new MacroSnippet("FOREACH_DROP で列挙", "FOREACH_DROP Item INDEX i\n    SET Body {{Item}}\n    REPLACE Body \" \" \"_\"\n    TEXT [{{i}}] {{Body}}\nENDFOREACH")
        })
    };

    public RegisterDialog()
    {
        var theme = ThemeService.GetCurrentTheme();
        _accentColorOptions = BuildAccentColorOptions(theme);
        InitializeComponent();
        _recordingService.LineRecorded += OnRecordingLineGenerated;
        if (RecordStartButton != null)
        {
            RecordStartButton.PreviewMouseDown += OnRecordStartPreviewMouseDown;
        }
        if (RecordStopButton != null)
        {
            RecordStopButton.PreviewMouseDown += OnRecordStopPreviewMouseDown;
        }
        if (ModeComboBox != null)
        {
            ModeComboBox.SelectionChanged += OnModeSelectionChanged;
            if (ModeComboBox.SelectedValue == null)
            {
                ModeComboBox.SelectedIndex = 0;
            }
        }
        if (RecordingStatusText != null)
        {
            RecordingStatusText.Text = "「記録開始」を押すと操作が Macro Script に追記されます。";
        }
        if (ColorComboBox != null)
        {
            ColorComboBox.ItemsSource = _accentColorOptions;
            ColorComboBox.SelectedValue = SlotAccentColor.Default;
        }
        if (MacroBox != null)
        {
            MacroBox.PreviewKeyDown += OnMacroBoxPreviewKeyDown;
        }
        if (SearchKeywordBox != null)
        {
            SearchKeywordBox.Text = string.Empty;
        }
        InitializeMacroInsertMenu();
        ApplyMinimizeOptions(SlotMinimizeOptions.CreateDefault());
        UpdateModeState();
    }

    public RegisterDialog(SlotModel slot) : this()
    {
        TitleBox.Text = slot.Title ?? string.Empty;
        CommandBox.Text = slot.Command ?? string.Empty;
        ArgsBox.Text = slot.ArgumentsTemplate ?? "{args}";
        MacroBox.Text = slot.KeyboardMacroScript ?? string.Empty;
        ShortcutBox.Text = slot.ShortcutKey ?? string.Empty;
        ShortcutChord = ShortcutBox.Text.Trim();
        if (ModeComboBox != null)
        {
            var mode = Enum.IsDefined(typeof(SlotExecutionMode), slot.ExecutionMode)
                ? slot.ExecutionMode
                : (!string.IsNullOrWhiteSpace(slot.KeyboardMacroScript) ? SlotExecutionMode.MacroScript : SlotExecutionMode.Command);
            ModeComboBox.SelectedValue = mode;
        }
        if (ColorComboBox != null)
        {
            var accent = Enum.IsDefined(typeof(SlotAccentColor), slot.AccentColor)
                ? slot.AccentColor
                : SlotAccentColor.Default;
            ColorComboBox.SelectedValue = accent;
        }
        if (SearchKeywordBox != null)
        {
            SearchKeywordBox.Text = slot.SearchKeywords ?? string.Empty;
        }
        ApplyMinimizeOptions(slot.MinimizeOptions ?? SlotMinimizeOptions.CreateDefault());
        UpdateModeState();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var mode = CurrentMode;
        bool macroRequired = mode != SlotExecutionMode.Command;
        bool commandRequired = mode != SlotExecutionMode.MacroScript;

        if (macroRequired)
        {
            var formattedMacro = MacroScriptFormatter.NormalizeIndentation(MacroBox.Text);
            MacroBox.Text = formattedMacro;
            if (string.IsNullOrWhiteSpace(formattedMacro))
            {
                var message = mode == SlotExecutionMode.MacroScriptExtended
                    ? "Macro Script 拡張モードでは Macro Script を入力してください。"
                    : "Macro Script モードでは Macro Script を入力してください。";
                WpfMessageBox.Show(message, "Edit Slot", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!KeyboardMacroService.TryValidateScript(formattedMacro, mode, out var macroError))
            {
                var message = (macroError ?? "Macro Script の構文が正しくありません。") +
                              "\n\n構文チェックを無視して保存しますか？";
                var result = WpfMessageBox.Show(message, "Macro Script", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            if (MacroScriptFormatter.TryGetPlaceholderWarning(formattedMacro, out var warning))
            {
                warning += "\n\nこのまま保存しますか？";
                var result = WpfMessageBox.Show(warning, "Macro Script", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
            }
        }
        else
        {
            MacroBox.Text = string.Empty;
        }

        if (commandRequired)
        {
            if (string.IsNullOrWhiteSpace(CommandBox.Text))
            {
                var message = mode == SlotExecutionMode.MacroScriptExtended
                    ? "Macro Script 拡張モードでは Command を入力してください。"
                    : "Command モードでは Command を入力してください。";
                WpfMessageBox.Show(message, "Edit Slot", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        else
        {
            CommandBox.Text = string.Empty;
        }

        if (mode == SlotExecutionMode.MacroScript)
        {
            ArgsBox.Text = "{args}";
        }

        var shortcutText = ShortcutBox.Text.Trim();
        if (shortcutText.Length > 0)
        {
            if (!ShortcutSequenceParser.TryParse(shortcutText, out var sequence, out var error))
            {
                WpfMessageBox.Show(error ?? "ショートカットの書式が正しくありません。", "Edit Slot", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ShortcutChord = sequence.NormalizedString;
            ShortcutBox.Text = sequence.NormalizedString;
        }
        else
        {
            ShortcutChord = string.Empty;
        }

        SlotSaved?.Invoke(this, new SlotSavedEventArgs(AppTitle, CommandPath, ArgumentsTemplate, MacroScript, ShortcutChord, mode, GetSelectedAccentColor(), BuildMinimizeOptions(), SearchKeywords));
        Close();
    }

    private void OnModeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateModeState();
    }

    private void UpdateModeState()
    {
        var mode = CurrentMode;
        bool macroEnabled = mode != SlotExecutionMode.Command;
        bool commandEnabled = mode != SlotExecutionMode.MacroScript;
        ApplyEnabledState(CommandBox, commandEnabled);
        ApplyEnabledState(ArgsBox, commandEnabled);
        ApplyEnabledState(CommandBrowseButton, commandEnabled);
        MacroBox.IsReadOnly = !macroEnabled;
        ApplyEnabledState(MacroBox, macroEnabled);

        if (!macroEnabled && _recordingService.IsRecording)
        {
            _recordingService.StopRecording();
            _recordedLineCount = 0;
        }

        RefreshRecordingControls();

        if (!macroEnabled && RecordingStatusText != null)
        {
            RecordingStatusText.Text = string.Empty;
        }
        else if (macroEnabled && RecordingStatusText != null && string.IsNullOrEmpty(RecordingStatusText.Text))
        {
            RecordingStatusText.Text = "「記録開始」を押すと操作が Macro Script に追記されます。";
        }
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dlg = new Win32OpenFileDialog
        {
            Filter = "All files (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) == true)
        {
            CommandBox.Text = dlg.FileName;
            if (string.IsNullOrWhiteSpace(TitleBox.Text))
            {
                TitleBox.Text = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
            }
        }
    }

    private void OnInsertNewline(object sender, RoutedEventArgs e)
    {
        var idx = TitleBox.CaretIndex;
        TitleBox.Text = TitleBox.Text.Insert(idx, System.Environment.NewLine);
        TitleBox.CaretIndex = idx + System.Environment.NewLine.Length;
        TitleBox.Focus();
    }

    private void OnShowMacroTips(object sender, RoutedEventArgs e)
    {
        if (_tipsWindow is { IsVisible: true })
        {
            _tipsWindow.Activate();
            return;
        }

        _tipsWindow = new MacroTipsWindow
        {
            Owner = this
        };
        WindowCascadeService.Arrange(_tipsWindow, this);
        _tipsWindow.Closed += OnTipsWindowClosed;
        _tipsWindow.Show();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_tipsWindow is { })
        {
            _tipsWindow.Closed -= OnTipsWindowClosed;
            _tipsWindow.Close();
            _tipsWindow = null;
        }
        if (_snippetSearchWindow is { })
        {
            _snippetSearchWindow.SnippetChosen -= OnSnippetSearchChosen;
            _snippetSearchWindow.Close();
            _snippetSearchWindow = null;
        }
        if (RecordStartButton != null)
        {
            RecordStartButton.PreviewMouseDown -= OnRecordStartPreviewMouseDown;
        }
        if (RecordStopButton != null)
        {
            RecordStopButton.PreviewMouseDown -= OnRecordStopPreviewMouseDown;
        }
        if (ModeComboBox != null)
        {
            ModeComboBox.SelectionChanged -= OnModeSelectionChanged;
        }
        if (MacroBox != null)
        {
            MacroBox.PreviewKeyDown -= OnMacroBoxPreviewKeyDown;
        }
        _recordingService.LineRecorded -= OnRecordingLineGenerated;
        if (_recordingService.IsRecording)
        {
            _recordingService.StopRecording();
        }
        _recordingService.Dispose();
        base.OnClosed(e);
    }

    private void OnTipsWindowClosed(object? sender, EventArgs e)
    {
        if (_tipsWindow != null)
        {
            _tipsWindow.Closed -= OnTipsWindowClosed;
        }
        _tipsWindow = null;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnRecordStart(object sender, RoutedEventArgs e)
    {
        if (CurrentMode == SlotExecutionMode.Command)
        {
            WpfMessageBox.Show("Macro Script モードまたは拡張モードでのみ記録を利用できます。", "Macro Recording", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_recordingService.IsRecording) return;

        var helper = new WindowInteropHelper(this);
        if (helper.Handle == IntPtr.Zero)
        {
            helper.EnsureHandle();
        }

        if (!_recordingService.StartRecording(helper.Handle, out var error))
        {
            WpfMessageBox.Show(error ?? "入力記録の開始に失敗しました。", "Macro Recording", MessageBoxButton.OK, MessageBoxImage.Error);
            RefreshRecordingControls();
            return;
        }

        _recordedLineCount = 0;
        _recordingStartTextLength = MacroBox?.Text.Length ?? 0;
        RefreshRecordingControls();
        _recordingService.SuppressNextLeftButtonUp();
        if (RecordingStatusText != null)
        {
            RecordingStatusText.Text = "記録中... Macro Script に追記する操作を行ってください。";
        }
    }

    private void OnRecordStop(object sender, RoutedEventArgs e)
    {
        if (!_recordingService.IsRecording) return;

        var lines = _recordingService.StopRecording();
        ReplaceRecordedSection(lines);
        _recordedLineCount = lines.Count;
        RefreshRecordingControls();
        if (RecordingStatusText != null)
        {
            RecordingStatusText.Text = _recordedLineCount > 0
                ? $"記録停止 - {_recordedLineCount} 行を追加しました。"
                : "記録を開始すると操作が Macro Script に追記されます。";
        }
    }

    private void RefreshRecordingControls()
    {
        bool macroEnabled = CurrentMode != SlotExecutionMode.Command;
        bool recording = _recordingService.IsRecording;
        if (RecordStartButton != null)
        {
            RecordStartButton.IsEnabled = macroEnabled && !recording;
        }
        if (RecordStopButton != null)
        {
            RecordStopButton.IsEnabled = macroEnabled && recording;
        }
        if (MacroBox != null)
        {
            MacroBox.IsReadOnly = recording;
        }
        if (MacroInsertButton != null)
        {
            MacroInsertButton.IsEnabled = macroEnabled && !recording;
        }
    }

    private void OnRecordingLineGenerated(object? sender, string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        Dispatcher.Invoke(() =>
        {
            AppendMacroLine(line);
            _recordedLineCount++;
            if (RecordingStatusText != null)
            {
                RecordingStatusText.Text = $"記録中... ({_recordedLineCount} 行)";
            }
        });
    }

    private void AppendMacroLine(string line)
    {
        if (MacroBox == null) return;

        if (MacroBox.Text.Length > 0 && !MacroBox.Text.EndsWith(Environment.NewLine, StringComparison.Ordinal))
        {
            MacroBox.AppendText(Environment.NewLine);
        }

        MacroBox.AppendText(line);
        MacroBox.AppendText(Environment.NewLine);
        MacroBox.CaretIndex = MacroBox.Text.Length;
        MacroBox.ScrollToEnd();
    }

    private void ReplaceRecordedSection(IReadOnlyList<string> lines)
    {
        if (MacroBox == null)
        {
            return;
        }

        var original = MacroBox.Text ?? string.Empty;
        int start = Math.Clamp(_recordingStartTextLength, 0, original.Length);
        var prefix = original[..start];
        var builder = new StringBuilder(prefix.Length + Math.Max(lines.Count * 16, 0));
        builder.Append(prefix);

        if (lines.Count > 0)
        {
            if (builder.Length > 0 && !prefix.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            {
                builder.AppendLine();
            }

            foreach (var line in lines)
            {
                builder.AppendLine(line);
            }
        }

        MacroBox.Text = builder.ToString();
        MacroBox.CaretIndex = MacroBox.Text.Length;
        MacroBox.ScrollToEnd();
    }

    private void OnRecordStartPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _recordingService.SuppressNextLeftButtonDown();
        _recordingService.SuppressNextLeftButtonUp();
    }

    private void OnRecordStopPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _recordingService.SuppressNextLeftButtonDown();
        _recordingService.SuppressNextLeftButtonUp();
    }

    private void OnMacroBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (MacroBox == null || MacroBox.IsReadOnly)
        {
            return;
        }
        if (e.Key != Key.Return && e.Key != Key.Enter)
        {
            return;
        }

        if (MacroBox.SelectionLength > 0)
        {
            var selectionStart = MacroBox.SelectionStart;
            MacroBox.SelectedText = string.Empty;
            MacroBox.CaretIndex = selectionStart;
        }

        var caretIndex = MacroBox.CaretIndex;
        AdjustClosingLineIndent(ref caretIndex);
        var indent = MacroScriptFormatter.GetIndentationForNewLine(MacroBox.Text, caretIndex);
        var insertion = Environment.NewLine + indent;
        MacroBox.SelectionStart = caretIndex;
        MacroBox.SelectionLength = 0;
        MacroBox.SelectedText = insertion;
        MacroBox.CaretIndex = caretIndex + insertion.Length;
        e.Handled = true;
    }

    private void AdjustClosingLineIndent(ref int caretIndex)
    {
        if (MacroBox == null || caretIndex < 0)
        {
            return;
        }

        var text = MacroBox.Text ?? string.Empty;
        if (text.Length == 0)
        {
            return;
        }

        int lineIndex = MacroBox.GetLineIndexFromCharacterIndex(caretIndex);
        if (lineIndex < 0)
        {
            return;
        }
        int lineStart = MacroBox.GetCharacterIndexFromLineIndex(lineIndex);
        if (lineStart < 0)
        {
            return;
        }

        var lineText = MacroBox.GetLineText(lineIndex);
        var lineContent = lineText.TrimEnd('\r', '\n');
        if (lineContent.Length == 0)
        {
            return;
        }

        var trimmed = lineContent.TrimStart();
        if (!IsOutdentDirective(trimmed))
        {
            return;
        }

        int leadingSpaces = lineContent.Length - trimmed.Length;
        if (leadingSpaces == 0)
        {
            return;
        }

        int reduction = Math.Min(leadingSpaces, MacroIndentSize);
        int newIndentLength = leadingSpaces - reduction;
        var newLineContent = new string(' ', newIndentLength) + trimmed;
        MacroBox.Select(lineStart, lineContent.Length);
        MacroBox.SelectedText = newLineContent;
        var newCaret = Math.Max(lineStart + newIndentLength, caretIndex - reduction);
        MacroBox.CaretIndex = newCaret;
        caretIndex = newCaret;
    }

    private static bool IsOutdentDirective(string trimmedLine)
    {
        if (string.IsNullOrWhiteSpace(trimmedLine))
        {
            return false;
        }

        return StartsWithDirective(trimmedLine, "ENDREPEAT")
            || StartsWithDirective(trimmedLine, "ENDIF")
            || StartsWithDirective(trimmedLine, "ENDFOREACH")
            || StartsWithDirective(trimmedLine, "ELSE")
            || StartsWithDirective(trimmedLine, "ELSEIF");
    }

    private static bool StartsWithDirective(string text, string command)
    {
        if (!text.StartsWith(command, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return text.Length == command.Length || char.IsWhiteSpace(text[command.Length]);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private SlotAccentColor GetSelectedAccentColor()
    {
        if (ColorComboBox?.SelectedValue is SlotAccentColor color)
        {
            return color;
        }

        return SlotAccentColor.Default;
    }

    private void ApplyMinimizeOptions(SlotMinimizeOptions options)
    {
        var opt = options ?? SlotMinimizeOptions.CreateDefault();
        MinimizeOnClickCheckBox.IsChecked = opt.EnableOnClick;
        MinimizeOnShortcutCheckBox.IsChecked = opt.EnableOnShortcut;
        MinimizeOnDropCheckBox.IsChecked = opt.EnableOnDrop;
        MinimizeOnKeyboardCheckBox.IsChecked = opt.EnableOnKeyboard;
    }

    private SlotMinimizeOptions BuildMinimizeOptions() => new()
    {
        EnableOnClick = MinimizeOnClickCheckBox.IsChecked == true,
        EnableOnShortcut = MinimizeOnShortcutCheckBox.IsChecked == true,
        EnableOnDrop = MinimizeOnDropCheckBox.IsChecked == true,
        EnableOnKeyboard = MinimizeOnKeyboardCheckBox.IsChecked == true
    };

    private static IReadOnlyList<SlotColorOption> BuildAccentColorOptions(AppTheme theme)
    {
        var options = new List<SlotColorOption>(AccentColorNames.Length);
        foreach (var entry in AccentColorNames)
        {
            var palette = SlotAccentPalette.GetScheme(entry.Color, theme);
            options.Add(CreateColorOption(entry.Color, entry.Name, palette, theme));
        }
        return options;
    }

    private static SlotColorOption CreateColorOption(
        SlotAccentColor color,
        string name,
        SlotAccentPaletteEntry palette,
        AppTheme theme) =>
        new(
            color,
            name,
            CreatePreviewBackgroundBrush(palette.Background, theme),
            CreateFrozenBrush(palette.Border),
            CreateFrozenBrush(palette.Foreground));

    private static System.Windows.Media.Brush CreatePreviewBackgroundBrush(System.Windows.Media.Color baseColor, AppTheme theme)
    {
        double ratio = theme == AppTheme.Dark ? 0.55 : 0.12;
        var tinted = BlendWithWhite(baseColor, ratio);
        var brush = new SolidColorBrush(tinted);
        brush.Freeze();
        return brush;
    }

    private static System.Windows.Media.Brush CreateFrozenBrush(System.Windows.Media.Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static System.Windows.Media.Color BlendWithWhite(System.Windows.Media.Color color, double ratio)
    {
        byte Blend(byte component)
        {
            var blended = component + (255 - component) * ratio;
            return (byte)Math.Clamp(blended, 0, 255);
        }

        return System.Windows.Media.Color.FromRgb(Blend(color.R), Blend(color.G), Blend(color.B));
    }

    private void InitializeMacroInsertMenu()
    {
        if (MacroInsertButton == null)
        {
            return;
        }
        MacroInsertButton.ContextMenu = null;
    }

    private void OnMacroInsertClick(object sender, RoutedEventArgs e)
    {
        OnMacroSnippetSearchClick(sender, e);
    }

    private void OnMacroSnippetSearchClick(object sender, RoutedEventArgs e)
    {
        if (_snippetSearchWindow is { IsVisible: true })
        {
            _snippetSearchWindow.Activate();
            return;
        }

        var entries = BuildMacroSnippetEntries();
        _snippetSearchWindow = new MacroSnippetSearchWindow(entries)
        {
            Owner = this
        };
        _snippetSearchWindow.SnippetChosen += OnSnippetSearchChosen;
        _snippetSearchWindow.Closed += (_, _) =>
        {
            if (_snippetSearchWindow != null)
            {
                _snippetSearchWindow.SnippetChosen -= OnSnippetSearchChosen;
                _snippetSearchWindow = null;
            }
        };
        WindowCascadeService.Arrange(_snippetSearchWindow, this);
        _snippetSearchWindow.Show();
    }

    private void OnSnippetSearchChosen(object? sender, string snippet)
    {
        if (!string.IsNullOrWhiteSpace(snippet))
        {
            InsertMacroSnippet(snippet);
        }
    }

    private IReadOnlyList<MacroSnippetEntry> BuildMacroSnippetEntries()
    {
        var list = new List<MacroSnippetEntry>();
        foreach (var group in MacroSnippetGroups)
        {
            foreach (var snippet in group.Items)
            {
                list.Add(new MacroSnippetEntry(group.Header, snippet.Header, snippet.Content));
            }
        }

        void AddWin(string header, string content) => list.Add(new MacroSnippetEntry("WIN_* 予約語", header, content));

        foreach (var token in WinAnchorTokens)
        {
            AddWin(token, token);
        }
        foreach (var token in WinAliasTokens)
        {
            AddWin(token, token);
        }
        foreach (var token in WinAnchorTokens.Concat(WinAliasTokens))
        {
            AddWin($"{token}_X", $"{token}_X");
            AddWin($"{token}_Y", $"{token}_Y");
        }
        AddWin("CURSOR_START_X", "CURSOR_START_X");
        AddWin("CURSOR_START_Y", "CURSOR_START_Y");

        return list;
    }

    private void InsertMacroSnippet(string snippet)
    {
        if (MacroBox == null || !MacroBox.IsEnabled || MacroBox.IsReadOnly)
        {
            return;
        }

        MacroBox.Focus();
        var selectionStart = MacroBox.SelectionStart;
        var insertion = snippet.EndsWith(Environment.NewLine, StringComparison.Ordinal) ? snippet : snippet + Environment.NewLine;
        MacroBox.SelectedText = insertion;
        MacroBox.CaretIndex = selectionStart + insertion.Length;

        try
        {
            var lineIndex = MacroBox.GetLineIndexFromCharacterIndex(MacroBox.CaretIndex);
            MacroBox.ScrollToLine(lineIndex);
        }
        catch
        {
            MacroBox.ScrollToEnd();
        }
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (IsInteractiveElement(e.OriginalSource)) return;
        DragMove();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        CloseSafely(false);
    }

    private void CloseSafely(bool? dialogResult)
    {
        try
        {
            DialogResult = dialogResult;
        }
        catch (InvalidOperationException)
        {
            Close();
        }
    }

    private void ApplyEnabledState(System.Windows.Controls.Control control, bool enabled)
    {
        if (control == null) return;
        control.IsEnabled = enabled;

        var textKey = enabled ? "Theme.PrimaryTextBrush" : "Theme.SubtleTextBrush";
        var inputBgKey = enabled ? "Theme.InputBackgroundBrush" : "Theme.SurfaceAltBackgroundBrush";
        var buttonBgKey = enabled ? "Theme.ControlBackgroundBrush" : "Theme.SurfaceAltBackgroundBrush";
        var borderKey = enabled ? "Theme.ControlBorderBrush" : "Theme.SubtleBorderBrush";

        switch (control)
        {
            case System.Windows.Controls.Primitives.TextBoxBase tb:
                tb.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, inputBgKey);
                tb.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, textKey);
                tb.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, borderKey);
                tb.Opacity = 1.0;
                break;
            case System.Windows.Controls.PasswordBox pw:
                pw.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, inputBgKey);
                pw.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, textKey);
                pw.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, borderKey);
                pw.Opacity = 1.0;
                break;
            case System.Windows.Controls.Primitives.ButtonBase btn:
                btn.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, buttonBgKey);
                btn.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, textKey);
                btn.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, borderKey);
                btn.Opacity = 1.0;
                break;
        }
    }

    private static bool IsInteractiveElement(object source)
    {
        if (source is not DependencyObject d) return false;
        while (d != null)
        {
            if (d is System.Windows.Controls.Primitives.ButtonBase
                || d is System.Windows.Controls.Primitives.TextBoxBase
                || d is System.Windows.Controls.PasswordBox)
            {
                return true;
            }
            d = VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    private sealed class SlotColorOption
    {
        public SlotColorOption(SlotAccentColor color, string name, System.Windows.Media.Brush background, System.Windows.Media.Brush accent, System.Windows.Media.Brush foreground)
        {
            Color = color;
            Name = name;
            BackgroundBrush = background;
            AccentBrush = accent;
            ForegroundBrush = foreground;
        }

        public SlotAccentColor Color { get; }
        public string Name { get; }
        public System.Windows.Media.Brush BackgroundBrush { get; }
        public System.Windows.Media.Brush AccentBrush { get; }
        public System.Windows.Media.Brush ForegroundBrush { get; }
    }

    private sealed record MacroSnippetGroup(string Header, IReadOnlyList<MacroSnippet> Items);

    private sealed record MacroSnippet(string Header, string Content);

    private static MenuItem CreateWinReservationMenu(RoutedEventHandler insertHandler)
    {
        var winMenu = new MenuItem { Header = EscapeAccessKey("WIN_* 予約語") };

        var anchorMenu = new MenuItem { Header = EscapeAccessKey("基本座標") };
        foreach (var token in WinAnchorTokens)
        {
            anchorMenu.Items.Add(CreateWinSnippetItem(token, insertHandler));
        }
        winMenu.Items.Add(anchorMenu);

        var aliasMenu = new MenuItem { Header = EscapeAccessKey("互換表記") };
        foreach (var token in WinAliasTokens)
        {
            aliasMenu.Items.Add(CreateWinSnippetItem(token, insertHandler));
        }
        winMenu.Items.Add(aliasMenu);

        var componentMenu = new MenuItem { Header = EscapeAccessKey("成分 (_X / _Y)") };
        foreach (var token in WinAnchorTokens.Concat(WinAliasTokens))
        {
            componentMenu.Items.Add(CreateWinSnippetItem($"{token}_X", insertHandler));
            componentMenu.Items.Add(CreateWinSnippetItem($"{token}_Y", insertHandler));
        }
        componentMenu.Items.Add(new Separator());
        componentMenu.Items.Add(CreateWinSnippetItem("CURSOR_START_X", insertHandler));
        componentMenu.Items.Add(CreateWinSnippetItem("CURSOR_START_Y", insertHandler));
        winMenu.Items.Add(componentMenu);

        return winMenu;
    }

private static MenuItem CreateWinSnippetItem(string content, RoutedEventHandler insertHandler)
{
    var item = new MenuItem { Header = EscapeAccessKey(content), Tag = content };
    item.Click += insertHandler;
    return item;
}

private static string EscapeAccessKey(string text) => text.Replace("_", "__", StringComparison.Ordinal);
}

public sealed class SlotSavedEventArgs : EventArgs
{
    public SlotSavedEventArgs(string appTitle, string commandPath, string argumentsTemplate, string macroScript, string shortcutChord, SlotExecutionMode executionMode, SlotAccentColor accentColor, SlotMinimizeOptions minimizeOptions, string searchKeywords)
    {
        AppTitle = appTitle;
        CommandPath = commandPath;
        ArgumentsTemplate = argumentsTemplate;
        MacroScript = macroScript;
        ShortcutChord = shortcutChord;
        ExecutionMode = executionMode;
        AccentColor = accentColor;
        MinimizeOptions = minimizeOptions;
        SearchKeywords = searchKeywords;
    }

    public string AppTitle { get; }
    public string CommandPath { get; }
    public string ArgumentsTemplate { get; }
    public string MacroScript { get; }
    public string ShortcutChord { get; }
    public SlotExecutionMode ExecutionMode { get; }
    public SlotAccentColor AccentColor { get; }
    public SlotMinimizeOptions MinimizeOptions { get; }
    public string SearchKeywords { get; }
}

public sealed record MacroSnippetEntry(string Group, string Header, string Content);
