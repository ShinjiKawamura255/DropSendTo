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

        ok.Should().BeTrue();
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

        KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScriptExtended, out error).Should().BeTrue();
        error.Should().BeNull();
    }
}
