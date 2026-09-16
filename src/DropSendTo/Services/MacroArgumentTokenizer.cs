using System.Collections.Generic;
using System.Text;

namespace DropSendTo.Services;

internal static class MacroArgumentTokenizer
{
    public static List<string>? Tokenize(string input, out string? error)
    {
        error = null;
        var tokens = new List<string>();
        int index = 0;
        while (index < input.Length)
        {
            while (index < input.Length && char.IsWhiteSpace(input[index]))
            {
                index++;
            }
            if (index >= input.Length)
            {
                break;
            }
            if (input[index] == '"')
            {
                if (!TryReadQuotedPathContent(input, ref index, "Macro", "引数", out var quoted, out error))
                {
                    return null;
                }
                tokens.Add(quoted);
                continue;
            }
            int start = index;
            while (index < input.Length && !char.IsWhiteSpace(input[index]))
            {
                index++;
            }
            tokens.Add(input[start..index]);
        }
        return tokens;
    }

    public static bool TryReadQuotedPathContent(
        string input,
        ref int index,
        string commandName,
        string argumentName,
        out string value,
        out string? error)
    {
        index++;
        var builder = new StringBuilder();
        error = null;
        bool closed = false;

        while (index < input.Length)
        {
            char character = input[index++];
            if (character == '"')
            {
                closed = true;
                break;
            }

            if (character == '\\' && index < input.Length && input[index] == '"')
            {
                if (IsPathQuoteTerminator(input, index + 1))
                {
                    builder.Append('\\');
                    index++;
                    closed = true;
                    break;
                }

                builder.Append('"');
                index++;
                continue;
            }

            builder.Append(character);
        }

        if (!closed)
        {
            error = $"{commandName} の {argumentName} が閉じられていません。";
            value = string.Empty;
            return false;
        }

        value = builder.ToString();
        return true;
    }

    public static bool IsPathQuoteTerminator(string input, int startIndex)
    {
        for (int i = startIndex; i < input.Length; i++)
        {
            char character = input[i];
            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            return character == '#';
        }

        return true;
    }
}
