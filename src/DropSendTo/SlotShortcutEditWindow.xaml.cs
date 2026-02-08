using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using DropSendTo.Services;

namespace DropSendTo;

internal sealed class ShortcutEditResultEventArgs : EventArgs
{
    public ShortcutEditResultEventArgs(string shortcut)
    {
        Shortcut = shortcut ?? string.Empty;
    }

    public string Shortcut { get; }
}

internal partial class SlotShortcutEditWindow : Window
{
    public event EventHandler<ShortcutEditResultEventArgs>? ShortcutApplied;

    public SlotShortcutEditWindow(string slotLabel, string title, string? currentShortcut)
    {
        InitializeComponent();
        SlotText.Text = string.IsNullOrWhiteSpace(title)
            ? slotLabel
            : $"{slotLabel} / {title}";
        ShortcutBox.Text = currentShortcut ?? string.Empty;
        Loaded += (_, _) =>
        {
            ShortcutBox.Focus();
            ShortcutBox.SelectAll();
        };
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var raw = ShortcutBox.Text ?? string.Empty;
        var trimmed = raw.Trim();
        if (trimmed.Length > 0)
        {
            if (!ShortcutSequenceParser.TryParse(trimmed, out var sequence, out var error))
            {
                ShowError(error ?? "ショートカットの書式が正しくありません。");
                return;
            }
            trimmed = sequence.NormalizedString;
        }

        ErrorText.Visibility = Visibility.Collapsed;
        ShortcutApplied?.Invoke(this, new ShortcutEditResultEventArgs(trimmed));
        Close();
    }

    private void OnShortcutChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (ErrorText != null)
        {
            ErrorText.Visibility = Visibility.Collapsed;
        }
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
        Close();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
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
}
