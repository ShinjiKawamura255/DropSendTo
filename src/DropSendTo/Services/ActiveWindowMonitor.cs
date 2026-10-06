using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace DropSendTo.Services;

internal interface IActiveWindowSource : IDisposable
{
    event Action<IntPtr, bool>? WindowChanged;
    void Start();
    Task<ActiveWindowInfo?> ReadAsync(IntPtr handle, CancellationToken cancellationToken);
}

/// <summary>Owns hooks/timer on the dispatcher; coalesces notifications into one bounded latest request.</summary>
internal sealed class ActiveWindowMonitor : IDisposable
{
    private readonly IActiveWindowSource _source;
    private readonly DispatcherTimer _timer;
    private readonly CancellationTokenSource _shutdown = new();
    private IntPtr _lastExternalHandle;
    private bool _ownForeground;
    private bool _reading;
    private bool _pending;
    private bool _disposed;
    private long _generation;

    public event Action<ActiveWindowInfo?, bool>? Changed;
    public event Action? ExternalTargetChanged;
    public event Action<Exception>? Failed;

    public ActiveWindowMonitor(Dispatcher dispatcher, IActiveWindowSource? source = null,
        TimeSpan? debounce = null)
    {
        _source = source ?? new NativeActiveWindowSource();
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = debounce ?? TimeSpan.FromMilliseconds(150)
        };
        _timer.Tick += OnTick;
        _source.WindowChanged += OnWindowChanged;
    }

    public void Start()
    {
        try { _source.Start(); }
        catch { Dispose(); throw; }
    }

    private void OnWindowChanged(IntPtr handle, bool ownForeground)
    {
        if (_disposed) return;
        _ownForeground = ownForeground;
        if (!ownForeground)
        {
            _lastExternalHandle = handle;
            ExternalTargetChanged?.Invoke();
        }
        _generation++;
        _pending = true;
        _timer.Stop();
        _timer.Start();
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        if (_disposed || _reading || !_pending) return;
        _reading = true;
        _pending = false;
        long generation = _generation;
        bool own = _ownForeground;
        try
        {
            var info = await _source.ReadAsync(_lastExternalHandle, _shutdown.Token);
            if (!_disposed && generation == _generation) Changed?.Invoke(info, own);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!_disposed && generation == _generation) Failed?.Invoke(ex);
        }
        finally
        {
            _reading = false;
            if (!_disposed && _pending) _timer.Start();
            if (_disposed) _shutdown.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _source.WindowChanged -= OnWindowChanged;
        _source.Dispose();
        _shutdown.Cancel();
        if (!_reading) _shutdown.Dispose();
        // The in-flight task owns the token; late results are ignored without blocking UI shutdown.
    }
}

internal sealed class NativeActiveWindowSource : IActiveWindowSource
{
    private const uint ForegroundEvent = 0x0003;
    private const uint NameChangeEvent = 0x800C;
    private readonly WinEventDelegate _callback;
    private IntPtr _foregroundHook;
    private IntPtr _titleHook;

    public event Action<IntPtr, bool>? WindowChanged;

    public NativeActiveWindowSource() => _callback = OnWinEvent;

    public void Start()
    {
        if (_foregroundHook != IntPtr.Zero) return;
        try
        {
            _foregroundHook = SetWinEventHook(ForegroundEvent, ForegroundEvent, IntPtr.Zero, _callback, 0, 0, 0);
            if (_foregroundHook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            _titleHook = SetWinEventHook(NameChangeEvent, NameChangeEvent, IntPtr.Zero, _callback, 0, 0, 0);
            if (_titleHook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            NotifyForeground();
        }
        catch { Dispose(); throw; }
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int objectId, int childId,
        uint threadId, uint timestamp)
    {
        if (eventType == NameChangeEvent && (objectId != 0 || childId != 0 || hwnd != GetForegroundWindow())) return;
        NotifyForeground();
    }

    private void NotifyForeground()
    {
        var hwnd = GetForegroundWindow();
        _ = GetWindowThreadProcessId(hwnd, out uint pid);
        WindowChanged?.Invoke(hwnd, pid == (uint)Environment.ProcessId);
    }

    public Task<ActiveWindowInfo?> ReadAsync(IntPtr handle, CancellationToken cancellationToken) =>
        Task.Run(() => ReadWindow(handle), cancellationToken);

    public static Task<IReadOnlyList<ActiveWindowInfo>> EnumerateAsync(CancellationToken cancellationToken) =>
        Task.Run<IReadOnlyList<ActiveWindowInfo>>(() =>
    {
        var windows = new List<ActiveWindowInfo>();
        EnumWindows((hwnd, _) =>
        {
            if (cancellationToken.IsCancellationRequested) return false;
            if (IsWindowVisible(hwnd) && ReadWindow(hwnd) is { Title.Length: > 0 } info) windows.Add(info);
            return true;
        }, IntPtr.Zero);
        cancellationToken.ThrowIfCancellationRequested();
        return windows;
    }, cancellationToken);

    private static ActiveWindowInfo? ReadWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return null;
        _ = GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0 || pid == (uint)Environment.ProcessId) return null;
        var title = new StringBuilder(4096);
        _ = GetWindowText(hwnd, title, title.Capacity);
        string path = string.Empty;
        var process = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process != IntPtr.Zero)
        {
            try
            {
                var buffer = new StringBuilder(32768);
                uint size = (uint)buffer.Capacity;
                if (QueryFullProcessImageName(process, 0, buffer, ref size)) path = buffer.ToString();
            }
            finally { _ = CloseHandle(process); }
        }
        if (path.Length == 0) return null;
        _ = GetWindowThreadProcessId(hwnd, out uint currentPid);
        if (!IsWindow(hwnd) || currentPid != pid) return null;
        return new ActiveWindowInfo(hwnd, Path.GetFileName(path), title.ToString(), path);
    }

    public void Dispose()
    {
        if (_foregroundHook != IntPtr.Zero) { _ = UnhookWinEvent(_foregroundHook); _foregroundHook = IntPtr.Zero; }
        if (_titleHook != IntPtr.Zero) { _ = UnhookWinEvent(_titleHook); _titleHook = IntPtr.Zero; }
    }

    private delegate void WinEventDelegate(IntPtr hook, uint eventType, IntPtr hwnd, int objectId,
        int childId, uint threadId, uint timestamp);
    private delegate bool EnumWindowsDelegate(IntPtr hwnd, IntPtr param);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventDelegate callback,
        uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsDelegate callback, IntPtr param);
    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref uint size);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
