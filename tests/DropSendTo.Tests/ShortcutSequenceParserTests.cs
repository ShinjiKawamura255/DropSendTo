using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class ShortcutSequenceParserTests
{
    [Fact]
    public void TryParse_WithTwoChords_ShouldNormalizeWithSpace()
    {
        var success = ShortcutSequenceParser.TryParse("B A", out var sequence, out var error);

        success.Should().BeTrue();
        error.Should().BeNull();
        sequence.Should().NotBeNull();
        sequence!.Chords.Count.Should().Be(2);
        sequence.NormalizedString.Should().Be("B A");
    }

    [Fact]
    public void TryParse_WithInvalidToken_ShouldFail()
    {
        var success = ShortcutSequenceParser.TryParse("Ctrl+Foo", out _, out var error);

        success.Should().BeFalse();
        error.Should().NotBeNull();
    }

    [Fact]
    public void TryParse_WithAltSpace_ShouldFail()
    {
        var success = ShortcutSequenceParser.TryParse("Alt+Space", out _, out var error);

        success.Should().BeFalse();
        error.Should().Contain("Alt+Space");
    }
}
