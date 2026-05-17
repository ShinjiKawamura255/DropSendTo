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
