using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace DropSendTo.Services;

internal static class ArgumentTemplateExpander
{
    private const string ArgsToken = "{args}";
    private const string DropArgsToken = "{drop_args}";
    private const string DropCountToken = "{drop_count}";
    private const string DropPathToken = "{drop_path}";
    private const string ClipboardToken = "{clipboard}";
    private const string ClipboardArgsToken = "{clipboard_args}";
    private const int ClipboardArgsLimitCap = 20;
    private static readonly Regex ClipboardArgsLimitRegex = new(@"\{clipboard_args:(\d+)\}", RegexOptions.Compiled);
    private static readonly Regex DropPathIndexRegex = new(@"\{drop_path:(\d+)\}", RegexOptions.Compiled);
    private static readonly Regex DropPathTokenRegex = new(@"\{drop_path\}", RegexOptions.Compiled);

    public static string Expand(string? template, IReadOnlyCollection<string> paths, Func<ClipboardSnapshot> clipboardProvider)
    {
        template ??= ArgsToken;
        var pathList = paths?.ToArray() ?? Array.Empty<string>();
        var joinedPaths = JoinPaths(pathList);
        string dropCount = pathList.Length.ToString(CultureInfo.InvariantCulture);

        var snapshot = clipboardProvider?.Invoke() ?? ClipboardSnapshot.Empty;
        string clipboardRaw = snapshot.RawText ?? string.Empty;
        string clipboardNormalized = clipboardRaw.Trim();
        var historyEntries = snapshot.Entries ?? Array.Empty<string>();

        string result = template.Replace(ArgsToken, joinedPaths);
        result = result.Replace(DropArgsToken, joinedPaths);
        result = result.Replace(DropCountToken, dropCount);
        result = ReplaceDropPathWithIndex(result, pathList);
        result = ReplaceDropPathToken(result, pathList);
        result = ReplaceClipboardArgsWithLimit(result, historyEntries);
        if (result.Contains(ClipboardArgsToken, StringComparison.Ordinal))
        {
            var latestEntries = snapshot.LatestEntries ?? Array.Empty<string>();
            if (latestEntries.Count == 0)
            {
                result = result.Replace(ClipboardArgsToken, string.Empty);
            }
            else
            {
                result = result.Replace(ClipboardArgsToken, BuildClipboardArgs(latestEntries));
            }
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

    private static string ReplaceDropPathToken(string template, IReadOnlyList<string> entries)
    {
        if (!DropPathTokenRegex.IsMatch(template))
        {
            return template;
        }

        string replacement = entries.Count > 0 ? QuotePath(entries[0]) : string.Empty;
        return DropPathTokenRegex.Replace(template, replacement);
    }

    private static string ReplaceDropPathWithIndex(string template, IReadOnlyList<string> entries)
    {
        if (!DropPathIndexRegex.IsMatch(template))
        {
            return template;
        }

        return DropPathIndexRegex.Replace(template, match =>
        {
            var token = match.Groups[1].Value;
            if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                return string.Empty;
            }
            if (index <= 0)
            {
                return string.Empty;
            }
            if (entries.Count == 0 || index > entries.Count)
            {
                return string.Empty;
            }
            return QuotePath(entries[index - 1]);
        });
    }

    private static string BuildClipboardArgs(IReadOnlyList<string> entries, int? limit = null)
    {
        if (entries.Count == 0)
        {
            return string.Empty;
        }
        if (limit.HasValue)
        {
            if (limit.Value <= 0)
            {
                return string.Empty;
            }
            int takeCount = Math.Min(limit.Value, entries.Count);
            int skip = entries.Count - takeCount;
            return string.Join(" ", entries.Skip(skip).Take(takeCount).Select(QuotePath));
        }
        return string.Join(" ", entries.Select(QuotePath));
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

    private static string ReplaceClipboardArgsWithLimit(string template, IReadOnlyList<string> entries)
    {
        if (!ClipboardArgsLimitRegex.IsMatch(template))
        {
            return template;
        }

        return ClipboardArgsLimitRegex.Replace(template, match =>
        {
            var token = match.Groups[1].Value;
            if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var limit))
            {
                return string.Empty;
            }
            if (limit <= 0)
            {
                return string.Empty;
            }
            limit = Math.Min(limit, ClipboardArgsLimitCap);
            return BuildClipboardArgs(entries, limit);
        });
    }
}
