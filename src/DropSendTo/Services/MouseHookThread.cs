using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace DropSendTo.Services;

// The installing thread owns the hook and pumps messages independently of WPF rendering.
internal sealed class MouseHookThread : IDisposable
{
    internal delegate IntPtr Callback(int code, IntPtr message, IntPtr data);
    private readonly Callback _callback;
    private readonly Func<Callback, IntPtr> _install;
    private readonly Action<IntPtr> _uninstall;
    private readonly Thread _thread;
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile Dispatcher? _dispatcher;
    private int _stopping;

    internal MouseHookThread(Callback callback) : this(callback, Install, Uninstall) { }

    internal MouseHookThread(Callback callback, Func<Callback, IntPtr> install, Action<IntPtr> uninstall,
        TimeSpan? startupTimeout = null)
    {
        _callback = callback;
        _install = install;
        _uninstall = uninstall;
        _thread = new Thread(Run) { IsBackground = true, Name = "DropSendTo mouse hook" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        try
        {
            _started.Task.WaitAsync(startupTimeout ?? TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        catch
        {
            // A late installation must clean itself up even if startup already timed out.
            RequestStop();
            throw;
        }
    }

    private void Run()
    {
        IntPtr handle = IntPtr.Zero;
        try
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            handle = _install(_callback);
            if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            _started.TrySetResult();
            if (Volatile.Read(ref _stopping) == 0) Dispatcher.Run();
        }
        catch (Exception ex)
        {
            if (!_started.TrySetException(ex))
                LoggerService.Instance.Warn($"Mouse hook worker failed (thread={Environment.CurrentManagedThreadId}): {ex}");
        }
        finally
        {
            try
            {
                if (handle != IntPtr.Zero) _uninstall(handle);
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn($"Mouse hook cleanup failed (thread={Environment.CurrentManagedThreadId}): {ex}");
            }
            // Keep the native callback alive until the hook has been removed.
            GC.KeepAlive(_callback);
        }
    }

    private void RequestStop()
    {
        Interlocked.Exchange(ref _stopping, 1);
        var dispatcher = _dispatcher;
        if (dispatcher != null && !dispatcher.HasShutdownStarted)
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
    }

    public void Dispose()
    {
        RequestStop();
        if (Thread.CurrentThread != _thread && !_thread.Join(TimeSpan.FromSeconds(5)))
            LoggerService.Instance.Warn($"Mouse hook worker did not stop within 5 seconds (thread={_thread.ManagedThreadId}).");
    }

    private static IntPtr Install(Callback callback) => SetWindowsHookEx(14, callback, IntPtr.Zero, 0);
    private static void Uninstall(IntPtr handle)
    {
        if (!UnhookWindowsHookEx(handle)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, Callback callback, IntPtr module, uint threadId);
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr handle);
}
