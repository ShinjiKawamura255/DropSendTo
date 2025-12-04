using System.Windows;
using DropSendTo.Services;

namespace DropSendTo;

public partial class SearchHotkeyDialog : Window
{
    public string NormalizedHotkey { get; private set; } = string.Empty;
    public bool IsHotkeyEnabled { get; private set; }
    public bool IsConfirmed { get; private set; }

    public SearchHotkeyDialog(string? initialHotkey, bool enabled)
    {
        InitializeComponent();
        HotkeyBox.Text = string.IsNullOrWhiteSpace(initialHotkey) ? string.Empty : initialHotkey.Trim();
        EnableCheckBox.IsChecked = enabled;
        IsHotkeyEnabled = enabled;
        NormalizedHotkey = HotkeyBox.Text;
        UpdateUiState();
    }

    private void OnEnableChanged(object sender, RoutedEventArgs e)
    {
        UpdateUiState();
    }

    private void UpdateUiState()
    {
        var enabled = EnableCheckBox.IsChecked == true;
        HotkeyBox.IsEnabled = enabled;
        IsHotkeyEnabled = enabled;
        ErrorBlock.Visibility = Visibility.Collapsed;
        if (enabled)
        {
            HotkeyBox.Focus();
            HotkeyBox.SelectAll();
        }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        ErrorBlock.Visibility = Visibility.Collapsed;
        NormalizedHotkey = HotkeyBox.Text.Trim();
        if (!IsHotkeyEnabled)
        {
            IsConfirmed = true;
            Close();
            return;
        }

        if (string.IsNullOrWhiteSpace(NormalizedHotkey))
        {
            ShowError("ホットキーを入力してください。");
            return;
        }

        if (!KeyChordParser.TryParse(NormalizedHotkey, out var chord, out var error))
        {
            ShowError(error ?? "キーの書式が正しくありません。");
            return;
        }

        NormalizedHotkey = chord.NormalizedString;
        IsConfirmed = true;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorBlock.Text = message;
        ErrorBlock.Visibility = Visibility.Visible;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        Close();
    }
}
