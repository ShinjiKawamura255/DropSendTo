using System;
using System.Collections.Generic;

namespace DropSendTo.Services;

internal static class MacroScriptFormatter
{
    private const int SpacesPerIndent = 4;

    public static string NormalizeIndentation(string? script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return string.Empty;
        }

        var normalized = NormalizeLineEndings(script);
        var lines = normalized.Split('\n');
        var formattedLines = new List<string>(lines.Length);
        int indentLevel = 0;

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();
            if (trimmed.Length == 0)
            {
                formattedLines.Add(string.Empty);
                continue;
            }

            bool isElseIf = IsElseIfDirective(trimmed);
            bool isElse = IsElseDirective(trimmed);
            bool isBlockClosing = IsBlockClosing(trimmed);
            int effectiveIndent = indentLevel;

            if (isBlockClosing)
            {
                indentLevel = Math.Max(indentLevel - 1, 0);
                effectiveIndent = indentLevel;
            }
            else if (isElse || isElseIf)
            {
                effectiveIndent = Math.Max(indentLevel - 1, 0);
                indentLevel = effectiveIndent;
            }

            formattedLines.Add(new string(' ', effectiveIndent * SpacesPerIndent) + trimmed);

            if (IsBlockOpening(trimmed) || isElseIf)
            {
                indentLevel++;
            }
            else if (isElse)
            {
                indentLevel++;
            }
        }

        var newline = Environment.NewLine;
        return string.Join(newline, formattedLines);
    }

    public static string GetIndentationForNewLine(string? script, int caretIndex)
    {
        if (string.IsNullOrEmpty(script))
        {
            return string.Empty;
        }

        var clampedIndex = Math.Clamp(caretIndex, 0, script.Length);
        if (clampedIndex == 0)
        {
            return string.Empty;
        }

        var preceding = script[..clampedIndex];
        var normalized = NormalizeLineEndings(preceding);
        var lines = normalized.Split('\n');
        int indentLevel = 0;

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            bool isElseIf = IsElseIfDirective(trimmed);
            bool isElse = IsElseDirective(trimmed);
            bool isBlockClosing = IsBlockClosing(trimmed);

            if (isBlockClosing)
            {
                indentLevel = Math.Max(indentLevel - 1, 0);
            }
            else if (isElse || isElseIf)
            {
                indentLevel = Math.Max(indentLevel - 1, 0);
            }

            if (IsBlockOpening(trimmed) || isElseIf)
            {
                indentLevel++;
            }
            else if (isElse)
            {
                indentLevel++;
            }
        }

        return new string(' ', indentLevel * SpacesPerIndent);
    }

    private static string NormalizeLineEndings(string input) =>
        input.Replace("\r\n", "\n").Replace('\r', '\n');

    private static bool IsBlockOpening(string line) =>
        StartsWithCommand(line, "REPEAT") || StartsWithCommand(line, "IF");

    private static bool IsBlockClosing(string line) =>
        StartsWithCommand(line, "ENDREPEAT") || StartsWithCommand(line, "ENDIF");

    private static bool IsElseDirective(string line) =>
        StartsWithCommand(line, "ELSE") && !IsElseIfDirective(line);

    private static bool IsElseIfDirective(string line)
    {
        if (StartsWithCommand(line, "ELSEIF"))
        {
            return true;
        }
        if (!StartsWithCommand(line, "ELSE"))
        {
            return false;
        }
        var remainder = line.Length > 4 ? line[4..].TrimStart() : string.Empty;
        return StartsWithCommand(remainder, "IF");
    }

    private static bool StartsWithCommand(string line, string command)
    {
        if (!line.StartsWith(command, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return line.Length == command.Length || char.IsWhiteSpace(line[command.Length]);
    }
}
