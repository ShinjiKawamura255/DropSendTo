using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace DropSendTo.Services;

internal static class ScreenBoundsResolver
{
    private const uint MonitorDefaultToNearest = 0x00000002;

    public static ScreenBounds ForWindow(Window window)
    {
        if (window == null) throw new ArgumentNullException(nameof(window));
        var rect = CreateWindowRect(window, window.Left, window.Top);
        return ForRect(window, rect);
    }

    public static ScreenBounds ForRect(Visual reference, Rect rect)
    {
        if (reference == null) throw new ArgumentNullException(nameof(reference));
        var normalized = NormalizeRect(rect);
        var screens = EnumerateScreens();
        if (screens.Count == 0)
        {
            return new ScreenBounds(normalized.Left, normalized.Top, normalized.Width, normalized.Height);
        }

        return SelectBounds(normalized, screens);
    }

    public static ScreenBounds SelectBounds(Rect rect, IReadOnlyList<ScreenBounds> screens)
    {
        if (screens == null) throw new ArgumentNullException(nameof(screens));
        if (screens.Count == 0)
        {
            return new ScreenBounds(rect.Left, rect.Top, rect.Width, rect.Height);
        }

        var normalized = NormalizeRect(rect);
        ScreenBounds bestBounds = screens[0];
        double bestArea = -1;

        for (int i = 0; i < screens.Count; i++)
        {
            var candidate = screens[i];
            var candidateRect = new Rect(candidate.Left, candidate.Top, candidate.Width, candidate.Height);
            candidateRect.Intersect(normalized);

            if (candidateRect.IsEmpty) continue;

            double area = candidateRect.Width * candidateRect.Height;
            if (area > bestArea)
            {
                bestArea = area;
                bestBounds = screens[i];
            }
        }

        if (bestArea >= 0)
        {
            return bestBounds;
        }

        var center = new System.Windows.Point(normalized.Left + normalized.Width / 2, normalized.Top + normalized.Height / 2);
        double bestDistance = double.MaxValue;
        foreach (var candidate in screens)
        {
            var candidateCenter = new System.Windows.Point(candidate.Left + candidate.Width / 2, candidate.Top + candidate.Height / 2);
            double dx = candidateCenter.X - center.X;
            double dy = candidateCenter.Y - center.Y;
            double distance = dx * dx + dy * dy;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestBounds = candidate;
            }
        }

        return bestBounds;
    }

    private static Rect CreateWindowRect(Window window, double left, double top)
    {
        double width = window.Width;
        if (double.IsNaN(width) || width <= 0)
        {
            width = window.ActualWidth;
        }
        if (width <= 0)
        {
            width = Math.Max(window.MinWidth, 1);
        }

        double height = window.Height;
        if (double.IsNaN(height) || height <= 0)
        {
            height = window.ActualHeight;
        }
        if (height <= 0)
        {
            height = Math.Max(window.MinHeight, 1);
        }

        return new Rect(left, top, width, height);
    }

    private static Rect NormalizeRect(Rect rect)
    {
        double left = NormalizeCoordinate(rect.Left);
        double top = NormalizeCoordinate(rect.Top);
        double width = NormalizeLength(rect.Width);
        double height = NormalizeLength(rect.Height);
        return new Rect(left, top, width, height);
    }

    private static double NormalizeCoordinate(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }
        return value;
    }

    private static double NormalizeLength(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
        {
            return 1;
        }
        return value;
    }

    private static List<ScreenBounds> EnumerateScreens()
    {
        return Forms.Screen.AllScreens.Select(ToBounds).ToList();
    }

    private static ScreenBounds ToBounds(Forms.Screen screen)
    {
        var (scaleX, scaleY) = GetScaleForScreen(screen);
        if (scaleX <= 0) scaleX = 1;
        if (scaleY <= 0) scaleY = 1;

        double left = screen.Bounds.Left / scaleX;
        double top = screen.Bounds.Top / scaleY;
        double width = screen.Bounds.Width / scaleX;
        double height = screen.Bounds.Height / scaleY;

        return new ScreenBounds(left, top, width, height);
    }

    private static (double scaleX, double scaleY) GetScaleForScreen(Forms.Screen screen)
    {
        var center = new NativeMethods.NativePoint
        {
            X = screen.Bounds.Left + screen.Bounds.Width / 2,
            Y = screen.Bounds.Top + screen.Bounds.Height / 2
        };

        IntPtr monitor = NativeMethods.MonitorFromPoint(center, MonitorDefaultToNearest);
        if (monitor != IntPtr.Zero && NativeMethods.TryGetMonitorScale(monitor, out double scaleX, out double scaleY))
        {
            return (scaleX, scaleY);
        }

        return (1d, 1d);
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct NativePoint
        {
            public int X;
            public int Y;
        }

        private enum MonitorDpiType
        {
            EffectiveDpi = 0,
            AngularDpi = 1,
            RawDpi = 2
        }

        [DllImport("user32.dll")]
        internal static extern IntPtr MonitorFromPoint(NativePoint pt, uint dwFlags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

        internal static bool TryGetMonitorScale(IntPtr monitor, out double scaleX, out double scaleY)
        {
            scaleX = 1d;
            scaleY = 1d;
            if (monitor == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                int result = GetDpiForMonitor(monitor, MonitorDpiType.EffectiveDpi, out uint dpiX, out uint dpiY);
                if (result == 0)
                {
                    scaleX = dpiX / 96.0;
                    scaleY = dpiY / 96.0;
                    return true;
                }
            }
            catch (DllNotFoundException)
            {
                // Windows 7 など shcore.dll が存在しない環境では既定 DPI (96) とみなす。
            }
            catch (EntryPointNotFoundException)
            {
                // shcore.dll が古い場合のフォールバック。
            }

            return false;
        }
    }
}
