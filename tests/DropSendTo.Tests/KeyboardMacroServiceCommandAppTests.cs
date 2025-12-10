using System.Collections.Generic;
using System.Threading.Tasks;
using DropSendTo.Models;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

[Collection("KeyboardMacroServiceTests")]
public class KeyboardMacroServiceCommandAppTests
{
    [Fact]
    public void TryValidateScript_ShouldFail_WhenCommandAppUsedOutsideExtendedMode()
    {
        var ok = KeyboardMacroService.TryValidateScript("COMMAND_APP foo.exe", SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeFalse();
        error.Should().NotBeNull();
        error.Should().Contain("COMMAND_APP");
    }

    [Fact]
    public async Task RunMacroAsync_ShouldUseOverrideCommandPath_UntilReset()
    {
        using var service = new KeyboardMacroService();
        var invoked = new List<(string? Args, string? CommandPath)>();
        var context = new MacroExecutionContext(
            SlotExecutionMode.MacroScriptExtended,
            (args, commandPath) =>
            {
                invoked.Add((args, commandPath));
                return LaunchResult.Ok();
            },
            "Test Slot",
            "default.exe");

        var result = await service.RunMacroAsync("""
COMMAND_APP "C:\alt.exe"
COMMAND --first
COMMAND_APP RESET
COMMAND --second
""", context);

        result.Success.Should().BeTrue();
        invoked.Should().HaveCount(2);
        invoked[0].CommandPath.Should().Be(@"C:\alt.exe");
        invoked[0].Args.Should().Be("--first");
        invoked[1].CommandPath.Should().Be("default.exe");
        invoked[1].Args.Should().Be("--second");
    }
}
