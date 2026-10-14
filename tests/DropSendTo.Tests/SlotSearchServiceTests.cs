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
