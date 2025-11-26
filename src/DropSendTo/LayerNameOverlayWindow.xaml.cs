using System.Windows;

namespace DropSendTo;

public partial class LayerNameOverlayWindow : Window
{
    public LayerNameOverlayWindow()
    {
        InitializeComponent();
    }

    public void SetLayerName(string layerName)
    {
        LayerNameText.Text = layerName;
    }
}
