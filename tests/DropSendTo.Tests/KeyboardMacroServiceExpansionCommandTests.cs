using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using DropSendTo.Models;
using DropSendTo.Services;
using FluentAssertions;
using Xunit;

namespace DropSendTo.Tests;

public class KeyboardMacroServiceExpansionCommandTests : IDisposable
{
    private readonly string _tempRoot;

    public KeyboardMacroServiceExpansionCommandTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "DropSendTo_ExpansionCommandTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public void TryValidateScript_ShouldPass_ForDatePathAndTextLoops()
    {
        var script = string.Join('\n',
            "NOW Stamp FILENAME",
            "PATH_BASENAME Name \"C:\\Temp\\report.txt\"",
            "FOREACH_LINE Line IN \"a\\nb\" INDEX i",
            "    TEXT {{i}}:{{Line}}",
            "ENDFOREACH_LINE",
            "SPLIT Part \"a,b\" \",\" INDEX n",
            "    TEXT {{n}}:{{Part}}",
            "ENDSPLIT");

        var ok = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeTrue("validation failed: {0}", error ?? "(null)");
        error.Should().BeNull();
    }

    [Fact]
    public void TryValidateScript_ShouldPass_ForTryCatchStructure()
    {
        var script = string.Join('\n',
            "TRY",
            "    TEXT ok",
            "CATCH ErrorMessage",
            "    POPUP \"{{ErrorMessage}}\"",
            "ENDTRY");

        var ok = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeTrue("validation failed: {0}", error ?? "(null)");
        error.Should().BeNull();
    }

    [Fact]
    public void TryValidateScript_ShouldResolveForeachLineInputAtRuntimeOrder()
    {
        var script = string.Join('\n',
            "SET Body \"a\\nb\"",
            "FOREACH_LINE Line IN {{Body}} INDEX i",
            "    SET Last {{Line}}",
            "ENDFOREACH_LINE");

        var ok = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeTrue("validation failed: {0}", error ?? "(null)");
        error.Should().BeNull();
    }

    [Fact]
    public async Task RunMacroAsync_ShouldRunCatchBlock_WhenRuntimeCommandFails()
    {
        using var service = new KeyboardMacroService();
        var missing = Path.Combine(_tempRoot, "missing.txt");
        var script = string.Join('\n',
            "TRY",
            $"    READFILE Body \"{missing}\"",
            "CATCH ErrorMessage",
            "    RETURN \"{{ErrorMessage}}\"",
            "ENDTRY");

        var result = await service.RunMacroAsync(script);

        result.Success.Should().BeTrue(result.Message);
        result.Message.Should().Contain("READFILE");
    }

    [Fact]
    public void TryValidateScript_ShouldNotCatchSyntaxErrorsInsideTry()
    {
        var script = string.Join('\n',
            "TRY",
            "    UNKNOWN_COMMAND",
            "CATCH ErrorMessage",
            "    TEXT {{ErrorMessage}}",
            "ENDTRY");

        var ok = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeFalse();
        error.Should().Contain("未知のマクロ命令");
    }

    [Fact]
    public async Task RunMacroAsync_ShouldExposeTryCatchReservedVariables()
    {
        using var service = new KeyboardMacroService();
        var missing = Path.Combine(_tempRoot, "missing.txt");
        var script = string.Join('\n',
            "TRY",
            $"    READFILE Body \"{missing}\"",
            "CATCH",
            "    RETURN \"{{error_command}}:{{error_line}}:{{error_message}}\"",
            "ENDTRY");

        var result = await service.RunMacroAsync(script);

        result.Success.Should().BeTrue(result.Message);
        result.Message.Should().Contain("TRY:");
        result.Message.Should().Contain("READFILE");
    }

