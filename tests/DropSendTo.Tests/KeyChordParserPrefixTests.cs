using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class KeyChordParserPrefixTests
{
    [Theory]
    [InlineData("Enter")]
    [InlineData("Ctrl+Enter")]
    [InlineData("Space")]
    [InlineData("Tab")]
    [InlineData("Backspace")]
    [InlineData("Ctrl")]
    [InlineData("Win")]
    public void TryParsePrefix_ShouldRejectReservedKeys(string expression)
    {
        KeyChordParser.TryParsePrefix(expression, out _, out var error).Should().BeFalse();
        error.Should().Contain("Prefix に使用できないキー");
    }

    [Fact]
    public void TryParsePrefix_ShouldNormalizeAllowedChord()
    {
        KeyChordParser.TryParsePrefix("ctrl+alt+q", out var chord, out var error)
            .Should().BeTrue($"prefix chord should parse: {error}");

        chord.NormalizedString.Should().Be("Ctrl+Alt+Q");
    }
}
