using System;
using System.Windows;
using System.Windows.Controls;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace DropSendTo;

public partial class SearchOverlayWindow : Window
{
    public event EventHandler<string>? SearchTextChanged;
    public event EventHandler? CancelRequested;
    public event EventHandler<SlotNavigationRequestedEventArgs>? SlotNavigationRequested;

    public bool EnableEmacsNavigation { get; set; } = true;
    public NavigationDirection NavigationDirectionToSlots { get; set; } = NavigationDirection.Down;

    public bool IsInputFocused => SearchBox.IsKeyboardFocusWithin;
    private bool _suppressNavigationUntilKeyUp;
    private System.Windows.Input.Key _suppressedNavigationKey = System.Windows.Input.Key.None;
    private System.Windows.Input.ModifierKeys _suppressedNavigationModifiers = System.Windows.Input.ModifierKeys.None;

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

    private void OnSearchBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!EnableEmacsNavigation)
        {
            return;
        }

        var modifiers = System.Windows.Input.Keyboard.Modifiers;
        bool ctrlOnly = modifiers == System.Windows.Input.ModifierKeys.Control;
        if (ctrlOnly && e.Key == System.Windows.Input.Key.A)
        {
            MoveCaretTo(0);
            e.Handled = true;
        }
    }

    private void OnSearchBoxKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = System.Windows.Input.Keyboard.Modifiers;
        bool ctrlOnly = modifiers == System.Windows.Input.ModifierKeys.Control;
        bool preferLastSlot = NavigationDirectionToSlots == NavigationDirection.Up;

        if (_suppressNavigationUntilKeyUp &&
            e.Key == _suppressedNavigationKey &&
            modifiers == _suppressedNavigationModifiers)
        {
            e.Handled = true;
            return;
        }

        if (ctrlOnly && e.Key == System.Windows.Input.Key.G)
        {
            CancelRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (e.Key == System.Windows.Input.Key.Escape)
        {
            CancelRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (EnableEmacsNavigation && ctrlOnly)
        {
            switch (e.Key)
            {
                case System.Windows.Input.Key.N when NavigationDirectionToSlots == NavigationDirection.Down:
                    RequestSlotNavigation(false);
                    SuppressNavigationUntilKeyUp(e.Key, modifiers);
                    e.Handled = true;
                    return;
                case System.Windows.Input.Key.P when NavigationDirectionToSlots == NavigationDirection.Up:
                    RequestSlotNavigation(true);
                    SuppressNavigationUntilKeyUp(e.Key, modifiers);
                    e.Handled = true;
                    return;
                case System.Windows.Input.Key.F:
                    MoveCaret(1);
                    e.Handled = true;
                    return;
                case System.Windows.Input.Key.B:
                    MoveCaret(-1);
                    e.Handled = true;
                    return;
                case System.Windows.Input.Key.A:
                    MoveCaretTo(0);
                    e.Handled = true;
                    return;
                case System.Windows.Input.Key.E:
                    MoveCaretTo(SearchBox.Text?.Length ?? 0);
                    e.Handled = true;
                    return;
                case System.Windows.Input.Key.K:
                    KillLine();
                    e.Handled = true;
                    return;
                case System.Windows.Input.Key.D:
                    DeleteForward();
                    e.Handled = true;
                    return;
                case System.Windows.Input.Key.H:
                    DeleteBackward();
                    e.Handled = true;
                    return;
            }
        }

        if (e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Tab)
        {
            RequestSlotNavigation(preferLastSlot);
            e.Handled = true;
        }
    }

    private void RequestSlotNavigation(bool preferLastSlot)
    {
        SlotNavigationRequested?.Invoke(this, new SlotNavigationRequestedEventArgs(preferLastSlot));
    }

    private void OnSearchBoxKeyUp(object sender, KeyEventArgs e)
    {
        var modifiers = System.Windows.Input.Keyboard.Modifiers;
        if (_suppressNavigationUntilKeyUp &&
            e.Key == _suppressedNavigationKey &&
            modifiers == _suppressedNavigationModifiers)
        {
            _suppressNavigationUntilKeyUp = false;
            _suppressedNavigationKey = System.Windows.Input.Key.None;
            _suppressedNavigationModifiers = System.Windows.Input.ModifierKeys.None;
        }
    }

    private void MoveCaret(int delta)
    {
        int next = Math.Max(0, Math.Min(SearchBox.Text?.Length ?? 0, SearchBox.CaretIndex + delta));
        SearchBox.CaretIndex = next;
        SearchBox.SelectionStart = next;
        SearchBox.SelectionLength = 0;
    }

    private void MoveCaretTo(int index)
    {
        int next = Math.Max(0, Math.Min(SearchBox.Text?.Length ?? 0, index));
        SearchBox.CaretIndex = next;
        SearchBox.SelectionStart = next;
        SearchBox.SelectionLength = 0;
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

    public void SuppressNavigationUntilKeyUp(System.Windows.Input.Key key, System.Windows.Input.ModifierKeys modifiers)
    {
        _suppressNavigationUntilKeyUp = true;
        _suppressedNavigationKey = key;
        _suppressedNavigationModifiers = modifiers;
    }
}

public sealed class SlotNavigationRequestedEventArgs : EventArgs
{
    public SlotNavigationRequestedEventArgs(bool preferLastSlot)
    {
        PreferLastSlot = preferLastSlot;
    }

    public bool PreferLastSlot { get; }
}

public enum NavigationDirection
{
    Down = 0,
    Up = 1
}
