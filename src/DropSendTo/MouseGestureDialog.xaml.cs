using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DropSendTo.Services;

namespace DropSendTo;

internal partial class MouseGestureDialog : Window, IConfirmableDialog
{
    private MouseGestureRadiusGuideWindow? _guideWindow;
    private bool _isDraggingRadius;

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
        RadiusSlider.Value = ResultOptions.RadiusPixels;
        RadiusSlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnRadiusDragStarted));
        RadiusSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnRadiusDragCompleted));
        UpdateRadiusText();
        UpdateEnabledState();
        Closed += (_, _) => StopRadiusGuide();
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
            SuppressPresentationCheckBox.IsChecked == true,
            (int)RadiusSlider.Value).Normalize();

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
        RadiusSlider.IsEnabled = enabled;
        if (!enabled)
        {
            StopRadiusGuide();
        }
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

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        Close();
    }

    private void OnRadiusChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateRadiusText();
        if (_isDraggingRadius)
        {
            UpdateRadiusGuide();
        }
    }

    private void UpdateRadiusText()
    {
        if (RadiusValueText != null)
        {
            RadiusValueText.Text = $"{(int)RadiusSlider.Value} px";
        }
    }

    private void OnRadiusDragStarted(object? sender, DragStartedEventArgs e)
    {
        _isDraggingRadius = true;
        StartRadiusGuide();
        UpdateRadiusGuide();
    }

    private void OnRadiusDragCompleted(object? sender, DragCompletedEventArgs e)
    {
        _isDraggingRadius = false;
        StopRadiusGuide();
    }

    private void StartRadiusGuide()
    {
        _guideWindow ??= new MouseGestureRadiusGuideWindow();
        _guideWindow.Show();
    }

    private void StopRadiusGuide()
    {
        if (_guideWindow != null)
        {
            _guideWindow.Close();
            _guideWindow = null;
        }
    }

    private void UpdateRadiusGuide()
    {
        if (_guideWindow == null) return;
        if (TryGetCursorPosition(out var pt))
        {
            _guideWindow.Update((int)RadiusSlider.Value, pt);
        }
    }

    private static bool TryGetCursorPosition(out System.Drawing.Point point)
    {
        if (GetCursorPos(out var p))
        {
            point = new System.Drawing.Point(p.X, p.Y);
            return true;
        }

        point = default;
        return false;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }
}
