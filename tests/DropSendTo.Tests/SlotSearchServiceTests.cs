using DropSendTo.Models;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class SlotSearchServiceTests
{
    [Fact]
    public void Search_ShouldReturnAllNonEmptySlotsInLayerOrder_WhenQueryIsEmpty()
    {
        var layers = new[]
        {
            new Layer
            {
                Slots =
                [
                    Slot(title: "First"),
                    EmptySlot(),
                    Slot(command: "cmd-only.exe")
                ]
            },
            new Layer
            {
                Slots =
                [
                    Slot(title: "Second")
                ]
            }
        };

        var results = SlotSearchService.Search(layers, "   ", IsSlotEmpty);

        results.Should().Equal(
            new SlotSearchResult(0, 0),
            new SlotSearchResult(0, 2),
            new SlotSearchResult(1, 0));
    }

    [Fact]
    public void Search_ShouldMatchTitleAndKeywordsOnly()
    {
        var layers = new[]
        {
            new Layer
            {
                Slots =
                [
                    Slot(title: "Visible title", keywords: "needle"),
                    Slot(title: "Another title", command: "needle.exe", arguments: "--needle", macro: "TEXT needle")
                ]
            }
        };

        SlotSearchService.Search(layers, "needle", IsSlotEmpty)
            .Should()
            .Equal(new SlotSearchResult(0, 0));
    }

    [Fact]
    public void Search_ShouldRequireAllTokens()
    {
        var layers = new[]
        {
            new Layer
            {
                Slots =
                [
                    Slot(title: "Alpha Beta"),
                    Slot(title: "Alpha Gamma")
                ]
            }
        };

        SlotSearchService.Search(layers, "alpha beta", IsSlotEmpty)
            .Should()
            .Equal(new SlotSearchResult(0, 0));
    }

    [Fact]
    public void Search_ShouldMatchSubsequenceToken()
    {
        var layers = new[]
        {
            new Layer
            {
                Slots =
                [
                    Slot(title: "Visual Studio Code"),
                    Slot(title: "Video Converter")
                ]
            }
        };

        SlotSearchService.Search(layers, "vsc", IsSlotEmpty)
            .Should()
            .Equal(new SlotSearchResult(0, 0));
    }

    [Fact]
    public void Search_ShouldNormalizeCaseWidthAndDiacritics()
    {
        var layers = new[]
        {
            new Layer
            {
                Slots =
                [
                    Slot(title: "CAF\u00c9 \uff21\uff22\uff23")
                ]
            }
        };

        SlotSearchService.Search(layers, "cafe abc", IsSlotEmpty)
            .Should()
            .Equal(new SlotSearchResult(0, 0));
    }

    [Fact]
    public void Search_ShouldHandleHyphenAndProlongedMarkTokens()
    {
        var layers = new[]
        {
            new Layer
            {
                Slots =
                [
                    Slot(title: "abc"),
                    Slot(title: "ラーメン")
                ]
            }
        };

        SlotSearchService.Search(layers, "a-b-c", IsSlotEmpty)
            .Should()
            .Equal(new SlotSearchResult(0, 0));
        SlotSearchService.Search(layers, "ラメン", IsSlotEmpty)
            .Should()
            .Equal(new SlotSearchResult(0, 1));
    }

    [Fact]
    public void Search_ShouldMatchKanaByRomajiIncludingSokuonDigraphAndLongVowel()
    {
        var layers = new[]
        {
            new Layer
            {
                Slots =
                [
                    Slot(title: "がっこう"),
                    Slot(title: "きょう"),
                    Slot(title: "ラーメン")
                ]
            }
        };

        SlotSearchService.Search(layers, "gakkou", IsSlotEmpty)
            .Should()
            .Equal(new SlotSearchResult(0, 0));
        SlotSearchService.Search(layers, "kyou", IsSlotEmpty)
            .Should()
            .Equal(new SlotSearchResult(0, 1));
        SlotSearchService.Search(layers, "raamen", IsSlotEmpty)
            .Should()
            .Equal(new SlotSearchResult(0, 2));
    }

    [Fact]
    public void Search_ShouldMatchExactSubstring_WhenSingleQuotePrefixIsUsed()
    {
        var layers = new[]
        {
            new Layer
            {
                Slots =
                [
                    Slot(title: "Visual Studio Code"),
                    Slot(title: "VLC Media Player")
                ]
            }
        };

        // Exact substring matches "Visual" but doesn't fuzzy match "vsc"
        SlotSearchService.Search(layers, "'Visual", IsSlotEmpty)
            .Should()
            .Equal(new SlotSearchResult(0, 0));

        // Without quote, fuzzy matching matches "vsc"
        SlotSearchService.Search(layers, "vsc", IsSlotEmpty)
            .Should()
            .Equal(new SlotSearchResult(0, 0));

        // Exact substring with 'v matches both VLC and Visual (since both contain 'v' or 'V')
        SlotSearchService.Search(layers, "'v", IsSlotEmpty)
            .Should()
            .Equal(new SlotSearchResult(0, 0), new SlotSearchResult(0, 1));
    }

    [Fact]
    public void Search_ShouldExcludeSlots_WhenExclamationPrefixIsUsed()
    {
        var layers = new[]
        {
            new Layer
            {
                Slots =
                [
                    Slot(title: "Visual Studio Code"),
                    Slot(title: "VLC Media Player"),
                    Slot(title: "Notepad")
                ]
            }
        };

        // Exclude slots matching "Visual"
        SlotSearchService.Search(layers, "!Visual", IsSlotEmpty)
            .Should()
            .Equal(new SlotSearchResult(0, 1), new SlotSearchResult(0, 2));

        // Exclude slots matching exact "VLC"
        SlotSearchService.Search(layers, "!'VLC", IsSlotEmpty)
            .Should()
            .Equal(new SlotSearchResult(0, 0), new SlotSearchResult(0, 2));
    }

    [Fact]
    public void Search_ShouldMatchAnchoredStart_WhenCaretPrefixIsUsed()
    {
        var layers = new[]
        {
            new Layer
            {
                Slots =
                [
                    Slot(title: "Visual Studio Code"),
                    Slot(title: "Another Visual Tool")
                ]
            }
        };

        // Starts with "Visual" (fuzzy or exact)
        SlotSearchService.Search(layers, "^Visual", IsSlotEmpty)
            .Should()
            .Equal(new SlotSearchResult(0, 0));

        // Starts with exact "Visual"
        SlotSearchService.Search(layers, "^'Visual", IsSlotEmpty)
            .Should()
            .Equal(new SlotSearchResult(0, 0));
    }

    [Fact]
    public void Search_ShouldMatchAnchoredEnd_WhenDollarSuffixIsUsed()
    {
        var layers = new[]
        {
            new Layer
            {
                Slots =
                [
                    Slot(title: "Visual Studio Code"),
                    Slot(title: "Code Runner")
                ]
            }
        };

        // Ends with "Code"
        SlotSearchService.Search(layers, "Code$", IsSlotEmpty)
            .Should()
            .Equal(new SlotSearchResult(0, 0));

        // Ends with exact "Code"
        SlotSearchService.Search(layers, "'Code$", IsSlotEmpty)
            .Should()
            .Equal(new SlotSearchResult(0, 0));
    }

    [Fact]
    public void Search_ShouldMatchAlternatives_WhenPipeIsUsed()
    {
        var layers = new[]
        {
            new Layer
            {
                Slots =
                [
                    Slot(title: "Visual Studio Code"),
                    Slot(title: "Notepad++"),
                    Slot(title: "VLC Media Player")
                ]
            }
        };

        // OR search
        SlotSearchService.Search(layers, "studio|notepad", IsSlotEmpty)
            .Should()
            .Equal(new SlotSearchResult(0, 0), new SlotSearchResult(0, 1));

        // Anchored OR search
        SlotSearchService.Search(layers, "^'Visual|^'Notepad", IsSlotEmpty)
            .Should()
            .Equal(new SlotSearchResult(0, 0), new SlotSearchResult(0, 1));
    }

    [Fact]
    public void Search_ShouldCombineOperatorsCorrectly()
    {
        var layers = new[]
        {
            new Layer
            {
                Slots =
                [
                    Slot(title: "Visual Studio Code"),
                    Slot(title: "Another Visual Tool")
                ]
            }
        };

        // "Visual" AND NOT "Tool"
        SlotSearchService.Search(layers, "Visual !Tool", IsSlotEmpty)
            .Should()
            .Equal(new SlotSearchResult(0, 0));
    }

    [Fact]
    public void Search_ShouldRankWholeMatchesBeforeSubstringsAndFuzzyMatches_AcrossLayers()
    {
        var layers = new[]
        {
            new Layer { Slots = [Slot(title: "Visual Studio Code"), Slot(title: "My VSC tool"), EmptySlot()] },
            new Layer { Slots = [Slot(title: "VSC", keywords: "editor"), Slot(title: "VSC helper")] },
            new Layer { Slots = [Slot(title: "Editor", keywords: "code vsc ide"), Slot(title: "Very Simple Compiler")] }
        };

        SlotSearchService.Search(layers, "vsc", IsSlotEmpty).Should().Equal(
            new SlotSearchResult(1, 0),
            new SlotSearchResult(2, 0),
            new SlotSearchResult(0, 1),
            new SlotSearchResult(1, 1),
            new SlotSearchResult(0, 0),
            new SlotSearchResult(2, 1));
    }

    [Theory]
    [InlineData("cafe", "CAF\u00c9", "Cafe helper", "Cool Application For Editing")]
    [InlineData("abc", "\uff21\uff22\uff23", "ABC helper", "Alpha Beta Compiler")]
    [InlineData("gakkou", "がっこう", "がっこう用", "がくこう")]
    [InlineData("raamen", "ラーメン", "ラーメン用", "ラアメモン")]
    [InlineData("a-b-c", "abc", "abc helper", "Alpha Beta Compiler")]
    public void Search_ShouldRankNormalizedWholeMatchesFirst(
        string query, string wholeTitle, string partialTitle, string fuzzyTitle)
    {
        var layers = new[]
        {
            new Layer { Slots = [Slot(title: fuzzyTitle), Slot(title: partialTitle), Slot(title: wholeTitle)] }
        };

        SlotSearchService.Search(layers, query, IsSlotEmpty).Should().Equal(
            new SlotSearchResult(0, 2), new SlotSearchResult(0, 1), new SlotSearchResult(0, 0));
    }

    [Theory]
    [InlineData("alpha beta")]
    [InlineData("alpha|gamma beta")]
    [InlineData("alpha beta !blocked")]
    [InlineData("  alpha\t beta  ")]
    public void Search_ShouldRankMultiTermMatchesUsingAllIncludeTerms(string query)
    {
        var layers = new[]
        {
            new Layer
            {
                Slots =
                [
                    Slot(title: "Alpha", keywords: "Be Cool Tool App"),
                    Slot(title: "Alpha", keywords: "Beta"),
                    Slot(title: "Alpha Beta helper"),
                    Slot(title: "Alpha Beta", keywords: "extra"),
                    Slot(title: "Other", keywords: "Alpha Beta"),
                    Slot(title: "Alpha"),
                    Slot(title: "Blocked")
                ]
            }
        };

        SlotSearchService.Search(layers, query, IsSlotEmpty).Should().Equal(
            new SlotSearchResult(0, 3), new SlotSearchResult(0, 4),
            new SlotSearchResult(0, 1), new SlotSearchResult(0, 2), new SlotSearchResult(0, 0));
    }

    [Theory]
    [InlineData("vsc|code")]
    [InlineData("vsc|code !blocked")]
    public void Search_ShouldRankTheBestMatchingAlternative_AndKeepExclusions(string query)
    {
        var layers = new[]
        {
            new Layer
            {
                Slots =
                [
                    Slot(title: "Visual Studio Code"),
                    Slot(title: "VSC"),
                    Slot(title: "Code"),
                    Slot(title: "Very Simple Compiler"),
                    Slot(title: "Blocked")
                ]
            }
        };

        SlotSearchService.Search(layers, query, IsSlotEmpty).Should().Equal(
            new SlotSearchResult(0, 1), new SlotSearchResult(0, 2),
            new SlotSearchResult(0, 0), new SlotSearchResult(0, 3));
    }

    [Theory]
    [InlineData("'vsc", false)]
    [InlineData("^vsc", true)]
    [InlineData("^'vsc", false)]
    [InlineData("vsc$", true)]
    [InlineData("'vsc$", false)]
    public void Search_ShouldPreserveOperatorsWhenRanking(string query, bool includesFuzzy)
    {
        var layers = new[]
        {
            new Layer
            {
                Slots = [Slot(title: "Very Simple Compiler classic"), Slot(title: "VSC VSC"), Slot(title: "VSC")]
            }
        };

        var expected = new List<SlotSearchResult> { new(0, 2), new(0, 1) };
        if (includesFuzzy)
        {
            expected.Add(new SlotSearchResult(0, 0));
        }

        SlotSearchService.Search(layers, query, IsSlotEmpty).Should().Equal(expected);
    }

    [Fact]
    public void Search_ShouldNotRankAnAlternativeThatFailsItsAnchorAsAWholeMatch()
    {
        var layers = new[]
        {
            new Layer { Slots = [Slot(title: "My Code VSC helper", keywords: "code"), Slot(title: "VSC")] }
        };

        SlotSearchService.Search(layers, "^code|vsc", IsSlotEmpty).Should().Equal(
            new SlotSearchResult(0, 1), new SlotSearchResult(0, 0));
    }

    [Fact]
    public void Search_ShouldKeepFuzzyTiesStable_WhenBothAnchorsRequireAWholeMatch()
    {
        var layers = new[]
        {
            new Layer
            {
                Slots = [Slot(title: "Very Simple Compiler classic"), Slot(title: "VSC VSC"), Slot(title: "VSC")]
            }
        };

        SlotSearchService.Search(layers, "^vsc$", IsSlotEmpty).Should().Equal(
            new SlotSearchResult(0, 2), new SlotSearchResult(0, 0), new SlotSearchResult(0, 1));
    }

    [Fact]
    public void Search_ShouldExcludeWholeMatchesBeforeRanking()
    {
        var layers = new[]
        {
            new Layer
            {
                Slots = [Slot(title: "VSC helper"), Slot(title: "VSC", keywords: "blocked"), Slot(title: "VSC")]
            }
        };

        SlotSearchService.Search(layers, "vsc !blocked", IsSlotEmpty).Should().Equal(
            new SlotSearchResult(0, 2), new SlotSearchResult(0, 0));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("!")]
    [InlineData("!blocked")]
    public void Search_ShouldKeepLayerAndSlotOrder_WhenThereAreNoIncludeTerms(string? query)
    {
        var layers = new[]
        {
            new Layer { Slots = [Slot(title: "VSC helper"), Slot(title: "Visual Studio Code")] },
            new Layer { Slots = [Slot(title: "VSC")] }
        };

        SlotSearchService.Search(layers, query, IsSlotEmpty).Should().Equal(
            new SlotSearchResult(0, 0), new SlotSearchResult(0, 1), new SlotSearchResult(1, 0));
    }

    [Theory]
    [InlineData("alpha beta", "Alpha\u3000Beta")]
    [InlineData("alpha beta", "Alpha\u00a0Beta")]
    [InlineData("alpha beta", "Alpha\u2003Beta")]
    [InlineData("alpha beta", "\uff21\uff4c\uff50\uff48\uff41\u3000\uff22\uff45\uff54\uff41")]
    [InlineData("alpha\u3000beta", "Alpha Beta")]
    [InlineData("alpha\u3000beta", "Alpha\u3000Beta")]
    public void Search_ShouldRankWholeTitlesFirst_WhenWhitespaceIsNormalized(string query, string wholeTitle)
    {
        var layers = new[]
        {
            new Layer { Slots = [Slot(title: "Alpha Beta helper"), Slot(title: wholeTitle)] }
        };

        SlotSearchService.Search(layers, query, IsSlotEmpty).Should().Equal(
            new SlotSearchResult(0, 1), new SlotSearchResult(0, 0));
    }

    [Theory]
    [InlineData("code\u3000vsc")]
    [InlineData("code\u00a0vsc")]
    [InlineData("code\u2003vsc")]
    public void Search_ShouldRankIndividualKeywordsFirst_WhenWhitespaceIsNormalized(string keywords)
    {
        var layers = new[]
        {
            new Layer { Slots = [Slot(title: "VSC helper"), Slot(title: "Editor", keywords: keywords)] }
        };

        SlotSearchService.Search(layers, "vsc", IsSlotEmpty).Should().Equal(
            new SlotSearchResult(0, 1), new SlotSearchResult(0, 0));
    }

    [Theory]
    [InlineData("alpha\u3000beta gamma")]
    [InlineData("alpha|alpha\u3000beta gamma")]
    [InlineData("alpha\u3000beta|alpha beta gamma")]
    public void Search_ShouldRankWholeTitlesFirst_WhenNormalizedPhrasesMixWithAndOrTerms(string query)
    {
        var layers = new[]
        {
            new Layer { Slots = [Slot(title: "Alpha Beta Gamma helper"), Slot(title: "Alpha Beta Gamma")] }
        };

        SlotSearchService.Search(layers, query, IsSlotEmpty).Should().Equal(
            new SlotSearchResult(0, 1), new SlotSearchResult(0, 0));
    }

    [Theory]
    [InlineData("VS-Code", "VS-Code", "VSCode helper")]
    [InlineData("VS-Code", "VS\uff0dCode", "VSCode helper")]
    [InlineData("VS\uff0dCode", "VS-Code", "VS-Code helper")]
    [InlineData("ラーメン", "ラーメン", "ラメン用")]
    [InlineData("ラーメン", "\uff97\uff70\uff92\uff9d", "ラメン用")]
    public void Search_ShouldRankWholeTitlesFirst_WhenQueryRemovesHyphensOrProlongedMarks(
        string query, string wholeTitle, string partialTitle)
    {
        var layers = new[]
        {
            new Layer { Slots = [Slot(title: partialTitle), Slot(title: wholeTitle)] }
        };

        SlotSearchService.Search(layers, query, IsSlotEmpty).Should().Equal(
            new SlotSearchResult(0, 1), new SlotSearchResult(0, 0));
    }

    [Theory]
    [InlineData("VS-Code", "VSCode helper", "code VS-Code ide")]
    [InlineData("ラーメン", "ラメン用", "food ラーメン noodles")]
    public void Search_ShouldRankIndividualKeywordsFirst_WhenQueryRemovesHyphensOrProlongedMarks(
        string query, string partialTitle, string keywords)
    {
        var layers = new[]
        {
            new Layer { Slots = [Slot(title: partialTitle), Slot(title: "Other", keywords: keywords)] }
        };

        SlotSearchService.Search(layers, query, IsSlotEmpty).Should().Equal(
            new SlotSearchResult(0, 1), new SlotSearchResult(0, 0));
    }

    [Theory]
    [InlineData("VS-Code", new[] { 0, 1, 7 })]
    [InlineData("'VS-Code", new[] { 1, 7 })]
    [InlineData("^'VS-Code", new[] { 1 })]
    [InlineData("VS-Code$", new[] { 0, 7 })]
    [InlineData("^VS-Code$", new[] { 0 })]
    [InlineData("VS-Code !blocked", new[] { 0, 1 })]
    [InlineData("!VS-Code", new[] { 0, 2, 3, 4, 5, 6 })]
    [InlineData("VS-Code|ラーメン", new[] { 0, 1, 2, 3, 7 })]
    [InlineData("'ラーメン", new[] { 3 })]
    [InlineData("alpha\u3000beta", new[] { 4, 5 })]
    [InlineData("vsc", new[] { 0, 1, 6, 7 })]
    public void Search_ShouldPreserveHitConditions_WhenOnlyRankingIsNormalized(string query, int[] expectedSlots)
    {
        var layers = new[]
        {
            new Layer
            {
                Slots =
                [
                    Slot(title: "VS-Code"),
                    Slot(title: "VSCode helper"),
                    Slot(title: "ラーメン"),
                    Slot(title: "ラメン用"),
                    Slot(title: "Alpha\u3000Beta"),
                    Slot(title: "Alpha Beta helper"),
                    Slot(title: "Editor", keywords: "code\u3000vsc"),
                    Slot(title: "blocked VSCode")
                ]
            }
        };

        SlotSearchService.Search(layers, query, IsSlotEmpty)
            .Select(result => result.SlotIndex).OrderBy(index => index)
            .Should().Equal(expectedSlots);
    }

    [Theory]
    [InlineData("^'vscode|code")]
    [InlineData("'vscode|code")]
    [InlineData("'vscode$|code")]
    [InlineData("^'vscode$|code")]
    public void Search_ShouldNotRankAnOrAlternativeThatOnlyMatchesAfterRankingCleanup(string query)
    {
        var layers = new[]
        {
            new Layer { Slots = [Slot(title: "VS-Code"), Slot(title: "Code")] }
        };

        // VS-Code hits only through the substring alternative "code". The quoted
        // "vscode" alternative must not promote it over the whole-field Code hit.
        SlotSearchService.Search(layers, query, IsSlotEmpty).Should().Equal(
            new SlotSearchResult(0, 1), new SlotSearchResult(0, 0));
    }

    private static SlotModel Slot(
        string? title = null,
        string? keywords = null,
        string? command = null,
        string? arguments = null,
        string? macro = null)
    {
        return new SlotModel
        {
            Title = title,
            SearchKeywords = keywords ?? string.Empty,
            Command = command,
            ArgumentsTemplate = arguments ?? "{args}",
            KeyboardMacroScript = macro ?? string.Empty
        };
    }

    private static SlotModel EmptySlot() => new();

    private static bool IsSlotEmpty(SlotModel slot)
    {
        bool baseTemplate = string.Equals(slot.ArgumentsTemplate ?? string.Empty, "{args}", StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(slot.Title) &&
               string.IsNullOrWhiteSpace(slot.Command) &&
               string.IsNullOrWhiteSpace(slot.KeyboardMacroScript) &&
               string.IsNullOrWhiteSpace(slot.ShortcutKey) &&
               baseTemplate &&
               slot.ClickEnabled &&
               string.IsNullOrWhiteSpace(slot.IconPath);
    }
}
