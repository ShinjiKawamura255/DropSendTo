using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DropSendTo;

public partial class LayerNamesDialog : Window, IConfirmableDialog
{
    private readonly List<LayerNameEntry> _entries = new();

    public IReadOnlyList<string> LayerNames => _entries.Select(e => e.Name ?? string.Empty).ToList();
    public bool IsConfirmed { get; private set; }

    public LayerNamesDialog(IEnumerable<string>? initialNames)
    {
        InitializeComponent();
        var names = initialNames?.ToArray() ?? Array.Empty<string>();
        for (int i = 0; i < names.Length; i++)
        {
            _entries.Add(new LayerNameEntry(i, names[i] ?? string.Empty));
        }
        LayerItems.ItemsSource = _entries;
        Loaded += (_, _) => FocusFirstTextBox();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        foreach (var entry in _entries)
        {
            entry.Name = entry.Name?.Trim() ?? string.Empty;
        }
        IsConfirmed = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
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

    private static System.Windows.Controls.TextBox? FindTextBox(DependencyObject root)
    {
        if (root is System.Windows.Controls.TextBox tb) return tb;
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var found = FindTextBox(VisualTreeHelper.GetChild(root, i));
            if (found != null) return found;
        }
        return null;
    }

    private void FocusFirstTextBox()
    {
        if (_entries.Count == 0) return;
        if (LayerItems.ItemContainerGenerator.ContainerFromIndex(0) is DependencyObject container)
        {
            var tb = FindTextBox(container);
            if (tb != null)
            {
                tb.Focus();
                tb.SelectAll();
            }
        }
    }

    private sealed class LayerNameEntry
    {
        public LayerNameEntry(int index, string name)
        {
            Index = index;
            Name = name;
        }

        public int Index { get; }
        public string Label => $"Layer {Index + 1}";
        public string Name { get; set; } = string.Empty;
    }
}
