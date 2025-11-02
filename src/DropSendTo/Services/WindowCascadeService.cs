using System;
using System.Windows;

namespace DropSendTo.Services;

internal static class WindowCascadeService
{
    private const double Margin = 24d;
    private const double OverlapOffset = 16d;
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
        double width = GetWindowWidth(window);
        double height = GetWindowHeight(window);

        var bounds = new ScreenBounds(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        if (owner == null)
        {
            var workArea = SystemParameters.WorkArea;
            double centeredLeft = workArea.Left + Math.Max(0, (workArea.Width - width) / 2);
            double centeredTop = workArea.Top + Math.Max(0, (workArea.Height - height) / 2);
            (centeredLeft, centeredTop) = Placement.Clamp(centeredLeft, centeredTop, bounds, width, height);
            window.Left = centeredLeft;
            window.Top = centeredTop;
            return;
        }

        double ownerWidth = GetWindowWidth(owner);
        double ownerHeight = GetWindowHeight(owner);
        double ownerLeft = owner.Left;
        double ownerTop = owner.Top;

        var candidates = new (double Left, double Top)[]
        {
            (ownerLeft + ownerWidth + Margin, ownerTop),                     // Right
            (ownerLeft - Margin - width, ownerTop),                          // Left
            (ownerLeft, ownerTop + ownerHeight + Margin),                    // Below
            (ownerLeft, ownerTop - Margin - height),                         // Above
            (ownerLeft + OverlapOffset, ownerTop + OverlapOffset)            // Slight overlap fallback
        };

        (double Left, double Top)? chosen = null;
        for (int i = 0; i < candidates.Length; i++)
        {
            var candidate = candidates[i];
            bool withinBounds = IsWithinBounds(candidate.Left, candidate.Top, width, height, bounds);
            if (!withinBounds) continue;

            bool overlaps = OverlapsOwner(candidate.Left, candidate.Top, width, height, ownerLeft, ownerTop, ownerWidth, ownerHeight);
            if (!overlaps || i == candidates.Length - 1)
            {
                chosen = candidate;
                break;
            }
        }

        if (!chosen.HasValue)
        {
            chosen = (ownerLeft + OverlapOffset, ownerTop + OverlapOffset);
        }

        var (left, top) = chosen.Value;
        if (!IsWithinBounds(left, top, width, height, bounds))
        {
            (left, top) = Placement.Clamp(left, top, bounds, width, height);
        }

        window.Left = left;
        window.Top = top;
    }

    private static bool IsWithinBounds(double left, double top, double width, double height, ScreenBounds bounds)
    {
        return left >= bounds.Left &&
               top >= bounds.Top &&
               left + width <= bounds.Right &&
               top + height <= bounds.Bottom;
    }

    private static bool OverlapsOwner(double left, double top, double width, double height, double ownerLeft, double ownerTop, double ownerWidth, double ownerHeight)
    {
        var windowRect = new Rect(left, top, width, height);
        var ownerRect = new Rect(ownerLeft, ownerTop, ownerWidth, ownerHeight);
        return windowRect.IntersectsWith(ownerRect);
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
