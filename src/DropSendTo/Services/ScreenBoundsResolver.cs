using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace DropSendTo.Services;

internal static class ScreenBoundsResolver
{
    // Cursor coordinates and the returned working area are in device pixels.
    public static ScreenBounds ForPoint(double x, double y)
    {
        var screen = Forms.Screen.FromPoint(new System.Drawing.Point(
            (int)NormalizeCoordinate(x), (int)NormalizeCoordinate(y)));
        return ToBounds(screen);
    }

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

        return SelectBounds(normalized, screens, VisualTreeHelper.GetDpi(reference));
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

    internal static ScreenBounds SelectBounds(Rect rect, IReadOnlyList<ScreenBounds> screens, DpiScale dpi)
    {
        var normalized = NormalizeRect(rect);
        var physicalRect = new Rect(normalized.Left * dpi.DpiScaleX, normalized.Top * dpi.DpiScaleY,
            normalized.Width * dpi.DpiScaleX, normalized.Height * dpi.DpiScaleY);
        var bounds = SelectBounds(physicalRect, screens);
        // A desktop has no global DIP coordinate space at mixed DPI. Convert every selected
        // screen using the reference window's scale, never the individual monitor's scale.
        return new ScreenBounds(bounds.Left / dpi.DpiScaleX, bounds.Top / dpi.DpiScaleY,
            bounds.Width / dpi.DpiScaleX, bounds.Height / dpi.DpiScaleY);
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
        var working = screen.WorkingArea;
        if (working.Width <= 0 || working.Height <= 0)
        {
            working = screen.Bounds;
        }

        return new ScreenBounds(working.Left, working.Top, working.Width, working.Height);
    }
}
