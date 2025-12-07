using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DropSendTo.Services;

internal static class ThemeHelper
{
    private const int DarkModeBefore20H1 = 19;
    private const int DarkModeSince20H1 = 20;

    public static void EnableDarkTitleBar(Window window)
    {
        if (window == null) return;

        if (window.IsInitialized)
        {
            Apply(window);
        }
        else
        {
            window.SourceInitialized += (_, _) => Apply(window);
        }
    }

    private static void Apply(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;

            var useDark = 1;
            var attribute = Environment.OSVersion.Version.Build >= 18985
                ? DarkModeSince20H1
                : DarkModeBefore20H1;

            _ = DwmSetWindowAttribute(handle, attribute, ref useDark, Marshal.SizeOf<int>());
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
        if (e.NewValue is bool useDark && useDark)
        {
            ThemeHelper.EnableDarkTitleBar(window);
        }
    }
}
