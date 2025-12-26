using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DropSendTo.Services;

namespace DropSendTo;

internal partial class MouseGestureDialog : Window, IConfirmableDialog
{
    private MouseGestureRadiusGuideWindow? _guideWindow;
    private bool _isDraggingRadius;
    private bool _isInitialized;

    internal MouseGestureOptions ResultOptions { get; private set; } = MouseGestureOptions.Default;
    public bool IsConfirmed { get; private set; }

    internal MouseGestureDialog(MouseGestureOptions options)
    {
        InitializeComponent();
        ResultOptions = (options ?? MouseGestureOptions.Default).Normalize();
        EnableCheckBox.IsChecked = ResultOptions.Enabled;
        DragMiddleClickCheckBox.IsChecked = ResultOptions.EnableDragMiddleClickShow;
        ClockwiseTurnsBox.Text = ResultOptions.ClockwiseTurnsToShow.ToString();
        CounterClockwiseTurnsBox.Text = ResultOptions.CounterClockwiseTurnsToHide.ToString();
        InvertDirectionsCheckBox.IsChecked = ResultOptions.InvertDirections;
        RequireCtrlCheckBox.IsChecked = ResultOptions.RequireCtrl;
        SuppressPresentationCheckBox.IsChecked = ResultOptions.SuppressDuringPresentation;
        MaxRadiusSlider.Value = ResultOptions.MaxRadiusPixels;
        EnforceRadiusCheckBox.IsChecked = ResultOptions.EnforceRadiusLimit;
        MaxRadiusSlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnRadiusDragStarted));
        MaxRadiusSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnRadiusDragCompleted));
        UpdateRadiusText();
        UpdateEnabledState();
        Closed += (_, _) => StopRadiusGuide();
        _isInitialized = true;
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
            EnforceRadiusCheckBox.IsChecked == true,
            (int)MaxRadiusSlider.Value,
            (int)MaxRadiusSlider.Value,
            DragMiddleClickCheckBox.IsChecked == true).Normalize();

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

    private void OnDragMiddleClickChanged(object sender, RoutedEventArgs e) => UpdateEnabledState();

    private void UpdateEnabledState()
    {
        bool gestureEnabled = EnableCheckBox.IsChecked == true;
        bool dragMiddleEnabled = DragMiddleClickCheckBox.IsChecked == true;
        bool anyEnabled = gestureEnabled || dragMiddleEnabled;
        ClockwiseTurnsBox.IsEnabled = gestureEnabled;
        CounterClockwiseTurnsBox.IsEnabled = gestureEnabled;
        InvertDirectionsCheckBox.IsEnabled = gestureEnabled;
        RequireCtrlCheckBox.IsEnabled = gestureEnabled;
        SuppressPresentationCheckBox.IsEnabled = anyEnabled;
        MaxRadiusSlider.IsEnabled = gestureEnabled && EnforceRadiusCheckBox.IsChecked == true;
        EnforceRadiusCheckBox.IsEnabled = gestureEnabled;
        if (!gestureEnabled || EnforceRadiusCheckBox.IsChecked != true)
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

    private void OnMaxRadiusChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isInitialized || MaxRadiusSlider == null) return;
        OnRadiusChanged();
    }

    private void OnRadiusChanged()
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
            RadiusValueText.Text = $"最大 {(int)MaxRadiusSlider.Value}px";
        }
    }

    private void OnRadiusDragStarted(object? sender, DragStartedEventArgs e)
    {
        if (!_isInitialized) return;
        _isDraggingRadius = true;
        if (EnforceRadiusCheckBox.IsChecked == true)
        {
            StartRadiusGuide();
            UpdateRadiusGuide();
        }
    }

    private void OnRadiusDragCompleted(object? sender, DragCompletedEventArgs e)
    {
        if (!_isInitialized) return;
        _isDraggingRadius = false;
        StopRadiusGuide();
    }

    private void StartRadiusGuide()
    {
        if (EnforceRadiusCheckBox.IsChecked != true) return;
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
        if (!_isInitialized) return;
        if (_guideWindow == null) return;
        if (TryGetCursorPosition(out var pt))
        {
            _guideWindow.Update(0, (int)MaxRadiusSlider.Value, pt);
        }
    }

    private void OnEnforceRadiusChanged(object sender, RoutedEventArgs e)
    {
        UpdateEnabledState();
    }

    private void ClampRadiusSliders()
    {
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

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (IsInteractiveElement(e.OriginalSource)) return;
        DragMove();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        Close();
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
}
