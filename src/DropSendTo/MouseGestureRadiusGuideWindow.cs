using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DropSendTo;

internal sealed class MouseGestureRadiusGuideWindow : Window
{
    private readonly Border _ring;
    private Matrix _fromDevice = Matrix.Identity;

    public MouseGestureRadiusGuideWindow()
    {
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        WindowStyle = WindowStyle.None;
        ShowInTaskbar = false;
        Topmost = true;
        IsHitTestVisible = false;
        ShowActivated = false;

        _ring = new Border
        {
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 0x4E, 0x9A, 0xFF)),
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, 0x4E, 0x9A, 0xFF)),
            CornerRadius = new CornerRadius(9999)
        };

        Content = _ring;
        SizeToContent = SizeToContent.WidthAndHeight;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice;
        if (transform.HasValue)
        {
            _fromDevice = transform.Value;
        }
    }

    public void Update(int radiusPixels, System.Drawing.Point screenPoint)
    {
        if (radiusPixels < 1) radiusPixels = 1;
        _ring.Width = radiusPixels * 2;
        _ring.Height = radiusPixels * 2;

        var pt = _fromDevice.Transform(new System.Windows.Point(screenPoint.X, screenPoint.Y));
        Left = pt.X - radiusPixels;
        Top = pt.Y - radiusPixels;
    }
}
