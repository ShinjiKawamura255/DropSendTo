using System;
using System.Collections.Generic;
using System.IO;
using DropSendTo.Models;

namespace DropSendTo.Services;

internal sealed record SlotDropRegistration(string Title, string Command, string ArgumentsTemplate, SlotExecutionMode ExecutionMode);

internal static class SlotDropRegistrationHelper
{
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe",
        ".bat",
        ".cmd",
        ".com",
        ".ps1"
    };

    internal static bool TryCreate(string[]? paths, string? text, out SlotDropRegistration registration)
    {
        if (paths is { Length: > 0 })
        {
            var primary = GetFirstNonEmptyPath(paths);
            if (string.IsNullOrWhiteSpace(primary))
            {
                registration = null!;
                return false;
            }

            var normalizedPath = Path.TrimEndingDirectorySeparator(primary.Trim());
            bool isDirectory = Directory.Exists(normalizedPath);
            string title = isDirectory
                ? Path.GetFileName(normalizedPath)
                : Path.GetFileNameWithoutExtension(normalizedPath);
            if (string.IsNullOrWhiteSpace(title))
            {
                title = normalizedPath;
            }

            var arguments = isDirectory ? string.Empty : (IsExecutable(normalizedPath) ? "{args}" : string.Empty);
            registration = new SlotDropRegistration(title, normalizedPath, arguments, SlotExecutionMode.Command);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            var trimmed = text.Trim();
            var firstLine = ExtractFirstLine(trimmed);
            if (string.IsNullOrWhiteSpace(firstLine))
            {
                registration = null!;
                return false;
            }

            registration = new SlotDropRegistration(firstLine, firstLine, string.Empty, SlotExecutionMode.Command);
            return true;
        }

        registration = null!;
        return false;
    }

    private static string ExtractFirstLine(string value)
    {
        var separator = new[] { "\r\n", "\n" };
        var firstLine = value.Split(separator, StringSplitOptions.None)[0];
        return firstLine.Trim();
    }

    private static string? GetFirstNonEmptyPath(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                return path;
            }
        }

        return null;
    }

    private static bool IsExecutable(string path)
    {
        var ext = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(ext))
        {
            return false;
        }

        return ExecutableExtensions.Contains(ext);
    }
}
