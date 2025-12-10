using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DropSendTo;

internal partial class InputPromptDialog : Window, IConfirmableDialog
{
    public InputPromptDialog(string title, string message, string? defaultValue)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        InputBox.Text = defaultValue ?? string.Empty;
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
        OkButton.IsEnabled = true;
    }

    public string InputText { get; private set; } = string.Empty;

    public bool IsConfirmed { get; private set; }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (ErrorText != null)
        {
            ErrorText.Visibility = Visibility.Collapsed;
        }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        InputText = InputBox.Text ?? string.Empty;
        IsConfirmed = true;
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        IsConfirmed = false;
        DialogResult = false;
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
        DialogResult = false;
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
}
