using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    private void UpdateButtonState()
    {
        if (OkButton != null)
        {
            OkButton.IsEnabled = SelectedOption != null;
        }
    }
}
