using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class MacroArgumentTokenizerTests
{
    [Fact]
    public void Tokenize_ShouldSplitUnquotedAndQuotedArguments()
    {
        var tokens = MacroArgumentTokenizer.Tokenize(
            "ExitCode TIMEOUT 1000 \"C:\\Program Files\\tool.exe\" /quiet",
            out var error);

        error.Should().BeNull();
        tokens.Should().Equal("ExitCode", "TIMEOUT", "1000", "C:\\Program Files\\tool.exe", "/quiet");
    }

    [Fact]
    public void Tokenize_ShouldKeepTrailingBackslashBeforeClosingQuote()
    {
        var tokens = MacroArgumentTokenizer.Tokenize("\"C:\\work folder\\\"", out var error);

        error.Should().BeNull();
        tokens.Should().ContainSingle().Which.Should().Be("C:\\work folder\\");
    }

    [Fact]
    public void Tokenize_ShouldTreatEscapedQuotesAsContent_WhenMoreTextFollows()
    {
        var tokens = MacroArgumentTokenizer.Tokenize("\"say \\\"hello\\\" now\"", out var error);

        error.Should().BeNull();
        tokens.Should().ContainSingle().Which.Should().Be("say \"hello\" now");
    }

    [Fact]
    public void Tokenize_ShouldReturnError_WhenQuotedArgumentIsNotClosed()
    {
        var tokens = MacroArgumentTokenizer.Tokenize("\"C:\\work folder", out var error);

        tokens.Should().BeNull();
        error.Should().Be("Macro の 引数 が閉じられていません。");
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("  # comment", true)]
    [InlineData(" next", false)]
    public void IsPathQuoteTerminator_ShouldRecognizeOnlyEndOrComment(string remainingInput, bool expected)
    {
        MacroArgumentTokenizer.IsPathQuoteTerminator(remainingInput, 0).Should().Be(expected);
    }
}
