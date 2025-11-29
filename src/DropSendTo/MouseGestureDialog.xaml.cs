using System.Linq;
using System.Windows;
using System.Windows.Input;
using DropSendTo.Services;

namespace DropSendTo;

internal partial class MouseGestureDialog : Window, IConfirmableDialog
{
    internal MouseGestureOptions ResultOptions { get; private set; } = MouseGestureOptions.Default;
    public bool IsConfirmed { get; private set; }

    internal MouseGestureDialog(MouseGestureOptions options)
    {
        InitializeComponent();
        ResultOptions = options.Normalize();
        EnableCheckBox.IsChecked = ResultOptions.Enabled;
        ClockwiseTurnsBox.Text = ResultOptions.ClockwiseTurnsToShow.ToString();
        CounterClockwiseTurnsBox.Text = ResultOptions.CounterClockwiseTurnsToHide.ToString();
        InvertDirectionsCheckBox.IsChecked = ResultOptions.InvertDirections;
        RequireCtrlCheckBox.IsChecked = ResultOptions.RequireCtrl;
        SuppressPresentationCheckBox.IsChecked = ResultOptions.SuppressDuringPresentation;
        UpdateEnabledState();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        ErrorBlock.Visibility = Visibility.Collapsed;

        if (!TryParseTurns(ClockwiseTurnsBox.Text, out var clockwise))
        {
            ShowError("表示の回数を 1 以上の数字で入力してください。");
            return;
        }

        if (!TryParseTurns(CounterClockwiseTurnsBox.Text, out var counterClockwise))
        {
            ShowError("非表示の回数を 1 以上の数字で入力してください。");
            return;
        }

        ResultOptions = new MouseGestureOptions(
            EnableCheckBox.IsChecked == true,
            clockwise,
            counterClockwise,
            InvertDirectionsCheckBox.IsChecked == true,
            RequireCtrlCheckBox.IsChecked == true,
            SuppressPresentationCheckBox.IsChecked == true).Normalize();

        IsConfirmed = true;
        Close();
    }

    private static bool TryParseTurns(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        return int.TryParse(text.Trim(), out value) && value > 0;
    }

    private void OnEnableChanged(object sender, RoutedEventArgs e) => UpdateEnabledState();

    private void UpdateEnabledState()
    {
        bool enabled = EnableCheckBox.IsChecked == true;
        ClockwiseTurnsBox.IsEnabled = enabled;
        CounterClockwiseTurnsBox.IsEnabled = enabled;
        InvertDirectionsCheckBox.IsEnabled = enabled;
        RequireCtrlCheckBox.IsEnabled = enabled;
        SuppressPresentationCheckBox.IsEnabled = enabled;
    }

    private void OnTurnsPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !IsDigitsOnly(e.Text);
    }

    private static bool IsDigitsOnly(string input) => !string.IsNullOrEmpty(input) && input.All(char.IsDigit);

    private void ShowError(string message)
    {
        ErrorBlock.Text = message;
        ErrorBlock.Visibility = Visibility.Visible;
    }
}
