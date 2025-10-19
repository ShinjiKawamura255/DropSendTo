using System.Windows;
using DropSendTo.Services;

namespace DropSendTo;

public partial class PrefixDialog : Window
{
    public string NormalizedPrefix { get; private set; }

    public PrefixDialog(string initialPrefix)
    {
        InitializeComponent();
        PrefixBox.Text = string.IsNullOrWhiteSpace(initialPrefix) ? "Ctrl+Q" : initialPrefix;
        PrefixBox.SelectAll();
        PrefixBox.Focus();
        NormalizedPrefix = PrefixBox.Text.Trim();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var input = PrefixBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            ShowError("プレフィックスを入力してください。");
            return;
        }

        if (!KeyChordParser.TryParse(input, out var chord, out var error))
        {
            ShowError(error ?? "キーの書式が正しくありません。");
            return;
        }

        NormalizedPrefix = chord.NormalizedString;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorBlock.Text = message;
        ErrorBlock.Visibility = Visibility.Visible;
    }
}
