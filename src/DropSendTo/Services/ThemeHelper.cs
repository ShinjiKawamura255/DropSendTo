using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DropSendTo.Services;

internal static class ThemeHelper
{
    private const int DarkModeBefore20H1 = 19;
    private const int DarkModeSince20H1 = 20;

    public static void SetDarkTitleBar(Window window, bool useDark)
    {
        if (window == null) return;

        if (window.IsInitialized)
        {
            Apply(window, useDark);
        }
        else
        {
            window.SourceInitialized += (_, _) => Apply(window, useDark);
        }
    }

    private static void Apply(Window window, bool useDark)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;

            var useDarkValue = useDark ? 1 : 0;
            var attribute = Environment.OSVersion.Version.Build >= 18985
                ? DarkModeSince20H1
                : DarkModeBefore20H1;

            _ = DwmSetWindowAttribute(handle, attribute, ref useDarkValue, Marshal.SizeOf<int>());
        }
        catch
        {
            // Unsupported OS or DWM unavailable; ignore.
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);
}

internal static class WindowThemeExtensions
{
    public static readonly DependencyProperty UseDarkTitleBarProperty =
        DependencyProperty.RegisterAttached(
            "UseDarkTitleBar",
            typeof(bool),
            typeof(WindowThemeExtensions),
            new PropertyMetadata(false, OnUseDarkTitleBarChanged));

    public static void SetUseDarkTitleBar(DependencyObject target, bool value) =>
        target.SetValue(UseDarkTitleBarProperty, value);

    public static bool GetUseDarkTitleBar(DependencyObject target) =>
        (bool)target.GetValue(UseDarkTitleBarProperty);

    private static void OnUseDarkTitleBarChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window) return;
        if (e.NewValue is bool useDark)
        {
            ThemeHelper.SetDarkTitleBar(window, useDark);
        }
    }
}
