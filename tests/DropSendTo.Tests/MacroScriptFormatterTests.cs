using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public sealed class MacroScriptFormatterTests
{
    [Fact]
    public void NormalizeIndentation_ShouldAlignBlocksWithFourSpaces()
    {
        var script = "IF condition\r\nTEXT One\r\nELSE\r\nTEXT Two\r\nENDIF\r\nREPEAT 2\r\nTEXT Loop\r\nENDREPEAT\r\n";

        var formatted = MacroScriptFormatter.NormalizeIndentation(script);

        formatted.Should().Be(
            "IF condition\r\n" +
            "    TEXT One\r\n" +
            "ELSE\r\n" +
            "    TEXT Two\r\n" +
            "ENDIF\r\n" +
            "REPEAT 2\r\n" +
            "    TEXT Loop\r\n" +
            "ENDREPEAT\r\n");
    }

    [Fact]
    public void NormalizeIndentation_ShouldKeepBlankLinesWithoutSpaces()
    {
        var script = "REPEAT 1\r\n\r\nTEXT X\r\n\r\nENDREPEAT";

        var formatted = MacroScriptFormatter.NormalizeIndentation(script);

        formatted.Should().Be(
            "REPEAT 1\r\n\r\n" +
            "    TEXT X\r\n\r\n" +
            "ENDREPEAT");
    }

    [Fact]
    public void NormalizeIndentation_ShouldAlignElseIfBranches()
    {
        var script =
            "IF {{Mode}} == 1\r\n" +
            "TEXT First\r\n" +
            "ELSEIF {{Mode}} == 2\r\n" +
            "TEXT Second\r\n" +
            "ELSE IF {{Mode}} == 3\r\n" +
            "TEXT Third\r\n" +
            "ENDIF";

        var formatted = MacroScriptFormatter.NormalizeIndentation(script);

        formatted.Should().Be(
            "IF {{Mode}} == 1\r\n" +
            "    TEXT First\r\n" +
            "ELSEIF {{Mode}} == 2\r\n" +
            "    TEXT Second\r\n" +
            "ELSE IF {{Mode}} == 3\r\n" +
            "    TEXT Third\r\n" +
            "ENDIF");
    }

    [Fact]
    public void GetIndentationForNewLine_ShouldIncreaseAfterRepeat()
    {
        var script = "REPEAT 3\r\n";

        var indent = MacroScriptFormatter.GetIndentationForNewLine(script, script.Length);

        indent.Should().Be("    ");
    }

    [Fact]
    public void GetIndentationForNewLine_ShouldAlignElseWithIf()
    {
        var script = "IF 1 == 1\r\n    TEXT OK\r\nELSE";

        var indent = MacroScriptFormatter.GetIndentationForNewLine(script, script.Length);

        indent.Should().Be("    ");
    }

    [Fact]
    public void GetIndentationForNewLine_ShouldIndentAfterElseIf()
    {
        var script = "IF 1 == 2\r\nELSEIF 2 == 2";

        var indent = MacroScriptFormatter.GetIndentationForNewLine(script, script.Length);

        indent.Should().Be("    ");
    }

    [Fact]
    public void GetIndentationForNewLine_ShouldResetAfterEndRepeat()
    {
        var script = "REPEAT 2\r\n    TEXT\r\nENDREPEAT";

        var indent = MacroScriptFormatter.GetIndentationForNewLine(script, script.Length);

        indent.Should().Be(string.Empty);
    }
}
