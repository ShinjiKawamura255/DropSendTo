using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DropSendTo.Services;

internal enum CursorWindowPlacement
{
    MouseFollow,
    ScreenCenter,
    NearMouse
}

internal static class CursorWindowPlacementService
{
    private static readonly WindowPlacementService Placement = new();

    internal static void Place(Window window, Point cursor, CursorWindowPlacement placement)
    {
        var bounds = ScreenBoundsResolver.ForPoint(cursor.X, cursor.Y);
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }
        var handle = new WindowInteropHelper(window).EnsureHandle();

        // Move X and Y together in device coordinates. Setting WPF Left/Top separately can
        // change the window DPI between the two assignments when crossing monitors.
        for (int pass = 0; pass < 2; pass++)
        {
            var dpi = VisualTreeHelper.GetDpi(window);
            var size = new Size(GetLength(window.ActualWidth, window.Width, window.MinWidth),
                GetLength(window.ActualHeight, window.Height, window.MinHeight));
            var position = CalculatePosition(cursor, size, bounds, dpi, placement);
            if (!SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(position.X), (int)Math.Round(position.Y),
                0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var updatedDpi = VisualTreeHelper.GetDpi(window);
            if (updatedDpi.DpiScaleX == dpi.DpiScaleX && updatedDpi.DpiScaleY == dpi.DpiScaleY)
            {
                break;
            }
            // WM_DPICHANGED can resize the window on the destination monitor. Re-anchor
            // with that window's new DPI, rather than using the previous monitor's scale.
            window.UpdateLayout();
        }
    }

    internal static Point CalculatePosition(Point cursor, Size windowSize, ScreenBounds bounds,
        DpiScale dpi, CursorWindowPlacement placement)
    {
        double width = windowSize.Width * dpi.DpiScaleX;
        double height = windowSize.Height * dpi.DpiScaleY;
        double left = cursor.X - width / 2;
        double top = cursor.Y - height / 2;
        switch (placement)
        {
            case CursorWindowPlacement.ScreenCenter:
                left = bounds.Left + (bounds.Width - width) / 2;
                top = bounds.Top + (bounds.Height - height) / 2;
                break;
            case CursorWindowPlacement.NearMouse:
                double offset = 16 * dpi.DpiScaleY;
                double spaceBelow = bounds.Bottom - (cursor.Y + offset);
                double spaceAbove = (cursor.Y - offset) - bounds.Top;
                top = spaceBelow >= height || spaceBelow >= spaceAbove
                    ? cursor.Y + offset
                    : cursor.Y - offset - height;
                break;
        }

        var clamped = Placement.Clamp(left, top, bounds, width, height);
        return new Point(clamped.left, clamped.top);
    }

    private static double GetLength(double actual, double configured, double minimum)
    {
        if (double.IsFinite(actual) && actual > 0) return actual;
        if (double.IsFinite(configured) && configured > 0) return configured;
        return Math.Max(minimum, 1);
    }

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y,
        int width, int height, uint flags);
}
