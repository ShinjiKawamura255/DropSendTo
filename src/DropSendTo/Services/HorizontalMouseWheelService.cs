using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace DropSendTo.Services;

internal sealed class HorizontalMouseWheelService : IDisposable
{
    private const int WM_MOUSEHWHEEL = 0x020E;
    private const int WHEEL_DELTA = 120;
    private const uint SPI_GETWHEELSCROLLCHARS = 0x006C;

    private bool _disposed;
    private bool _hookAttached;
    private int _scrollCharsPerNotch;

    public void Start()
    {
        if (_disposed || _hookAttached)
        {
            return;
        }

        _scrollCharsPerNotch = GetHorizontalScrollChars();
        ComponentDispatcher.ThreadPreprocessMessage += OnThreadPreprocessMessage;
        _hookAttached = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_hookAttached)
        {
            ComponentDispatcher.ThreadPreprocessMessage -= OnThreadPreprocessMessage;
            _hookAttached = false;
        }

        _disposed = true;
    }

    private void OnThreadPreprocessMessage(ref MSG msg, ref bool handled)
    {
        if (handled || msg.message != WM_MOUSEHWHEEL)
        {
            return;
        }

        int delta = unchecked((short)(((long)msg.wParam >> 16) & 0xFFFF));
        if (delta == 0)
        {
            return;
        }

        if (TryScroll(msg.hwnd, delta))
        {
            handled = true;
        }
    }

    private bool TryScroll(IntPtr hwnd, int delta)
    {
        var target = Mouse.DirectlyOver as DependencyObject ?? HitTestFromWindowHandle(hwnd);
        var scrollViewer = FindScrollableViewer(target);
        if (scrollViewer == null)
        {
            return false;
        }

        if (scrollViewer.ComputedHorizontalScrollBarVisibility != Visibility.Visible
            && scrollViewer.ScrollableWidth <= 0)
        {
            return false;
        }

        int detents = delta / WHEEL_DELTA;
        if (detents == 0)
        {
            detents = Math.Sign(delta);
        }

        int scrollUnits = _scrollCharsPerNotch;
        if (scrollUnits == 0)
        {
            return false;
        }

        if (scrollUnits < 0)
        {
            for (int i = 0; i < Math.Abs(detents); i++)
            {
                if (detents > 0)
                {
                    scrollViewer.PageRight();
                }
                else
                {
                    scrollViewer.PageLeft();
                }
            }
            return true;
        }

        int steps = Math.Max(1, Math.Abs(detents) * scrollUnits);
        for (int i = 0; i < steps; i++)
        {
            if (detents > 0)
            {
                scrollViewer.LineRight();
            }
            else
            {
                scrollViewer.LineLeft();
            }
        }

        return true;
    }

    private static DependencyObject? HitTestFromWindowHandle(IntPtr hwnd)
    {
        var source = HwndSource.FromHwnd(hwnd);
        if (source?.RootVisual is not DependencyObject root)
        {
            return null;
        }

        if (root is not IInputElement inputRoot || root is not Visual visualRoot)
        {
            return null;
        }

        var point = Mouse.GetPosition(inputRoot);
        var result = VisualTreeHelper.HitTest(visualRoot, point);
        return result?.VisualHit;
    }

    private static ScrollViewer? FindScrollableViewer(DependencyObject? node)
    {
        ScrollViewer? firstEncountered = null;
        while (node != null)
        {
            if (node is ScrollViewer scrollViewer)
            {
                firstEncountered ??= scrollViewer;
                if (scrollViewer.ComputedHorizontalScrollBarVisibility == Visibility.Visible
                    || scrollViewer.ScrollableWidth > 0)
                {
                    return scrollViewer;
                }
            }
            node = GetParent(node);
        }

        return firstEncountered;
    }

    private static DependencyObject? GetParent(DependencyObject node)
    {
        return node switch
        {
            Visual or Visual3D => VisualTreeHelper.GetParent(node),
            FrameworkContentElement fce => fce.Parent,
            ContentElement ce => ContentOperations.GetParent(ce),
            _ => null
        };
    }

    private static int GetHorizontalScrollChars()
    {
        if (NativeMethods.SystemParametersInfo(SPI_GETWHEELSCROLLCHARS, 0, out uint chars, 0))
        {
            if (chars == uint.MaxValue)
            {
                return -1;
            }

            return (int)chars;
        }

        return 3;
    }

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out uint pvParam, uint fWinIni);
    }
}
