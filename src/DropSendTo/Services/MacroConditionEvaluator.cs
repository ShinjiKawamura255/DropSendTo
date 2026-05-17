using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DropSendTo.Services;

internal static class MacroConditionEvaluator
{
    private static readonly HashSet<string> ComparisonOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "==",
        "=",
        "!=",
        ">",
        "<",
        ">=",
        "<=",
        "CONTAINS",
        "CONTAIN",
        "NOTCONTAINS",
        "STARTSWITH",
        "SW",
        "ENDSWITH",
        "EW"
    };

    public static bool TryEvaluateExpanded(string args, out bool result, out string? error)
    {
        result = false;
        error = null;
        if (string.IsNullOrWhiteSpace(args))
        {
            error = "IF 条件を指定してください。";
            return false;
        }

        if (!TryTokenizeConditionExpression(args, out var tokens, out var splitError))
        {
            error = splitError;
            return false;
        }

        var conditionResults = new List<bool>();
        var logicalOps = new List<string>();
        int index = 0;
        while (index < tokens.Count)
        {
            if (!TryEvaluateConditionTerm(tokens, ref index, out var condResult, out error))
            {
                return false;
            }
            conditionResults.Add(condResult);

            if (index < tokens.Count)
            {
                var logical = tokens[index].Trim();
                if (IsLogicalOperator(logical))
                {
                    logicalOps.Add(logical.ToUpperInvariant());
                    index++;
                }
                else
                {
                    error = $"IF でサポートされていない演算子です: \"{logical}\"";
                    return false;
                }
            }
        }

        if (logicalOps.Count != conditionResults.Count - 1 && conditionResults.Count > 0)
        {
            error = "IF 条件が不完全です。AND/OR の後に条件を指定してください。";
            return false;
        }

        bool acc = conditionResults[0];
        var orTerms = new List<bool>();
        for (int i = 0; i < logicalOps.Count; i++)
        {
            if (logicalOps[i] == "AND")
            {
                acc = acc && conditionResults[i + 1];
            }
            else
            {
                orTerms.Add(acc);
                acc = conditionResults[i + 1];
            }
        }
        orTerms.Add(acc);
        result = orTerms.Any(v => v);
        return true;
    }

    private static bool TryTokenizeConditionExpression(string input, out List<string> tokens, out string? error)
    {
        tokens = new List<string>(capacity: 3);
        error = null;
        int index = 0;

        static void SkipWhitespace(string text, ref int idx)
        {
            while (idx < text.Length && char.IsWhiteSpace(text[idx]))
            {
                idx++;
            }
        }

        while (true)
        {
            SkipWhitespace(input, ref index);
            if (index >= input.Length)
            {
                break;
            }

            if (!TryParseConditionToken(input, ref index, out var token, out error))
            {
                return false;
            }
            tokens.Add(token);
        }

        return true;
    }

    private static bool TryEvaluateConditionTerm(IReadOnlyList<string> tokens, ref int index, out bool result, out string? error)
    {
        result = false;
        error = null;
        if (index >= tokens.Count)
        {
            error = "IF 条件が不完全です。";
            return false;
        }

        var left = tokens[index];
        if (index + 1 < tokens.Count)
        {
            var opCandidate = tokens[index + 1].Trim();
            if (ComparisonOperators.Contains(opCandidate))
            {
                if (index + 2 >= tokens.Count)
                {
                    error = "IF 条件が不完全です。演算子の右辺を指定してください。";
                    return false;
                }
                var right = tokens[index + 2];
                if (!TryEvaluateSimpleCondition(left, opCandidate, right, out result, out error))
                {
                    return false;
                }
                index += 3;
                return true;
            }
        }

        result = EvaluateTruthy(left);
        index += 1;
        return true;
    }

    private static bool TryEvaluateSimpleCondition(string left, string op, string right, out bool result, out string? error)
    {
        result = false;
        error = null;
        var opNormalized = op.ToUpperInvariant();

        bool IsNumeric(string value, out long number) =>
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number);

        static bool CompareStrings(string l, string r) => string.Equals(l, r, StringComparison.Ordinal);

        switch (opNormalized)
        {
            case "==":
            case "=":
                if (IsNumeric(left, out var eqLeft) && IsNumeric(right, out var eqRight))
                {
                    result = eqLeft == eqRight;
                    return true;
                }
                result = CompareStrings(left, right);
                return true;
            case "!=":
                if (IsNumeric(left, out var neLeft) && IsNumeric(right, out var neRight))
                {
                    result = neLeft != neRight;
                    return true;
                }
                result = !CompareStrings(left, right);
                return true;
            case ">":
            case "<":
            case ">=":
            case "<=":
                if (!IsNumeric(left, out var numLeft) || !IsNumeric(right, out var numRight))
                {
                    error = $"IF の演算子 \"{op}\" には整数を指定してください。";
                    return false;
                }
                result = opNormalized switch
                {
                    ">" => numLeft > numRight,
                    "<" => numLeft < numRight,
                    ">=" => numLeft >= numRight,
                    "<=" => numLeft <= numRight,
                    _ => false
                };
                return true;
            case "CONTAINS":
            case "CONTAIN":
                result = left.Contains(right, StringComparison.Ordinal);
                return true;
            case "NOTCONTAINS":
                result = !left.Contains(right, StringComparison.Ordinal);
                return true;
            case "STARTSWITH":
            case "SW":
                result = left.StartsWith(right, StringComparison.Ordinal);
                return true;
            case "ENDSWITH":
            case "EW":
                result = left.EndsWith(right, StringComparison.Ordinal);
                return true;
            default:
                error = $"IF でサポートされていない演算子です: \"{op}\"";
                return false;
        }
    }

    private static bool EvaluateTruthy(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (trimmed.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return number != 0;
        }

        return true;
    }

    private static bool IsLogicalOperator(string token) =>
        token.Equals("AND", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("OR", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseConditionToken(string input, ref int index, out string token, out string? error)
    {
        token = string.Empty;
        error = null;
        while (index < input.Length && char.IsWhiteSpace(input[index]))
        {
            index++;
        }

        if (index >= input.Length)
        {
            error = "IF 条件が不完全です。";
            return false;
        }

        if (input[index] != '"')
        {
            int start = index;
            while (index < input.Length && !char.IsWhiteSpace(input[index]))
            {
                index++;
            }
            token = input[start..index];
            return true;
        }

        if (!TryReadQuotedContent(input, ref index, "IF", "条件", out var literal, out error))
        {
            return false;
        }

        token = literal;
        return true;
    }

    private static bool TryReadQuotedContent(string input, ref int index, string commandName, string argumentName, out string value, out string? error)
    {
        index++;
        var sb = new StringBuilder();
        error = null;
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
