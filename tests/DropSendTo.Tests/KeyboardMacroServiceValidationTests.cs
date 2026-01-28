using DropSendTo.Models;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class KeyboardMacroServiceValidationTests
{
    [Fact]
    public void TryValidateScript_ShouldPass_ForSimpleMacro()
    {
        const string script = "KEY Ctrl+C\nWAIT 100\nKEY V\n";

        var ok = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeTrue("validation failed: {0}", error ?? "(null)");
        error.Should().BeNull();
    }

    [Fact]
    public void TryValidateScript_ShouldFail_ForUnclosedRepeat()
    {
        const string script = "REPEAT 2\nKEY A\n";

        var ok = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeFalse();
        error.Should().NotBeNull();
        error.Should().Contain("ENDREPEAT");
    }

    [Fact]
    public void TryValidateScript_ShouldRespectCommandMode()
    {
        const string script = "COMMAND {{drop_path}}\n";

        KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error).Should().BeFalse();
        error.Should().NotBeNull();

        KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScriptExtended, out error)
            .Should().BeTrue("validation failed: {0}", error ?? "(null)");
        error.Should().BeNull();
    }

    [Fact]
    public void TryValidateScript_ShouldFail_ForUnknownKey()
    {
        var ok = KeyboardMacroService.TryValidateScript("KEY Entr", SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeFalse();
        error.Should().NotBeNull();
        error.Should().Contain("キーの指定");
    }

    [Fact]
    public void TryValidateScript_ShouldFail_ForUnknownCommand()
    {
        var ok = KeyboardMacroService.TryValidateScript("FOOBAR", SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeFalse();
        error.Should().NotBeNull();
        error.Should().Contain("未知のマクロ命令");
    }

    [Fact]
    public void TryValidateScript_ShouldPass_ForReplaceRegexCommand()
    {
        const string script = "SET Body foo\nREPLACE_REGEX Body \"o\" \"x\"\n";

        var ok = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeTrue("validation failed: {0}", error ?? "(null)");
        error.Should().BeNull();
    }

    [Fact]
    public void TryValidateScript_ShouldAllowCommandsWithoutWhitespaceBeforePlaceholders()
    {
        const string script = "SET Mode 1\nIF{{Mode}} == 1\n    TEXT{{clipboard}}\nENDIF\n";

        var ok = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeTrue("validation failed: {0}", error ?? "(null)");
        error.Should().BeNull();
    }

    [Fact]
    public void TryValidateScript_ShouldAllowElseIfAndElseBlocks()
    {
        const string script = """
SET Mode 2
IF {{Mode}} == 1
    TEXT first
ELSEIF {{Mode}} == 2
    TEXT second
ELSE
    TEXT fallback
ENDIF
""";

        var ok = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeTrue("validation failed: {0}", error ?? "(null)");
        error.Should().BeNull();
    }

    [Fact]
    public void TryValidateScript_ShouldAllowElseIfWithoutWhitespaceBeforePlaceholder()
    {
        const string script = """
SET Mode 2
IF {{Mode}} == 1
    TEXT first
ELSEIF{{Mode}} == 2
    TEXT second
ELSE
    TEXT fallback
ENDIF
""";

        var ok = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeTrue("validation failed: {0}", error ?? "(null)");
        error.Should().BeNull();
    }

    [Fact]
    public void TryValidateScript_ShouldFail_ForInvalidKeyInsideInactiveElseIf()
    {
        const string script = """
IF 1 == 1
    KEY Enter
ELSEIF 2 == 2
    KEY Entr
ENDIF
""";

        var ok = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeFalse();
        error.Should().NotBeNull();
        error.Should().Contain("キーの指定");
    }

    [Fact]
    public void TryValidateScript_ShouldFail_ForInvalidKeyInsideInactiveElse()
    {
        const string script = """
IF 1 == 1
    KEY Enter
ELSE
    KEY Entr
ENDIF
""";

        var ok = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeFalse();
        error.Should().NotBeNull();
        error.Should().Contain("キーの指定");
    }

    [Fact]
    public void TryValidateScript_ShouldAllowTestPathAndPopupCommands()
    {
        const string script = """
TESTPATH PathOk {{drop_path}}
IF {{PathOk}} == 0
    POPUP "Path missing: {{drop_path}}"
ENDIF
""";

        var ok = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeTrue("validation failed: {0}", error ?? "(null)");
        error.Should().BeNull();
    }

    [Fact]
    public void TryValidateScript_ShouldAllowWifiSsidCommand()
    {
        var ok = KeyboardMacroService.TryValidateScript("WIFI_SSID CurrentSsid", SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeTrue("validation failed: {0}", error ?? "(null)");
        error.Should().BeNull();
    }

    [Fact]
    public void TryValidateScript_ShouldFail_ForWifiSsidMissingVariable()
    {
        var ok = KeyboardMacroService.TryValidateScript("WIFI_SSID", SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeFalse();
        error.Should().NotBeNull();
        error.Should().Contain("WIFI_SSID");
    }

    [Fact]
    public void TryValidateScript_ShouldAllowAndOrConditions()
    {
        const string script = """
IF "Alpha" == "Alpha" AND "Beta" != "Gamma"
    TEXT ok
ELSE
    TEXT ng
ENDIF
""";

        var ok = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeTrue("validation failed: {0}", error ?? "(null)");
        error.Should().BeNull();
    }

    [Fact]
    public void TryValidateScript_ShouldAllowTruthyVariableCondition()
    {
        const string script = """
SET Flag 1
IF {{Flag}}
    TEXT ok
ENDIF
""";

        var ok = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeTrue("validation failed: {0}", error ?? "(null)");
        error.Should().BeNull();
    }

    [Fact]
    public void TryValidateScript_ShouldAllowMixedTruthyAndComparisonCondition()
    {
        const string script = """
SET Flag 1
SET Status OK-200
IF {{Flag}} AND {{Status}} CONTAINS "OK"
    TEXT ok
ENDIF
""";

        var ok = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeTrue("validation failed: {0}", error ?? "(null)");
        error.Should().BeNull();
    }

    [Fact]
    public void TryValidateScript_ShouldFail_ForTestPathMissingArguments()
    {
        var ok = KeyboardMacroService.TryValidateScript("TESTPATH OnlyVar", SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeFalse();
        error.Should().NotBeNull();
        error.Should().Contain("TESTPATH");
    }

    [Fact]
    public void TryValidateScript_ShouldFail_ForPopupWithoutMessage()
    {
        var ok = KeyboardMacroService.TryValidateScript("POPUP", SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeFalse();
        error.Should().NotBeNull();
        error.Should().Contain("POPUP");
    }

    [Fact]
    public void TryValidateScript_ShouldFail_ForCommandAppWithoutPath()
    {
        var ok = KeyboardMacroService.TryValidateScript("COMMAND_APP", SlotExecutionMode.MacroScriptExtended, out var error);

        ok.Should().BeFalse();
        error.Should().NotBeNull();
        error.Should().Contain("COMMAND_APP");
    }

    [Fact]
    public void TryValidateScript_ShouldFail_ForPromptWithoutArguments()
    {
        var ok = KeyboardMacroService.TryValidateScript("PROMPT", SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeFalse();
        error.Should().NotBeNull();
        error.Should().Contain("PROMPT");
    }

    [Fact]
    public void TryValidateScript_ShouldFail_ForPromptWithInvalidVariable()
    {
        var ok = KeyboardMacroService.TryValidateScript("PROMPT 123 \"input\"", SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeFalse();
        error.Should().NotBeNull();
        error.Should().Contain("変数名");
    }

    [Fact]
    public void TryValidateScript_ShouldPass_ForPromptWithDefault()
    {
        var ok = KeyboardMacroService.TryValidateScript("PROMPT Name \"Enter\" DEFAULT \"value\"", SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeTrue("validation failed: {0}", error ?? "(null)");
        error.Should().BeNull();
    }

    [Fact]
    public void TryValidateScript_ShouldPass_ForPromptWithTimeout()
    {
        var ok = KeyboardMacroService.TryValidateScript("PROMPT Name \"Enter\" TIMEOUT 5000 \"auto\"", SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeTrue("validation failed: {0}", error ?? "(null)");
        error.Should().BeNull();
    }

    [Fact]
    public void TryValidateScript_ShouldFail_ForPromptWithTimeoutMissingValue()
    {
        var ok = KeyboardMacroService.TryValidateScript("PROMPT Name \"Enter\" TIMEOUT 5000", SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeFalse();
        error.Should().NotBeNull();
        error.Should().Contain("タイムアウト");
    }

    [Fact]
    public void TryValidateScript_ShouldAllowInlineCommentsOnControlStatements()
    {
        const string script = """
SET Mode 1
IF {{Mode}} == 1 # first branch
    TEXT ok
ELSE # fallback branch
    TEXT ng
ENDIF # done
REPEAT 2 # loops
    TEXT loop
ENDREPEAT # finish
""";

        var ok = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeTrue("validation failed: {0}", error ?? "(null)");
        error.Should().BeNull();
    }

    [Fact]
    public void TryValidateScript_ShouldFail_ForCommandWithoutDelimiter()
    {
        var ok = KeyboardMacroService.TryValidateScript("KEYEnter", SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeFalse();
        error.Should().NotBeNull();
        error.Should().Contain("未知のマクロ命令");
    }

    [Fact]
    public void TryValidateScript_ShouldAllowQuotedOperandsWithWhitespace()
    {
        const string script = "IF \"Hello World\" CONTAINS \"World\"\n    TEXT ok\nENDIF\n";

        var ok = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeTrue("validation failed: {0}", error ?? "(null)");
        error.Should().BeNull();
    }

    [Fact]
    public void TryValidateScript_ShouldAllowQuotedOperandsWithNewlineEscapes()
    {
        const string script = "IF \"Line1\\nLine2\" CONTAINS \"\\nLine2\"\n    TEXT ok\nENDIF\n";

        var ok = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeTrue("validation failed: {0}", error ?? "(null)");
        error.Should().BeNull();
    }
}
