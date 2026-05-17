using System;
using System.Text;

namespace DropSendTo.Services;

internal static class MacroQuotedTextReader
{
    public static bool TryRead(
        string input,
        ref int index,
        string commandName,
        string argumentName,
        out string value,
        out string? error)
    {
        value = string.Empty;
        error = null;

        if (index >= input.Length || input[index] != '"')
        {
            error = $"{commandName} の {argumentName} は \"\" で囲んでください。";
            return false;
        }

        index++;
        var sb = new StringBuilder();
        bool closed = false;

        while (index < input.Length)
        {
            char ch = input[index++];
            if (ch == '\\')
            {
                int slashCount = 1;
                while (index < input.Length && input[index] == '\\')
                {
                    slashCount++;
                    index++;
                }

                if (index >= input.Length)
                {
                    sb.Append('\\', slashCount);
                    break;
                }

                var nextChar = input[index];
                if (nextChar == '"')
                {
                    sb.Append('\\', slashCount / 2);
                    if (slashCount % 2 == 0)
                    {
                        index++;
                        closed = true;
                        break;
                    }

                    if (IsQuoteTerminator(input, index + 1))
                    {
                        sb.Append('\\');
                        index++;
                        closed = true;
                        break;
                    }

                    index++;
                    sb.Append('"');
                    continue;
                }

                var literalPairs = slashCount / 2;
                if (literalPairs > 0)
                {
                    sb.Append('\\', literalPairs);
                }

                if (slashCount % 2 == 1)
                {
                    if (nextChar == 'n' || nextChar == 'r' || nextChar == 't' || nextChar == '"' || nextChar == '\\')
                    {
                        index++;
                        sb.Append(nextChar switch
                        {
                            'n' => '\n',
                            'r' => '\r',
                            't' => '\t',
                            '"' => '"',
                            '\\' => '\\',
                            _ => nextChar
                        });
                        continue;
                    }

                    sb.Append('\\');
                    continue;
                }

                continue;
            }

            if (ch == '"')
            {
                closed = true;
                break;
            }

            sb.Append(ch);
        }

        if (!closed)
        {
            error = $"{commandName} の {argumentName} が閉じられていません。";
            value = string.Empty;
            return false;
        }

        value = sb.ToString();
        return true;
    }

    private static bool IsQuoteTerminator(string input, int startIndex)
    {
        for (int i = startIndex; i < input.Length; i++)
        {
            char c = input[i];
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            return c == '#';
        }

        return true;
    }
}
