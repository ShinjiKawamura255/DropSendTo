using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMessageBox = System.Windows.MessageBox;
using Win32OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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
    private static readonly IReadOnlyList<SlotColorOption> AccentColorOptions = new[]
    {
        CreateColorOption(SlotAccentColor.Default, "Default",
            System.Windows.Media.Color.FromRgb(0x11, 0x11, 0x11),
            System.Windows.Media.Color.FromRgb(0x33, 0x33, 0x33),
            System.Windows.Media.Colors.White),
        CreateColorOption(SlotAccentColor.Teal, "Teal",
            System.Windows.Media.Color.FromRgb(0x10, 0x2A, 0x30),
            System.Windows.Media.Color.FromRgb(0x1F, 0x76, 0x7D),
            System.Windows.Media.Color.FromRgb(0xE4, 0xFD, 0xFF)),
        CreateColorOption(SlotAccentColor.Indigo, "Indigo",
            System.Windows.Media.Color.FromRgb(0x16, 0x15, 0x2E),
            System.Windows.Media.Color.FromRgb(0x4E, 0x52, 0xA6),
            System.Windows.Media.Color.FromRgb(0xF4, 0xF2, 0xFF)),
        CreateColorOption(SlotAccentColor.Amber, "Amber",
            System.Windows.Media.Color.FromRgb(0x2D, 0x1F, 0x0F),
            System.Windows.Media.Color.FromRgb(0xB5, 0x6B, 0x17),
            System.Windows.Media.Color.FromRgb(0xFF, 0xE8, 0xC2)),
        CreateColorOption(SlotAccentColor.Olive, "Olive",
            System.Windows.Media.Color.FromRgb(0x20, 0x27, 0x12),
            System.Windows.Media.Color.FromRgb(0x6E, 0x8C, 0x23),
            System.Windows.Media.Color.FromRgb(0xF0, 0xFF, 0xD8)),
        CreateColorOption(SlotAccentColor.Crimson, "Crimson",
            System.Windows.Media.Color.FromRgb(0x2B, 0x11, 0x16),
            System.Windows.Media.Color.FromRgb(0xB5, 0x45, 0x4F),
            System.Windows.Media.Color.FromRgb(0xFF, 0xE3, 0xE7))
    };

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
    private readonly MacroRecordingService _recordingService = new();
    private int _recordedLineCount;
    private int _recordingStartTextLength;
    private const int MacroIndentSize = 4;

    public RegisterDialog()
    {
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
            ColorComboBox.ItemsSource = AccentColorOptions;
            ColorComboBox.SelectedValue = SlotAccentColor.Default;
        }
        if (MacroBox != null)
        {
            MacroBox.PreviewKeyDown += OnMacroBoxPreviewKeyDown;
        }
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
                WpfMessageBox.Show(macroError ?? "Macro Script の構文が正しくありません。", "Edit Slot", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
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

        SlotSaved?.Invoke(this, new SlotSavedEventArgs(AppTitle, CommandPath, ArgumentsTemplate, MacroScript, ShortcutChord, mode, GetSelectedAccentColor()));
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
        CommandBox.IsEnabled = commandEnabled;
        ArgsBox.IsEnabled = commandEnabled;
        CommandBrowseButton.IsEnabled = commandEnabled;
        MacroBox.IsEnabled = macroEnabled;

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

    private static SlotColorOption CreateColorOption(SlotAccentColor color, string name, System.Windows.Media.Color background, System.Windows.Media.Color accent, System.Windows.Media.Color foreground) =>
        new(
            color,
            name,
            CreatePreviewBackgroundBrush(background),
            CreateFrozenBrush(accent),
            CreateFrozenBrush(foreground));

    private static System.Windows.Media.Brush CreatePreviewBackgroundBrush(System.Windows.Media.Color baseColor)
    {
        var tinted = BlendWithWhite(baseColor, 0.55);
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
}

public sealed class SlotSavedEventArgs : EventArgs
{
    public SlotSavedEventArgs(string appTitle, string commandPath, string argumentsTemplate, string macroScript, string shortcutChord, SlotExecutionMode executionMode, SlotAccentColor accentColor)
    {
        AppTitle = appTitle;
        CommandPath = commandPath;
        ArgumentsTemplate = argumentsTemplate;
        MacroScript = macroScript;
        ShortcutChord = shortcutChord;
        ExecutionMode = executionMode;
        AccentColor = accentColor;
    }

    public string AppTitle { get; }
    public string CommandPath { get; }
    public string ArgumentsTemplate { get; }
    public string MacroScript { get; }
    public string ShortcutChord { get; }
    public SlotExecutionMode ExecutionMode { get; }
    public SlotAccentColor AccentColor { get; }
}
