using System.Windows;

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
}
