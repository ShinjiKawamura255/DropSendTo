using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using DropSendTo.Models;

namespace DropSendTo.Services;

public class LauncherService
{
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

    private static string BuildArguments(string template, string[] paths)
    {
        string joined = string.Join(" ", paths.Select(Quote));
        return template.Replace("{args}", joined);
    }

    private static string Quote(string path)
    {
        if (string.IsNullOrEmpty(path)) return "\"\"";
        if (path.Contains(' ') || path.Contains('\t')) return $"\"{path}\"";
        return path;
    }
}

public record LaunchResult(bool Success, string Message)
{
    public static LaunchResult Ok() => new(true, "");
    public static LaunchResult Fail(string message) => new(false, message);
}

