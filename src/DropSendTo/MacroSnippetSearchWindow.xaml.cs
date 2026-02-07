using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DropSendTo;

public partial class MacroSnippetSearchWindow : Window
{
    private const string GroupFilterAllLabel = "すべて";
    private readonly List<MacroSnippetEntry> _all;
    private readonly ObservableCollection<MacroSnippetEntry> _filtered = new();
    private bool _suspendFilterUpdate;

    public string? SelectedSnippet { get; private set; }
    public event EventHandler<string>? SnippetChosen;

    public MacroSnippetSearchWindow(IEnumerable<MacroSnippetEntry> snippets)
    {
        _all = snippets?.ToList() ?? new List<MacroSnippetEntry>();
        _suspendFilterUpdate = true;
        InitializeComponent();
        SnippetList.ItemsSource = _filtered;
        InitializeGroupFilterOptions();
        _suspendFilterUpdate = false;
        ApplyFilter(string.Empty);
        Loaded += (_, _) =>
        {
            SearchBox.Focus();
            if (_filtered.Count > 0)
            {
                SnippetList.SelectedIndex = 0;
            }
        };
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyFilter(SearchBox.Text);
    }

    private void OnSearchTargetChanged(object sender, RoutedEventArgs e)
    {
        if (_suspendFilterUpdate)
        {
            return;
        }

        if (GetSearchTargets() == SearchTargets.None)
        {
            _suspendFilterUpdate = true;
            if (sender is System.Windows.Controls.CheckBox checkBox)
            {
                checkBox.IsChecked = true;
            }
            _suspendFilterUpdate = false;
            return;
        }

        ApplyFilter(SearchBox.Text);
    }

    private void OnGroupFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suspendFilterUpdate)
        {
            return;
        }
        ApplyFilter(SearchBox.Text);
    }

    private void OnCommandFilterChanged(object sender, TextChangedEventArgs e)
    {
        if (_suspendFilterUpdate)
        {
            return;
        }
        ApplyFilter(SearchBox.Text);
    }

    private void OnContentFilterChanged(object sender, TextChangedEventArgs e)
    {
        if (_suspendFilterUpdate)
        {
            return;
        }
        ApplyFilter(SearchBox.Text);
    }

    private void OnInsertClicked(object sender, RoutedEventArgs e)
    {
        CommitSelection();
    }

    private void OnSnippetDoubleClick(object sender, MouseButtonEventArgs e)
    {
        CommitSelection();
    }

    private void CommitSelection()
    {
        if (SnippetList.SelectedItem is MacroSnippetEntry entry)
        {
            SelectedSnippet = entry.Content;
            SnippetChosen?.Invoke(this, entry.Content);
        }
    }

    private void ApplyFilter(string? query)
    {
        _filtered.Clear();
        var q = (query ?? string.Empty).Trim();
        bool hasQuery = q.Length > 0;
        var targets = GetSearchTargets();
        var groupFilter = (GroupFilterBox?.SelectedItem as string) ?? GroupFilterAllLabel;
        var commandFilter = (CommandFilterBox?.Text ?? string.Empty).Trim();
        var contentFilter = (ContentFilterBox?.Text ?? string.Empty).Trim();

        foreach (var entry in _all)
        {
            if (!MatchesColumnFilters(entry, groupFilter, commandFilter, contentFilter))
            {
                continue;
            }

            if (!hasQuery || Matches(entry, q, targets))
            {
                _filtered.Add(entry);
            }
        }
    }

    private static bool Matches(MacroSnippetEntry entry, string query, SearchTargets targets)
    {
        bool match = false;
        if ((targets & SearchTargets.Header) == SearchTargets.Header)
        {
            match |= entry.Header.Contains(query, StringComparison.OrdinalIgnoreCase);
        }
        if ((targets & SearchTargets.Content) == SearchTargets.Content)
        {
            match |= entry.Content.Contains(query, StringComparison.OrdinalIgnoreCase);
        }
        if ((targets & SearchTargets.Group) == SearchTargets.Group)
        {
            match |= entry.Group.Contains(query, StringComparison.OrdinalIgnoreCase);
        }
        return match;
    }

    private static bool MatchesColumnFilters(MacroSnippetEntry entry, string groupFilter, string commandFilter, string contentFilter)
    {
        if (!string.IsNullOrWhiteSpace(groupFilter) && groupFilter != GroupFilterAllLabel
            && !entry.Group.Equals(groupFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(commandFilter)
            && !entry.Header.Contains(commandFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(contentFilter)
            && !entry.Content.Contains(contentFilter, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (IsInteractiveElement(e.OriginalSource)) return;
        DragMove();
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

    private void InitializeGroupFilterOptions()
    {
        if (GroupFilterBox == null)
        {
            return;
        }

        _suspendFilterUpdate = true;
        GroupFilterBox.Items.Clear();
        GroupFilterBox.Items.Add(GroupFilterAllLabel);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _all)
        {
            if (seen.Add(entry.Group))
            {
                GroupFilterBox.Items.Add(entry.Group);
            }
        }

        GroupFilterBox.SelectedIndex = 0;
        _suspendFilterUpdate = false;
    }

    private SearchTargets GetSearchTargets()
    {
        SearchTargets targets = SearchTargets.None;
        if (SearchGroupCheck?.IsChecked == true)
        {
            targets |= SearchTargets.Group;
        }
        if (SearchHeaderCheck?.IsChecked == true)
        {
            targets |= SearchTargets.Header;
        }
        if (SearchContentCheck?.IsChecked == true)
        {
            targets |= SearchTargets.Content;
        }
        return targets;
    }

    [Flags]
    private enum SearchTargets
    {
        None = 0,
        Group = 1,
        Header = 2,
        Content = 4
    }
}
