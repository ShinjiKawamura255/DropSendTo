using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DropSendTo.Services;

internal static class OwnedWindowPlacementService
{
    private static readonly WindowPlacementService Placement = new();

    internal static bool Place(Window owner, Window overlay)
    {
        var ownerHandle = new WindowInteropHelper(owner).EnsureHandle();
        var overlayHandle = new WindowInteropHelper(overlay).EnsureHandle();
        if (!GetWindowRect(ownerHandle, out var nativeRect))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        var ownerRect = new Rect(nativeRect.Left, nativeRect.Top,
            nativeRect.Right - nativeRect.Left, nativeRect.Bottom - nativeRect.Top);
        var bounds = ScreenBoundsResolver.ForWindowPixels(owner);
        bool placedBelow = false;
        for (int pass = 0; pass < 2; pass++)
        {
            overlay.InvalidateMeasure();
            overlay.Measure(new Size(overlay.MaxWidth, double.PositiveInfinity));
            overlay.Width = overlay.DesiredSize.Width;
            overlay.Height = overlay.DesiredSize.Height;
            overlay.UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(overlay);
            var result = CalculatePosition(ownerRect, new Size(overlay.Width, overlay.Height), bounds,
                VisualTreeHelper.GetDpi(owner), dpi);
            placedBelow = result.PlacedBelow;
            // Owner and overlay can have different DPIs during a monitor transition.
            // Move both axes atomically in physical pixels; never copy owner DIPs.
            if (!SetWindowPos(overlayHandle, IntPtr.Zero, (int)Math.Round(result.Position.X),
                (int)Math.Round(result.Position.Y), 0, 0, 0x0001 | 0x0004 | 0x0010))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var updatedDpi = VisualTreeHelper.GetDpi(overlay);
            if (dpi.DpiScaleX == updatedDpi.DpiScaleX && dpi.DpiScaleY == updatedDpi.DpiScaleY) break;
            // WM_DPICHANGED may resize and remeasure the overlay on its destination.
        }
        return placedBelow;
    }

    internal static (Point Position, bool PlacedBelow) CalculatePosition(Rect ownerRect, Size overlaySize,
        ScreenBounds bounds, DpiScale ownerDpi, DpiScale overlayDpi)
    {
        double width = overlaySize.Width * overlayDpi.DpiScaleX;
        double height = overlaySize.Height * overlayDpi.DpiScaleY;
        double gap = 10 * ownerDpi.DpiScaleY;
        double left = ownerRect.Left + (ownerRect.Width - width) / 2;
        double top = ownerRect.Top - height - gap;
        bool placedBelow = top < bounds.Top;
        if (placedBelow) top = ownerRect.Bottom + gap;
        var clamped = Placement.Clamp(left, top, bounds, width, height);
        return (new Point(clamped.left, clamped.top), placedBelow);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y,
        int width, int height, uint flags);
}
