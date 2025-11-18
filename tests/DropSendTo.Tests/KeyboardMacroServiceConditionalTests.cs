using System.Collections.Generic;
using System.Threading.Tasks;
using DropSendTo.Models;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

[Collection("KeyboardMacroServiceTests")]
public class KeyboardMacroServiceConditionalTests
{
    [Fact]
    public async Task RunMacroAsync_ShouldExecuteThenBranch_WhenConditionMatched()
    {
        using var service = new KeyboardMacroService();
        var invoked = new List<string>();
        var context = new MacroExecutionContext(
            SlotExecutionMode.MacroScriptExtended,
            args =>
            {
                invoked.Add(args ?? string.Empty);
                return LaunchResult.Ok();
            },
            "Conditional",
            "cmd.exe");

        var script = """
SET Mode 1
IF {{Mode}} == 1
    COMMAND ok
ELSE
    COMMAND ng
ENDIF
""";

        var result = await service.RunMacroAsync(script, context);

        result.Success.Should().BeTrue();
        invoked.Should().ContainSingle().Which.Should().Be("ok");
    }

    [Fact]
    public async Task RunMacroAsync_ShouldSkipNestedIf_WhenParentIsInactive()
    {
        using var service = new KeyboardMacroService();
        var invoked = new List<string>();
        var context = new MacroExecutionContext(
            SlotExecutionMode.MacroScriptExtended,
            args =>
            {
                invoked.Add(args ?? string.Empty);
                return LaunchResult.Ok();
            },
            "Conditional",
            "cmd.exe");

        var script = """
SET Mode 2
IF {{Mode}} == 1
    IF {{Undefined}} == 1
        COMMAND never
    ENDIF
ELSE
    COMMAND fallback
ENDIF
""";

        var result = await service.RunMacroAsync(script, context);

        result.Success.Should().BeTrue();
        invoked.Should().ContainSingle().Which.Should().Be("fallback");
    }

    [Fact]
    public async Task RunMacroAsync_ShouldExecuteElseIfBranch_WhenConditionMatches()
    {
        using var service = new KeyboardMacroService();
        var invoked = new List<string>();
        var context = new MacroExecutionContext(
            SlotExecutionMode.MacroScriptExtended,
            args =>
            {
                invoked.Add(args ?? string.Empty);
                return LaunchResult.Ok();
            },
            "Conditional",
            "cmd.exe");

        var script = """
SET Mode 2
IF {{Mode}} == 1
    COMMAND first
ELSEIF {{Mode}} == 2
    COMMAND second
ELSE
    COMMAND fallback
ENDIF
""";

        var result = await service.RunMacroAsync(script, context);

        result.Success.Should().BeTrue();
        invoked.Should().ContainSingle().Which.Should().Be("second");
    }

    [Fact]
    public async Task RunMacroAsync_ShouldExecuteElseIfWithSpaceSyntax()
    {
        using var service = new KeyboardMacroService();
        var invoked = new List<string>();
        var context = new MacroExecutionContext(
            SlotExecutionMode.MacroScriptExtended,
            args =>
            {
                invoked.Add(args ?? string.Empty);
                return LaunchResult.Ok();
            },
            "Conditional",
            "cmd.exe");

        var script = """
SET Mode 3
IF {{Mode}} == 1
    COMMAND first
ELSE IF {{Mode}} == 3
    COMMAND spaced
ELSE
    COMMAND fallback
ENDIF
""";

        var result = await service.RunMacroAsync(script, context);

        result.Success.Should().BeTrue();
        invoked.Should().ContainSingle().Which.Should().Be("spaced");
    }

    [Fact]
    public async Task RunMacroAsync_ShouldSkipElseIfCondition_WhenPreviousBranchMatched()
    {
        using var service = new KeyboardMacroService();
        var invoked = new List<string>();
        var context = new MacroExecutionContext(
            SlotExecutionMode.MacroScriptExtended,
            args =>
            {
                invoked.Add(args ?? string.Empty);
                return LaunchResult.Ok();
            },
            "Conditional",
            "cmd.exe");

        var script = """
SET Mode 1
IF {{Mode}} == 1
    COMMAND first
ELSE IF {{Undefined}} == 2
    COMMAND unreachable
ENDIF
""";

        var result = await service.RunMacroAsync(script, context);

        result.Success.Should().BeTrue();
        invoked.Should().ContainSingle().Which.Should().Be("first");
    }
}
