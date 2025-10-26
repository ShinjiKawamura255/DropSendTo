using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using DropSendTo.Models;
using DropSendTo.Services;
using System.Windows.Interop;

namespace DropSendTo;

public partial class RegisterDialog : Window
{
    public string AppTitle => TitleBox.Text.Trim();
    public string CommandPath => IsMacroMode ? string.Empty : CommandBox.Text.Trim();
    public string ArgumentsTemplate => IsMacroMode ? "{args}" : ArgsBox.Text;
    public string MacroScript => IsMacroMode ? MacroBox.Text : string.Empty;
    public string ShortcutChord { get; private set; } = string.Empty;
    public event EventHandler<SlotSavedEventArgs>? SlotSaved;

    private bool IsMacroMode => MacroModeToggle?.IsChecked == true;
    private MacroTipsWindow? _tipsWindow;
    private readonly MacroRecordingService _recordingService = new();
    private int _recordedLineCount;

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
        if (RecordingStatusText != null)
        {
            RecordingStatusText.Text = "「記録開始」を押すと操作が Macro Script に追記されます。";
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
        MacroModeToggle.IsChecked = !string.IsNullOrWhiteSpace(slot.KeyboardMacroScript);
        UpdateModeState();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (IsMacroMode)
        {
            if (string.IsNullOrWhiteSpace(MacroBox.Text))
            {
                MessageBox.Show("Macro Script モードでは Macro Script を入力してください。", "Edit Slot", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            CommandBox.Text = string.Empty;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(CommandBox.Text))
            {
                MessageBox.Show("Command モードでは Command を入力してください。", "Edit Slot", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            MacroBox.Text = string.Empty;
        }

        var shortcutText = ShortcutBox.Text.Trim();
        if (shortcutText.Length > 0)
        {
            if (!KeyChordParser.TryParse(shortcutText, out var chord, out var error))
            {
                MessageBox.Show(error ?? "ショートカットの書式が正しくありません。", "Edit Slot", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ShortcutChord = chord.NormalizedString;
            ShortcutBox.Text = chord.NormalizedString;
        }
        else
        {
            ShortcutChord = string.Empty;
        }

        SlotSaved?.Invoke(this, new SlotSavedEventArgs(AppTitle, CommandPath, ArgumentsTemplate, MacroScript, ShortcutChord));
        Close();
    }

    private void OnModeToggleChanged(object sender, RoutedEventArgs e)
    {
        UpdateModeState();
    }

    private void UpdateModeState()
    {
        bool macroMode = IsMacroMode;
        CommandBox.IsEnabled = !macroMode;
        ArgsBox.IsEnabled = !macroMode;
        CommandBrowseButton.IsEnabled = !macroMode;
        MacroBox.IsEnabled = macroMode;

        if (!macroMode && _recordingService.IsRecording)
        {
            _recordingService.StopRecording();
            _recordedLineCount = 0;
        }

        RefreshRecordingControls();

        if (!macroMode && RecordingStatusText != null)
        {
            RecordingStatusText.Text = string.Empty;
        }
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
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
        if (!IsMacroMode)
        {
            MessageBox.Show("Macro Script モードでのみ記録を利用できます。", "Macro Recording", MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show(error ?? "入力記録の開始に失敗しました。", "Macro Recording", MessageBoxButton.OK, MessageBoxImage.Error);
            RefreshRecordingControls();
            return;
        }

        _recordedLineCount = 0;
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
        if (lines.Count > _recordedLineCount)
        {
            foreach (var line in lines.Skip(_recordedLineCount))
            {
                AppendMacroLine(line);
                _recordedLineCount++;
            }
        }
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
        bool macroMode = IsMacroMode;
        bool recording = _recordingService.IsRecording;
        if (RecordStartButton != null)
        {
            RecordStartButton.IsEnabled = macroMode && !recording;
        }
        if (RecordStopButton != null)
        {
            RecordStopButton.IsEnabled = macroMode && recording;
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
}

public sealed class SlotSavedEventArgs : EventArgs
{
    public SlotSavedEventArgs(string appTitle, string commandPath, string argumentsTemplate, string macroScript, string shortcutChord)
    {
        AppTitle = appTitle;
        CommandPath = commandPath;
        ArgumentsTemplate = argumentsTemplate;
        MacroScript = macroScript;
        ShortcutChord = shortcutChord;
    }

    public string AppTitle { get; }
    public string CommandPath { get; }
    public string ArgumentsTemplate { get; }
    public string MacroScript { get; }
    public string ShortcutChord { get; }
}
