using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace DropSendTo.Services;

internal sealed class ClipboardHistoryService : IDisposable
{
    private const int MaxEntries = 20;
    private const int WmClipboardUpdate = 0x031D;

    private static readonly Lazy<ClipboardHistoryService> LazyInstance = new(() => new ClipboardHistoryService());

    public static ClipboardHistoryService Instance => LazyInstance.Value;

    private readonly object _lock = new();
    private readonly Queue<string> _entries = new();
    private readonly List<string> _latestEntries = new();
    private readonly LoggerService _logger = LoggerService.Instance;

    private bool _initialized;
    private HwndSource? _source;
    private IntPtr _windowHandle;
    private string _latestRaw = string.Empty;
    private string _lastRecordedRaw = string.Empty;

    private ClipboardHistoryService()
    {
    }

    public void Initialize(Window window)
    {
        if (_initialized)
        {
            return;
        }

        var helper = new WindowInteropHelper(window);
        _windowHandle = helper.Handle;
        if (_windowHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Window handle is not ready for clipboard listener registration.");
        }

        _source = HwndSource.FromHwnd(_windowHandle);
        if (_source == null)
        {
            throw new InvalidOperationException("Failed to acquire window source for clipboard listener registration.");
        }

        _source.AddHook(WndProc);
        if (!AddClipboardFormatListener(_windowHandle))
        {
            var error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, $"AddClipboardFormatListener failed with error {error}.");
        }

        _initialized = true;
        CaptureCurrentClipboard();
    }

    public ClipboardSnapshot GetSnapshot(string? latestRawText)
    {
        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(latestRawText))
            {
                ProcessRawClipboard(latestRawText);
            }

            return new ClipboardSnapshot(_latestRaw, _entries.ToArray(), _latestEntries.ToArray());
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (!_initialized)
            {
                return;
            }

            if (_windowHandle != IntPtr.Zero)
            {
                RemoveClipboardFormatListener(_windowHandle);
            }
            if (_source != null)
            {
                _source.RemoveHook(WndProc);
                _source = null;
            }
            _windowHandle = IntPtr.Zero;
            _initialized = false;
        }
    }

    private void CaptureCurrentClipboard()
    {
        try
        {
            string? raw = null;
            if (System.Windows.Clipboard.ContainsText())
            {
                raw = System.Windows.Clipboard.GetText();
            }

            if (!string.IsNullOrEmpty(raw))
            {
                lock (_lock)
                {
                    ProcessRawClipboard(raw);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to capture clipboard text: {ex.Message}");
        }
    }

    private void ProcessRawClipboard(string raw)
    {
        var trimmedRaw = (raw ?? string.Empty).Trim();
        if (trimmedRaw.Length == 0)
        {
            _latestRaw = string.Empty;
            _latestEntries.Clear();
            return;
        }

        if (string.Equals(trimmedRaw, _lastRecordedRaw, StringComparison.Ordinal))
        {
            _latestRaw = trimmedRaw;
            return;
        }

        _latestEntries.Clear();

        foreach (var entry in SplitEntries(trimmedRaw))
        {
            if (entry.Length == 0)
            {
                continue;
            }
            _latestEntries.Add(entry);
            _entries.Enqueue(entry);
            if (_entries.Count > MaxEntries)
            {
                _entries.Dequeue();
            }
        }

        _lastRecordedRaw = trimmedRaw;
        _latestRaw = trimmedRaw;
    }

    private static IEnumerable<string> SplitEntries(string raw)
    {
        var sanitized = raw.Replace("\r", string.Empty);
        var lines = sanitized.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length <= 1)
        {
            var single = NormalizeEntry(raw);
            if (!string.IsNullOrWhiteSpace(single))
            {
                yield return single;
            }
            yield break;
        }

        foreach (var line in lines)
        {
            var normalized = NormalizeEntry(line);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                yield return normalized;
            }
        }
    }

    private static string NormalizeEntry(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return string.Empty;
        }

        var trimmed = entry.Trim();
        if (trimmed.Length >= 2 &&
            trimmed.StartsWith("\"", StringComparison.Ordinal) &&
            trimmed.EndsWith("\"", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..^1];
        }

        return trimmed;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmClipboardUpdate)
        {
            CaptureCurrentClipboard();
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
}

internal readonly record struct ClipboardSnapshot(string RawText, IReadOnlyList<string> Entries, IReadOnlyList<string> LatestEntries)
{
    public static readonly ClipboardSnapshot Empty = new(string.Empty, Array.Empty<string>(), Array.Empty<string>());
}
