using System.Windows;
using DropSendTo.Services;

namespace DropSendTo;

public partial class PrefixDialog : Window, IConfirmableDialog
{
    public string NormalizedPrefix { get; private set; }
    public bool IsPrefixDisabled { get; private set; }
    public bool IsConfirmed { get; private set; }

    public PrefixDialog(string initialPrefix, bool prefixDisabled)
    {
        InitializeComponent();
        PrefixBox.Text = string.IsNullOrWhiteSpace(initialPrefix) ? "Ctrl+Q" : initialPrefix;
        DisableCheckBox.IsChecked = prefixDisabled;
        NormalizedPrefix = PrefixBox.Text.Trim();
        IsPrefixDisabled = prefixDisabled;
        UpdateUiState();
    }

    private void OnDisableChanged(object sender, RoutedEventArgs e)
    {
        UpdateUiState();
    }

    private void UpdateUiState()
    {
        var disabled = DisableCheckBox.IsChecked == true;
        PrefixBox.IsEnabled = !disabled;
        WarningBlock.Visibility = disabled ? Visibility.Visible : Visibility.Collapsed;
        if (!disabled)
        {
            PrefixBox.Focus();
            PrefixBox.SelectAll();
        }
        else
        {
            ErrorBlock.Visibility = Visibility.Collapsed;
        }
        IsPrefixDisabled = disabled;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        ErrorBlock.Visibility = Visibility.Collapsed;
        NormalizedPrefix = PrefixBox.Text.Trim();
        if (IsPrefixDisabled)
        {
            IsConfirmed = true;
            Close();
            return;
        }

        if (string.IsNullOrWhiteSpace(NormalizedPrefix))
        {
            ShowError("プレフィックスを入力してください。");
            return;
        }

        if (!KeyChordParser.TryParsePrefix(NormalizedPrefix, out var chord, out var error))
        {
            ShowError(error ?? "キーの書式が正しくありません。");
            return;
        }

        NormalizedPrefix = chord.NormalizedString;
        IsConfirmed = true;
        Close();
    }

    private void ShowError(string message)
    {
        ErrorBlock.Text = message;
        ErrorBlock.Visibility = Visibility.Visible;
    }
}
