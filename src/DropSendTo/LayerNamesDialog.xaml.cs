using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace DropSendTo;

public partial class LayerNamesDialog : Window, IConfirmableDialog
{
    private readonly string[] _names = { string.Empty, string.Empty, string.Empty, string.Empty };

    public IReadOnlyList<string> LayerNames => _names;
    public bool IsConfirmed { get; private set; }

    public LayerNamesDialog(IEnumerable<string>? initialNames)
    {
        InitializeComponent();
        var names = initialNames?.ToArray() ?? Array.Empty<string>();
        Layer1Box.Text = names.Length > 0 ? names[0] : string.Empty;
        Layer2Box.Text = names.Length > 1 ? names[1] : string.Empty;
        Layer3Box.Text = names.Length > 2 ? names[2] : string.Empty;
        Layer4Box.Text = names.Length > 3 ? names[3] : string.Empty;
        Layer1Box.Focus();
        Layer1Box.SelectAll();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        _names[0] = Layer1Box.Text?.Trim() ?? string.Empty;
        _names[1] = Layer2Box.Text?.Trim() ?? string.Empty;
        _names[2] = Layer3Box.Text?.Trim() ?? string.Empty;
        _names[3] = Layer4Box.Text?.Trim() ?? string.Empty;
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
}
