using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DropSendTo;

internal sealed class MouseGestureRadiusGuideWindow : Window
{
    private readonly Border _outer;
    private readonly Border _inner;
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

        _outer = new Border
        {
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(160, 0x4E, 0x9A, 0xFF)),
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(25, 0x4E, 0x9A, 0xFF)),
            CornerRadius = new CornerRadius(9999)
        };

        _inner = new Border
        {
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(180, 0xE8, 0x6A, 0x17)),
            BorderThickness = new Thickness(2),
            Background = System.Windows.Media.Brushes.Transparent,
            CornerRadius = new CornerRadius(9999)
        };

        var grid = new Grid();
        grid.Children.Add(_outer);
        grid.Children.Add(_inner);
        Content = grid;
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

    public void Update(int minRadiusPixels, int maxRadiusPixels, System.Drawing.Point screenPoint)
    {
        minRadiusPixels = Math.Max(1, minRadiusPixels);
        maxRadiusPixels = Math.Max(minRadiusPixels, maxRadiusPixels);

        _outer.Width = maxRadiusPixels * 2;
        _outer.Height = maxRadiusPixels * 2;

        _inner.Width = minRadiusPixels * 2;
        _inner.Height = minRadiusPixels * 2;
        double offset = maxRadiusPixels - minRadiusPixels;
        _inner.Margin = new Thickness(offset);

        var pt = _fromDevice.Transform(new System.Windows.Point(screenPoint.X, screenPoint.Y));
        Left = pt.X - maxRadiusPixels;
        Top = pt.Y - maxRadiusPixels;
    }
}
