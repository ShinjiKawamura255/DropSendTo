using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DropSendTo.Models;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

[Collection("KeyboardMacroServiceTests")]
public class KeyboardMacroServiceTestPathTests
{
    [Fact]
    public async Task RunMacroAsync_ShouldSetVariableToOne_WhenPathExists()
    {
        using var service = new KeyboardMacroService();
        var invoked = new List<string>();
        var tempFile = CreateTempFile(withSpaces: false);
        try
        {
            var script = """
TESTPATH PathOk {{drop_path}}
IF {{PathOk}} == 1
    COMMAND ok
ELSE
    COMMAND ng
ENDIF
""";

            var context = new MacroExecutionContext(
                SlotExecutionMode.MacroScriptExtended,
                args =>
                {
                    invoked.Add(args ?? string.Empty);
                    return LaunchResult.Ok();
                },
                "PathChecker",
                "cmd.exe",
                new[] { tempFile });

            var result = await service.RunMacroAsync(script, context);

            result.Success.Should().BeTrue(result.Message);
            invoked.Should().ContainSingle().Which.Should().Be("ok");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task RunMacroAsync_ShouldHandlePathsContainingSpaces()
    {
        using var service = new KeyboardMacroService();
        var invoked = new List<string>();
        var tempFile = CreateTempFile(withSpaces: true);
        try
        {
            var script = """
TESTPATH PathOk {{drop_path}}
IF {{PathOk}} == 1
    COMMAND ok
ELSE
    COMMAND ng
ENDIF
""";

            var context = new MacroExecutionContext(
                SlotExecutionMode.MacroScriptExtended,
                args =>
                {
                    invoked.Add(args ?? string.Empty);
                    return LaunchResult.Ok();
                },
                "PathChecker",
                "cmd.exe",
                new[] { tempFile });

            var result = await service.RunMacroAsync(script, context);

            result.Success.Should().BeTrue(result.Message);
            invoked.Should().ContainSingle().Which.Should().Be("ok");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task RunMacroAsync_ShouldHandleQuotedTestPathArguments()
    {
        using var service = new KeyboardMacroService();
        var invoked = new List<string>();
        var tempFile = CreateTempFile(withSpaces: true);
        try
        {
            var script = """
TESTPATH PathOk "{{drop_path}}"
IF {{PathOk}} == 1
    COMMAND ok
ELSE
    COMMAND ng
ENDIF
""";

            var context = new MacroExecutionContext(
                SlotExecutionMode.MacroScriptExtended,
                args =>
                {
                    invoked.Add(args ?? string.Empty);
                    return LaunchResult.Ok();
                },
                "PathChecker",
                "cmd.exe",
                new[] { tempFile });

            var result = await service.RunMacroAsync(script, context);

            result.Success.Should().BeTrue(result.Message);
            invoked.Should().ContainSingle().Which.Should().Be("ok");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task RunMacroAsync_ShouldHandleLiteralQuotedPath()
    {
        using var service = new KeyboardMacroService();
        var invoked = new List<string>();
        var tempFile = CreateTempFile(withSpaces: true);
        try
        {
            var template = """
TESTPATH PathOk "{PATH}"
IF {{PathOk}} == 1
    COMMAND ok
ELSE
    COMMAND ng
ENDIF
""";
            var script = template.Replace("{PATH}", tempFile, StringComparison.Ordinal);

            var context = new MacroExecutionContext(
                SlotExecutionMode.MacroScriptExtended,
                args =>
                {
                    invoked.Add(args ?? string.Empty);
                    return LaunchResult.Ok();
                },
                "PathChecker",
                "cmd.exe",
                Array.Empty<string>());

            var result = await service.RunMacroAsync(script, context);

            result.Success.Should().BeTrue(result.Message);
            invoked.Should().ContainSingle().Which.Should().Be("ok");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task RunMacroAsync_ShouldEvaluateIfWithQuotedDropPath()
    {
        using var service = new KeyboardMacroService();
        var invoked = new List<string>();
        var tempFile = CreateTempFile(withSpaces: true);
        try
        {
            var script = """
SET Original {{drop_path}}
IF "{{drop_path}}" == "{{Original}}"
    COMMAND ok
ELSE
    COMMAND ng
ENDIF
""";

            var context = new MacroExecutionContext(
                SlotExecutionMode.MacroScriptExtended,
                args =>
                {
                    invoked.Add(args ?? string.Empty);
                    return LaunchResult.Ok();
                },
                "PathChecker",
                "cmd.exe",
                new[] { tempFile });

            var result = await service.RunMacroAsync(script, context);

            result.Success.Should().BeTrue(result.Message);
            invoked.Should().ContainSingle().Which.Should().Be("ok");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task RunMacroAsync_ShouldSetVariableToZero_WhenPathMissing()
    {
        using var service = new KeyboardMacroService();
        var invoked = new List<string>();
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var script = """
TESTPATH PathOk {{drop_path}}
IF {{PathOk}} == 0
    COMMAND missing
ELSE
    COMMAND unexpected
ENDIF
""";

        var context = new MacroExecutionContext(
            SlotExecutionMode.MacroScriptExtended,
            args =>
            {
                invoked.Add(args ?? string.Empty);
                return LaunchResult.Ok();
            },
            "PathChecker",
            "cmd.exe",
            new[] { missingPath });

        var result = await service.RunMacroAsync(script, context);

        result.Success.Should().BeTrue();
        invoked.Should().ContainSingle().Which.Should().Be("missing");
    }
    private static string CreateTempFile(bool withSpaces)
    {
        var directory = Path.GetTempPath();
        var fileName = withSpaces
            ? $"macro test {Guid.NewGuid():N}.txt"
            : $"macrotest_{Guid.NewGuid():N}.txt";
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, "test");
        return path;
    }
}
