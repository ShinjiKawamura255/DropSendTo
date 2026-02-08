using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using DropSendTo.Models;

namespace DropSendTo;

internal partial class SlotShortcutListWindow : Window
{
    public event EventHandler<SlotShortcutInfo>? EditRequested;
    public event EventHandler<SlotShortcutInfo>? ShortcutEditRequested;

    public SlotShortcutListWindow()
    {
        InitializeComponent();
    }

    public void SetEntries(IEnumerable<SlotShortcutInfo> entries)
    {
        var items = entries?.ToList() ?? new List<SlotShortcutInfo>();
        var view = CollectionViewSource.GetDefaultView(items);
        if (view.SortDescriptions.Count == 0)
        {
            view.SortDescriptions.Add(new SortDescription(nameof(SlotShortcutInfo.SlotSortKey), ListSortDirection.Ascending));
        }
        ShortcutGrid.ItemsSource = view;
        bool hasItems = items.Count > 0;
        ShortcutGrid.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        UpdateActionState();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateActionState();
    }

    private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TryGetSelectedEntry(out var entry))
        {
            EditRequested?.Invoke(this, entry);
        }
    }

    private void OnEditClicked(object sender, RoutedEventArgs e)
    {
        if (TryGetSelectedEntry(out var entry))
        {
            EditRequested?.Invoke(this, entry);
        }
    }

    private void OnShortcutEditClicked(object sender, RoutedEventArgs e)
    {
        if (TryGetSelectedEntry(out var entry))
        {
            ShortcutEditRequested?.Invoke(this, entry);
        }
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void UpdateActionState()
    {
        bool enabled = ShortcutGrid?.SelectedItems?.Count == 1;
        if (EditButton != null)
        {
            EditButton.IsEnabled = enabled;
        }
        if (ShortcutEditButton != null)
        {
            ShortcutEditButton.IsEnabled = enabled;
        }
    }

    private bool TryGetSelectedEntry(out SlotShortcutInfo entry)
    {
        entry = null!;
        if (ShortcutGrid?.SelectedItems?.Count != 1)
        {
            return false;
        }

        if (ShortcutGrid.SelectedItem is not SlotShortcutInfo info)
        {
            return false;
        }

        entry = info;
        return true;
    }
}
