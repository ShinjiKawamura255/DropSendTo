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
        var spec = ParseQuery(query);
        bool matchAll = spec.IncludeTerms.Count == 0 && spec.ExcludeTerms.Count == 0;

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

                if (matchAll || MatchesQuery(BuildSlotSearchTargets(slot), spec))
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

    internal static QuerySpec ParseQuery(string? query)
    {
        var spec = new QuerySpec();
        query = (query ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(query))
        {
            return spec;
        }

        var tokens = query.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var token in tokens)
        {
            if (token == "!" || token == "'")
            {
                continue;
            }

            if (token.StartsWith('!'))
            {
                var stripped = token[1..];
                if (!string.IsNullOrEmpty(stripped))
                {
                    var compiled = CompileAlternativeSet(stripped);
                    foreach (var alt in compiled.Alternatives)
                    {
                        alt.Exact = true; // Exclude terms are always exact substring matches
                    }
                    if (compiled.Alternatives.Count > 0)
                    {
                        spec.ExcludeTerms.Add(compiled);
                    }
                }
                continue;
            }

            var includeCompiled = CompileAlternativeSet(token);
            if (includeCompiled.Alternatives.Count > 0)
            {
                spec.IncludeTerms.Add(includeCompiled);
            }
        }

        return spec;
    }

    private static AlternativeSet CompileAlternativeSet(string term)
    {
        var set = new AlternativeSet();
        var alts = term.Split('|', StringSplitOptions.RemoveEmptyEntries);
        if (alts.Length == 0)
        {
            alts = new[] { term };
        }

        foreach (var alt in alts)
        {
            var (exact, parsed) = ParseIncludeAlternative(alt);
            if (string.IsNullOrEmpty(parsed)) continue;

            var (anchoredStart, anchoredEnd, core) = SplitAnchor(parsed);
            if (string.IsNullOrEmpty(core)) continue;

            var normalizedCore = NormalizeTokenForSearch(core);
            if (string.IsNullOrEmpty(normalizedCore)) continue;

            set.Alternatives.Add(new MatcherPattern
            {
                Exact = exact,
                AnchoredStart = anchoredStart,
                AnchoredEnd = anchoredEnd,
                Core = normalizedCore
            });
        }

        return set;
    }

    private static (bool exact, string parsed) ParseIncludeAlternative(string candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return (false, string.Empty);
        }
        if (candidate.StartsWith("^'", StringComparison.Ordinal))
        {
            return (true, "^" + candidate[2..]);
        }
        if (candidate.StartsWith('\''))
        {
            return (true, candidate[1..]);
        }
        return (false, candidate);
    }

    private static (bool anchoredStart, bool anchoredEnd, string core) SplitAnchor(string term)
    {
        bool anchoredStart = term.StartsWith('^');
        bool anchoredEnd = term.EndsWith('$');
        string core = term;
        if (anchoredStart)
        {
            core = core[1..];
        }
        if (anchoredEnd && core.Length > 0)
        {
            core = core[..^1];
        }
        return (anchoredStart, anchoredEnd, core);
    }

    internal static bool MatchesQuery(IReadOnlyList<string> haystacks, QuerySpec spec)
    {
        if (haystacks.Count == 0)
        {
            return false;
        }

        foreach (var excludeSet in spec.ExcludeTerms)
        {
            if (MatchesAlternativeSet(excludeSet, haystacks))
            {
                return false;
            }
        }

        foreach (var includeSet in spec.IncludeTerms)
        {
            if (!MatchesAlternativeSet(includeSet, haystacks))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesAlternativeSet(AlternativeSet set, IReadOnlyList<string> haystacks)
    {
        foreach (var pattern in set.Alternatives)
        {
            foreach (var haystack in haystacks)
            {
                if (MatchesPattern(pattern, haystack))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool MatchesPattern(MatcherPattern pattern, string haystack)
    {
        if (pattern.Exact)
        {
            return MatchesExact(pattern, haystack);
        }
        else
        {
            return MatchesFuzzy(pattern, haystack);
        }
    }

    private static bool MatchesExact(MatcherPattern pattern, string haystack)
    {
        if (pattern.AnchoredStart && pattern.AnchoredEnd)
        {
            return string.Equals(haystack, pattern.Core, StringComparison.OrdinalIgnoreCase);
        }
        if (pattern.AnchoredStart)
        {
            return haystack.StartsWith(pattern.Core, StringComparison.OrdinalIgnoreCase);
        }
        if (pattern.AnchoredEnd)
        {
            return haystack.EndsWith(pattern.Core, StringComparison.OrdinalIgnoreCase);
        }
        return haystack.IndexOf(pattern.Core, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool MatchesFuzzy(MatcherPattern pattern, string haystack)
    {
        if (pattern.AnchoredStart)
        {
            if (haystack.Length == 0 || pattern.Core.Length == 0)
            {
                return false;
            }
            if (char.ToLowerInvariant(haystack[0]) != pattern.Core[0])
            {
                return false;
            }
        }
        if (pattern.AnchoredEnd)
        {
            if (haystack.Length == 0 || pattern.Core.Length == 0)
            {
                return false;
            }
            if (char.ToLowerInvariant(haystack[^1]) != pattern.Core[^1])
            {
                return false;
            }
        }

        return IsSubsequence(haystack, pattern.Core);
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
        ["りょ"] = "ryo",
        ["ゔぁ"] = "va",
        ["ゔぃ"] = "vi",
        ["ゔぅ"] = "vu",
        ["ゔぇ"] = "ve",
        ["ゔぉ"] = "vo",
        ["てぃ"] = "ti",
        ["でぃ"] = "di",
        ["ちぇ"] = "che",
        ["しぇ"] = "she",
        ["じぇ"] = "je",
        ["ふぁ"] = "fa",
        ["ふぃ"] = "fi",
        ["ふぇ"] = "fe",
        ["ふぉ"] = "fo",
        ["うぃ"] = "wi",
        ["うぇ"] = "we",
        ["うぉ"] = "wo"
    };

    private static readonly Dictionary<char, string> SingleKanaRomaji = new()
    {
        ['あ'] = "a",
        ['い'] = "i",
        ['う'] = "u",
        ['え'] = "e",
        ['お'] = "o",
        ['ぁ'] = "a",
        ['ぃ'] = "i",
        ['ぅ'] = "u",
        ['ぇ'] = "e",
        ['ぉ'] = "o",
        ['か'] = "ka",
        ['き'] = "ki",
        ['く'] = "ku",
        ['け'] = "ke",
        ['こ'] = "ko",
        ['さ'] = "sa",
        ['し'] = "shi",
        ['す'] = "su",
        ['せ'] = "se",
        ['そ'] = "so",
        ['た'] = "ta",
        ['ち'] = "chi",
        ['つ'] = "tsu",
        ['て'] = "te",
        ['と'] = "to",
        ['な'] = "na",
        ['に'] = "ni",
        ['ぬ'] = "nu",
        ['ね'] = "ne",
        ['の'] = "no",
        ['は'] = "ha",
        ['ひ'] = "hi",
        ['ふ'] = "fu",
        ['へ'] = "he",
        ['ほ'] = "ho",
        ['ま'] = "ma",
        ['み'] = "mi",
        ['む'] = "mu",
        ['め'] = "me",
        ['も'] = "mo",
        ['や'] = "ya",
        ['ゆ'] = "yu",
        ['よ'] = "yo",
        ['ら'] = "ra",
        ['り'] = "ri",
        ['る'] = "ru",
        ['れ'] = "re",
        ['ろ'] = "ro",
        ['わ'] = "wa",
        ['を'] = "o",
        ['ん'] = "n",
        ['が'] = "ga",
        ['ぎ'] = "gi",
        ['ぐ'] = "gu",
        ['げ'] = "ge",
        ['ご'] = "go",
        ['ざ'] = "za",
        ['じ'] = "ji",
        ['ず'] = "zu",
        ['ぜ'] = "ze",
        ['ぞ'] = "zo",
        ['だ'] = "da",
        ['ぢ'] = "ji",
        ['づ'] = "zu",
        ['で'] = "de",
        ['ど'] = "do",
        ['ば'] = "ba",
        ['び'] = "bi",
        ['ぶ'] = "bu",
        ['べ'] = "be",
        ['ぼ'] = "bo",
        ['ぱ'] = "pa",
        ['ぴ'] = "pi",
        ['ぷ'] = "pu",
        ['ぺ'] = "pe",
        ['ぽ'] = "po",
        ['ゔ'] = "vu",
        ['ゐ'] = "i",
        ['ゑ'] = "e",
        ['ー'] = string.Empty
    };
}

internal readonly record struct SlotSearchResult(int LayerIndex, int SlotIndex);

internal class QuerySpec
{
    public List<AlternativeSet> IncludeTerms { get; } = new();
    public List<AlternativeSet> ExcludeTerms { get; } = new();
}

internal class AlternativeSet
{
    public List<MatcherPattern> Alternatives { get; } = new();
}

internal class MatcherPattern
{
    public bool Exact { get; set; }
    public bool AnchoredStart { get; set; }
    public bool AnchoredEnd { get; set; }
    public string Core { get; set; } = string.Empty;
}
