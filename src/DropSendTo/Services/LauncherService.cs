using System;
using WpfClipboard = System.Windows.Clipboard;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using DropSendTo.Models;

namespace DropSendTo.Services;

public class LauncherService
{
    private readonly LoggerService _logger = LoggerService.Instance;

    public LaunchResult Launch(SlotModel slot, string[] paths, string? argumentOverride = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(slot.Command))
            {
                _logger.Warn("Launch requested but command is not set.");
                return LaunchResult.Fail("Command is not set.");
            }

            var slotTitle = slot.Title?.ReplaceLineEndings(" ").Trim() ?? string.Empty;
            if (slotTitle.Length == 0)
            {
                slotTitle = "(untitled)";
            }

            var isDirectory = Directory.Exists(slot.Command);
            Process? p = null;

            if (isDirectory)
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = slot.Command,
                    UseShellExecute = true,
                    WorkingDirectory = slot.Command,
                    Arguments = string.Empty
                };
                _logger.Info($"Launching directory \"{startInfo.FileName}\" for slot \"{slotTitle}\".");
                try
                {
                    p = StartProcess(startInfo);
                }
                catch (Exception ex)
                {
                    var explorerInfo = new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        UseShellExecute = true,
                        Arguments = $"\"{slot.Command}\""
                    };
                    _logger.Warn($"Primary directory launch failed, falling back to explorer.exe: {ex.Message}");
                    p = StartProcess(explorerInfo);
                }
            }
            else
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = slot.Command,
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(slot.Command) ?? Environment.CurrentDirectory,
                    Arguments = string.Empty
                };
                var arguments = argumentOverride ?? BuildArguments(slot.ArgumentsTemplate ?? "{args}", paths);
                startInfo.Arguments = arguments;
                _logger.Info($"Launching process \"{startInfo.FileName}\" for slot \"{slotTitle}\" with arguments \"{arguments}\" (paths={paths.Length}).");
                p = StartProcess(startInfo);
            }

            var pid = GetProcessId(p);

            if (pid > 0)
            {
                _logger.Info($"Launch succeeded (pid={pid}).");
                ScheduleLaunchedProcessForegroundPromotion(pid, slotTitle);
            }
            else
            {
                _logger.Info("Launch succeeded (pid unavailable).");
            }
            return LaunchResult.Ok();
        }
        catch (Exception ex)
        {
            _logger.Error($"Launch failed with exception: {ex}");
            return LaunchResult.Fail(ex.Message);
        }
    }

    private string BuildArguments(string template, string[] paths)
    {
        return ArgumentTemplateExpander.Expand(template, paths, GetClipboardSnapshot);
    }

    internal virtual Process? StartProcess(ProcessStartInfo startInfo)
    {
        return Process.Start(startInfo);
    }

    internal virtual int GetProcessId(Process? process)
    {
        try
        {
            return process?.Id ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    internal virtual void ScheduleLaunchedProcessForegroundPromotion(int processId, string slotTitle)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (await TryPromoteProcessWindowToForegroundAsync(processId).ConfigureAwait(false))
                {
                    _logger.Info($"Foreground promotion succeeded (pid={processId}, slot=\"{slotTitle}\").");
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"Foreground promotion failed (pid={processId}, slot=\"{slotTitle}\"): {ex.Message}");
            }
        });
    }

    private static async Task<bool> TryPromoteProcessWindowToForegroundAsync(int processId)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(processId);
        }
        catch
        {
            return false;
        }

        using (process)
        {
            try
            {
                process.WaitForInputIdle(1200);
            }
            catch
            {
                // Ignore: the process can be console/non-GUI or can exit quickly.
            }

            const int maxAttempts = 20;
            const int delayMs = 100;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    if (process.HasExited)
                    {
                        return false;
                    }

                    process.Refresh();
                    var handle = process.MainWindowHandle;
                    if (handle != IntPtr.Zero && TryFocusWindow(handle))
                    {
                        return true;
                    }
                }
                catch
                {
                    return false;
                }

                await Task.Delay(delayMs).ConfigureAwait(false);
            }
        }

        return false;
    }

    private static bool TryFocusWindow(IntPtr windowHandle)
    {
        if (!IsWindow(windowHandle))
        {
            return false;
        }

        if (IsIconic(windowHandle))
        {
            ShowWindow(windowHandle, SwRestore);
        }

        var foregroundWindow = GetForegroundWindow();
        var currentThread = GetCurrentThreadId();
        var targetThread = GetWindowThreadProcessId(windowHandle, out _);
        var foregroundThread = foregroundWindow != IntPtr.Zero
            ? GetWindowThreadProcessId(foregroundWindow, out _)
            : 0u;

        var attachedCurrent = false;
        var attachedForeground = false;
        try
        {
            if (foregroundThread != 0 && foregroundThread != targetThread)
            {
                attachedForeground = AttachThreadInput(foregroundThread, targetThread, true);
            }

            if (currentThread != targetThread && currentThread != foregroundThread)
            {
                attachedCurrent = AttachThreadInput(currentThread, targetThread, true);
            }

            BringWindowToTop(windowHandle);
            return SetForegroundWindow(windowHandle);
        }
        finally
        {
            if (attachedCurrent)
            {
                AttachThreadInput(currentThread, targetThread, false);
            }

            if (attachedForeground)
            {
                AttachThreadInput(foregroundThread, targetThread, false);
            }
        }
    }

    private ClipboardSnapshot GetClipboardSnapshot()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            return ClipboardSnapshot.Empty;
        }

        string? clipboardText = null;
        Exception? operationError = null;

        void ReadClipboard()
        {
            try
            {
                if (WpfClipboard.ContainsText())
                {
                    clipboardText = WpfClipboard.GetText();
                }
            }
            catch (Exception ex)
            {
                operationError = ex;
            }
        }

        if (dispatcher.CheckAccess())
        {
            ReadClipboard();
        }
        else
        {
            dispatcher.Invoke(ReadClipboard);
        }

        if (operationError != null)
        {
            _logger.Warn($"Failed to read clipboard text: {operationError.Message}");
        }

        return ClipboardHistoryService.Instance.GetSnapshot(clipboardText);
    }

    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
}

public record LaunchResult(bool Success, string Message)
{
    public static LaunchResult Ok() => new(true, "");
    public static LaunchResult Fail(string message) => new(false, message);
}
