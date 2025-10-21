using System;
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
                return LaunchResult.Fail("Command is not set.");

            var startInfo = new ProcessStartInfo
            {
                FileName = slot.Command,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(slot.Command) ?? Environment.CurrentDirectory,
                Arguments = BuildArguments(slot.ArgumentsTemplate ?? "{args}", paths)
            };
            var p = Process.Start(startInfo);
            return p != null ? LaunchResult.Ok() : LaunchResult.Fail("Failed to start process.");
        }
        catch (Exception ex)
        {
            return LaunchResult.Fail(ex.Message);
        }
    }

    private string BuildArguments(string template, string[] paths)
    {
        return ArgumentTemplateExpander.Expand(template, paths, TryReadClipboardText);
    }

    private string? TryReadClipboardText()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            return null;
        }

        string? clipboardText = null;
        Exception? operationError = null;

        void ReadClipboard()
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    clipboardText = Clipboard.GetText();
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

        return clipboardText;
    }
}

public record LaunchResult(bool Success, string Message)
{
    public static LaunchResult Ok() => new(true, "");
    public static LaunchResult Fail(string message) => new(false, message);
}
