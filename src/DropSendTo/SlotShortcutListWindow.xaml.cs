using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using DropSendTo.Models;

namespace DropSendTo;

internal partial class SlotShortcutListWindow : Window
{
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
            view.SortDescriptions.Add(new SortDescription(nameof(SlotShortcutInfo.SlotId), ListSortDirection.Ascending));
        }
        ShortcutGrid.ItemsSource = view;
        bool hasItems = items.Count > 0;
        ShortcutGrid.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
