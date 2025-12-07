using System.Windows;
using System.Windows.Input;

namespace DropSendTo;

public partial class MacroTipsWindow : Window
{
    public MacroTipsWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
        e.Handled = true;
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }
}