    [Fact]
    public void TryValidateScript_ShouldAllowMixedNestedTextLoops()
    {
        var script = string.Join('\n',
            "SET Body \"a,b\\nc,d\"",
            "FOREACH_LINE Line IN {{Body}}",
            "    SPLIT Part {{Line}} \",\"",
            "        TEXT {{Part}}",
            "    ENDSPLIT",
            "ENDFOREACH_LINE",
            "SPLIT Line {{Body}} \"\\n\"",
            "    FOREACH_LINE Part IN {{Line}}",
            "        TEXT {{Part}}",
            "    ENDFOREACH_LINE",
            "ENDSPLIT");

        var ok = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeTrue("validation failed: {0}", error ?? "(null)");
        error.Should().BeNull();
    }

    [Fact]
    public void TryValidateScript_ShouldPass_ForFileProcessAndWindowCommands_InValidationMode()
    {
        var script = string.Join('\n',
            "MKDIR \"C:\\Temp\\DropSendToValidation\"",
            "COPY \"C:\\Temp\\missing-source.txt\" \"C:\\Temp\\missing-target.txt\"",
            "MOVE \"C:\\Temp\\missing-source.txt\" \"C:\\Temp\\missing-target.txt\"",
            "COMMAND_WAIT ExitCode TIMEOUT 1000 \"cmd.exe\" \"/c\" \"exit 0\"",
            "RUN_CAPTURE ExitCode Out Err TIMEOUT 1000 MAX 1024 \"cmd.exe\" \"/c\" \"echo ok\"",
            "WINDOW_FIND Hwnd TITLE \"Notepad\" INDEX 1",
            "WINDOW_ACTIVATE {{Hwnd}}");

        var ok = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeTrue("validation failed: {0}", error ?? "(null)");
        error.Should().BeNull();
    }

    [Fact]
    public void TryValidateScript_ShouldFail_ForUnknownWindowFindMode()
    {
        var ok = KeyboardMacroService.TryValidateScript(
            "WINDOW_FIND Hwnd UNKNOWN \"value\"",
            SlotExecutionMode.MacroScript,
            out var error);

        ok.Should().BeFalse();
        error.Should().Contain("WINDOW_FIND");
    }

    [Fact]
    public void TryValidateScript_ShouldFail_ForInvalidRunCaptureWorkingDirectory()
    {
        var script = "RUN_CAPTURE ExitCode Out Err CWD \"Z:\\DefinitelyMissing\" \"cmd.exe\"";

        var ok = KeyboardMacroService.TryValidateScript(script, SlotExecutionMode.MacroScript, out var error);

        ok.Should().BeFalse();
        error.Should().Contain("CWD");
    }

    [Fact]
    public void FileOperation_ShouldNotOverwriteExistingDestination()
    {
        var source = Path.Combine(_tempRoot, "source.txt");
        var target = Path.Combine(_tempRoot, "target.txt");
        File.WriteAllText(source, "source");
        File.WriteAllText(target, "target");

        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var args = new object?[] { $"COPY \"{source}\" \"{target}\"", variables, null, false, null };

        var ok = (bool)GetFileOperationMethod().Invoke(null, args)!;

        ok.Should().BeFalse();
        args[4].Should().BeOfType<string>().Subject.Should().Contain("既に存在");
        File.ReadAllText(target).Should().Be("target");
    }

    [Fact]
    public void FileOperation_ShouldSkipSideEffects_WhenValidateOnly()
    {
        var dir = Path.Combine(_tempRoot, "created");
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var args = new object?[] { $"MKDIR \"{dir}\"", variables, null, true, null };

        var ok = (bool)GetFileOperationMethod().Invoke(null, args)!;

        ok.Should().BeTrue();
        Directory.Exists(dir).Should().BeFalse();
    }

    private static MethodInfo GetFileOperationMethod()
    {
        var method = typeof(KeyboardMacroService).GetMethod(
            "TryApplyFileOperationDirective",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("ファイル操作ヘルパーが存在すること");
        return method!;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // ignore cleanup failures in tests
        }
    }
}
