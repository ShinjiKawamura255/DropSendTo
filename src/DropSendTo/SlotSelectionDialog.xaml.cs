using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DropSendTo.Models;

namespace DropSendTo;

internal partial class SlotSelectionDialog : Window, IConfirmableDialog
{
    private readonly List<SlotSelectionOption> _options;

    public bool IsConfirmed { get; private set; }

    public SlotSelectionDialog(IEnumerable<SlotSelectionOption> options, string title)
    {
        InitializeComponent();
        Title = title;
        _options = options?.ToList() ?? new List<SlotSelectionOption>();
        SlotList.ItemsSource = _options;
        if (_options.Count > 0)
        {
            SlotList.SelectedIndex = 0;
        }
        UpdateButtonState();
    }

    public SlotSelectionOption? SelectedOption => SlotList.SelectedItem as SlotSelectionOption;

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (SelectedOption == null)
        {
            return;
        }
        IsConfirmed = true;
        Close();
    }

    private void OnSlotDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedOption == null) return;
        IsConfirmed = true;
        Close();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateButtonState();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        Close();
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

    private void UpdateButtonState()
    {
        if (OkButton != null)
        {
            OkButton.IsEnabled = SelectedOption != null;
        }
    }
}
