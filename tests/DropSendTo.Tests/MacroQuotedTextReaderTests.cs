using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class MacroQuotedTextReaderTests
{
    [Fact]
    public void TryRead_ShouldReturnLiteralAndAdvanceIndex_WhenQuotedTextCloses()
    {
        const string input = "\"Hello\" tail";
        var index = 0;

        var ok = MacroQuotedTextReader.TryRead(input, ref index, "TEST", "値", out var value, out var error);

        ok.Should().BeTrue();
        value.Should().Be("Hello");
        index.Should().Be(7);
        error.Should().BeNull();
    }

    [Fact]
    public void TryRead_ShouldDecodeStandardEscapes()
    {
        const string input = "\"Line1\\nLine2\\t\\\"Q\\\"\"";
        var index = 0;

        var ok = MacroQuotedTextReader.TryRead(input, ref index, "TEST", "値", out var value, out var error);

        ok.Should().BeTrue();
        value.Should().Be("Line1\nLine2\t\"Q\"");
        error.Should().BeNull();
    }

    [Fact]
    public void TryRead_ShouldTreatEvenBackslashesBeforeQuoteAsTerminator()
    {
        const string input = "\"C:\\Temp\\\\\" tail";
        var index = 0;

        var ok = MacroQuotedTextReader.TryRead(input, ref index, "TEST", "値", out var value, out var error);

        ok.Should().BeTrue();
        value.Should().Be("C:\\Temp\\");
        index.Should().Be(11);
        error.Should().BeNull();
    }

    [Fact]
    public void TryRead_ShouldTreatOddBackslashQuoteAsEscapedQuote_WhenMoreTextFollows()
    {
        const string input = "\"Hello \\\"World\\\"\"";
        var index = 0;

        var ok = MacroQuotedTextReader.TryRead(input, ref index, "TEST", "値", out var value, out var error);

        ok.Should().BeTrue();
        value.Should().Be("Hello \"World\"");
        error.Should().BeNull();
    }

    [Fact]
    public void TryRead_ShouldTreatOddBackslashQuoteAsTerminator_WhenOnlyCommentFollows()
    {
        const string input = "\"C:\\Temp\\\" # comment";
        var index = 0;

        var ok = MacroQuotedTextReader.TryRead(input, ref index, "TEST", "値", out var value, out var error);

        ok.Should().BeTrue();
        value.Should().Be("C:\\Temp\\");
        index.Should().Be(10);
        error.Should().BeNull();
    }

    [Fact]
    public void TryRead_ShouldFail_WhenQuoteIsUnterminated()
    {
        const string input = "\"unterminated";
        var index = 0;

        var ok = MacroQuotedTextReader.TryRead(input, ref index, "IF", "条件", out var value, out var error);

        ok.Should().BeFalse();
        value.Should().BeEmpty();
        error.Should().Contain("IF の 条件 が閉じられていません。");
    }
}
