using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DropSendTo.Models;

namespace DropSendTo.Services;

internal static class SlotSearchService
{
    public static IReadOnlyList<SlotSearchResult> Search(
        IReadOnlyList<Layer>? layers,
        string? query,
        Func<SlotModel, bool> isSlotEmpty)
    {
        if (layers == null) throw new ArgumentNullException(nameof(layers));
        if (isSlotEmpty == null) throw new ArgumentNullException(nameof(isSlotEmpty));

        var results = new List<SlotSearchResult>();
        var tokens = Tokenize(query);
        bool matchAll = tokens.Count == 0;

        for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
        {
            var layer = layers[layerIndex];
            if (layer?.Slots == null) continue;

            for (int slotIndex = 0; slotIndex < layer.Slots.Count; slotIndex++)
            {
                var slot = layer.Slots[slotIndex];
                if (slot == null || isSlotEmpty(slot))
                {
                    continue;
                }

                if (matchAll || MatchesAllTokens(BuildSlotSearchTargets(slot), tokens))
                {
                    results.Add(new SlotSearchResult(layerIndex, slotIndex));
                }
            }
        }

        return results;
    }

    internal static IReadOnlyList<string> Tokenize(string? query)
    {
        query = (query ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(query))
        {
            return Array.Empty<string>();
        }

        return query
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeTokenForSearch)
            .Where(t => !string.IsNullOrEmpty(t))
            .ToArray();
    }

    internal static IReadOnlyList<string> BuildSlotSearchTargets(SlotModel slot)
    {
        var title = slot.Title ?? string.Empty;
        var keywords = slot.SearchKeywords ?? string.Empty;
        var baseText = (title + " " + keywords).ReplaceLineEndings(" ").Trim();
        if (string.IsNullOrWhiteSpace(baseText))
        {
            return Array.Empty<string>();
        }

        var normalized = NormalizeForSearch(baseText);
        var romaji = ConvertKanaToRomaji(baseText);
        return new[] { baseText, normalized, romaji }
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static string NormalizeTokenForSearch(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        var cleaned = token
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("ー", string.Empty, StringComparison.Ordinal);
        return NormalizeForSearch(cleaned);
    }

    internal static bool MatchesAllTokens(IReadOnlyList<string> haystacks, IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0 || haystacks.Count == 0)
        {
            return false;
        }

        foreach (var token in tokens)
        {
            bool matched = false;
            foreach (var haystack in haystacks)
            {
                if (IsFuzzyMatch(haystack, token))
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsFuzzyMatch(string haystack, string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return true;
        }

        if (haystack.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return IsSubsequence(haystack, token);
    }

    internal static bool IsSubsequence(string haystack, string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return true;
        }

        int hIndex = 0;
        var source = haystack.ToLowerInvariant();
        var needle = token.ToLowerInvariant();
        foreach (char ch in needle)
        {
            hIndex = source.IndexOf(ch, hIndex);
            if (hIndex < 0)
            {
                return false;
            }
            hIndex++;
        }

        return true;
    }

    internal static string NormalizeForSearch(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var normalized = text.Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            sb.Append(char.ToLowerInvariant(ch));
        }

        return sb.ToString();
    }

    internal static string ConvertKanaToRomaji(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var hira = ToHiragana(text.Normalize(NormalizationForm.FormKC));
        var sb = new StringBuilder(hira.Length * 3);
        bool sokuonPending = false;

        for (int i = 0; i < hira.Length; i++)
        {
            char ch = hira[i];
            if (ch == 'っ')
            {
                sokuonPending = true;
                continue;
            }

            if (ch == 'ー')
            {
                if (sb.Length > 0)
                {
                    char last = sb[sb.Length - 1];
                    if ("aeiou".Contains(last))
                    {
                        sb.Append(last);
                    }
                }
                continue;
            }

            string? roma = TryGetDigraph(hira, i, out int consumed)
                ?? TryGetSingleKanaRomaji(ch);

            if (consumed > 0)
            {
                i += consumed;
            }

            if (string.IsNullOrEmpty(roma))
            {
                sokuonPending = false;
                continue;
            }

            if (sokuonPending)
            {
                var first = roma[0];
                if (char.IsLetter(first) && !"aeiou".Contains(char.ToLowerInvariant(first)))
                {
                    sb.Append(first);
                }
                sokuonPending = false;
            }

            sb.Append(roma);
        }

        return sb.ToString();
    }

    private static string ToHiragana(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char ch in text)
        {
            if (ch >= '\u30A1' && ch <= '\u30F4')
            {
                sb.Append((char)(ch - 0x60));
            }
            else
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    private static string? TryGetDigraph(string hira, int index, out int consumed)
    {
        consumed = 0;
        if (index + 1 >= hira.Length)
        {
            return null;
        }

        char first = hira[index];
        char second = hira[index + 1];
        string key = new string(new[] { first, second });
        if (DigraphRomaji.TryGetValue(key, out var roma))
        {
            consumed = 1;
            return roma;
        }
        return null;
    }

    private static string? TryGetSingleKanaRomaji(char ch)
    {
        if (SingleKanaRomaji.TryGetValue(ch, out var roma))
        {
            return roma;
        }
        return null;
    }

    private static readonly Dictionary<string, string> DigraphRomaji = new(StringComparer.Ordinal)
    {
        ["きゃ"] = "kya",
        ["きゅ"] = "kyu",
        ["きょ"] = "kyo",
        ["ぎゃ"] = "gya",
        ["ぎゅ"] = "gyu",
        ["ぎょ"] = "gyo",
        ["しゃ"] = "sha",
        ["しゅ"] = "shu",
        ["しょ"] = "sho",
        ["じゃ"] = "ja",
        ["じゅ"] = "ju",
        ["じょ"] = "jo",
        ["ちゃ"] = "cha",
        ["ちゅ"] = "chu",
        ["ちょ"] = "cho",
        ["にゃ"] = "nya",
        ["にゅ"] = "nyu",
        ["にょ"] = "nyo",
        ["ひゃ"] = "hya",
        ["ひゅ"] = "hyu",
        ["ひょ"] = "hyo",
        ["びゃ"] = "bya",
        ["びゅ"] = "byu",
        ["びょ"] = "byo",
        ["ぴゃ"] = "pya",
        ["ぴゅ"] = "pyu",
        ["ぴょ"] = "pyo",
        ["みゃ"] = "mya",
        ["みゅ"] = "myu",
        ["みょ"] = "myo",
        ["りゃ"] = "rya",
        ["りゅ"] = "ryu",
        ["りょ"] = "ryo"
    };

    private static readonly Dictionary<char, string> SingleKanaRomaji = new()
    {
        ['あ'] = "a", ['い'] = "i", ['う'] = "u", ['え'] = "e", ['お'] = "o",
        ['ぁ'] = "a", ['ぃ'] = "i", ['ぅ'] = "u", ['ぇ'] = "e", ['ぉ'] = "o",
        ['か'] = "ka", ['き'] = "ki", ['く'] = "ku", ['け'] = "ke", ['こ'] = "ko",
        ['さ'] = "sa", ['し'] = "shi", ['す'] = "su", ['せ'] = "se", ['そ'] = "so",
        ['た'] = "ta", ['ち'] = "chi", ['つ'] = "tsu", ['て'] = "te", ['と'] = "to",
        ['な'] = "na", ['に'] = "ni", ['ぬ'] = "nu", ['ね'] = "ne", ['の'] = "no",
        ['は'] = "ha", ['ひ'] = "hi", ['ふ'] = "fu", ['へ'] = "he", ['ほ'] = "ho",
        ['ま'] = "ma", ['み'] = "mi", ['む'] = "mu", ['め'] = "me", ['も'] = "mo",
        ['や'] = "ya", ['ゆ'] = "yu", ['よ'] = "yo",
        ['ら'] = "ra", ['り'] = "ri", ['る'] = "ru", ['れ'] = "re", ['ろ'] = "ro",
        ['わ'] = "wa", ['を'] = "o", ['ん'] = "n",
        ['が'] = "ga", ['ぎ'] = "gi", ['ぐ'] = "gu", ['げ'] = "ge", ['ご'] = "go",
        ['ざ'] = "za", ['じ'] = "ji", ['ず'] = "zu", ['ぜ'] = "ze", ['ぞ'] = "zo",
        ['だ'] = "da", ['ぢ'] = "ji", ['づ'] = "zu", ['で'] = "de", ['ど'] = "do",
        ['ば'] = "ba", ['び'] = "bi", ['ぶ'] = "bu", ['べ'] = "be", ['ぼ'] = "bo",
        ['ぱ'] = "pa", ['ぴ'] = "pi", ['ぷ'] = "pu", ['ぺ'] = "pe", ['ぽ'] = "po",
        ['ゔ'] = "vu",
        ['ー'] = string.Empty
    };
}

internal readonly record struct SlotSearchResult(int LayerIndex, int SlotIndex);
