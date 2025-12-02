using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DropSendTo;

public partial class MacroSnippetSearchWindow : Window
{
    private readonly List<MacroSnippetEntry> _all;
    private readonly ObservableCollection<MacroSnippetEntry> _filtered = new();

    public string? SelectedSnippet { get; private set; }
    public event EventHandler<string>? SnippetChosen;

    public MacroSnippetSearchWindow(IEnumerable<MacroSnippetEntry> snippets)
    {
        InitializeComponent();
        _all = snippets?.ToList() ?? new List<MacroSnippetEntry>();
        SnippetList.ItemsSource = _filtered;
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

        foreach (var entry in _all)
        {
            if (!hasQuery || Matches(entry, q))
            {
                _filtered.Add(entry);
            }
        }
    }

    private static bool Matches(MacroSnippetEntry entry, string query)
    {
        return entry.Header.Contains(query, StringComparison.OrdinalIgnoreCase)
               || entry.Content.Contains(query, StringComparison.OrdinalIgnoreCase)
               || entry.Group.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
