using System;
using WpfClipboard = System.Windows.Clipboard;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using DropSendTo.Models;

namespace DropSendTo.Services;

public class LauncherService
{
    private readonly LoggerService _logger = LoggerService.Instance;

    public LaunchResult Launch(SlotModel slot, string[] paths)
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

            var startInfo = new ProcessStartInfo
            {
                FileName = slot.Command,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(slot.Command) ?? Environment.CurrentDirectory,
                Arguments = string.Empty
            };
            var arguments = BuildArguments(slot.ArgumentsTemplate ?? "{args}", paths);
            startInfo.Arguments = arguments;
            _logger.Info($"Launching process \"{startInfo.FileName}\" for slot \"{slotTitle}\" with arguments \"{arguments}\" (paths={paths.Length}).");
            var p = Process.Start(startInfo);
            if (p != null)
            {
                int pid = 0;
                try
                {
                    pid = p.Id;
                }
                catch
                {
                    pid = 0;
                }

                if (pid > 0)
                {
                    _logger.Info($"Launch succeeded (pid={pid}).");
                }
                else
                {
                    _logger.Info("Launch succeeded (pid unavailable).");
                }
                return LaunchResult.Ok();
            }

            _logger.Warn("Process.Start returned null.");
            return LaunchResult.Fail("Failed to start process.");
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
}

public record LaunchResult(bool Success, string Message)
{
    public static LaunchResult Ok() => new(true, "");
    public static LaunchResult Fail(string message) => new(false, message);
}
