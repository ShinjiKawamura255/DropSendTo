using System;
using System.Collections.Generic;
using System.Linq;

namespace DropSendTo.Services;

internal static class ArgumentTemplateExpander
{
    private const string ArgsToken = "{args}";
    private const string ClipboardToken = "{clipboard}";
    private const string ClipboardArgsToken = "{clipboard_args}";

    public static string Expand(string? template, IReadOnlyCollection<string> paths, Func<string?> clipboardProvider)
    {
        template ??= ArgsToken;
        var joinedPaths = JoinPaths(paths);

        string clipboardRaw = clipboardProvider?.Invoke() ?? string.Empty;
        string clipboardNormalized = clipboardRaw.Trim();
        string clipboardArgs = BuildClipboardArgs(clipboardNormalized);

        string result = template.Replace(ArgsToken, joinedPaths);
        if (result.Contains(ClipboardArgsToken, StringComparison.Ordinal))
        {
            result = result.Replace(ClipboardArgsToken, clipboardArgs);
        }
        if (result.Contains(ClipboardToken, StringComparison.Ordinal))
        {
            result = result.Replace(ClipboardToken, clipboardNormalized);
        }

        return result;
    }

    private static string JoinPaths(IEnumerable<string> paths)
    {
        return string.Join(" ", paths.Select(QuotePath));
    }

    private static string BuildClipboardArgs(string clipboardText)
    {
        var tokens = ParseClipboardEntries(clipboardText);
        if (tokens.Count == 0)
        {
            return string.Empty;
        }
        return string.Join(" ", tokens.Select(QuotePath));
    }

    private static IReadOnlyList<string> ParseClipboardEntries(string clipboardText)
    {
        if (string.IsNullOrWhiteSpace(clipboardText))
        {
            return Array.Empty<string>();
        }

        var entries = new List<string>();
        var lines = clipboardText.Replace("\r", string.Empty)
                                 .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length <= 1)
        {
            var single = NormalizeClipboardEntry(clipboardText);
            if (!string.IsNullOrWhiteSpace(single))
            {
                entries.Add(single);
            }
            return entries;
        }

        foreach (var line in lines)
        {
            var normalized = NormalizeClipboardEntry(line);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                entries.Add(normalized);
            }
        }

        return entries;
    }

    private static string NormalizeClipboardEntry(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            return string.Empty;
        }

        var trimmed = entry.Trim();
        if (trimmed.Length >= 2 && trimmed.StartsWith("\"", StringComparison.Ordinal) && trimmed.EndsWith("\"", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..^1];
        }

        return trimmed;
    }

    private static string QuotePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "\"\"";
        }

        if (path.Any(char.IsWhiteSpace) && !(path.StartsWith("\"", StringComparison.Ordinal) && path.EndsWith("\"", StringComparison.Ordinal)))
        {
            return $"\"{path}\"";
        }

        return path;
    }
}
