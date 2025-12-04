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
        bool ctrlOnly = modifiers == InputModifiers.Control;

        if (ctrlOnly && e.Key == InputKey.G)
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

        if (ctrlOnly)
        {
            switch (e.Key)
            {
                case InputKey.F:
                    MoveCaret(1);
                    e.Handled = true;
                    return;
                case InputKey.B:
                    MoveCaret(-1);
                    e.Handled = true;
                    return;
                case InputKey.A:
                    MoveCaretTo(0);
                    e.Handled = true;
                    return;
                case InputKey.E:
                    MoveCaretTo(SearchBox.Text?.Length ?? 0);
                    e.Handled = true;
                    return;
                case InputKey.K:
                    KillLine();
                    e.Handled = true;
                    return;
                case InputKey.D:
                    DeleteForward();
                    e.Handled = true;
                    return;
                case InputKey.H:
                    DeleteBackward();
                    e.Handled = true;
                    return;
            }
        }

        if (e.Key == InputKey.Enter || e.Key == InputKey.Tab)
        {
            SlotNavigationRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void MoveCaret(int delta)
    {
        int next = Math.Max(0, Math.Min(SearchBox.Text?.Length ?? 0, SearchBox.CaretIndex + delta));
        SearchBox.CaretIndex = next;
    }

    private void MoveCaretTo(int index)
    {
        int next = Math.Max(0, Math.Min(SearchBox.Text?.Length ?? 0, index));
        SearchBox.CaretIndex = next;
    }

    private void KillLine()
    {
        var text = SearchBox.Text ?? string.Empty;
        int caret = SearchBox.CaretIndex;
        if (caret < 0 || caret > text.Length)
        {
            return;
        }
        SearchBox.Text = text.Substring(0, caret);
        SearchBox.CaretIndex = SearchBox.Text.Length;
    }

    private void DeleteForward()
    {
        var text = SearchBox.Text ?? string.Empty;
        int caret = SearchBox.CaretIndex;
        if (caret < 0 || caret >= text.Length)
        {
            return;
        }
        SearchBox.Text = text.Remove(caret, 1);
        SearchBox.CaretIndex = caret;
    }

    private void DeleteBackward()
    {
        var text = SearchBox.Text ?? string.Empty;
        int caret = SearchBox.CaretIndex;
        if (caret <= 0 || caret > text.Length)
        {
            return;
        }
        SearchBox.Text = text.Remove(caret - 1, 1);
        SearchBox.CaretIndex = caret - 1;
    }
}
