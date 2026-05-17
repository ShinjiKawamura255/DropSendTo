using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class MacroConditionEvaluatorTests
{
    [Theory]
    [InlineData("Admin == Admin", true)]
    [InlineData("Admin = User", false)]
    [InlineData("Admin != User", true)]
    [InlineData("10 > 5", true)]
    [InlineData("10 < 5", false)]
    [InlineData("10 >= 10", true)]
    [InlineData("9 <= 10", true)]
    [InlineData("\"Hello World\" CONTAINS World", true)]
    [InlineData("\"Hello World\" CONTAIN World", true)]
    [InlineData("\"Hello World\" NOTCONTAINS Admin", true)]
    [InlineData("\"Hello World\" STARTSWITH Hello", true)]
    [InlineData("\"Hello World\" SW Hello", true)]
    [InlineData("\"Hello World\" ENDSWITH World", true)]
    [InlineData("\"Hello World\" EW World", true)]
    public void TryEvaluateExpanded_ShouldEvaluateComparisonOperators(string expression, bool expected)
    {
        var ok = MacroConditionEvaluator.TryEvaluateExpanded(expression, out var result, out var error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("true", true)]
    [InlineData("0", false)]
    [InlineData("1", true)]
    [InlineData("nonempty", true)]
    public void TryEvaluateExpanded_ShouldEvaluateTruthyTerms(string expression, bool expected)
    {
        var ok = MacroConditionEvaluator.TryEvaluateExpanded(expression, out var result, out var error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        result.Should().Be(expected);
    }

    [Fact]
    public void TryEvaluateExpanded_ShouldEvaluateAndBeforeOr()
    {
        var ok = MacroConditionEvaluator.TryEvaluateExpanded("1 == 0 OR 1 == 1 AND 2 == 2", out var result, out var error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        result.Should().BeTrue();
    }

    [Fact]
    public void TryEvaluateExpanded_ShouldHandleUncLiterals()
    {
        const string expression = "\"\\\\server\\share\" == \"\\\\server\\share\"";

        var ok = MacroConditionEvaluator.TryEvaluateExpanded(expression, out var result, out var error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        result.Should().BeTrue();
    }

    [Fact]
    public void TryEvaluateExpanded_ShouldHandleEscapedQuotes()
    {
        const string expression = "\"Hello \\\"World\\\"\" == \"Hello \\\"World\\\"\"";

        var ok = MacroConditionEvaluator.TryEvaluateExpanded(expression, out var result, out var error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        result.Should().BeTrue();
    }

    [Fact]
    public void TryEvaluateExpanded_ShouldHandleTrailingBackslashBeforeClosingQuote()
    {
        const string expression = "\"C:\\Temp\\\\\" == \"C:\\Temp\\\\\"";

        var ok = MacroConditionEvaluator.TryEvaluateExpanded(expression, out var result, out var error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        result.Should().BeTrue();
    }

    [Fact]
    public void TryEvaluateExpanded_ShouldKeepLogicalAndCommentLikeTextInsideQuotes()
    {
        const string expression = "\"A AND B OR C # comment\" == \"A AND B OR C # comment\"";

        var ok = MacroConditionEvaluator.TryEvaluateExpanded(expression, out var result, out var error);

        ok.Should().BeTrue();
        error.Should().BeNull();
        result.Should().BeTrue();
    }

    [Fact]
    public void TryEvaluateExpanded_ShouldFail_OnUnterminatedQuote()
    {
        var ok = MacroConditionEvaluator.TryEvaluateExpanded("\"unterminated == value", out _, out var error);

        ok.Should().BeFalse();
        error.Should().Contain("閉じられていません");
    }

    [Fact]
    public void TryEvaluateExpanded_ShouldFail_OnUnknownOperator()
    {
        var ok = MacroConditionEvaluator.TryEvaluateExpanded("Flag ??? 1", out _, out var error);

        ok.Should().BeFalse();
        error.Should().Contain("演算子");
    }
}
