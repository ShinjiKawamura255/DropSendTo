using System;
using System.Windows;

namespace DropSendTo.Services;

internal static class WindowCascadeService
{
    private static readonly object SyncRoot = new();
    private static System.Windows.Point? _lastPosition;
    private const double OffsetX = 28;
    private const double OffsetY = 28;
    private static readonly WindowPlacementService Placement = new();

    public static void Arrange(Window window, Window? owner)
    {
        if (window == null) return;
        window.Loaded += OnLoaded;

        void OnLoaded(object? sender, RoutedEventArgs e)
        {
            window.Loaded -= OnLoaded;
            Apply(window, owner);
        }
    }

    private static void Apply(Window window, Window? owner)
    {
        lock (SyncRoot)
        {
            double width = GetWindowWidth(window);
            double height = GetWindowHeight(window);

            var bounds = new ScreenBounds(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);

            double left;
            double top;

            if (_lastPosition.HasValue)
            {
                left = _lastPosition.Value.X + OffsetX;
                top = _lastPosition.Value.Y + OffsetY;
            }
            else if (owner != null)
            {
                left = owner.Left + OffsetX;
                top = owner.Top + OffsetY;
            }
            else
            {
                var workArea = SystemParameters.WorkArea;
                left = workArea.Left + Math.Max(0, (workArea.Width - width) / 2);
                top = workArea.Top + Math.Max(0, (workArea.Height - height) / 2);
            }

            (left, top) = Placement.Clamp(left, top, bounds, width, height);
            window.Left = left;
            window.Top = top;
            _lastPosition = new System.Windows.Point(left, top);
        }
    }

    private static double GetWindowWidth(Window window)
    {
        if (!double.IsNaN(window.Width) && window.Width > 0) return window.Width;
        if (window.ActualWidth > 0) return window.ActualWidth;
        return Math.Max(320, window.MinWidth);
    }

    private static double GetWindowHeight(Window window)
    {
        if (!double.IsNaN(window.Height) && window.Height > 0) return window.Height;
        if (window.ActualHeight > 0) return window.ActualHeight;
        return Math.Max(220, window.MinHeight);
    }
}
