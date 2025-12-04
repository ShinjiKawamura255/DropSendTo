using System;
using System.Windows;
using System.Windows.Controls;
using InputKey = System.Windows.Input.Key;
using InputModifiers = System.Windows.Input.ModifierKeys;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace DropSendTo;

public partial class SearchOverlayWindow : Window
{
    public event EventHandler<string>? SearchTextChanged;
    public event EventHandler? CancelRequested;
    public event EventHandler? SlotNavigationRequested;

    public SearchOverlayWindow()
    {
        InitializeComponent();
    }

    public string Query
    {
        get => SearchBox.Text;
        set
        {
            if (!string.Equals(SearchBox.Text, value, StringComparison.Ordinal))
            {
                SearchBox.Text = value;
            }
        }
    }

    public void FocusInput(bool selectAll)
    {
        Activate();
        SearchBox.Focus();
        if (selectAll)
        {
            SearchBox.SelectAll();
        }
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        SearchTextChanged?.Invoke(this, SearchBox.Text);
    }

    private void OnSearchBoxKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = System.Windows.Input.Keyboard.Modifiers;

        if (modifiers == InputModifiers.Control && e.Key == InputKey.G)
        {
            CancelRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (e.Key == InputKey.Escape)
        {
            CancelRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (e.Key == InputKey.Enter || e.Key == InputKey.Tab)
        {
            SlotNavigationRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }
}
