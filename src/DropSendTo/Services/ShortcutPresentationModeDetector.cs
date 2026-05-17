using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DropSendTo.Services;

internal static class ShortcutPresentationModeDetector
{
    public static bool IsPresentationModeLikelyActive()
    {
        try
        {
            if (SHQueryUserNotificationState(out var state) == 0
                && (state == UserNotificationState.QunsPresentationMode
                    || state == UserNotificationState.QunsRunningD3DFullScreen))
            {
                return true;
            }
        }
        catch
        {
            // Shell API unavailable; fall back to window heuristics.
        }

        try
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return false;
            }

            return IsPowerPointSlideShow(foreground);
        }
        catch
        {
            // Heuristic failed; treat as non-presentation to avoid false positives.
            return false;
        }
    }

    public static bool IsPowerPointSlideShow(string? processName, string? className, string? title)
    {
        if (!string.Equals(processName, "powerpnt", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(className)
            && (className.Contains("screenclass", StringComparison.OrdinalIgnoreCase)
                || className.Contains("fullscreen", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (string.IsNullOrEmpty(title))
        {
            return false;
        }

        return title.Contains("slide show", StringComparison.OrdinalIgnoreCase)
               || title.Contains("スライド ショー", StringComparison.OrdinalIgnoreCase)
               || title.Contains("スライドショー", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPowerPointSlideShow(IntPtr hwnd)
    {
        return IsPowerPointSlideShow(
            GetProcessNameFromWindow(hwnd),
            GetWindowClassName(hwnd),
            GetWindowTitle(hwnd));
    }

    private static string GetProcessNameFromWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return string.Empty;
        }

        try
        {
            _ = GetWindowThreadProcessId(hwnd, out var pid);
            using var proc = Process.GetProcessById((int)pid);
            return proc.ProcessName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetWindowClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        try
        {
            if (GetClassName(hwnd, sb, sb.Capacity) > 0)
            {
                return sb.ToString();
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(512);
        try
        {
            if (GetWindowText(hwnd, sb, sb.Capacity) > 0)
            {
                return sb.ToString();
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private enum UserNotificationState
    {
        QunsNotPresent = 1,
        QunsBusy = 2,
        QunsRunningD3DFullScreen = 3,
        QunsPresentationMode = 4,
        QunsAcceptsNotifications = 5,
        QunsQuietTime = 6,
        QunsApp = 7
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out UserNotificationState pstate);
}
