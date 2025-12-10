using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace DropSendTo;

internal sealed record LayerCountChoice(int Value, string Label);

public partial class LayerCountDialog : Window, IConfirmableDialog
{
    private readonly List<LayerCountChoice> _choices = new();

    public bool IsConfirmed { get; private set; }
    public int SelectedCount { get; private set; }

    public LayerCountDialog(int current, int min, int max)
    {
        InitializeComponent();
        BuildChoices(min, max);
        LayerCountCombo.ItemsSource = _choices;
        LayerCountCombo.SelectedValue = Math.Clamp(current, min, max);
        SelectedCount = current;
    }

    private void BuildChoices(int min, int max)
    {
        _choices.Clear();
        for (int value = min; value <= max; value++)
        {
            _choices.Add(new LayerCountChoice(value, $"Layer {value}"));
        }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        SelectedCount = GetSelected(LayerCountCombo, SelectedCount);
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

    private static int GetSelected(Selector combo, int fallback)
    {
        if (combo.SelectedValue is int v)
        {
            return v;
        }
        return fallback;
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
}
